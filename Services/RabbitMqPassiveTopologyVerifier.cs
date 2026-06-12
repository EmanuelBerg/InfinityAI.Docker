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
        var port = int.TryParse(configuration["RabbitMQPort"], out int p) ? p : 5672;

        const int maxAttempts = 3;
        const int retryDelayMs = 5000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = server,
                    Port = port,
                    AutomaticRecoveryEnabled = false
                };

                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

                foreach (var exchange in RequiredExchanges)
                {
                    await channel.ExchangeDeclarePassiveAsync(exchange, ct);
                    logger.LogInformation("RabbitMQ exchange '{Exchange}' verified", exchange);
                }

                return;
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                logger.LogError(
                    "RabbitMQ topology verification failed: exchange not found. " +
                    "Ensure RabbitMqTopologyInitializer has run in InfinityAI.Api. Attempt {Attempt}/{Max}",
                    attempt, maxAttempts);

                if (attempt < maxAttempts)
                    await Task.Delay(retryDelayMs, ct);
                else
                    throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex,
                    "RabbitMQ topology verification attempt {Attempt}/{Max} failed, retrying in {Delay}ms",
                    attempt, maxAttempts, retryDelayMs);
                await Task.Delay(retryDelayMs, ct);
            }
        }
    }
}
