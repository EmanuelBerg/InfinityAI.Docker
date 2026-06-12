namespace InfinityAI.Docker.Models;

public sealed class ServiceSnapshotHash
{
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string StackName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public int ReplicasDesired { get; init; }
    public int ReplicasRunning { get; init; }
    public string HealthStatus { get; init; } = string.Empty;
    public int HealthScore { get; init; }
    public string UpdatedAt { get; init; } = string.Empty;
    public string LabelsHash { get; init; } = string.Empty;
    public string TaskStateHash { get; init; } = string.Empty;
}
