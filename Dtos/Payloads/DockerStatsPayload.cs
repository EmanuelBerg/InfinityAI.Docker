namespace InfinityAI.Docker.Dtos.Payloads;

public sealed class DockerStatsPayload
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<DockerStatsDto> Stats { get; init; } = [];
    public DateTime CollectedAt { get; init; }
}
