using System.Threading;
using Serilog;
﻿using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;

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

    protected override void GetMessages(string topicName, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
    {
        var topicDir = Path.Combine(clusterDir, topicName);
        if (!Directory.Exists(topicDir))
        {
            messages.HasMore = false;
            return;
        }
        var partitionDirs = Directory.GetDirectories(topicDir);
        using var semaphore = new SemaphoreSlim(10);
        var tasks = partitionDirs.Select(async partitionDir =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            var partition = int.Parse(Path.GetFileName(partitionDir));
            await LoadMessagesForPartitionAsync(topicName, partition, options, messages, semaphore, cancellationToken);
        });
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        messages.HasMore = false;
    }

    protected override void GetMessages(string topicName, int partition, FetchOptions options, MessageStream stream, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(10);
        LoadMessagesForPartitionAsync(topicName, partition, options, stream, semaphore, cancellationToken).GetAwaiter().GetResult();
        stream.HasMore = false;
    }

    private async Task LoadMessagesForPartitionAsync(
        string topicName,
        int partition,
        FetchOptions options,
        MessageStream stream,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var partitionDir = Path.Combine(clusterDir, topicName, partition.ToString());
        if (!Directory.Exists(partitionDir))
        {
            return;
        }
        var messageFiles = Directory.GetFiles(partitionDir, "*.klm");
        var textFiles = Directory.GetFiles(partitionDir, "*.txt");
        var allFiles = messageFiles.Concat(textFiles).ToArray();

        var tasks = allFiles.Select(async s =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var message = await CreateMessageAsync(s);
                message.Partition = partition;
                lock (stream.Messages)
                {
                    stream.Messages.Add(message);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to load message {File}", s);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
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
                if (DateTime.TryParse(timeText, out var dt))
                {
                    epochMillis = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
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