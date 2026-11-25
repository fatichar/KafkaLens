namespace GrpcApi.Config;

public class ServiceConfig
{

    public KestrelConfig Kestrel { get; set; } = new();
    public GrpcFetchConfig GrpcFetch { get; set; } = new();
    public string DatabasePath { get; set; } = "";
}

public class GrpcFetchConfig
{
    public int MaxMessagesPerRequest { get; set; } = 25_000;
    public int MaxConcurrentFetches { get; set; } = 12;
    public int QueueTimeoutMs { get; set; } = 5_000;
    public int FetchTimeoutMs { get; set; } = 120_000;
}

public class KestrelConfig
{
    public EndpointConfig EndpointDefaults { get; set; } = new();

    public class EndpointConfig
    {
        public const int DEFAULT_HTTP_PORT = 80;
        public const int DEFAULT_HTTPS_PORT = 443;
        public int HttpPort { get; set; } = DEFAULT_HTTP_PORT;
        public int HttpsPort { get; set; } = DEFAULT_HTTPS_PORT;

        /// <summary>
        /// PFX/PKCS#12 server certificate. When set, gRPC is also served over TLS on <see cref="HttpsPort"/>.
        /// Its password is read from KAFKALENS_SECRET__SERVER__HTTPS_CERTIFICATE_PASSWORD (or the OS keyring).
        /// </summary>
        public string? CertificatePath { get; set; }
    }
}
