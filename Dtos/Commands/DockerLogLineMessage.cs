namespace InfinityAI.Docker.Dtos.Commands;

public sealed class DockerLogLineMessage
{
    public int SchemaVersion { get; init; } = 1;
    public string SubscriptionId { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    // stdout | stderr
    public string Source { get; init; } = "stdout";
    public string Line { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
