using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using FluentAssertions;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;
using KafkaConfig = KafkaLens.Shared.Models.KafkaConfig;

namespace KafkaLens.Core.Tests.Services;

public class KafkaErrorTranslatorTests
{
    [Theory]
    [InlineData(ErrorCode.SaslAuthenticationFailed, "", ConnectionProblemKind.Authentication)]
    [InlineData(ErrorCode.Local_Authentication, "SASL authentication error: Authentication failed during authentication due to invalid credentials with SASL mechanism SCRAM-SHA-256", ConnectionProblemKind.Authentication)]
    [InlineData(ErrorCode.TopicAuthorizationFailed, "", ConnectionProblemKind.Authorization)]
    [InlineData(ErrorCode.ClusterAuthorizationFailed, "", ConnectionProblemKind.Authorization)]
    [InlineData(ErrorCode.Local_Ssl, "", ConnectionProblemKind.Tls)]
    [InlineData(ErrorCode.Local_Transport, "ssl://b:9093/bootstrap: SSL handshake failed: error:0A000086:SSL routines::certificate verify failed", ConnectionProblemKind.Tls)]
    [InlineData(ErrorCode.Local_Transport, "Connection refused", ConnectionProblemKind.Unreachable)]
    [InlineData(ErrorCode.Local_AllBrokersDown, "1/1 brokers are down", ConnectionProblemKind.Unreachable)]
    [InlineData(ErrorCode.UnknownTopicOrPart, "", ConnectionProblemKind.Other)]
    public void Classify_MapsCodesAndReasons(ErrorCode code, string reason, ConnectionProblemKind expected)
    {
        KafkaErrorTranslator.Classify(new Error(code, reason)).Should().Be(expected);
    }

    [Fact]
    public void ClientError_MoreSpecificThanTransportFailure_Wins()
    {
        var thrown = new KafkaException(new Error(ErrorCode.Local_Transport, "Broker transport failure"));
        var client = new Error(ErrorCode.Local_Authentication, "SASL authentication error: invalid credentials");

        KafkaErrorTranslator.Classify(thrown, client).Should().Be(ConnectionProblemKind.Authentication);
        KafkaErrorTranslator.Message(thrown, client).Should().Be(KafkaErrorTranslator.AuthenticationMessage);
    }

    [Fact]
    public void UnknownCause_KeepsOriginalMessage()
    {
        KafkaErrorTranslator.Message(new InvalidOperationException("Something odd"), null).Should().Be("Something odd");
    }
}

public class ConnectionDiagnosticsTests
{
    private static Metadata MetadataWith(int originatingBroker, ErrorCode topicError = ErrorCode.NoError) =>
        new(new List<BrokerMetadata> { new(1, "broker", 9092) },
            new List<TopicMetadata>
            {
                new("orders", new List<PartitionMetadata> { new(0, 1, [1], [1], new Error(ErrorCode.NoError)) }, new Error(topicError))
            }, originatingBroker, originatingBroker == -1 ? "" : "broker");

    [Fact]
    public async Task Validation_ReportsAuthenticationFailure_FromClientErrorCallback()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(_ => throw new KafkaException(new Error(ErrorCode.Local_Transport, "Broker transport failure")));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin, () => Substitute.For<IConsumer<byte[], byte[]>>());
        consumer.RecordClientError(new Error(ErrorCode.Local_Authentication, "SASL authentication error: invalid credentials"));

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.AuthenticationMessage).And.Contain("Broker transport failure");
        result.ErrorDetails.Should().Contain("Local_Authentication");
    }

    [Fact]
    public void Validation_RedactsSecretsFromMessagesAndDetails()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(_ => throw new KafkaException(new Error(ErrorCode.Local_Transport, "failed for pw-xyz")));
        var settings = new KafkaConnectionSettings("broker:9092",
            new ClusterSecurity { Protocol = KafkaSecurityProtocol.SaslSsl, SaslUsername = "u" },
            new Dictionary<string, string> { [ClusterSecretNames.SaslPassword] = "pw-xyz" });
        using var consumer = new ConfluentConsumer(settings, new KafkaConfig(), admin, () => Substitute.For<IConsumer<byte[], byte[]>>());

        var result = consumer.ValidateConnectionWithDetails();

        result.ErrorMessage.Should().NotContain("pw-xyz");
        result.ErrorDetails.Should().NotContain("pw-xyz");
    }

    [Fact]
    public async Task Validation_WaitsForABrokerToAnswerMetadata()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(MetadataWith(-1), MetadataWith(1));
        var native = Substitute.For<IConsumer<byte[], byte[]>>();
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin, () => native);

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        admin.Received(2).GetMetadata(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Validation_NoBrokerAnswers_IsReportedAsUnreachable()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(MetadataWith(-1));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig { AdminMetadataTimeoutMs = 300 }, admin,
            () => Substitute.For<IConsumer<byte[], byte[]>>());

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.UnreachableMessage);
    }

    [Fact]
    public void Topics_MissingAcl_IsActionable()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(MetadataWith(1, ErrorCode.TopicAuthorizationFailed));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin, () => Substitute.For<IConsumer<byte[], byte[]>>());

        var act = () => consumer.GetTopics();

        act.Should().Throw<KafkaAccessException>()
            .Where(e => e.Kind == ConnectionProblemKind.Authorization)
            .WithMessage(KafkaErrorTranslator.AuthorizationMessage + "*");
    }

    [Fact]
    public async Task Validation_MissingAcl_IsActionable()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(MetadataWith(1, ErrorCode.TopicAuthorizationFailed));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin, () => Substitute.For<IConsumer<byte[], byte[]>>());

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.AuthorizationMessage);
    }

    [Fact]
    public async Task SpecificClientError_IsNotReplacedByLaterGenericError()
    {
        var admin = Substitute.For<IAdminClient>();
        admin.GetMetadata(Arg.Any<TimeSpan>()).Returns(_ => throw new KafkaException(new Error(ErrorCode.Local_Transport, "Broker transport failure")));
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), admin, () => Substitute.For<IConsumer<byte[], byte[]>>());
        consumer.RecordClientError(new Error(ErrorCode.Local_Authentication, "SASL authentication error: invalid credentials"));
        consumer.RecordClientError(new Error(ErrorCode.Local_AllBrokersDown, "1/1 brokers are down"));

        var result = await consumer.ValidateConnectionWithDetailsAsync();

        result.ErrorMessage.Should().StartWith(KafkaErrorTranslator.AuthenticationMessage);
    }

    [Fact]
    public void StaleSpecificClientError_IsReplaced()
    {
        using var consumer = new ConfluentConsumer("broker:9092", new KafkaConfig(), Substitute.For<IAdminClient>(),
            () => Substitute.For<IConsumer<byte[], byte[]>>());
        consumer.RecordClientError(new Error(ErrorCode.Local_Authentication, "old"), DateTime.UtcNow.AddMinutes(-5));
        consumer.RecordClientError(new Error(ErrorCode.Local_AllBrokersDown, "1/1 brokers are down"));

        consumer.LastClientError!.Code.Should().Be(ErrorCode.Local_AllBrokersDown);
    }
}
