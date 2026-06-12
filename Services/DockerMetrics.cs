using System.Diagnostics.Metrics;

namespace InfinityAI.Docker.Services;

public sealed class DockerMetrics : IDisposable
{
    private readonly Meter _meter = new("InfinityAI.Docker", "1.0");

    // docker.inventory.*
    public Counter<long> InventoryCycles { get; }
    public Counter<long> InventoryErrors { get; }
    public Histogram<double> InventoryDurationMs { get; }
    public Counter<long> InventoryServicesDiscovered { get; }
    public Counter<long> InventoryServiceStateChanges { get; }

    // docker.cache.*
    public Counter<long> CacheWriteErrors { get; }
    public Counter<long> CacheReadsTotal { get; }

    // docker.rabbitmq.*
    public Counter<long> MessagesPublished { get; }
    public Counter<long> PublishErrors { get; }

    // docker.stats.*
    public Counter<long> StatsCollections { get; }
    public Counter<long> StatsErrors { get; }

    public DockerMetrics()
    {
        InventoryCycles = _meter.CreateCounter<long>(
            "docker.inventory.cycles", description: "Total inventory poll cycles completed");
        InventoryErrors = _meter.CreateCounter<long>(
            "docker.inventory.errors", description: "Total inventory poll cycle errors");
        InventoryDurationMs = _meter.CreateHistogram<double>(
            "docker.inventory.duration_ms", description: "Inventory poll cycle duration in milliseconds");
        InventoryServicesDiscovered = _meter.CreateCounter<long>(
            "docker.inventory.services_discovered", description: "Total services discovered after allow-list filtering");
        InventoryServiceStateChanges = _meter.CreateCounter<long>(
            "docker.inventory.service_state_changes", description: "Total service state change events detected");

        CacheWriteErrors = _meter.CreateCounter<long>(
            "docker.cache.write_errors", description: "Total Redis cache write errors");
        CacheReadsTotal = _meter.CreateCounter<long>(
            "docker.cache.reads_total", description: "Total Redis cache reads performed");

        MessagesPublished = _meter.CreateCounter<long>(
            "docker.rabbitmq.messages_published", description: "Total RabbitMQ messages published");
        PublishErrors = _meter.CreateCounter<long>(
            "docker.rabbitmq.publish_errors", description: "Total RabbitMQ publish errors");

        StatsCollections = _meter.CreateCounter<long>(
            "docker.stats.collections", description: "Total stats collection cycles");
        StatsErrors = _meter.CreateCounter<long>(
            "docker.stats.errors", description: "Total stats collection errors");
    }

    public void Dispose() => _meter.Dispose();
}
