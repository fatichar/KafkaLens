using Confluent.Kafka;
using GrpcApi.Config;
using GrpcApi.Services;
using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using KafkaLens.Shared;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

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
builder.Services.AddGrpc();
builder.Services.AddSingleton<IKafkaLensClient, SharedClient>();
builder.Services.AddSingleton<ConsumerFactory>();
builder.Services.AddDbContext<KlServerContext>(opt =>
    opt.UseSqlite($"Data Source={config.DatabasePath};", b => b.MigrationsAssembly("GrpcApi")));


var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<KafkaService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. " +
        "To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();