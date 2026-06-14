using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Configuration;
using InfinityAI.Docker.Dtos;
using InfinityAI.Docker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InfinityAI.Docker.Workers;

// Collects host-level resource metrics (CPU, memory, disk, load, Docker counts)
// on every poll cycle and:
//   1. Writes latest snapshot to Redis (infinity:docker:host:metrics)
//   2. Appends to rolling 24-hour history list in Redis
//   3. Publishes to docker.host exchange → SignalR forwards to browser (docker:host_metrics)
//   4. Also runs DockerStorageAnalyzer every ~5 minutes.
public sealed class HostMetricsPoller(
    HostMetricsCollector metricsCollector,
    DockerStorageAnalyzer storageAnalyzer,
    DockerCacheService cache,
    IOptions<DockerOptions> options,
    IConfiguration configuration,
    ILogger<HostMetricsPoller> logger) : BackgroundService
{
    private const string Exchange   = "docker.host";
    private const string RoutingKey = "host.metrics";
    private const int StorageAnalysisEveryNPolls = 10; // every ~5 minutes at 30s interval

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private int _pollCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[HOST-METRICS] HostMetricsPoller starting (interval={Interval}s)",
            options.Value.PollIntervalSeconds);

        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        // Brief initial delay to let the Docker socket stabilise on startup.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        _pollCount++;

        try
        {
            var metrics = await metricsCollector.CollectAsync(ct);

            await cache.WriteHostMetricsAsync(metrics, ct);
            await cache.AppendHostMetricsHistoryAsync(metrics, ct);
            await PublishHostMetricsAsync(metrics, ct);

            logger.LogDebug(
                "[HOST-METRICS] cpu={Cpu:F1}% mem={Mem:F1}% disk={Disk:F1}% score={Score}",
                metrics.Cpu.UsagePercent, metrics.Memory.UsagePercent,
                metrics.Disk.UsagePercent, metrics.HostHealthScore);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[HOST-METRICS] Host metrics poll failed");
        }

        // Storage analysis: run immediately on first poll, then every N polls (~5 min).
        if (_pollCount == 1 || _pollCount % StorageAnalysisEveryNPolls == 0)
        {
            try
            {
                var analysis = await storageAnalyzer.AnalyzeAsync(ct);
                if (analysis is not null)
                    await cache.WriteStorageAnalysisAsync(analysis, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[HOST-METRICS] Storage analysis failed — skipping");
            }
        }
    }

    // Publish to docker.host exchange so DockerHostMetricsConsumer (SignalR) can forward
    // to connected browsers. Connection is created per-publish (same pattern as
    // DockerInventoryPublisher) — frequency is 30s so the overhead is negligible.
    private async Task PublishHostMetricsAsync(DockerHostMetricsDto metrics, CancellationToken ct)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;

        try
        {
            var factory = new ConnectionFactory
            {
                HostName                 = server,
                Port                     = port,
                AutomaticRecoveryEnabled = false
            };

            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel    = await connection.CreateChannelAsync(cancellationToken: ct);

            // Publish the DTO directly — no envelope needed; SchemaVersion is in the DTO.
            var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metrics, _json));
            var props = new BasicProperties { Persistent = false }; // metrics are ephemeral

            await channel.BasicPublishAsync(
                exchange:         Exchange,
                routingKey:       RoutingKey,
                mandatory:        false,
                basicProperties:  props,
                body:             body,
                cancellationToken: ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            // Non-fatal — Redis already has the latest value; SignalR push is best-effort.
            logger.LogDebug(ex, "[HOST-METRICS] RabbitMQ publish failed — exchange may not be ready yet");
        }
    }
}

