using System.Text;
using Serilog;
using System.IO;
using System.Threading;

namespace KafkaLens.ViewModels.Services;

public class MessageSaver(IClientFactory clientFactory) : IMessageSaver
{
    private static readonly string SaveMessagesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KafkaLens",
        "SavedMessages");

    public bool CanSaveMessages(string clusterId)
    {
        var client = clientFactory.GetClient(clusterId);
        return client?.CanSaveMessages ?? false;
    }

    public async Task SaveAsync(IList<MessageViewModel> messages, string clusterName, bool formatted)
    {
        if (messages.Count == 0)
            return;

        await Task.Run(() => SaveAllInternal(messages, clusterName, formatted));
    }

    private void SaveAllInternal(IList<MessageViewModel> messages, string clusterName, bool formatted)
    {
        Log.Information("Saving {Count} messages for cluster {ClusterName}", messages.Count, clusterName);

        var baseDir = Path.Join(SaveMessagesDir, clusterName);
        var dirCache = new Dictionary<(string Topic, int Partition), string>();

        foreach (var m in messages)
        {
            var key = (m.Topic, m.Partition);
            if (!dirCache.TryGetValue(key, out var dir))
            {
                dir = Path.Join(baseDir, m.Topic, m.Partition.ToString());
                Directory.CreateDirectory(dir);
                dirCache[key] = dir;
            }
        }

        var throttler = new SemaphoreSlim(8); // tune 4–12

        var tasks = messages.Select(async msg =>
        {
            var dir = dirCache[(msg.Topic, msg.Partition)];

            await throttler.WaitAsync().ConfigureAwait(false);
            try
            {
                await SaveSingleAsync(dir, msg, formatted)
                    .ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });

        Task.WhenAll(tasks)
            .ContinueWith(t => { Log.Information("Saved {Count} messages", messages.Count); })
            .ConfigureAwait(false);
    }

    private async Task SaveSingleAsync(
        string dir,
        MessageViewModel msg,
        bool formatted)
    {
        var filePath = Path.Join(dir, msg.Offset + GetExtension(formatted));

        if (!formatted)
        {
            await SaveRaw(msg, filePath);
        }
        else
        {
            await SaveFormatted(msg, filePath);
        }
    }

    private static async Task SaveFormatted(MessageViewModel msg, string filePath)
    {
        msg.PrettyFormat();

        var sb = new StringBuilder(512);

        sb.AppendLine($"Key: {msg.Key}");
        sb.AppendLine($"Timestamp: {msg.Timestamp}");
        sb.AppendLine($"Partition: {msg.Partition}");
        sb.AppendLine($"Offset: {msg.Offset}");

        if (msg.Message.Headers.Count > 0)
        {
            sb.AppendLine("Headers:");
            foreach (var header in msg.Message.Headers)
            {
                sb.Append("  ")
                    .Append(header.Key)
                    .Append(": ")
                    .AppendLine(Encoding.UTF8.GetString(header.Value));
            }
        }

        sb.AppendLine();
        sb.AppendLine(msg.DisplayText);

        await File.WriteAllTextAsync(filePath, sb.ToString())
            .ConfigureAwait(false);
    }

    private static async Task SaveRaw(MessageViewModel msg, string filePath)
    {
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,   // larger buffer for binary
            useAsync: true);

        msg.Message.Serialize(fileStream);
        await fileStream.FlushAsync().ConfigureAwait(false);
    }

    private static string GetExtension(bool formatted)
    {
        return formatted ? ".txt" : ".klm";
    }
}