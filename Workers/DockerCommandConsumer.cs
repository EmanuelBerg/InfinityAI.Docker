using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos.Commands;
using InfinityAI.Docker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InfinityAI.Docker.Workers;

internal enum MessageDisposition { Ack, NackDiscard, NackRequeue }

// Consumes commands from docker.commands.executor.
// Dispatches to DockerCommandExecutor for restart / upgrade / logs operations.
public sealed class DockerCommandConsumer(
    IDockerCommandExecutor executor,
    IConfiguration configuration,
    ILogger<DockerCommandConsumer> logger) : BackgroundService
{
    private const string QueueName = "docker.commands.executor";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[DOCKER-CMD-CONSUMER] Starting on queue={Queue}", QueueName);

        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;
        var factory = new ConnectionFactory { HostName = server, Port = port, AutomaticRecoveryEnabled = true };

        IConnection? connection = null;
        while (!stoppingToken.IsCancellationRequested && connection is null)
        {
            try { connection = await factory.CreateConnectionAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("[DOCKER-CMD-CONSUMER] RabbitMQ unavailable ({Msg}), retrying in 10s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        if (connection is null) return;
        await using (connection)
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            // Low prefetch — operations are long-running
            await channel.BasicQosAsync(0, 2, false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                var disposition = await HandleAsync(ea.Body.ToArray(), stoppingToken);
                switch (disposition)
                {
                    case MessageDisposition.Ack:
                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                        break;
                    case MessageDisposition.NackDiscard:
                        await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
                        break;
                    case MessageDisposition.NackRequeue:
                        await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
                        break;
                }
            };

            await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, stoppingToken);
            logger.LogInformation("[DOCKER-CMD-CONSUMER] Listening on '{Queue}'", QueueName);
            try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    internal async Task<MessageDisposition> HandleAsync(byte[] body, CancellationToken ct)
    {
        DockerCommandMessage? cmd;
        try { cmd = JsonSerializer.Deserialize<DockerCommandMessage>(Encoding.UTF8.GetString(body), _json); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DOCKER-CMD-CONSUMER] Deserialize failed — discarding");
            return MessageDisposition.NackDiscard;
        }

        if (cmd is null || cmd.SchemaVersion != 1)
        {
            logger.LogWarning("[DOCKER-CMD-CONSUMER] Null or schema-mismatched command — discarding");
            return MessageDisposition.NackDiscard;
        }

        logger.LogInformation("[DOCKER-CMD-CONSUMER] Received {Type} op={Op} svc={Svc}",
            cmd.CommandType, cmd.OperationId, cmd.ServiceId);

        try
        {
            switch (cmd.CommandType)
            {
                case "restart":
                    await executor.RestartServiceAsync(cmd, ct);
                    break;

                case "upgrade":
                    await executor.UpgradeServiceAsync(cmd, ct);
                    break;

                case "logs.start":
                    await executor.StartLogStreamAsync(cmd, ct);
                    break;

                case "logs.stop":
                    cmd.Parameters.TryGetValue("subscriptionId", out var subId);
                    if (!string.IsNullOrWhiteSpace(subId))
                        executor.StopLogStream(subId);
                    break;

                default:
                    logger.LogWarning("[DOCKER-CMD-CONSUMER] Unknown command type '{Type}' — discarding", cmd.CommandType);
                    return MessageDisposition.NackDiscard;
            }
        }
        catch (OperationCanceledException) { return MessageDisposition.NackRequeue; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-CMD-CONSUMER] Command execution failed for {Type} op={Op}",
                cmd.CommandType, cmd.OperationId);
            return MessageDisposition.NackDiscard;
        }

        return MessageDisposition.Ack;
    }
}
