using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrpcApi.Config;

public class ServiceConfig
{

    public KestrelConfig Kestrel { get; set; } = new();
    public string DatabasePath { get; set; } = "";
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
    }
}