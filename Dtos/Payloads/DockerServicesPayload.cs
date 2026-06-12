namespace InfinityAI.Docker.Dtos.Payloads;

public sealed class DockerServicesPayload
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<DockerServiceDto> Services { get; init; } = [];
    public DateTime PolledAt { get; init; }
}
