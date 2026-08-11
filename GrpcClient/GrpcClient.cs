using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Serilog;
using FetchOptions = KafkaLens.Shared.Models.FetchOptions;
using FetchPosition = KafkaLens.Grpc.FetchPosition;
using Message = KafkaLens.Shared.Models.Message;
using Topic = KafkaLens.Shared.Models.Topic;

namespace KafkaLens.Clients;

public class GrpcClient : IKafkaLensClient, IDisposable
{
    #region fields

    private readonly string url;
    private readonly object channelLock = new();
    private GrpcChannel? channel;
    private KafkaApi.KafkaApiClient? client;
    public bool CanEditClusters => false;

    #endregion

    #region Constructor
    public GrpcClient(string name, string url)
    {
        Name = name;
        CanSaveMessages = true;
        this.url = url;
    }

    public string Name { get; }
    public bool CanSaveMessages { get; }

    /// <summary>
    /// Lazily creates the channel/client on first use or after a previous failure
    /// invalidated it. As long as calls keep succeeding, the same channel is reused.
    /// </summary>
    private KafkaApi.KafkaApiClient GetClient()
    {
        lock (channelLock)
        {
            if (client == null)
            {
                channel = GrpcChannel.ForAddress(url);
                client = new KafkaApi.KafkaApiClient(channel);
            }
            return client;
        }
    }

    /// <summary>
    /// Tears down the current channel so the next call builds a fresh one instead of
    /// reusing a channel stuck in gRPC's internal reconnect backoff after an outage.
    /// </summary>
    private void InvalidateChannel()
    {
        lock (channelLock)
        {
            channel?.Dispose();
            channel = null;
            client = null;
        }
    }

    public void Dispose()
    {
        InvalidateChannel();
    }

    public async Task<bool> ValidateConnectionAsync(string bootstrapServers)
    {
        try
        {
            var response = await GetClient().ValidateConnectionAsync(new ValidateConnectionRequest
            {
                BootstrapServers = bootstrapServers
            });
            return response.IsConnected;
        }
        catch (RpcException e)
        {
             if (e.Status.StatusCode == StatusCode.Unimplemented)
             {
                 return true;
             }

             InvalidateChannel();
             return false;
        }
    }
    #endregion Constructor

    #region  Create
    public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        try
        {
            var response = await GetClient().AddClusterAsync(new AddClusterRequest
            {
                Name = newCluster.Name,
                BootstrapServers = newCluster.Address
            });

            return ToClusterModel(response);
        }
        catch (RpcException)
        {
            InvalidateChannel();
            throw;
        }
    }
    #endregion Create

    #region Read
    public async Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        try
        {
            var response = await GetClient().GetAllClustersAsync(
                new Empty(),
                null,
                DateTime.UtcNow.AddSeconds(5));
            var clusters = response.Clusters.Select(ToClusterModel).ToList();

            // Set initial connection state to unknown for clusters missing it.
            // MainViewModel will handle the background connectivity check.
            foreach (var cluster in clusters)
            {
                var grpcCluster = response.Clusters.First(rc => rc.Id == cluster.Id);
                if (!grpcCluster.HasIsConnected)
                {
                    cluster.Status = ConnectionState.Unknown;
                }
            }

            return clusters;
        }
        catch (RpcException e)
        {
            Log.Error($"Failed to connect to grpc server: {url}", e);
            InvalidateChannel();
            return new List<KafkaCluster>()
            {
                new KafkaCluster($"grpc-unavailable:{url}", Name, url)
                {
                    Status = ConnectionState.Failed
                }
            };
        }
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
        try
        {
            var response = await GetClient().GetTopicsAsync(new GetTopicsRequest { ClusterId = clusterId }).ResponseAsync;
            return response.Topics.Select(ToTopicModel).ToList();
        }
        catch (RpcException)
        {
            InvalidateChannel();
            throw;
        }
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        Log.Information("Fetching {MessageCount} messages for topic {Topic}", options.Limit, topic);
        var request = new GetTopicMessagesRequest
        {
            ClusterId = clusterId,
            TopicName = topic,
            FetchOptions = ToGrpcFetchOptions(options)
        };
        var response = GetClient().GetTopicMessages(request, cancellationToken: cancellationToken);

        return ToStream(response, cancellationToken, topic, null);
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        Log.Information("Fetching {MessageCount} messages for topic {Topic} partition {Partition}", options.Limit, topic, partition);
        var request = new GetPartitionMessagesRequest
        {
            ClusterId = clusterId,
            TopicName = topic,
            Partition = (uint)partition,
            FetchOptions = ToGrpcFetchOptions(options)
        };
        var response = GetClient().GetPartitionMessages(request, cancellationToken: cancellationToken);
        return ToStream(response, cancellationToken, topic, partition);
    }

    private MessageStream ToStream(global::Grpc.Core.AsyncServerStreamingCall<Grpc.Message> response, CancellationToken cancellationToken, string topic, int? partition)
    {
        var stream = new MessageStream();
        Task.Run(async () =>
        {
            try
            {
                while (await response.ResponseStream.MoveNext(cancellationToken))
                {
                    var message = response.ResponseStream.Current;
                    stream.Messages.Add(ToMessageModel(message));
                }
            }
            catch (RpcException e)
            {
                Log.Error(e, "Error reading stream");
                if (cancellationToken.IsCancellationRequested)
                {
                    stream.SetCanceled();
                }
                else
                {
                    stream.SetError(e);
                    InvalidateChannel();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error reading stream");
                if (e is OperationCanceledException)
                    stream.SetCanceled();
                else
                    stream.SetError(e);
            }
            stream.HasMore = false;

            // Log completion when stream actually finishes
            if (partition.HasValue)
            {
                Log.Information("Fetched {MessageCount} messages for topic {Topic} partition {Partition}", stream.Messages.Count, topic, partition.Value);
            }
            else
            {
                Log.Information("Fetched {MessageCount} messages for topic {Topic}", stream.Messages.Count, topic);
            }
        }, cancellationToken);

        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
    #endregion Read

    #region Update
    public Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        throw new NotImplementedException();
    }
    #endregion Update

    #region Delete
    public async Task RemoveClusterByIdAsync(string clusterId)
    {
        try
        {
            await GetClient().RemoveClusterAsync(new RemoveClusterRequest { ClusterId = clusterId }).ResponseAsync;
        }
        catch (RpcException)
        {
            InvalidateChannel();
            throw;
        }
    }
    #endregion Delete

    #region Convertors
    private static KafkaCluster ToClusterModel(Cluster cluster)
    {
        var model = new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        if (cluster.HasIsConnected)
        {
            model.Status = cluster.IsConnected ? ConnectionState.Connected : ConnectionState.Failed;
        }
        return model;
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
            Offset = message.Offset,
            Partition = message.Partition
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
        if (position.Type == PositionType.Offset)
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