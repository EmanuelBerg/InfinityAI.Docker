using InfinityAI.Docker.Configuration;
using InfinityAI.Docker.Services;
using InfinityAI.Docker.Services.Mappers;
using InfinityAI.Docker.Workers;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.Configure<DockerOptions>(
    builder.Configuration.GetSection("DockerOptions"));

var redisConnectionString = builder.Configuration["RedisConnectionString"] ?? "redis:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var opts = ConfigurationOptions.Parse(redisConnectionString);
    opts.AbortOnConnectFail = false;
    opts.ConnectRetry = 5;
    return ConnectionMultiplexer.Connect(opts);
});

builder.Services.AddSingleton<DockerClientFactory>();
builder.Services.AddSingleton<DockerMetrics>();
builder.Services.AddSingleton<ServiceSnapshotDiffTracker>();
builder.Services.AddSingleton<DockerServiceMapper>();
builder.Services.AddSingleton<DockerCacheService>();
builder.Services.AddSingleton<DockerAckCacheService>();
builder.Services.AddSingleton<DockerInventoryPublisher>();
builder.Services.AddSingleton<RabbitMqPassiveTopologyVerifier>();

builder.Services.AddSingleton<DockerProgressPublisher>();
builder.Services.AddSingleton<DockerLogPublisher>();
builder.Services.AddSingleton<IDockerCommandExecutor, DockerCommandExecutor>();

builder.Services.AddHostedService<DockerInventoryPoller>();
builder.Services.AddHostedService<DockerStatsCollector>();
builder.Services.AddHostedService<DockerCommandConsumer>();

var host = builder.Build();
host.Run();
