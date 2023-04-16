using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.Core.Services;

public abstract class ConsumerBase : IKafkaConsumer
{
    protected Dictionary<string, Topic> Topics { get; set; } = new();

    public virtual List<Topic> GetTopics()
    {
        if (Topics.Count == 0)
        {
            try
            {
                LoadTopics();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw new Exception("Failed to load topics", e);
            }
        }
        return Topics.Values.ToList();
    }

    protected void LoadTopics()
    {
        Log.Information("Loading topics...");
        var topics = FetchTopics();
        topics.ForEach(topic => Topics.Add(topic.Name, topic));
        Log.Information("Loaded {TopicsCount} topics", topics.Count);
    }

    protected abstract List<Topic> FetchTopics();

    public MessageStream GetMessageStream(string topic, FetchOptions options)
    {
        var messages = new MessageStream();
        Task.Run(() => GetMessages(topic, options, messages));
        return messages;
    }

    public MessageStream GetMessageStream(string topic, int partition, FetchOptions options)
    {
        var messages = new MessageStream();
        Task.Run(() => GetMessages(topic, partition, options, messages));
        return messages;
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, FetchOptions options)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, options, messages));
        return messages.Messages.ToList();
    }

    public async Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options)
    {
        var messages = new MessageStream();
        await Task.Run(() => GetMessages(topic, partition, options, messages));
        return messages.Messages.ToList();
    }

    protected abstract void GetMessages(string topicName, FetchOptions options, MessageStream messages);

    protected abstract void GetMessages(string topicName, int partition, FetchOptions options,
        MessageStream messages);

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