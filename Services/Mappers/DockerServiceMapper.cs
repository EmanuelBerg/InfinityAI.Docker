using Docker.DotNet.Models;
using InfinityAI.Docker.Configuration;
using InfinityAI.Docker.Dtos;
using Microsoft.Extensions.Options;

namespace InfinityAI.Docker.Services.Mappers;

public sealed class DockerServiceMapper(IOptions<DockerOptions> options)
{
    private readonly string[] _redactedSubstrings = options.Value.RedactedLabelSubstrings;

    public DockerServiceDto Map(
        SwarmService service,
        IReadOnlyList<DockerTaskDto> tasks,
        IDictionary<string, string> networkNames,
        DockerHealthAcknowledgementDto? acknowledgement = null)
    {
        var serviceLabels = service.Spec?.Labels ?? new Dictionary<string, string>();
        var containerLabels = service.Spec?.TaskTemplate?.ContainerSpec?.Labels ?? new Dictionary<string, string>();

        var redactedServiceLabels = RedactLabels(serviceLabels);
        var redactedContainerLabels = RedactLabels(containerLabels);

        serviceLabels.TryGetValue("com.docker.stack.namespace", out var stackName);
        stackName ??= string.Empty;

        var portConfigs = service.Spec?.EndpointSpec?.Ports ?? [];
        var ports = portConfigs.Select(p => new DockerPortMappingDto
        {
            PublishedPort = p.PublishedPort > 0 ? (int?)p.PublishedPort : null,
            TargetPort = (int)p.TargetPort,
            Protocol = p.Protocol ?? "tcp",
            PublishMode = p.PublishMode ?? "ingress"
        }).ToList();

        var mounts = service.Spec?.TaskTemplate?.ContainerSpec?.Mounts ?? [];
        var volumes = mounts.Select(m => new DockerVolumeDto
        {
            Type = m.Type ?? "volume",
            Source = m.Source ?? string.Empty,
            Target = m.Target ?? string.Empty,
            ReadOnly = m.ReadOnly
        }).ToList();

        var networkAttachments = service.Spec?.TaskTemplate?.Networks ?? [];
        var networkIds = networkAttachments
            .Select(n => n.Target ?? string.Empty)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var placement = service.Spec?.TaskTemplate?.Placement;
        DockerPlacementDto? placementDto = null;
        if (placement != null)
        {
            placementDto = new DockerPlacementDto
            {
                Constraints = (placement.Constraints ?? []).ToList(),
                Preferences = placement.Preferences?
                    .Select(p => p.Spread?.SpreadDescriptor ?? string.Empty)
                    .ToList() ?? [],
                MaxReplicas = placement.MaxReplicas > 0 ? (int?)placement.MaxReplicas : null
            };
        }

        var runningTasks = tasks.Where(t => t.State == "running").ToList();
        int replicasDesired = service.Spec?.Mode?.Replicated?.Replicas != null
            ? (int)service.Spec.Mode.Replicated.Replicas.Value
            : 0;
        int replicasRunning = runningTasks.Count;

        string image = service.Spec?.TaskTemplate?.ContainerSpec?.Image ?? string.Empty;
        string imageDigest = string.Empty;
        int atIdx = image.IndexOf('@');
        if (atIdx >= 0)
        {
            imageDigest = image[(atIdx + 1)..];
            image = image[..atIdx];
        }

        int displayOrder = ParseDisplayOrder(serviceLabels);
        var serviceMode = service.Spec?.Mode?.Global != null ? "global" : "replicated";

        var restartPolicy = service.Spec?.TaskTemplate?.RestartPolicy;
        long? restartDelay = restartPolicy?.Delay;
        long? restartMaxAttempts = null;
        if (restartPolicy?.MaxAttempts != null && restartPolicy.MaxAttempts.HasValue)
            restartMaxAttempts = (long)restartPolicy.MaxAttempts.Value;

        string? updateParallelism = service.Spec?.UpdateConfig != null
            ? service.Spec.UpdateConfig.Parallelism.ToString()
            : null;

        var draft = new DockerServiceDto
        {
            ServiceId = service.ID ?? string.Empty,
            ServiceName = service.Spec?.Name ?? string.Empty,
            StackName = stackName,
            Image = image,
            ImageDigest = imageDigest,
            ReplicasDesired = replicasDesired,
            ReplicasRunning = replicasRunning,
            Mode = serviceMode,
            Ports = ports,
            Volumes = volumes,
            NetworkIds = networkIds,
            Labels = redactedServiceLabels,
            ContainerLabels = redactedContainerLabels,
            Placement = placementDto,
            Tasks = tasks,
            DisplayOrder = displayOrder,
            CreatedAt = service.CreatedAt,
            UpdatedAt = service.UpdatedAt,
            UpdateParallelism = updateParallelism,
            UpdateFailureAction = service.Spec?.UpdateConfig?.FailureAction,
            RestartCondition = restartPolicy?.Condition,
            RestartMaxAttempts = restartMaxAttempts,
            PlacementDelayFormatted = restartDelay.HasValue ? FormatNanoseconds(restartDelay.Value) : null,
            DependsOn = ResolveDependencies(serviceLabels),
            Env = new Dictionary<string, string>(),
            HealthScore = 0,
            HealthStatus = "unknown",
            HealthFlags = [],
            HealthScoreVersion = DockerHealthCalculator.Version
        };

        var (score, status, flags) = DockerHealthCalculator.Calculate(draft, acknowledgement?.ClearedBeforeUtc);

        return new DockerServiceDto
        {
            ServiceId = draft.ServiceId,
            ServiceName = draft.ServiceName,
            StackName = draft.StackName,
            Image = draft.Image,
            ImageDigest = draft.ImageDigest,
            ReplicasDesired = draft.ReplicasDesired,
            ReplicasRunning = draft.ReplicasRunning,
            Mode = draft.Mode,
            Ports = draft.Ports,
            Volumes = draft.Volumes,
            NetworkIds = draft.NetworkIds,
            Labels = draft.Labels,
            ContainerLabels = draft.ContainerLabels,
            Env = draft.Env,
            Placement = draft.Placement,
            Tasks = draft.Tasks,
            DependsOn = draft.DependsOn,
            DisplayOrder = draft.DisplayOrder,
            CreatedAt = draft.CreatedAt,
            UpdatedAt = draft.UpdatedAt,
            UpdateParallelism = draft.UpdateParallelism,
            UpdateFailureAction = draft.UpdateFailureAction,
            RestartCondition = draft.RestartCondition,
            RestartMaxAttempts = draft.RestartMaxAttempts,
            PlacementDelayFormatted = draft.PlacementDelayFormatted,
            HealthScore = score,
            HealthStatus = status,
            HealthFlags = flags,
            HealthScoreVersion = DockerHealthCalculator.Version,
            HealthAcknowledgement = acknowledgement
        };
    }

    private Dictionary<string, string> RedactLabels(IDictionary<string, string> labels)
    {
        var result = new Dictionary<string, string>(labels.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in labels)
        {
            bool shouldRedact = _redactedSubstrings.Any(s =>
                key.Contains(s, StringComparison.OrdinalIgnoreCase));
            result[key] = shouldRedact ? "[redacted]" : value;
        }
        return result;
    }

    private static int ParseDisplayOrder(IDictionary<string, string> labels)
    {
        if (labels.TryGetValue("infinityai.display.order", out var raw) && int.TryParse(raw, out int order))
            return order;
        return int.MaxValue;
    }

    private static IReadOnlyList<string> ResolveDependencies(IDictionary<string, string> labels)
    {
        if (labels.TryGetValue("infinityai.depends_on", out var deps) && !string.IsNullOrWhiteSpace(deps))
            return deps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [];
    }

    private static string FormatNanoseconds(long ns)
    {
        var ts = TimeSpan.FromTicks(ns / 100);
        if (ts.TotalSeconds < 60) return $"{ts.TotalSeconds:F0}s";
        if (ts.TotalMinutes < 60) return $"{ts.TotalMinutes:F0}m";
        return $"{ts.TotalHours:F1}h";
    }
}
