using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InfinityAI.Docker.Services;

// Publishes log lines to the docker.logs topic exchange.
// Uses a persistent connection/channel to avoid opening a new TCP connection per log line —
// a critical performance requirement for streaming at high line rates.
public sealed class DockerLogPublisher(
    IConfiguration configuration,
    ILogger<DockerLogPublisher> logger) : IAsyncDisposable
{
    private const string Exchange = RabbitMqPassiveTopologyVerifier.LogsExchange;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private IConnection? _connection;
    private IChannel?    _channel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private int _linesPublished;

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel?.IsOpen == true) return _channel;

        await _channelLock.WaitAsync(ct);
        try
        {
            if (_channel?.IsOpen == true) return _channel;

            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); } catch { }
                _connection = null;
                _channel    = null;
            }

            var server  = configuration["RabbitMQServer"] ?? "rabbitmq";
            var port    = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;
            var factory = new ConnectionFactory { HostName = server, Port = port, AutomaticRecoveryEnabled = true };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);
            logger.LogInformation("[DOCKER-LOGS-PUB] Persistent channel established to {Server}:{Port}", server, port);
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async Task PublishLineAsync(DockerLogLineMessage msg, CancellationToken ct = default)
    {
        try
        {
            var channel = await GetChannelAsync(ct);
            var body    = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, _json));
            var props   = new BasicProperties { Persistent = false }; // logs are ephemeral

            await channel.BasicPublishAsync(
                Exchange,
                routingKey: $"logs.line.{msg.SubscriptionId}",
                mandatory:  false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            var count = Interlocked.Increment(ref _linesPublished);
            if (count == 1)
                logger.LogInformation("[DOCKER-LOGS-PUB] First log line published sub={Sub} svc={Svc}",
                    msg.SubscriptionId, msg.ServiceId);
            else if (count % 50 == 0)
                logger.LogDebug("[DOCKER-LOGS-PUB] Published {Count} lines total for sub={Sub}",
                    count, msg.SubscriptionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DOCKER-LOGS-PUB] Failed to publish log line sub={Sub} — resetting channel",
                msg.SubscriptionId);
            // Reset so next call rebuilds the channel
            _channel = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.DisposeAsync(); } catch { }
            _channel = null;
        }
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); } catch { }
            _connection = null;
        }
        _channelLock.Dispose();
    }
}
