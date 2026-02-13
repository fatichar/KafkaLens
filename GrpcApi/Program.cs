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

builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Any,
            config.Kestrel.EndpointDefaults.HttpPort,
            listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    });

// Additional configuration is required to successfully run gRPC on macOS.
// For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682

// Add services to the container.
var services = builder.Services;
services.AddGrpc();
services.AddSingleton(config);
var clusterRepo = new ClusterInfoRepository(config.DatabasePath);
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