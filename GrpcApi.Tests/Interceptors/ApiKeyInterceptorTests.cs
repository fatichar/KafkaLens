using Grpc.Core;
using GrpcApi.Config;
using GrpcApi.Interceptors;
using NSubstitute;
using Xunit;
using FluentAssertions;

namespace GrpcLens.GrpcApi.Tests.Interceptors;

public class ApiKeyInterceptorTests
{
    private readonly ServiceConfig _config;
    private const string ApiKey = "TestApiKey";

    public ApiKeyInterceptorTests()
    {
        _config = new ServiceConfig { ApiKey = ApiKey };
    }

    [Fact]
    public async Task UnaryServerHandler_ValidApiKey_ShouldCallContinuation()
    {
        // Arrange
        var interceptor = new ApiKeyInterceptor(_config);
        var metadata = new Metadata { { "x-api-key", ApiKey } };
        var context = Substitute.For<ServerCallContext>();
        context.RequestHeaders.Returns(metadata);

        bool continuationCalled = false;
        Task<string> Continuation(string request, ServerCallContext context)
        {
            continuationCalled = true;
            return Task.FromResult("response");
        }

        // Act
        await interceptor.UnaryServerHandler("request", context, Continuation);

        // Assert
        continuationCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UnaryServerHandler_InvalidApiKey_ShouldThrowUnauthenticated()
    {
        // Arrange
        var interceptor = new ApiKeyInterceptor(_config);
        var metadata = new Metadata { { "x-api-key", "wrong-key" } };
        var context = Substitute.For<ServerCallContext>();
        context.RequestHeaders.Returns(metadata);

        // Act
        Func<Task> act = () => interceptor.UnaryServerHandler("request", context,
            (req, ctx) => Task.FromResult("response"));

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task UnaryServerHandler_MissingApiKey_ShouldThrowUnauthenticated()
    {
        // Arrange
        var interceptor = new ApiKeyInterceptor(_config);
        var metadata = new Metadata();
        var context = Substitute.For<ServerCallContext>();
        context.RequestHeaders.Returns(metadata);

        // Act
        Func<Task> act = () => interceptor.UnaryServerHandler("request", context,
            (req, ctx) => Task.FromResult("response"));

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task UnaryServerHandler_UnconfiguredApiKey_ShouldThrowInternal()
    {
        // Arrange
        _config.ApiKey = "";
        var interceptor = new ApiKeyInterceptor(_config);
        var metadata = new Metadata { { "x-api-key", "any-key" } };
        var context = Substitute.For<ServerCallContext>();
        context.RequestHeaders.Returns(metadata);

        // Act
        Func<Task> act = () => interceptor.UnaryServerHandler("request", context,
            (req, ctx) => Task.FromResult("response"));

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.Internal);
    }
}
