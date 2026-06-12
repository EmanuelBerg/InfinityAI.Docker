using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InfinityAI.Docker.Services;

public sealed class DockerProgressPublisher(
    IConfiguration configuration,
    ILogger<DockerProgressPublisher> logger)
{
    private const string Exchange = RabbitMqPassiveTopologyVerifier.ProgressExchange;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(DockerOperationProgressMessage msg, CancellationToken ct = default)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;

        try
        {
            var factory = new ConnectionFactory { HostName = server, Port = port, AutomaticRecoveryEnabled = true };
            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel   = await connection.CreateChannelAsync(cancellationToken: ct);

            var body  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, _json));
            var props = new BasicProperties { Persistent = true };
            await channel.BasicPublishAsync(
                Exchange,
                routingKey: $"progress.{msg.CommandType}.{msg.Status}",
                mandatory:  false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-PROGRESS] Failed to publish progress op={Op} status={Status}",
                msg.OperationId, msg.Status);
        }
    }
}
