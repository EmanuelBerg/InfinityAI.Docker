using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InfinityAI.Docker.Services;

public sealed class DockerLogPublisher(
    IConfiguration configuration,
    ILogger<DockerLogPublisher> logger)
{
    private const string Exchange = RabbitMqPassiveTopologyVerifier.LogsExchange;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task PublishLineAsync(DockerLogLineMessage msg, CancellationToken ct = default)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;

        try
        {
            var factory = new ConnectionFactory { HostName = server, Port = port, AutomaticRecoveryEnabled = true };
            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel   = await connection.CreateChannelAsync(cancellationToken: ct);

            var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, _json));
            var props = new BasicProperties { Persistent = false }; // logs are ephemeral
            await channel.BasicPublishAsync(
                Exchange,
                routingKey: $"logs.line.{msg.SubscriptionId}",
                mandatory:  false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DOCKER-LOGS] Failed to publish log line for subscription={Sub}", msg.SubscriptionId);
        }
    }
}
