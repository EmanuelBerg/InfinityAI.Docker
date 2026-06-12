namespace InfinityAI.Docker.Dtos.Messages;

public sealed class DockerInventorySnapshotMessage
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<string> StackNames { get; init; } = [];
    public int TotalServices { get; init; }
    public int TotalNodes { get; init; }
    public string OverallHealth { get; init; } = string.Empty;
    public DateTime PolledAt { get; init; }
}
