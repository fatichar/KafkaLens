using Confluent.Kafka;

namespace KafkaLens.Core.Services;

public enum ConnectionProblemKind
{
    Other,
    Unreachable,
    Authentication,
    Authorization,
    Tls
}

/// <summary>
/// A connection failure with a known, user-actionable cause (authentication, authorization or TLS).
/// The message is safe to show: it never contains secrets.
/// </summary>
public sealed class KafkaAccessException(ConnectionProblemKind kind, string message, Exception inner)
    : InvalidOperationException(message, inner)
{
    public ConnectionProblemKind Kind { get; } = kind;
}

/// <summary>
/// Turns librdkafka errors into messages that tell the user what to check. librdkafka often reports
/// only "Broker transport failure" to the caller while the real cause (failed SASL login, untrusted
/// certificate) arrives on the client's error callback, so both are considered.
/// </summary>
public static class KafkaErrorTranslator
{
    public const string AuthenticationMessage =
        "Authentication failed: check the user name, password and SASL mechanism.";
    public const string AuthorizationMessage =
        "Not authorized: the credentials are valid but lack the required ACLs (Describe on the cluster and topics, Read on topics).";
    public const string TlsMessage =
        "TLS handshake failed: the broker certificate is not trusted or does not match the host name. " +
        "Check the CA certificate, the bootstrap host name and the security protocol.";
    public const string UnreachableMessage =
        "Cannot reach the brokers: check the bootstrap address, network/VPN, and that the security protocol " +
        "matches the listener (e.g. TLS vs. plaintext).";

    public static ConnectionProblemKind Classify(Error? error)
    {
        if (error == null || !error.IsError) return ConnectionProblemKind.Other;
        var reason = error.Reason ?? "";
        switch (error.Code)
        {
            case ErrorCode.SaslAuthenticationFailed:
            case ErrorCode.Local_Authentication:
                return ConnectionProblemKind.Authentication;
            case ErrorCode.TopicAuthorizationFailed:
            case ErrorCode.GroupAuthorizationFailed:
            case ErrorCode.ClusterAuthorizationFailed:
            case ErrorCode.TransactionalIdAuthorizationFailed:
                return ConnectionProblemKind.Authorization;
            case ErrorCode.Local_Ssl:
                return ConnectionProblemKind.Tls;
        }

        if (Contains(reason, "SASL authentication", "Authentication failed", "invalid credentials", "Invalid username or password"))
            return ConnectionProblemKind.Authentication;
        if (Contains(reason, "SSL handshake", "certificate verify failed", "SSL routines", "ssl.ca", "Certificate", "hostname"))
            return ConnectionProblemKind.Tls;
        if (Contains(reason, "Authorization", "not authorized"))
            return ConnectionProblemKind.Authorization;
        if (error.Code is ErrorCode.Local_Transport or ErrorCode.Local_AllBrokersDown or ErrorCode.Local_TimedOut
            or ErrorCode.Local_Resolve or ErrorCode.RequestTimedOut)
            return ConnectionProblemKind.Unreachable;
        return ConnectionProblemKind.Other;
    }

    /// <summary>
    /// Classifies a failure using the thrown exception and the last error the client reported.
    /// The more specific cause wins (authentication/TLS over a generic transport failure).
    /// </summary>
    public static ConnectionProblemKind Classify(Exception? exception, Error? lastClientError)
    {
        if (exception is KafkaAccessException access) return access.Kind;
        var fromException = exception is KafkaException ke ? Classify(ke.Error)
            : exception?.InnerException is KafkaException inner ? Classify(inner.Error)
            : exception is TimeoutException ? ConnectionProblemKind.Unreachable
            : ConnectionProblemKind.Other;
        var fromClient = Classify(lastClientError);
        return Specificity(fromClient) > Specificity(fromException) ? fromClient : fromException;
    }

    public static string? Describe(ConnectionProblemKind kind) => kind switch
    {
        ConnectionProblemKind.Authentication => AuthenticationMessage,
        ConnectionProblemKind.Authorization => AuthorizationMessage,
        ConnectionProblemKind.Tls => TlsMessage,
        ConnectionProblemKind.Unreachable => UnreachableMessage,
        _ => null
    };

    /// <summary>User-facing message: an actionable explanation when the cause is known, else the original message.</summary>
    public static string Message(Exception exception, Error? lastClientError) =>
        Describe(Classify(exception, lastClientError)) ?? exception.Message;

    /// <summary>How specific a cause is: authentication/authorization/TLS (2) &gt; unreachable (1) &gt; other (0).</summary>
    public static int Specificity(ConnectionProblemKind kind) => kind switch
    {
        ConnectionProblemKind.Authentication or ConnectionProblemKind.Tls or ConnectionProblemKind.Authorization => 2,
        ConnectionProblemKind.Unreachable => 1,
        _ => 0
    };

    private static bool Contains(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}
