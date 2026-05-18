using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcApi.Config;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using Models = KafkaLens.Shared.Models;
using Topic = KafkaLens.Grpc.Topic;

namespace GrpcApi.Services;

public class KafkaService(
    ILogger<KafkaService> logger,
    IKafkaLensClient kafkaLensClient,
    GrpcFetchConfig? fetchConfig = null,
    GrpcFetchLimiter? fetchLimiter = null)
    : KafkaApi.KafkaApiBase
{
    #region fields

    private readonly ILogger<KafkaService> logger = logger;
    private readonly GrpcFetchConfig fetchConfig = fetchConfig ?? new GrpcFetchConfig();
    private readonly GrpcFetchLimiter fetchLimiter = fetchLimiter ?? new GrpcFetchLimiter(fetchConfig ?? new GrpcFetchConfig());

    #endregion

    #region Constructor

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
        var options = ToFetchOptions(request.FetchOptions);
        await StreamFetchAsync(
            request.ClusterId,
            request.TopicName,
            null,
            options,
            responseStream,
            context);
    }

    public override async Task GetPartitionMessages(
        GetPartitionMessagesRequest request,
        IServerStreamWriter<Message> responseStream,
        ServerCallContext context)
    {
        var options = ToFetchOptions(request.FetchOptions);
        await StreamFetchAsync(
            request.ClusterId,
            request.TopicName,
            (int)request.Partition,
            options,
            responseStream,
            context);
    }

    private async Task StreamFetchAsync(
        string clusterId,
        string topic,
        int? partition,
        Models.FetchOptions options,
        IServerStreamWriter<Message> responseStream,
        ServerCallContext context)
    {
        ValidateFetchRequest(options);

        var queueTimeout = TimeSpan.FromMilliseconds(Math.Max(0, fetchConfig.QueueTimeoutMs));
        var acquired = false;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, fetchConfig.FetchTimeoutMs)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, timeoutCts.Token);

        try
        {
            acquired = await fetchLimiter.TryAcquireAsync(queueTimeout, context.CancellationToken);
            if (!acquired)
            {
                throw new RpcException(new Status(
                    StatusCode.ResourceExhausted,
                    "The gRPC server is busy with other fetch requests. Try again later."));
            }

            var messages = CreateMessageEnumerable(clusterId, topic, partition, options, linkedCts.Token);
            var writtenCount = 0;
            await foreach (var message in messages.WithCancellation(linkedCts.Token))
            {
                await responseStream.WriteAsync(ToMessageResponse(message));
                writtenCount++;
            }

            logger.LogInformation(
                "Streamed {MessageCount} messages for topic {Topic} partition {Partition}",
                writtenCount,
                topic,
                partition);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "The fetch request was cancelled."));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.DeadlineExceeded, "The fetch request exceeded the server timeout."));
        }
        catch (RpcException)
        {
            throw;
        }
        catch (ArgumentException e)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, e.Message));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled error while streaming messages for topic {Topic} partition {Partition}", topic, partition);
            throw new RpcException(new Status(StatusCode.Internal, "The fetch request failed."));
        }
        finally
        {
            await linkedCts.CancelAsync();
            if (acquired)
            {
                fetchLimiter.Release();
            }
        }
    }

    private void ValidateFetchRequest(Models.FetchOptions options)
    {
        if (options.Limit <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Fetch count must be greater than zero."));
        }

        if (options.Limit > fetchConfig.MaxMessagesPerRequest)
        {
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                $"Fetch count {options.Limit} exceeds the server limit of {fetchConfig.MaxMessagesPerRequest}."));
        }
    }

    private IAsyncEnumerable<Models.Message> CreateMessageEnumerable(
        string clusterId,
        string topic,
        int? partition,
        Models.FetchOptions options,
        CancellationToken cancellationToken)
    {
        if (kafkaLensClient is IStreamingKafkaLensClient streamingClient)
        {
            return partition.HasValue
                ? streamingClient.StreamMessagesAsync(clusterId, topic, partition.Value, options, cancellationToken)
                : streamingClient.StreamMessagesAsync(clusterId, topic, options, cancellationToken);
        }

        return CreateFallbackEnumerable(clusterId, topic, partition, options, cancellationToken);
    }

    private async IAsyncEnumerable<Models.Message> CreateFallbackEnumerable(
        string clusterId,
        string topic,
        int? partition,
        Models.FetchOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var messages = partition.HasValue
            ? await kafkaLensClient.GetMessagesAsync(clusterId, topic, partition.Value, options, cancellationToken)
            : await kafkaLensClient.GetMessagesAsync(clusterId, topic, options, cancellationToken);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return message;
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
            IsConnected = cluster.Status == Models.ConnectionState.Connected
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
        var options = new Models.FetchOptions(ToFetchPosition(request.Start), (int)request.MaxCount);
        if (request.Backward)
        {
            options.Direction = Models.FetchDirection.Backward;
        }
        return options;
    }

    private Models.FetchPosition ToFetchPosition(FetchPosition request)
    {
        if (request.PositionCase == FetchPosition.PositionOneofCase.Offset)
        {
            return new Models.FetchPosition(Models.PositionType.Offset, (long)request.Offset);
        }
        return new Models.FetchPosition(Models.PositionType.Timestamp, request.Timestamp.ToDateTimeOffset().ToUnixTimeMilliseconds());
    }

    #endregion Convertors
}
