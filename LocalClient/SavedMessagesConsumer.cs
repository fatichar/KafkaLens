using System.Collections.Concurrent;
using System.Globalization;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.Clients;

public class SavedMessagesConsumer(string clusterDir) : ConsumerBase
{
    #region Read
    public override bool ValidateConnection()
    {
        return Directory.Exists(clusterDir);
    }

    protected override List<Topic> FetchTopics()
    {
        var topicDirs = Directory.EnumerateDirectories(clusterDir);
        var topics = topicDirs.Select(topicDir =>
        {
            var topicName = Path.GetFileName(topicDir);
            var partitionDirs = Directory.EnumerateDirectories(topicDir);
            var partitions = partitionDirs.Select(partitionDir =>
            {
                var partition = int.Parse(Path.GetFileName(partitionDir));
                return new Partition(partition);
            }).ToList();
            var topic = new Topic(topicName, partitions);
            return topic;
        });
        return topics.ToList();
    }

    private async Task<long> GetMessageTimestampAsync(string messageFile)
    {
        try
        {
            if (messageFile.EndsWith(".klm", StringComparison.OrdinalIgnoreCase))
            {
                await using var fs = File.OpenRead(messageFile);
                using var reader = new System.IO.BinaryReader(fs, System.Text.Encoding.UTF8, false);
                if (fs.Length < 9) return 0; // 1 byte version + 8 bytes long
                reader.ReadByte(); // version
                return reader.ReadInt64(); // epochMillis
            }
            else
            {
                await foreach (var line in File.ReadLinesAsync(messageFile))
                {
                    if (line.StartsWith("Timestamp: "))
                    {
                        var timeText = line.AsSpan(11);
                        if (DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto))
                        {
                            return dto.ToUnixTimeMilliseconds();
                        }
                        return 0;
                    }
                    // Stop reading if past headers
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }
                }
                return 0;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to get timestamp for file {File}", messageFile);
            return 0;
        }
    }

    protected override async Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
    {
        var topicDir = Path.Combine(clusterDir, topicName);
        if (!Directory.Exists(topicDir))
        {
            return;
        }
        var partitionDirs = Directory.EnumerateDirectories(topicDir);

        var allFilesWithTimestamp = new ConcurrentBag<(string file, int partition, long timestamp)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 20,
            CancellationToken = cancellationToken
        };

        var allFiles = partitionDirs.SelectMany(partitionDir =>
        {
            var partition = int.Parse(Path.GetFileName(partitionDir));
            var messageFiles = Directory.EnumerateFiles(partitionDir, "*.klm");
            var textFiles = Directory.EnumerateFiles(partitionDir, "*.txt");
            return messageFiles.Concat(textFiles).Select(file => (file, partition));
        });

        // Scan all files in parallel
        await Parallel.ForEachAsync(allFiles, parallelOptions, async (fileMeta, ct) =>
        {
            var timestamp = await GetMessageTimestampAsync(fileMeta.file);
            allFilesWithTimestamp.Add((fileMeta.file, fileMeta.partition, timestamp));
        });

        if (cancellationToken.IsCancellationRequested) return;

        IOrderedEnumerable<(string file, int partition, long timestamp)> sortedFiles;

        bool fromEnd = options.Start.Type == PositionType.Offset && options.Start.Offset < 0;

        if (fromEnd)
        {
            sortedFiles = allFilesWithTimestamp.OrderByDescending(f => f.timestamp);
        }
        else
        {
            sortedFiles = allFilesWithTimestamp.OrderBy(f => f.timestamp);
        }

        IEnumerable<(string file, int partition, long timestamp)> filesToProcess = sortedFiles;

        if (options.Start.Type == PositionType.Timestamp)
        {
            filesToProcess = filesToProcess.Where(f => f.timestamp >= options.Start.Timestamp);
        }

        filesToProcess = filesToProcess.Take(options.Limit);

        var filesToLoad = filesToProcess.ToList();

        // Chunked processing to ensure order while loading in parallel
        const int chunkSize = 100;

        for (int i = 0; i < filesToLoad.Count; i += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var chunk = filesToLoad.Skip(i).Take(chunkSize).ToList();
            var chunkMessages = new ConcurrentBag<Message>();

            await Parallel.ForEachAsync(chunk, parallelOptions, async (fileMeta, ct) =>
            {
                try
                {
                    var message = await CreateMessageAsync(fileMeta.file);
                    message.Partition = fileMeta.partition;
                    chunkMessages.Add(message);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Failed to load message {File}", fileMeta.file);
                }
            });

            // Flush chunk
            FlushBatch(messages, chunkMessages);
        }
    }

    private void FlushBatch(MessageStream messages, ConcurrentBag<Message> batch)
    {
        var items = new List<Message>();
        while (batch.TryTake(out var msg))
        {
            items.Add(msg);
        }

        if (items.Count > 0)
        {
            // Lock required for thread safety of ObservableCollection
            lock (messages.Messages)
            {
                 // Sort by timestamp within the batch to maintain local order
                 items.Sort((a, b) => a.EpochMillis.CompareTo(b.EpochMillis));
                 messages.Messages.AddRange(items);
            }
        }
    }

    protected override async Task GetMessagesAsync(string topicName, int partition, FetchOptions options, MessageStream stream, CancellationToken cancellationToken)
    {
        await LoadMessagesForPartitionAsync(topicName, partition, options, stream, cancellationToken);
    }

    private async Task LoadMessagesForPartitionAsync(
        string topicName,
        int partition,
        FetchOptions options,
        MessageStream stream,
        CancellationToken cancellationToken)
    {
        var partitionDir = Path.Combine(clusterDir, topicName, partition.ToString());
        if (!Directory.Exists(partitionDir))
        {
            return;
        }
        var messageFiles = Directory.EnumerateFiles(partitionDir, "*.klm");
        var textFiles = Directory.EnumerateFiles(partitionDir, "*.txt");
        var allFiles = messageFiles.Concat(textFiles);

        var fileOffsets = allFiles.Select(file =>
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            long.TryParse(fileName, out var offset);
            return (file, offset);
        }).ToList();

        fileOffsets.Sort((a, b) => a.offset.CompareTo(b.offset));

        var totalCount = fileOffsets.Count;
        IEnumerable<(string file, long offset)> filesToProcess;

        if (options.Start.Type == PositionType.Timestamp)
        {
            var messages = new List<(string file, long offset)>();

            // To avoid sequential await bottleneck, we load timestamps for all potential files in parallel.
            // Since we need to find an anchor based on timestamp, we can query timestamps using GetMessageTimestampAsync.
            var fileTimestamps = new long[fileOffsets.Count];
            var timestampParallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 20,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, fileOffsets.Count), timestampParallelOptions, async (i, ct) =>
            {
                fileTimestamps[i] = await GetMessageTimestampAsync(fileOffsets[i].file);
            });

            if (options.Direction == FetchDirection.Backward)
            {
                // Find index of first message >= Timestamp.
                int anchorIndex = -1;
                for (int i = 0; i < fileOffsets.Count; i++)
                {
                    if (fileTimestamps[i] >= options.Start.Timestamp)
                    {
                        anchorIndex = i;
                        break;
                    }
                }

                if (anchorIndex == -1)
                {
                    anchorIndex = fileOffsets.Count;
                }

                int startIndex = Math.Max(0, anchorIndex - options.Limit + 1);
                int count = Math.Min(options.Limit, anchorIndex - startIndex + 1);
                filesToProcess = fileOffsets.Skip(startIndex).Take(count);
            }
            else
            {
                for (int i = 0; i < fileOffsets.Count; i++)
                {
                    if (fileTimestamps[i] >= options.Start.Timestamp)
                    {
                        messages.Add(fileOffsets[i]);
                        if (messages.Count >= options.Limit)
                        {
                            break;
                        }
                    }
                }
                filesToProcess = messages;
            }
        }
        else // OFFSET
        {
            int startIndex = 0;
            if (options.Start.Offset >= 0)
            {
                startIndex = fileOffsets.FindIndex(f => f.offset >= options.Start.Offset);
                if (startIndex == -1) startIndex = totalCount;
            }
            else // from end
            {
                startIndex = Math.Max(0, totalCount + (int)options.Start.Offset);
            }

            if (options.Direction == FetchDirection.Backward && options.Start.Offset >= 0)
            {
                // Adjust start index for backward fetch
                // Target is startIndex.
                // NewStart = startIndex - Limit + 1.
                startIndex = Math.Max(0, startIndex - options.Limit + 1);
            }

            int endIndex = totalCount;
            // Existing logic for "End" (FetchPosition.End) is not affected by Direction flag
            // because "End" logic is handled by "else // from end" block above or implicit limit.
            // But we have explicit End property in Options.
            if (options.End != null)
            {
                if (options.End.Offset >= 0)
                {
                    endIndex = fileOffsets.FindLastIndex(f => f.offset <= options.End.Offset);
                    if (endIndex != -1) endIndex++;
                    else endIndex = startIndex;
                }
                else
                {
                    endIndex = totalCount + (int)options.End.Offset + 1;
                    if (endIndex < startIndex)
                    {
                        endIndex = startIndex;
                    }
                }
            }

            var count = Math.Max(0, endIndex - startIndex);
            filesToProcess = fileOffsets.Skip(startIndex).Take(count).Take(options.Limit);
        }

        var filesToLoad = filesToProcess.ToList();
        const int chunkSize = 100;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 20,
            CancellationToken = cancellationToken
        };

        for (int i = 0; i < filesToLoad.Count; i += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var chunk = filesToLoad.Skip(i).Take(chunkSize).ToList();
            var chunkMessages = new ConcurrentBag<Message>();

            await Parallel.ForEachAsync(chunk, parallelOptions, async (s, ct) =>
            {
                try
                {
                    var message = await CreateMessageAsync(s.file);
                    message.Partition = partition;
                    chunkMessages.Add(message);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Failed to load message {File}", s.file);
                }
            });

            FlushBatch(stream, chunkMessages);
        }
    }

    private Message CreateMessage(string messageFile)
    {
        if (messageFile.EndsWith(".klm", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(messageFile);
            return Message.Deserialize(fs);
        }
        else
        {
            var lines = File.ReadAllLines(messageFile);
            return ParseMessageFromLines(lines);
        }
    }

    private async Task<Message> CreateMessageAsync(string messageFile)
    {
        if (messageFile.EndsWith(".klm", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(messageFile);
            return await Message.DeserializeAsync(fs);
        }
        else
        {
            var lines = await File.ReadAllLinesAsync(messageFile);
            return ParseMessageFromLines(lines);
        }
    }

    private Message ParseMessageFromLines(string[] lines)
    {
        long epochMillis = 0;
        var headers = new Dictionary<string, byte[]>();
        byte[]? key = null;
        byte[]? value = null;
        int partition = 0;
        long offset = 0;

        int i = 0;
        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++; // Skip empty line
                break; // End of metadata
            }

            if (line.StartsWith("Key: "))
            {
                var keyText = line.Substring(5);
                key = System.Text.Encoding.UTF8.GetBytes(keyText);
            }
            else if (line.StartsWith("Timestamp: "))
            {
                var timeText = line.Substring(11);
                if (DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dto))
                {
                    epochMillis = dto.ToUnixTimeMilliseconds();
                }
            }
            else if (line.StartsWith("Partition: "))
            {
                int.TryParse(line.Substring(11), out partition);
            }
            else if (line.StartsWith("Offset: "))
            {
                long.TryParse(line.Substring(8), out offset);
            }
            else if (line.StartsWith("Headers:"))
            {
                i++;
                for (; i < lines.Length; i++)
                {
                    line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }
                    var parts = line.Trim().Split(new[] { ": " }, 2, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        headers[parts[0]] = System.Text.Encoding.UTF8.GetBytes(parts[1]);
                    }
                }
                // The loop breaks on empty line, which matches the outer break condition
                break;
            }
        }

        // The rest is the body
        var bodyBuilder = new System.Text.StringBuilder();
        for (; i < lines.Length; i++)
        {
            bodyBuilder.AppendLine(lines[i]);
        }
        value = System.Text.Encoding.UTF8.GetBytes(bodyBuilder.ToString().TrimEnd());

        var msg = new Message(epochMillis, headers, key, value)
        {
            Partition = partition,
            Offset = offset
        };
        return msg;
    }

    #endregion Read
}