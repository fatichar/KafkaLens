using GrpcApi.Config;
using GrpcApi.Services;
using KafkaLens.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;
using KafkaLens.Core.Services;
using KafkaLens.Shared.DataAccess;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .AddJsonFile("appsettings.json")
    .Build();

var config = new ServiceConfig();
configuration.Bind(config);

var builder = WebApplication.CreateBuilder(args);

// Credentials: KAFKALENS_SECRET__* environment variables first, then the OS keyring if one is available.
var secretStore = KafkaLens.Shared.Security.SecretStoreFactory.CreateForServer();

builder.WebHost.ConfigureKestrel(options =>
    {
        var endpoints = config.Kestrel.EndpointDefaults;
        options.Listen(IPAddress.Any,
            endpoints.HttpPort,
            listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        if (!string.IsNullOrWhiteSpace(endpoints.CertificatePath))
        {
            var password = secretStore.Get("server/https.certificate.password");
            options.Listen(IPAddress.Any, endpoints.HttpsPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(endpoints.CertificatePath, password);
            });
        }
    });

// Additional configuration is required to successfully run gRPC on macOS.
// For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682

// Add services to the container.
var services = builder.Services;
services.AddGrpc();
services.AddSingleton(config);
services.AddSingleton(config.GrpcFetch);
services.AddSingleton(new KafkaLens.Shared.Models.KafkaConfig());
services.AddSingleton<GrpcFetchLimiter>();
services.AddSingleton(secretStore);
var clusterRepo = new ClusterInfoRepository(config.DatabasePath, secretStore);
services.AddSingleton<IClusterInfoRepository>(clusterRepo);
services.AddSingleton<IKafkaLensClient, SharedClient>();
services.AddSingleton<ConsumerFactory>();


var app = builder.Build();

using var scope = app.Services.CreateScope();
var serviceProvider = scope.ServiceProvider;
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

// Configure the HTTP request pipeline.
app.MapGrpcService<KafkaService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. " +
        "To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
