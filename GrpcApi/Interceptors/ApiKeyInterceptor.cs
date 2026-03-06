using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using GrpcApi.Config;

namespace GrpcApi.Interceptors;

public class ApiKeyInterceptor : Interceptor
{
    private readonly string _apiKey;

    public ApiKeyInterceptor(ServiceConfig config)
    {
        _apiKey = config.ApiKey;
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        CheckAuth(context);
        return continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        CheckAuth(context);
        return continuation(requestStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        CheckAuth(context);
        return continuation(request, responseStream, context);
    }

    public override Task<TResponse> DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        CheckAuth(context);
        return continuation(requestStream, responseStream, context);
    }

    private void CheckAuth(ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new RpcException(new Status(StatusCode.Internal, "Server API Key is not configured."));
        }

        var apiKeyHeader = context.RequestHeaders.Get("x-api-key")?.Value;
        if (apiKeyHeader == null || !SafeEquals(apiKeyHeader, _apiKey))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API Key"));
        }
    }

    private static bool SafeEquals(string a, string b)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}
