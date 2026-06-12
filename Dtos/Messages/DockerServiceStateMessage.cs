namespace InfinityAI.Docker.Dtos.Messages;

public sealed class DockerServiceStateMessage
{
    public int SchemaVersion { get; init; } = 1;
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string StackName { get; init; } = string.Empty;
    public string HealthStatus { get; init; } = string.Empty;
    public int HealthScore { get; init; }
    public int ReplicasDesired { get; init; }
    public int ReplicasRunning { get; init; }
    public IReadOnlyList<string> ChangeTypes { get; init; } = [];
    public string ChangeSummary { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}
