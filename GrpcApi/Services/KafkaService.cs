using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using Models = KafkaLens.Shared.Models;
using Topic = KafkaLens.Grpc.Topic;

namespace GrpcApi.Services;

public class KafkaService : KafkaApi.KafkaApiBase
{
    #region fields

    private readonly ILogger<KafkaService> _logger;
    private readonly IKafkaLensClient kafkaLensClient;

    #endregion

    #region Constructor
    public KafkaService(ILogger<KafkaService> logger, IKafkaLensClient kafkaLensClient)
    {
        _logger = logger;
        this.kafkaLensClient = kafkaLensClient;
    }
    #endregion Constructor

    #region  Create
    public override async Task<Cluster> AddCluster(AddClusterRequest request, ServerCallContext context)
    {
        var cluster = await kafkaLensClient.AddAsync(new Models.NewKafkaCluster(request.Name, request.BootstrapServers));
        return ToClusterResponse(cluster);
    }

    public override async Task<ValidateConnectionResponse> ValidateConnection(ValidateConnectionRequest request, ServerCallContext context)
    {
        var isConnected = await kafkaLensClient.ValidateConnectionAsync(request.BootstrapServers);
        return new ValidateConnectionResponse
        {
            IsConnected = isConnected,
            Message = isConnected ? "Connected" : "Failed to connect"
        };
    }
    #endregion Create

    #region Read
    public override async Task<GetClustersResponse> GetAllClusters(Empty request, ServerCallContext context)
    {
        var allClusters = await kafkaLensClient.GetAllClustersAsync();
        var response = new GetClustersResponse();
        response.Clusters.AddRange(allClusters.Select(ToClusterResponse));

        return response;
    }

    public override async Task<GetTopicsResponse> GetTopics(GetTopicsRequest request, ServerCallContext context)
    {
        var topics = await kafkaLensClient.GetTopicsAsync(request.ClusterId);
        var response = new GetTopicsResponse();
        response.Topics.AddRange(topics.Select(ToTopicResponse));

        return response;
    }

    public override async Task GetTopicMessages(
        GetTopicMessagesRequest request,
        IServerStreamWriter<Message> responseStream,
        ServerCallContext context)
    {
        var messagesStream = kafkaLensClient.GetMessageStream(request.ClusterId, request.TopicName, ToFetchOptions(request.FetchOptions));

        var writtenCount = 0;
        while (messagesStream.HasMore)
        {
            writtenCount = await WriteMessagesAsync(responseStream, messagesStream, writtenCount);
            await Task.Delay(100);
        }
        if (writtenCount < messagesStream.Messages.Count)
        {
            await WriteMessagesAsync(responseStream, messagesStream, writtenCount);
        }
    }

    private static async Task<int> WriteMessagesAsync(IServerStreamWriter<Message> responseStream,
        Models.MessageStream messagesStream,
        int writtenCount)
    {
        for (; writtenCount < messagesStream.Messages.Count; ++writtenCount)
        {
            var grpcMessage = ToMessageResponse(messagesStream.Messages[writtenCount]);
            await responseStream.WriteAsync(grpcMessage);
        }
        return writtenCount;
    }

    public override async Task GetPartitionMessages(
        GetPartitionMessagesRequest request,
        IServerStreamWriter<Message> responseStream,
        ServerCallContext context)
    {
        var messagesStream = kafkaLensClient.GetMessageStream(request.ClusterId, request.TopicName, (int) request.Partition, ToFetchOptions(request.FetchOptions));

        var writtenCount = 0;
        while (messagesStream.HasMore)
        {
            writtenCount = await WriteMessagesAsync(responseStream, messagesStream, writtenCount);
            await Task.Delay(100);
        }
        if (writtenCount < messagesStream.Messages.Count)
        {
            await WriteMessagesAsync(responseStream, messagesStream, writtenCount);
        }
    }
    #endregion Read

    #region Update
    #endregion Update

    #region Delete
    public override async Task<Empty> RemoveCluster(RemoveClusterRequest request, ServerCallContext context)
    {
        await kafkaLensClient.RemoveClusterByIdAsync(request.ClusterId);
        return new Empty();
    }
    #endregion Delete

    #region Convertors

    private static Cluster ToClusterResponse(Models.KafkaCluster cluster)
    {
        return new()
        {
            Id = cluster.Id,
            Name = cluster.Name,
            BootstrapServers = cluster.Address,
            IsConnected = cluster.IsConnected
        };
    }

    private static Topic ToTopicResponse(Models.Topic topic)
    {
        return new Topic
        {
            Name = topic.Name,
            PartitionCount = (uint)topic.PartitionCount
        };
    }

    private static Message ToMessageResponse(Models.Message message)
    {
        var response = new Message
        {
            Key = message.Key == null ? ByteString.Empty : ByteString.CopyFrom(message.Key),
            Value = message.Value == null ? ByteString.Empty : ByteString.CopyFrom(message.Value),
            Offset = message.Offset,
            Partition = message.Partition,
            Timestamp = ToGrpcTimestamp(message.EpochMillis)
        };
        response.Headers.Add(CreateHeaders(message.Headers));
        return response;
    }

    private static MapField<string, ByteString> CreateHeaders(Dictionary<string,byte[]>? headers)
    {
        var map = new MapField<string, ByteString>();
        if (headers == null)
        {
            return map;
        }
        foreach (var header in headers)
        {
            map.Add(header.Key, ByteString.CopyFrom(header.Value));
        }
        return map;
    }

    private static Timestamp ToGrpcTimestamp(long milliseconds)
    {
        return new Timestamp
        {
            Seconds = milliseconds / 1000,
            Nanos = (int)((milliseconds % 1000) * 1000000)
        };
    }

    private Models.FetchOptions ToFetchOptions(FetchOptions request)
    {
        return new Models.FetchOptions(ToFetchPosition(request.Start), (int)request.MaxCount);
        // return new Models.FetchOptions(ToFetchPosition(request.Start), ToFetchPosition(request.End));
    }

    private Models.FetchPosition ToFetchPosition(FetchPosition request)
    {
        if (request.PositionCase == FetchPosition.PositionOneofCase.Offset)
        {
            return new Models.FetchPosition(Models.PositionType.OFFSET, (long)request.Offset);
        }
        return new Models.FetchPosition(Models.PositionType.TIMESTAMP, request.Timestamp.ToDateTimeOffset().ToUnixTimeMilliseconds());
    }

    #endregion Convertors
}