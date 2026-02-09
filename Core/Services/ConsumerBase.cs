using System.Threading;
﻿using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.Core.Services;

public abstract class ConsumerBase : IKafkaConsumer
{
    protected readonly TimeSpan MaxRefreshInterval = TimeSpan.FromMinutes(60);
    protected Dictionary<string, Topic> Topics { get; set; } = new();

    protected DateTime LastRefreshTime { get; set; } = DateTime.Now;

    public virtual List<Topic> GetTopics()
    {
        // if topics were loaded in the last 5 minutes, return them
        // otherwise, refresh the topics
        if (!RecentlyRefreshed())
        {
            Topics.Clear();
        }
        if (Topics.Count != 0)
        {
            return Topics.Values.ToList();
        }
        try
        {
            LoadTopics();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw new Exception("Failed to load topics", e);
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
        topics.ForEach(topic => Topics.Add(topic.Name, topic));
        Log.Information("Loaded {TopicsCount} topics", topics.Count);
    }

    protected abstract List<Topic> FetchTopics();

    public MessageStream GetMessageStream(string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        Task.Run(() => GetMessages(topic, options, messages, cancellationToken), cancellationToken);
        return messages;
    }

    public MessageStream GetMessageStream(string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        Task.Run(() => GetMessages(topic, partition, options, messages, cancellationToken), cancellationToken);
        return messages;
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, options, messages, cancellationToken), cancellationToken);
        return messages.Messages.ToList();
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, partition, options, messages, cancellationToken), cancellationToken);
        return messages.Messages.ToList();
    }

    protected abstract void GetMessages(string topicName, FetchOptions options, MessageStream messages, CancellationToken cancellationToken);

    protected abstract void GetMessages(string topicName, int partition, FetchOptions options,
        MessageStream messages, CancellationToken cancellationToken);

    protected Topic ValidateTopic(string topicName)
    {
        if (Topics.Count == 0)
        {
            LoadTopics();
        }
        if (Topics.TryGetValue(topicName, out var topic))
        {
            return topic;
        }
        throw new Exception($"Topic {topicName} does not exist.");
    }
}