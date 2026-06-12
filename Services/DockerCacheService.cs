using System.Text.Json;
using InfinityAI.Docker.Dtos;
using InfinityAI.Docker.Dtos.Payloads;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InfinityAI.Docker.Services;

public sealed class DockerCacheService(IConnectionMultiplexer redis, ILogger<DockerCacheService> logger)
{
    // SchemaVersionKey kept public for backward-compat; canonical definition lives in DockerCacheKeys.
    public const string SchemaVersionKey = DockerCacheKeys.SchemaVersion;

    private const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task EnsureSchemaVersionAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(DockerCacheKeys.SchemaVersion, SchemaVersion);
    }

    public async Task WriteStacksAsync(DockerStacksPayload payload, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        try
        {
            var json = JsonSerializer.Serialize(payload, _json);
            if (json.Length > 2 * 1024 * 1024)
                logger.LogWarning("docker:stacks:all payload exceeds 2MB warning threshold ({Size} bytes)", json.Length);

            await db.StringSetAsync(DockerCacheKeys.StacksAll, json);
            logger.LogDebug("Wrote docker:stacks:all ({Size} bytes)", json.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write stacks payload to Redis");
            throw;
        }
    }

    public async Task WritePerStackAsync(IReadOnlyList<DockerStackDto> stacks, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>();
        try
        {
            foreach (var stack in stacks)
            {
                var json = JsonSerializer.Serialize(stack, _json);
                tasks.Add(batch.StringSetAsync(DockerCacheKeys.Stack(stack.StackName), json));
            }
            batch.Execute();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write per-stack payloads to Redis");
            throw;
        }
    }

    public async Task WriteServicesFlatAsync(DockerServicesPayload payload, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        try
        {
            var json = JsonSerializer.Serialize(payload, _json);
            await db.StringSetAsync(DockerCacheKeys.ServicesFlat, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write services:flat payload to Redis");
            throw;
        }
    }

    public async Task WriteNodesAsync(IReadOnlyList<DockerNodeDto> nodes, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        try
        {
            var json = JsonSerializer.Serialize(nodes, _json);
            await db.StringSetAsync(DockerCacheKeys.NodesAll, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write nodes to Redis");
            throw;
        }
    }

    public async Task WriteSwarmOverviewAsync(DockerSwarmOverviewDto overview, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        try
        {
            var json = JsonSerializer.Serialize(overview, _json);
            await db.StringSetAsync(DockerCacheKeys.SwarmOverview, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write swarm:overview to Redis");
            throw;
        }
    }

    public async Task WriteServiceAsync(DockerServiceDto service, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        try
        {
            var json = JsonSerializer.Serialize(service, _json);
            if (json.Length > 500 * 1024)
                logger.LogWarning("Per-service payload for {ServiceId} exceeds 500KB warning threshold ({Size} bytes)",
                    service.ServiceId, json.Length);

            await db.StringSetAsync(DockerCacheKeys.Service(service.ServiceId), json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write service {ServiceId} to Redis", service.ServiceId);
            throw;
        }
    }

    public async Task WriteAllServicesIndividuallyAsync(IReadOnlyList<DockerServiceDto> services, CancellationToken ct)
    {
        var tasks = services.Select(s => WriteServiceAsync(s, ct));
        await Task.WhenAll(tasks);
    }
}
