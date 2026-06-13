using System.Text.Json;
using InfinityAI.Docker.Dtos;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InfinityAI.Docker.Services;

public sealed class DockerAckCacheService(IConnectionMultiplexer redis, ILogger<DockerAckCacheService> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, DockerHealthAcknowledgementDto>> GetAcksAsync(
        IEnumerable<string> serviceIds,
        CancellationToken ct)
    {
        var ids = serviceIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, DockerHealthAcknowledgementDto>();

        var db = redis.GetDatabase();
        var keys = ids.Select(id => (RedisKey)DockerCacheKeys.HealthAck(id)).ToArray();

        RedisValue[] values;
        try
        {
            values = await db.StringGetAsync(keys);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-ACK] Failed to batch-read health acks from Redis");
            return new Dictionary<string, DockerHealthAcknowledgementDto>();
        }

        var result = new Dictionary<string, DockerHealthAcknowledgementDto>(StringComparer.Ordinal);
        for (int i = 0; i < ids.Count; i++)
        {
            if (!values[i].HasValue) continue;
            try
            {
                var ack = JsonSerializer.Deserialize<DockerHealthAcknowledgementDto>(values[i].ToString(), _json);
                if (ack != null)
                    result[ids[i]] = ack;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DOCKER-ACK] Failed to deserialize ack for service {ServiceId}", ids[i]);
            }
        }
        return result;
    }
}
