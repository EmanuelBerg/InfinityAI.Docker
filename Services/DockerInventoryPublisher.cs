using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos;
using InfinityAI.Docker.Dtos.Messages;
using InfinityAI.Docker.Dtos.Payloads;
using InfinityAI.Docker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InfinityAI.Docker.Services;

public sealed class DockerInventoryPublisher(
    IConfiguration configuration,
    DockerMetrics metrics,
    ILogger<DockerInventoryPublisher> logger)
{
    private const string Exchange = RabbitMqPassiveTopologyVerifier.InventoryExchange;

    // Routing key for full inventory snapshots — caught by signalr.docker.inventory (#)
    private const string RoutingKeySnapshot = "inventory.snapshot";

    // Per-service state change routing key pattern: inventory.statechange.{stackName}
    // The {stackName} segment allows consumers to filter by stack via binding keys.
    private static string StateChangeRoutingKey(string stackName)
        => $"inventory.statechange.{SanitizeRoutingKeySegment(stackName)}";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task PublishInventorySnapshotAsync(DockerStacksPayload payload, CancellationToken ct)
    {
        var message = new DockerInventorySnapshotMessage
        {
            SchemaVersion = 1,
            StackNames = payload.Stacks.Select(s => s.StackName).ToList(),
            TotalServices = payload.Stacks.Sum(s => s.ServiceCount),
            TotalNodes = payload.Nodes.Count,
            OverallHealth = payload.SwarmOverview?.OverallHealth ?? "unknown",
            PolledAt = payload.PolledAt
        };

        await PublishAsync(RoutingKeySnapshot, message, ct);
    }

    public async Task PublishServiceStateChangesAsync(
        IReadOnlyList<DockerServiceDto> changedServices,
        IReadOnlyDictionary<string, (IReadOnlyList<string> ChangeTypes, string ChangeSummary)> changeInfo,
        CancellationToken ct)
    {
        foreach (var service in changedServices)
        {
            changeInfo.TryGetValue(service.ServiceId, out var info);

            var message = new DockerServiceStateMessage
            {
                SchemaVersion = 1,
                ServiceId = service.ServiceId,
                ServiceName = service.ServiceName,
                StackName = service.StackName,
                HealthStatus = service.HealthStatus,
                HealthScore = service.HealthScore,
                ReplicasDesired = service.ReplicasDesired,
                ReplicasRunning = service.ReplicasRunning,
                ChangeTypes = info.ChangeTypes ?? [],
                ChangeSummary = info.ChangeSummary ?? string.Empty,
                OccurredAt = DateTime.UtcNow
            };

            await PublishAsync(StateChangeRoutingKey(service.StackName), message, ct);
            metrics.InventoryServiceStateChanges.Add(1);
        }
    }

    private async Task PublishAsync<T>(string routingKey, T message, CancellationToken ct)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port = int.TryParse(configuration["RabbitMQPort"], out int p) ? p : 5672;

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = server,
                Port = port,
                AutomaticRecoveryEnabled = true
            };

            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _json));
            var props = new BasicProperties { Persistent = true };

            await channel.BasicPublishAsync(
                exchange: Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            metrics.MessagesPublished.Add(1);
        }
        catch (Exception ex)
        {
            metrics.PublishErrors.Add(1);
            logger.LogError(ex, "Failed to publish message with routing key '{RoutingKey}'", routingKey);
        }
    }

    // RabbitMQ routing key segments must not contain spaces or AMQP-reserved characters.
    private static string SanitizeRoutingKeySegment(string value)
    {
        if (string.IsNullOrEmpty(value)) return "_standalone";
        return value.Replace(' ', '_').Replace('#', '-').Replace('*', '-');
    }
}
