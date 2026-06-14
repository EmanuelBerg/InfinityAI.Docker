using System.Collections.Concurrent;
using System.Text;
using DockerDotNet = Docker.DotNet;
using DockerModels = Docker.DotNet.Models;
using InfinityAI.Docker.Dtos.Commands;
using Microsoft.Extensions.Logging;

namespace InfinityAI.Docker.Services;

// Executes Docker Swarm operations: restart, upgrade, log streaming, and cleanup.
// Log streams are tracked by subscriptionId so a stop command can cancel them.
public sealed class DockerCommandExecutor(
    DockerClientFactory clientFactory,
    DockerProgressPublisher progressPublisher,
    DockerLogPublisher logPublisher,
    DockerStorageAnalyzer storageAnalyzer,
    DockerCacheService cache,
    ILogger<DockerCommandExecutor> logger) : IDockerCommandExecutor
{
    private const DockerModels.TaskState _stateRunning  = DockerModels.TaskState.Running;
    private const DockerModels.TaskState _stateFailed   = DockerModels.TaskState.Failed;
    private const DockerModels.TaskState _stateRejected = DockerModels.TaskState.Rejected;

    // Active log streams: subscriptionId → CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _logStreams = new();

    private static readonly TimeSpan RestartPollTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LogStreamTimeout   = TimeSpan.FromHours(1);

    // ── Restart ───────────────────────────────────────────────────────────────

    public async Task RestartServiceAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "starting", "Inspecting service…", 5, ct: ct);

        try
        {
            var client  = clientFactory.GetClient();
            var service = await client.Swarm.InspectServiceAsync(cmd.ServiceId, ct);
            var spec    = service.Spec;
            var version = (long)service.Version.Index;

            spec.TaskTemplate.ForceUpdate += 1;

            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", "Issuing force-update…", 20, ct: ct);

            await client.Swarm.UpdateServiceAsync(
                cmd.ServiceId,
                new DockerModels.ServiceUpdateParameters { Service = spec, Version = version },
                ct);

            logger.LogInformation("[DOCKER-EXEC] Force-update issued for svc={Svc} op={Op}",
                cmd.ServiceId, cmd.OperationId);

            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", "Waiting for tasks to restart…", 40, ct: ct);

            await WaitForServiceHealthyAsync(cmd, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-EXEC] Restart failed svc={Svc} op={Op}", cmd.ServiceId, cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Restart failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Upgrade ───────────────────────────────────────────────────────────────

    public async Task UpgradeServiceAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        cmd.Parameters.TryGetValue("targetImage", out var targetImage);
        if (string.IsNullOrWhiteSpace(targetImage))
        {
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Missing targetImage", 0, "targetImage parameter is required", ct: ct);
            return;
        }

        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "starting", $"Inspecting service for upgrade to {targetImage}…", 5, ct: ct);

        try
        {
            var client  = clientFactory.GetClient();
            var service = await client.Swarm.InspectServiceAsync(cmd.ServiceId, ct);
            var spec    = service.Spec;
            var version = (long)service.Version.Index;

            spec.TaskTemplate.ContainerSpec.Image = targetImage;

            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", $"Updating image to {targetImage}…", 20, ct: ct);

            await client.Swarm.UpdateServiceAsync(
                cmd.ServiceId,
                new DockerModels.ServiceUpdateParameters { Service = spec, Version = version },
                ct);

            logger.LogInformation("[DOCKER-EXEC] Upgrade issued svc={Svc} image={Image} op={Op}",
                cmd.ServiceId, targetImage, cmd.OperationId);

            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", "Waiting for tasks to restart with new image…", 40, ct: ct);

            await WaitForServiceHealthyAsync(cmd, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-EXEC] Upgrade failed svc={Svc} op={Op}", cmd.ServiceId, cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Upgrade failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Log streaming ─────────────────────────────────────────────────────────

    public Task StartLogStreamAsync(DockerCommandMessage cmd, CancellationToken workerCt)
    {
        cmd.Parameters.TryGetValue("subscriptionId", out var subscriptionId);
        cmd.Parameters.TryGetValue("tail",           out var tail);
        logger.LogInformation("[DOCKER-LOGS] logs.start received svc={Svc} sub={Sub}",
            cmd.ServiceId, subscriptionId ?? "(none)");
        if (string.IsNullOrWhiteSpace(subscriptionId)) return Task.CompletedTask;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(workerCt);
        cts.CancelAfter(LogStreamTimeout);

        if (!_logStreams.TryAdd(subscriptionId, cts))
        {
            cts.Dispose();
            logger.LogWarning("[DOCKER-LOGS] Duplicate subscription {Sub} ignored", subscriptionId);
            return Task.CompletedTask;
        }

        // Fire-and-forget: log streaming runs in background until cancelled or EOF
        _ = StreamLogsAsync(cmd.ServiceId, cmd.ServiceName, subscriptionId, tail ?? "200", cts.Token);
        return Task.CompletedTask;
    }

    public void StopLogStream(string subscriptionId)
    {
        if (_logStreams.TryRemove(subscriptionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            logger.LogInformation("[DOCKER-LOGS] Cancelled subscription {Sub}", subscriptionId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task StreamLogsAsync(
        string serviceId, string serviceName, string subscriptionId,
        string tail, CancellationToken ct)
    {
        logger.LogInformation("[DOCKER-LOGS] Starting stream sub={Sub} svc={Svc} tail={Tail}",
            subscriptionId, serviceId, tail);

        var linesPublished = 0;
        try
        {
            var client = clientFactory.GetClient();

            logger.LogInformation("[DOCKER-LOGS] Calling Docker API GetServiceLogsAsync svc={Svc}", serviceId);
            using var stream = await client.Swarm.GetServiceLogsAsync(
                serviceId,
                tty: false,
                new DockerModels.ServiceLogsParameters
                {
                    Follow      = true,
                    ShowStdout  = true,
                    ShowStderr  = true,
                    Timestamps  = true,
                    Tail        = tail
                },
                ct);

            logger.LogInformation("[DOCKER-LOGS] Docker stream opened sub={Sub}", subscriptionId);

            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                if (result.EOF) break;

                var text   = Encoding.UTF8.GetString(buffer, 0, result.Count).TrimEnd('\r', '\n');
                var source = result.Target == DockerDotNet.MultiplexedStream.TargetStream.StandardError
                    ? "stderr" : "stdout";

                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    await logPublisher.PublishLineAsync(new DockerLogLineMessage
                    {
                        SubscriptionId = subscriptionId,
                        ServiceId      = serviceId,
                        ServiceName    = serviceName,
                        Source         = source,
                        Line           = line.TrimEnd('\r'),
                        OccurredAt     = DateTime.UtcNow
                    }, ct);
                    linesPublished++;
                }
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-LOGS] Stream error sub={Sub} svc={Svc} linesPublished={N}",
                subscriptionId, serviceId, linesPublished);
        }
        finally
        {
            _logStreams.TryRemove(subscriptionId, out _);
            logger.LogInformation("[DOCKER-LOGS] Stream ended sub={Sub} linesPublished={N} reason=finished/cancelled",
                subscriptionId, linesPublished);
        }
    }

    private async Task WaitForServiceHealthyAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RestartPollTimeout);

        int lastPercent = 40;

        try
        {
            while (!timeout.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), timeout.Token);

                var client = clientFactory.GetClient();
                var tasks  = await client.Tasks.ListAsync(
                    new DockerModels.TasksListParameters
                    {
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["service"] = new Dictionary<string, bool> { [cmd.ServiceId] = true }
                        }
                    }, timeout.Token);

                var running  = tasks.Count(t => t.Status?.State == _stateRunning && t.DesiredState == _stateRunning);
                var desired  = tasks.Count(t => t.DesiredState == _stateRunning);
                var failed   = tasks.Count(t => t.Status?.State == _stateFailed || t.Status?.State == _stateRejected);

                if (desired > 0 && failed > 0 && running == 0)
                {
                    var errMsg = tasks.FirstOrDefault(t =>
                            t.Status?.State == _stateFailed || t.Status?.State == _stateRejected)
                        ?.Status?.Err ?? "Task failed";
                    await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                        "failed", "Restart failed — task error", 0, errMsg, ct: ct);
                    return;
                }

                if (desired > 0 && running >= desired)
                {
                    await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                        "completed", $"All {desired} task(s) running", 100, ct: ct);
                    return;
                }

                var percent = desired > 0 ? Math.Min(95, 40 + (running * 55 / desired)) : lastPercent;
                lastPercent = percent;
                await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                    "in_progress", $"{running}/{desired} task(s) running…", percent, ct: ct);
            }

            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", "Operation timed out waiting for all tasks — check Docker state", 90, ct: ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", "Timed out waiting for tasks — command was issued", 90, ct: ct);
        }
    }

    // ── Prune: images ─────────────────────────────────────────────────────────

    public async Task PruneImagesAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "in_progress", "Pruning unused images…", 10, ct: ct);
        try
        {
            var client = clientFactory.GetClient();
            // dangling=false → remove ALL images not used by any container (equivalent to docker image prune -a).
            // This matches the "Reclaimable" definition (ContainerCount == 0) in the Storage tab.
            var pruneParams = new DockerModels.ImagesPruneParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["dangling"] = new Dictionary<string, bool> { ["false"] = true }
                }
            };
            var response = await client.Images.PruneImagesAsync(pruneParams, ct);
            var reclaimed = response.SpaceReclaimed;
            var count     = response.ImagesDeleted?.Count ?? 0;

            logger.LogInformation("[DOCKER-PRUNE] images.prune op={Op} deleted={N} reclaimed={Bytes}",
                cmd.OperationId, count, reclaimed);

            await RefreshStorageAsync(ct);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", $"Removed {count} image(s), reclaimed {FormatBytes((long?)reclaimed)}", 100, ct: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-PRUNE] images.prune failed op={Op}", cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Image prune failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Prune: containers ─────────────────────────────────────────────────────

    public async Task PruneContainersAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "in_progress", "Pruning stopped containers…", 10, ct: ct);
        try
        {
            var client   = clientFactory.GetClient();
            var response = await client.Containers.PruneContainersAsync(cancellationToken: ct);
            var reclaimed = response.SpaceReclaimed;
            var count     = response.ContainersDeleted?.Count ?? 0;

            logger.LogInformation("[DOCKER-PRUNE] containers.prune op={Op} deleted={N} reclaimed={Bytes}",
                cmd.OperationId, count, reclaimed);

            await RefreshStorageAsync(ct);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", $"Removed {count} container(s), reclaimed {FormatBytes((long?)reclaimed)}", 100, ct: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-PRUNE] containers.prune failed op={Op}", cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Container prune failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Prune: volumes ────────────────────────────────────────────────────────

    public async Task PruneVolumesAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "in_progress", "Pruning unused volumes…", 10, ct: ct);
        try
        {
            var client   = clientFactory.GetClient();
            var response = await client.Volumes.PruneAsync(cancellationToken: ct);
            var reclaimed = response.SpaceReclaimed;
            var count     = response.VolumesDeleted?.Count ?? 0;

            logger.LogInformation("[DOCKER-PRUNE] volumes.prune op={Op} deleted={N} reclaimed={Bytes}",
                cmd.OperationId, count, reclaimed);

            await RefreshStorageAsync(ct);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", $"Removed {count} volume(s), reclaimed {FormatBytes((long?)reclaimed)}", 100, ct: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-PRUNE] volumes.prune failed op={Op}", cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "Volume prune failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Prune: system (images + containers + networks, optionally volumes) ────

    public async Task PruneSystemAsync(DockerCommandMessage cmd, CancellationToken ct)
    {
        cmd.Parameters.TryGetValue("includeVolumes", out var includeVolumesStr);
        var includeVolumes = string.Equals(includeVolumesStr, "true", StringComparison.OrdinalIgnoreCase);

        await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
            "in_progress", "Starting system prune…", 5, ct: ct);

        long totalReclaimed = 0;
        var  results        = new List<string>();

        try
        {
            var client = clientFactory.GetClient();

            // 1. Containers first (images cannot be pruned if containers reference them)
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", "Pruning stopped containers…", 20, ct: ct);
            try
            {
                var cResponse = await client.Containers.PruneContainersAsync(cancellationToken: ct);
                totalReclaimed += (long)cResponse.SpaceReclaimed;
                results.Add($"{cResponse.ContainersDeleted?.Count ?? 0} container(s)");
            }
            catch (Exception ex) { logger.LogWarning(ex, "[DOCKER-PRUNE] system.prune containers step failed"); }

            // 2. Images — remove all unused (not just dangling) to match PruneImagesAsync behavior
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "in_progress", "Pruning unused images…", 50, ct: ct);
            try
            {
                var imgPruneParams = new DockerModels.ImagesPruneParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["dangling"] = new Dictionary<string, bool> { ["false"] = true }
                    }
                };
                var iResponse = await client.Images.PruneImagesAsync(imgPruneParams, ct);
                totalReclaimed += (long)iResponse.SpaceReclaimed;
                results.Add($"{iResponse.ImagesDeleted?.Count ?? 0} image(s)");
            }
            catch (Exception ex) { logger.LogWarning(ex, "[DOCKER-PRUNE] system.prune images step failed"); }

            // 3. Volumes (only if requested — DESTRUCTIVE, may contain data)
            if (includeVolumes)
            {
                await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                    "in_progress", "Pruning unused volumes…", 75, ct: ct);
                try
                {
                    var vResponse = await client.Volumes.PruneAsync(cancellationToken: ct);
                    totalReclaimed += (long)vResponse.SpaceReclaimed;
                    results.Add($"{vResponse.VolumesDeleted?.Count ?? 0} volume(s)");
                }
                catch (Exception ex) { logger.LogWarning(ex, "[DOCKER-PRUNE] system.prune volumes step failed"); }
            }

            var summary = results.Count > 0
                ? $"Removed {string.Join(", ", results)}, reclaimed {FormatBytes(totalReclaimed)}"
                : "Nothing to remove";

            logger.LogInformation("[DOCKER-PRUNE] system.prune op={Op} reclaimed={Bytes} includeVolumes={V}",
                cmd.OperationId, totalReclaimed, includeVolumes);

            await RefreshStorageAsync(ct);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "completed", summary, 100, ct: ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DOCKER-PRUNE] system.prune failed op={Op}", cmd.OperationId);
            await PublishProgress(cmd.OperationId, cmd.CommandType, cmd.ServiceId, cmd.ServiceName,
                "failed", "System prune failed", 0, ex.Message, ct: ct);
        }
    }

    // ── Storage refresh ───────────────────────────────────────────────────────

    // Called after each prune to immediately update the Redis storage snapshot so
    // the Storage tab reflects the freed space without waiting for the next scheduled poll.
    private async Task RefreshStorageAsync(CancellationToken ct)
    {
        try
        {
            var analysis = await storageAnalyzer.AnalyzeAsync(ct);
            if (analysis is not null)
                await cache.WriteStorageAnalysisAsync(analysis, ct);
        }
        catch (OperationCanceledException) { /* ignore — worker is stopping */ }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DOCKER-PRUNE] Post-prune storage refresh failed — stale data may persist until next poll");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null or 0) return "0 B";
        double b = bytes.Value;
        if (b >= 1_073_741_824) return $"{b / 1_073_741_824:F1} GB";
        if (b >= 1_048_576)     return $"{b / 1_048_576:F1} MB";
        if (b >= 1_024)         return $"{b / 1_024:F1} KB";
        return $"{b:F0} B";
    }

    private Task PublishProgress(
        Guid operationId, string commandType, string serviceId, string serviceName,
        string status, string step, int percent, string? errorMessage = null,
        CancellationToken ct = default)
        => progressPublisher.PublishAsync(new DockerOperationProgressMessage
        {
            OperationId     = operationId,
            CommandType     = commandType,
            ServiceId       = serviceId,
            ServiceName     = serviceName,
            Status          = status,
            Step            = step,
            ProgressPercent = percent,
            ErrorMessage    = errorMessage,
            OccurredAt      = DateTime.UtcNow
        }, ct);
}
