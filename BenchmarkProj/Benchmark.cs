using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Clients;
using KafkaLens.Shared.Models;
using KafkaLens.Core.Services;
using Serilog;
using System.Collections.Generic;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        Console.WriteLine("Setting up benchmark...");
        string clusterDir = Path.Combine(Path.GetTempPath(), "KafkaLensBenchmarkCluster");
        if (Directory.Exists(clusterDir))
        {
            Directory.Delete(clusterDir, true);
        }
        Directory.CreateDirectory(clusterDir);

        string topicName = "test-topic";
        string topicDir = Path.Combine(clusterDir, topicName);
        Directory.CreateDirectory(topicDir);

        int numPartitions = 5;
        int filesPerPartition = 2000;

        long baseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000000;

        for (int p = 0; p < numPartitions; p++)
        {
            string partitionDir = Path.Combine(topicDir, p.ToString());
            Directory.CreateDirectory(partitionDir);

            for (int i = 0; i < filesPerPartition; i++)
            {
                long offset = i;
                long timestamp = baseTime + i * 10;
                string file = Path.Combine(partitionDir, $"{offset}.klm");

                var msg = new Message(timestamp, new Dictionary<string, byte[]>(), new byte[10], new byte[10])
                {
                    Partition = p,
                    Offset = offset
                };

                using var fs = File.OpenWrite(file);
                msg.Serialize(fs);
            }
        }

        Console.WriteLine($"Created {numPartitions} partitions with {filesPerPartition} files each.");

        var consumer = new SavedMessagesConsumerWrapper(clusterDir);

        // Warmup
        var options = new FetchOptions(new FetchPosition(PositionType.Offset, 0), 10);
        var stream = new MessageStream();
        await consumer.PublicGetMessagesAsync(topicName, options, stream, CancellationToken.None);

        Console.WriteLine("Running benchmark for multi-partition GetMessagesAsync...");
        var sw = Stopwatch.StartNew();
        options = new FetchOptions(new FetchPosition(PositionType.Offset, 0), 5000);
        stream = new MessageStream();
        await consumer.PublicGetMessagesAsync(topicName, options, stream, CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"Multi-partition took: {sw.ElapsedMilliseconds} ms");

        Console.WriteLine("Running benchmark for single-partition (LoadMessagesForPartitionAsync) with Timestamp...");
        sw.Restart();
        options = new FetchOptions(new FetchPosition(PositionType.Timestamp, baseTime + 1000 * 10), 1000) { Direction = FetchDirection.Forward };
        stream = new MessageStream();
        await consumer.PublicGetMessagesAsync(topicName, 0, options, stream, CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"Single-partition (Forward from Timestamp) took: {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        options = new FetchOptions(new FetchPosition(PositionType.Timestamp, baseTime + 1500 * 10), 100) { Direction = FetchDirection.Backward };
        stream = new MessageStream();
        await consumer.PublicGetMessagesAsync(topicName, 0, options, stream, CancellationToken.None);
        sw.Stop();
        Console.WriteLine($"Single-partition (Backward from Timestamp) took: {sw.ElapsedMilliseconds} ms");

        Directory.Delete(clusterDir, true);
    }
}

public class SavedMessagesConsumerWrapper : SavedMessagesConsumer
{
    public SavedMessagesConsumerWrapper(string clusterDir) : base(clusterDir) { }

    public Task PublicGetMessagesAsync(string topicName, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
    {
        return GetMessagesAsync(topicName, options, messages, cancellationToken);
    }

    public Task PublicGetMessagesAsync(string topicName, int partition, FetchOptions options, MessageStream messages, CancellationToken cancellationToken)
    {
        return GetMessagesAsync(topicName, partition, options, messages, cancellationToken);
    }
}
