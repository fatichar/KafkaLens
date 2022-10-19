using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using FetchOptions = KafkaLens.Shared.Models.FetchOptions;
using FetchPosition = KafkaLens.Grpc.FetchPosition;
using Message = KafkaLens.Shared.Models.Message;
using Topic = KafkaLens.Shared.Models.Topic;

namespace KafkaLens.Clients;

public class GrpcClient : IKafkaLensClient
{
    private readonly KafkaApi.KafkaApiClient client;
    private string url;

    public GrpcClient(string url)
    {
        this.url = url;
        var channel = GrpcChannel.ForAddress(url);
        client = new KafkaApi.KafkaApiClient(channel);
    }
    
    public Task<bool> ValidateConnectionAsync(string bootstrapServers)
    {
        throw new NotImplementedException();
    }

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        var response = await client.GetAllClustersAsync(new Empty()).ResponseAsync;
        return response.Clusters.Select(ToClusterModel);
    }

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        throw new NotImplementedException();
    }

    public Task<KafkaCluster> GetClusterByNameAsync(string name)
    {
        throw new NotImplementedException();
    }

    public async Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        var response = await client.GetTopicsAsync(new GetTopicsRequest { ClusterId = clusterId }).ResponseAsync;
        return response.Topics.Select(ToTopicModel).ToList();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options)
    {
        GetTopicMessagesRequest request = new GetTopicMessagesRequest
        {
            ClusterId = clusterId,
            TopicName = topic,
            FetchOptions = ToGrpcFetchOptions(options)
        };
        var response = client.GetTopicMessages(request);
        
        var stream = new MessageStream();
        Task.Run(() =>
        {
            while (response.ResponseStream.MoveNext(CancellationToken.None).Result)
            {
                var message = response.ResponseStream.Current;
                stream.Messages.Add(ToMessageModel(message));
            }
            stream.HasMore = false;
        });

        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public async Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options)
    {
        var response = await client.GetPartitionMessagesAsync(new GetPartitionMessagesRequest()
        {
            ClusterId = clusterId,
            TopicName = topic,
            Partition = (uint) partition,
            FetchOptions = ToGrpcFetchOptions(options)
        }).ResponseAsync;
        
        return response.Messages.Select(ToMessageModel).ToList();
    }

    public Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        throw new NotImplementedException();
    }

    public Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId)
    {
        throw new NotImplementedException();
    }

    #region Convertors
    private static KafkaCluster ToClusterModel(Cluster cluster)
    {
        return new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
    }
    
    private static Topic ToTopicModel(Grpc.Topic topic)
    {
        return new Topic(topic.Name, (int)topic.PartitionCount);
    }
    
    private static Message ToMessageModel(Grpc.Message message)
    {
        return new Message(message.Timestamp.ToDateTimeOffset().ToUnixTimeMilliseconds(), 
            new Dictionary<string, byte[]>(),
            message.Key.ToByteArray(), 
            message.Value.ToByteArray())
        {
            Offset = (long)message.Offset
        };
    }

    private static Grpc.FetchOptions ToGrpcFetchOptions(FetchOptions options)
    {
        return new Grpc.FetchOptions()
        {
            Start = ToGrpcFetchPosition(options.Start),
            MaxCount = (uint)options.Limit,
        };
    }

    private static FetchPosition ToGrpcFetchPosition(Shared.Models.FetchPosition position)
    {
        if (position.Type == PositionType.OFFSET)
        {
            return new FetchPosition()
            {
                Offset = (ulong)position.Offset
            };
        }
        else
        {
            return new FetchPosition()
            {
                Timestamp = ToGrpcTimestamp(position.Timestamp)
            };
        }
    }

    private static Timestamp ToGrpcTimestamp(long milliseconds)
    {
        return new Timestamp
        {
            Seconds = milliseconds / 1000, 
            Nanos = (int)((milliseconds % 1000) * 1000000)
        };
    }
    #endregion Convertors
}