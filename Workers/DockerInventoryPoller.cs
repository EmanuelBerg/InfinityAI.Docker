using System.Diagnostics;
using DockerDotNet = Docker.DotNet;
using DockerModels = Docker.DotNet.Models;
using InfinityAI.Docker.Configuration;
using InfinityAI.Docker.Dtos;
using InfinityAI.Docker.Dtos.Payloads;
using InfinityAI.Docker.Services;
using InfinityAI.Docker.Services.Mappers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfinityAI.Docker.Workers;

public sealed class DockerInventoryPoller(
    DockerClientFactory clientFactory,
    DockerServiceMapper serviceMapper,
    DockerCacheService cache,
    DockerAckCacheService ackCache,
    DockerInventoryPublisher publisher,
    ServiceSnapshotDiffTracker diffTracker,
    RabbitMqPassiveTopologyVerifier topologyVerifier,
    DockerMetrics metrics,
    IOptions<DockerOptions> options,
    ILogger<DockerInventoryPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DockerInventoryPoller starting");

        await topologyVerifier.VerifyAsync(stoppingToken);
        await cache.EnsureSchemaVersionAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        metrics.InventoryCycles.Add(1);

        try
        {
            var client = clientFactory.GetClient();
            var (services, nodes, networks, swarmInfo) = await FetchAllAsync(client, ct);

            var nodeHostnames = nodes.ToDictionary(
                n => n.ID ?? string.Empty,
                n => n.Description?.Hostname ?? string.Empty);

            var tasks = await client.Tasks.ListAsync(new DockerModels.TasksListParameters(), ct);
            var tasksByService = tasks
                .GroupBy(t => t.ServiceID ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.ToList());

            var networkNames = networks.ToDictionary(
                n => n.ID ?? string.Empty,
                n => n.Name ?? string.Empty);

            // Read active health acknowledgements from Redis (batch) so health calculator
            // can ignore pre-ack task failures and restart counts for each service.
            var acks = await ackCache.GetAcksAsync(
                services.Select(s => s.ID ?? string.Empty), ct);

            var allServiceDtos = new List<DockerServiceDto>();
            foreach (var svc in services)
            {
                var svcTasks = tasksByService.TryGetValue(svc.ID ?? string.Empty, out var t) ? t : [];

                // Restart count per slot = number of shutdown (replaced) tasks with the same slot.
                // Docker Swarm creates a new Task each time a task is recycled; the old one gets
                // DesiredState=shutdown. This is the only reliable restart count in the Swarm API.
                var historicalCountBySlot = svcTasks
                    .GroupBy(t2 => t2.Slot)
                    .ToDictionary(
                        g => g.Key,
                        g => (long)g.Count(t2 => t2.DesiredState.ToString().ToLowerInvariant() == "shutdown"));

                var taskDtos = svcTasks.Select(t2 =>
                {
                    bool isCurrent = t2.DesiredState.ToString().ToLowerInvariant() != "shutdown";
                    long rc = isCurrent && historicalCountBySlot.TryGetValue(t2.Slot, out var n) ? n : 0L;
                    return DockerTaskMapper.Map(t2, nodeHostnames, rc);
                }).ToList();

                acks.TryGetValue(svc.ID ?? string.Empty, out var ack);
                var dto = serviceMapper.Map(svc, taskDtos, networkNames, ack);
                allServiceDtos.Add(dto);
            }

            // Apply allow-list filtering before writing to cache
            var allowedPrefixes = options.Value.AllowedStackPrefixes;
            var filteredServiceDtos = allowedPrefixes.Length > 0
                ? allServiceDtos.Where(s => IsStackAllowed(s.StackName, allowedPrefixes)).ToList()
                : allServiceDtos;

            metrics.InventoryServicesDiscovered.Add(filteredServiceDtos.Count);

            var nodeDtos = nodes.Select(DockerNodeMapper.Map).ToList();
            var stacks = DockerStackBuilder.Build(filteredServiceDtos);
            var swarmOverview = BuildSwarmOverview(swarmInfo, nodeDtos, stacks);

            var now = DateTime.UtcNow;

            var stacksPayload = new DockerStacksPayload
            {
                SchemaVersion = 1,
                Stacks = stacks,
                Nodes = nodeDtos,
                SwarmOverview = swarmOverview,
                PolledAt = now
            };

            var servicesPayload = new DockerServicesPayload
            {
                SchemaVersion = 1,
                Services = filteredServiceDtos,
                PolledAt = now
            };

            await cache.WriteStacksAsync(stacksPayload, ct);
            await cache.WritePerStackAsync(stacks, ct);
            await cache.WriteServicesFlatAsync(servicesPayload, ct);
            await cache.WriteNodesAsync(nodeDtos, ct);
            if (swarmOverview != null) await cache.WriteSwarmOverviewAsync(swarmOverview, ct);
            await cache.WriteAllServicesIndividuallyAsync(filteredServiceDtos, ct);

            var (changed, changeInfo) = diffTracker.ComputeDiff(filteredServiceDtos);
            if (changed.Count > 0)
            {
                await publisher.PublishInventorySnapshotAsync(stacksPayload, ct);
                await publisher.PublishServiceStateChangesAsync(changed, changeInfo, ct);
            }

            sw.Stop();
            metrics.InventoryDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            logger.LogInformation("Poll completed: {ServiceCount} services ({Allowed}/{Total} allowed), {NodeCount} nodes in {Ms:F0}ms",
                filteredServiceDtos.Count, filteredServiceDtos.Count, allServiceDtos.Count, nodeDtos.Count, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            metrics.InventoryErrors.Add(1);
            logger.LogError(ex, "Poll cycle failed");
        }
    }

    private static bool IsStackAllowed(string stackName, string[] allowedPrefixes)
    {
        if (string.IsNullOrEmpty(stackName)) return true; // standalone services pass through
        return allowedPrefixes.Any(prefix =>
            stackName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(
        IReadOnlyList<DockerModels.SwarmService> Services,
        IReadOnlyList<DockerModels.NodeListResponse> Nodes,
        IReadOnlyList<DockerModels.NetworkResponse> Networks,
        DockerModels.SwarmInspectResponse? Swarm)>
        FetchAllAsync(DockerDotNet.DockerClient client, CancellationToken ct)
    {
        var servicesTask = client.Swarm.ListServicesAsync(new DockerModels.ServicesListParameters(), ct);
        var nodesTask = client.Swarm.ListNodesAsync(ct);
        var networksTask = client.Networks.ListNetworksAsync(new DockerModels.NetworksListParameters(), ct);

        await Task.WhenAll(servicesTask, nodesTask, networksTask);

        DockerModels.SwarmInspectResponse? swarm = null;
        try { swarm = await client.Swarm.InspectSwarmAsync(ct); } catch { }

        return (
            (await servicesTask).ToList(),
            (await nodesTask).ToList(),
            (await networksTask).ToList(),
            swarm);
    }

    private static DockerSwarmOverviewDto? BuildSwarmOverview(
        DockerModels.SwarmInspectResponse? swarm,
        IReadOnlyList<DockerNodeDto> nodes,
        IReadOnlyList<DockerStackDto> stacks)
    {
        if (swarm == null) return null;

        int healthy = stacks.SelectMany(s => s.Services).Count(s => s.HealthStatus == "healthy");
        int degraded = stacks.SelectMany(s => s.Services).Count(s => s.HealthStatus == "degraded");
        int unhealthy = stacks.SelectMany(s => s.Services).Count(s => s.HealthStatus == "unhealthy");

        string overallHealth = unhealthy > 0 ? "unhealthy"
            : degraded > 0 ? "degraded"
            : "healthy";

        return new DockerSwarmOverviewDto
        {
            SwarmId = swarm.ID ?? string.Empty,
            TotalNodes = nodes.Count,
            ManagerNodes = nodes.Count(n => n.Role == "manager"),
            WorkerNodes = nodes.Count(n => n.Role == "worker"),
            TotalServices = stacks.Sum(s => s.ServiceCount),
            TotalStacks = stacks.Count,
            OverallHealth = overallHealth,
            HealthyServices = healthy,
            DegradedServices = degraded,
            UnhealthyServices = unhealthy,
            PolledAt = DateTime.UtcNow
        };
    }
}
