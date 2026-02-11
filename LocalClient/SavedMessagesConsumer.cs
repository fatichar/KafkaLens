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
        var partitionDirs = Directory.GetDirectories(topicDir);
        Array.ForEach(partitionDirs, partitionDir =>
        {
            var partition = int.Parse(Path.GetFileName(partitionDir));
            GetMessages(topicName, partition, options, messages, cancellationToken);
        });
    }

    protected override void GetMessages(string topicName, int partition, FetchOptions options, MessageStream stream, CancellationToken cancellationToken)
    {
        var partitionDir = Path.Combine(clusterDir, topicName, partition.ToString());
        var messageFiles = Directory.GetFiles(partitionDir, "*.klm");
        Array.ForEach(messageFiles, s =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                var message = CreateMessage(s);
                message.Partition = partition;
                stream.Messages.Add(message);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to load message {File}", s);
            }
        });
    }

    private Message CreateMessage(string messageFile)
    {
        using var fs = File.OpenRead(messageFile);
        return Message.Deserialize(fs);
    }

    #endregion Read
}