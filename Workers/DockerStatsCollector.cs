using InfinityAI.Docker.Configuration;
using InfinityAI.Docker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfinityAI.Docker.Workers;

public sealed class DockerStatsCollector(
    DockerMetrics metrics,
    IOptions<DockerOptions> options,
    ILogger<DockerStatsCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.StatsCollectionMode == StatsCollectionMode.Disabled)
        {
            logger.LogInformation("DockerStatsCollector disabled (StatsCollectionMode=Disabled)");
            return;
        }

        logger.LogInformation("DockerStatsCollector starting (mode: {Mode})", options.Value.StatsCollectionMode);

        var interval = TimeSpan.FromSeconds(options.Value.StatsIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Phase 2: implement actual stats collection via Docker Engine API
                logger.LogDebug("DockerStatsCollector tick (Phase 2 implementation pending)");
                metrics.StatsCollections.Add(1);
            }
            catch (Exception ex)
            {
                metrics.StatsErrors.Add(1);
                logger.LogError(ex, "Stats collection failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
