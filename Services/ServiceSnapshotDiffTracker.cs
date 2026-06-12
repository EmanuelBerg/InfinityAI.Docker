using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfinityAI.Docker.Dtos;
using InfinityAI.Docker.Models;

namespace InfinityAI.Docker.Services;

public sealed class ServiceSnapshotDiffTracker
{
    private readonly ConcurrentDictionary<string, ServiceSnapshotHash> _snapshots = new();

    public (IReadOnlyList<DockerServiceDto> Changed,
            IReadOnlyDictionary<string, (IReadOnlyList<string> ChangeTypes, string ChangeSummary)> ChangeInfo)
        ComputeDiff(IReadOnlyList<DockerServiceDto> current)
    {
        var changed = new List<DockerServiceDto>();
        var changeInfo = new Dictionary<string, (IReadOnlyList<string>, string)>();

        foreach (var service in current)
        {
            var newHash = BuildHash(service);
            if (_snapshots.TryGetValue(service.ServiceId, out var prev))
            {
                var (types, summary) = ComputeChanges(prev, newHash);
                if (types.Count > 0)
                {
                    changed.Add(service);
                    changeInfo[service.ServiceId] = (types, summary);
                }
            }
            else
            {
                changed.Add(service);
                changeInfo[service.ServiceId] = (["discovered"], $"Service {service.ServiceName} discovered");
            }

            _snapshots[service.ServiceId] = newHash;
        }

        // Detect removed services
        var currentIds = current.Select(s => s.ServiceId).ToHashSet();
        var removedIds = _snapshots.Keys.Except(currentIds).ToList();
        foreach (var id in removedIds)
            _snapshots.TryRemove(id, out _);

        return (changed, changeInfo);
    }

    private static ServiceSnapshotHash BuildHash(DockerServiceDto service)
    {
        var taskStates = string.Join("|", service.Tasks.OrderBy(t => t.Slot).Select(t => $"{t.Slot}:{t.State}:{t.RestartCount}"));
        var labelsJson = JsonSerializer.Serialize(service.Labels.OrderBy(kv => kv.Key));

        return new ServiceSnapshotHash
        {
            ServiceId = service.ServiceId,
            ServiceName = service.ServiceName,
            StackName = service.StackName,
            Image = service.Image,
            ReplicasDesired = service.ReplicasDesired,
            ReplicasRunning = service.ReplicasRunning,
            HealthStatus = service.HealthStatus,
            HealthScore = service.HealthScore,
            UpdatedAt = service.UpdatedAt.ToString("O"),
            LabelsHash = ComputeMd5(labelsJson),
            TaskStateHash = ComputeMd5(taskStates)
        };
    }

    private static (List<string> Types, string Summary) ComputeChanges(ServiceSnapshotHash prev, ServiceSnapshotHash curr)
    {
        var types = new List<string>();
        var parts = new List<string>();

        if (prev.Image != curr.Image)
        {
            types.Add("image_changed");
            parts.Add($"image: {prev.Image} → {curr.Image}");
        }

        if (prev.ReplicasDesired != curr.ReplicasDesired || prev.ReplicasRunning != curr.ReplicasRunning)
        {
            types.Add("replicas_changed");
            parts.Add($"replicas: {prev.ReplicasRunning}/{prev.ReplicasDesired} → {curr.ReplicasRunning}/{curr.ReplicasDesired}");
        }

        if (prev.HealthStatus != curr.HealthStatus)
        {
            types.Add("health_changed");
            parts.Add($"health: {prev.HealthStatus} → {curr.HealthStatus}");
        }

        if (prev.TaskStateHash != curr.TaskStateHash && !types.Contains("replicas_changed"))
        {
            types.Add("task_state_changed");
            parts.Add("task states changed");
        }

        if (prev.LabelsHash != curr.LabelsHash)
        {
            types.Add("labels_changed");
            parts.Add("labels changed");
        }

        string summary = types.Count > 0
            ? $"{curr.ServiceName}: {string.Join("; ", parts)}"
            : string.Empty;

        return (types, summary);
    }

    private static string ComputeMd5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
