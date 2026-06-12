namespace InfinityAI.Docker.Dtos.Payloads;

public sealed class DockerStacksPayload
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<DockerStackDto> Stacks { get; init; } = [];
    public IReadOnlyList<DockerNodeDto> Nodes { get; init; } = [];
    public DockerSwarmOverviewDto? SwarmOverview { get; init; }
    public DateTime PolledAt { get; init; }
}
