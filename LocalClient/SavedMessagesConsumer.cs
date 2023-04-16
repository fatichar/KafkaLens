using KafkaLens.Core.Services;
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
    public override List<Topic> GetTopics()
    {
        if (Topics.Count > 0)
        {
            Topics.Clear();
        }
        LoadTopics();
        return Topics.Values.ToList();
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

    public MessageStream GetMessageStream(string topic, int partition, FetchOptions options)
    {
        var messages = new MessageStream();
        Task.Run(() =>
            GetMessages(topic, partition, options, messages));
        return messages;
    }

    public MessageStream GetMessageStream(string topic, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public Task<List<Message>> GetMessagesAsync(string topic, int partition, FetchOptions options)
    {
        var partitionDir = Path.Combine(clusterDir, topic, partition.ToString());
        return null;
    }

    protected override void GetMessages(string topicName, FetchOptions options, MessageStream messages)
    {
        var topicDir = Path.Combine(clusterDir, topicName);
        var partitionDirs = Directory.GetDirectories(topicDir);
        Array.ForEach(partitionDirs, partitionDir =>
        {
            var partition = int.Parse(Path.GetFileName(partitionDir));
            GetMessages(topicName, partition, options, messages);
        });
    }

    protected override void GetMessages(string topicName, int partition, FetchOptions options, MessageStream stream)
    {
        var partitionDir = Path.Combine(clusterDir, topicName, partition.ToString());
        var messageFiles = Directory.GetFiles(partitionDir, "*.klm");
        Array.ForEach(messageFiles, s =>
        {
            var message = CreateMessage(s);
            message.Partition = partition;
            stream.Messages.Add(message);
        });
    }

    private Message CreateMessage(string messageFile)
    {
        var data = File.ReadAllBytes(messageFile);
        var message = new Message(
            0,
            new Dictionary<string, byte[]>(),
            null,
            data)
        {
            Offset = GetOffset(messageFile)
        };
        return message;
    }

    private static long GetOffset(string messageFile)
    {
        var fileName = Path.GetFileNameWithoutExtension(messageFile);
        return long.TryParse(fileName, out var offset) ? offset : -1;
    }

    #endregion Read
}