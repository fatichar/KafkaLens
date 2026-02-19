using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Utils;

namespace KafkaLens;

public class SavedMessagesConsumer : ConsumerBase
{
    private readonly string clusterDir;

    public SavedMessagesConsumer(string clusterDir)
    {
        this.clusterDir = clusterDir;
    }

    #region Read
    public override bool ValidateConnection()
    {
        return Directory.Exists(clusterDir);
    }

    public override List<Topic> GetTopics()
    {
        if (Topics.Count > 0)
        {
            Topics.Clear();
        }

        return base.GetTopics();
    }

    protected override List<Topic> FetchTopics()
    {
        var topicDirs = Directory.GetDirectories(clusterDir);
        var topics = Array.ConvertAll(topicDirs, topicDir =>
        {
            var topicName = Path.GetFileName(topicDir);
            var partitionDirs = Directory.GetDirectories(topicDir);
            var partitions = Array.ConvertAll(partitionDirs, partitionDir =>
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
        var partitionDirs = Directory.GetDirectories(topicDir);

        var allFilesWithTimestamp = new ConcurrentBag<(string file, int partition, long timestamp)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 20,
            CancellationToken = cancellationToken
        };

        // Scan all partitions in parallel, and files within partitions in parallel
        await Parallel.ForEachAsync(partitionDirs, parallelOptions, async (partitionDir, ct) =>
        {
            var partition = int.Parse(Path.GetFileName(partitionDir));
            var messageFiles = Directory.GetFiles(partitionDir, "*.klm");
            var textFiles = Directory.GetFiles(partitionDir, "*.txt");
            var allFiles = messageFiles.Concat(textFiles).ToList();

            await Parallel.ForEachAsync(allFiles, parallelOptions, async (file, innerCt) =>
            {
                var timestamp = await GetMessageTimestampAsync(file);
                allFilesWithTimestamp.Add((file, partition, timestamp));
            });
        });

        if (cancellationToken.IsCancellationRequested) return;

        IOrderedEnumerable<(string file, int partition, long timestamp)> sortedFiles;

        bool fromEnd = options.Start.Type == PositionType.OFFSET && options.Start.Offset < 0;

        if (fromEnd)
        {
            sortedFiles = allFilesWithTimestamp.OrderByDescending(f => f.timestamp);
        }
        else
        {
            sortedFiles = allFilesWithTimestamp.OrderBy(f => f.timestamp);
        }

        IEnumerable<(string file, int partition, long timestamp)> filesToProcess = sortedFiles;

        if (options.Start.Type == PositionType.TIMESTAMP)
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
        var messageFiles = Directory.GetFiles(partitionDir, "*.klm");
        var textFiles = Directory.GetFiles(partitionDir, "*.txt");
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

        if (options.Start.Type == PositionType.TIMESTAMP)
        {
            var messages = new List<(string file, long offset)>();
            foreach (var fileOffset in fileOffsets)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var message = await CreateMessageAsync(fileOffset.file);
                if (message.EpochMillis >= options.Start.Timestamp)
                {
                    messages.Add(fileOffset);
                    if (messages.Count >= options.Limit)
                    {
                        break;
                    }
                }
            }
            filesToProcess = messages;
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

            int endIndex = totalCount;
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