using System.Collections.Concurrent;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.Core.Services;

public abstract class ConsumerBase : IKafkaConsumer
{
    protected readonly TimeSpan MaxRefreshInterval = TimeSpan.FromMinutes(60);
    protected ConcurrentDictionary<string, Topic> Topics { get; set; } = new();
    private readonly object topicsLock = new();

    protected DateTime LastRefreshTime { get; set; } = DateTime.Now;

    public abstract bool ValidateConnection();

    public virtual List<Topic> GetTopics()
    {
        // if topics were loaded in the last 60 minutes, return them
        // otherwise, refresh the topics
        if (!RecentlyRefreshed() || Topics.IsEmpty)
        {
            lock (topicsLock)
            {
                if (!RecentlyRefreshed() || Topics.IsEmpty)
                {
                    try
                    {
                        LoadTopics();
                        LastRefreshTime = DateTime.Now;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw new Exception("Failed to load topics", e);
                    }
                }
            }
        }

        return Topics.Values.ToList();
    }

    private bool RecentlyRefreshed()
    {
        return (DateTime.Now - LastRefreshTime) < MaxRefreshInterval;
    }

    private void LoadTopics()
    {
        Log.Information("Loading topics...");
        var topics = FetchTopics();
        var duplicatesCount = 0;

        var newTopics = new ConcurrentDictionary<string, Topic>();
        foreach (var topic in topics)
        {
            if (!newTopics.TryAdd(topic.Name, topic))
            {
                duplicatesCount++;
                Log.Warning("Duplicate topic name encountered while loading topics: {TopicName}", topic.Name);
            }
        }

        Topics = newTopics;

        if (duplicatesCount > 0)
        {
            Log.Warning("Loaded topics with {DuplicatesCount} duplicate names filtered out", duplicatesCount);
        }
        Log.Information("Loaded {TopicsCount} topics", Topics.Count);
    }

    protected abstract List<Topic> FetchTopics();

    public MessageStream GetMessageStream(string topic, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Fetching {MessageCount} messages for topic {Topic}", options.Limit, topic);
                await GetMessagesAsync(topic, options, messages, cancellationToken);
                Log.Information("Fetched {MessageCount} messages for topic {Topic}", messages.Messages.Count, topic);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error fetching messages for topic {Topic}", topic);
            }
            finally
            {
                messages.HasMore = false;
            }
        }, cancellationToken);
        return messages;
    }

    public MessageStream GetMessageStream(string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Fetching {MessageCount} messages for topic {Topic} partition {Partition}", options.Limit,
                    topic, partition);
                await GetMessagesAsync(topic, partition, options, messages, cancellationToken);
                Log.Information("Fetched {MessageCount} messages for topic {Topic} partition {Partition}",
                    messages.Messages.Count, topic, partition);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error fetching messages for topic {Topic} partition {Partition}", topic, partition);
            }
            finally
            {
                messages.HasMore = false;
            }
        }, cancellationToken);
        return messages;
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        await GetMessagesAsync(topic, options, messages, cancellationToken);
        return messages.Messages.ToList();
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        await GetMessagesAsync(topic, partition, options, messages, cancellationToken);
        return messages.Messages.ToList();
    }

    protected abstract Task GetMessagesAsync(string topicName, FetchOptions options, MessageStream messages,
        CancellationToken cancellationToken);

    protected abstract Task GetMessagesAsync(string topicName, int partition, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken);

    protected Topic ValidateTopic(string topicName)
    {
        if (Topics.IsEmpty || !Topics.ContainsKey(topicName))
        {
            lock (topicsLock)
            {
                if (Topics.IsEmpty || !Topics.ContainsKey(topicName))
                {
                    LoadTopics();
                    LastRefreshTime = DateTime.Now;
                }
            }
        }

        if (Topics.TryGetValue(topicName, out var topic))
        {
            return topic;
        }

        throw new Exception($"Topic {topicName} does not exist.");
    }

    public virtual void Dispose()
    {
    }
}