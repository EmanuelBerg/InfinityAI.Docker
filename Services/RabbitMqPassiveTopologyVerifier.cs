using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace InfinityAI.Docker.Services;

public sealed class RabbitMqPassiveTopologyVerifier(
    IConfiguration configuration,
    ILogger<RabbitMqPassiveTopologyVerifier> logger)
{
    public const string InventoryExchange = "docker.inventory";
    public const string CommandsExchange  = "docker.commands";
    public const string ProgressExchange  = "docker.progress";
    public const string LogsExchange      = "docker.logs";

    // Command worker requires all four exchanges to be pre-declared by InfinityAI.Api.
    private static readonly string[] RequiredExchanges =
        [InventoryExchange, CommandsExchange, ProgressExchange, LogsExchange];

    public async Task VerifyAsync(CancellationToken ct)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out int p) ? p : 5672;

        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName               = server,
                    Port                   = port,
                    AutomaticRecoveryEnabled = false
                };

                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel   = await connection.CreateChannelAsync(cancellationToken: ct);

                foreach (var exchange in RequiredExchanges)
                {
                    await channel.ExchangeDeclarePassiveAsync(exchange, ct);
                    logger.LogInformation("[STARTUP-WAIT] RabbitMQ exchange '{Exchange}' verified", exchange);
                }

                if (attempt > 1)
                    logger.LogInformation("[STARTUP-WAIT] RabbitMQ topology verified after {Attempt} attempt(s)", attempt);

                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                // Exchange not yet declared — InfinityAI.Api topology initializer still running.
                logger.LogWarning(
                    "[STARTUP-WAIT] RabbitMQ topology not ready (attempt {Attempt}) — " +
                    "waiting for InfinityAI.Api to declare exchanges: {Msg}",
                    attempt, ex.Message);
                await Task.Delay(5_000, ct);
            }
            catch (Exception ex)
            {
                // Connection refused or other transient error — RabbitMQ not yet reachable.
                logger.LogWarning(
                    "[STARTUP-WAIT] RabbitMQ not reachable (attempt {Attempt}): {Msg}",
                    attempt, ex.Message);
                await Task.Delay(5_000, ct);
            }
        }
    }
}
