namespace InfinityAI.Docker.Dtos;

public sealed class DockerPlacementDto
{
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public IReadOnlyList<string> Preferences { get; init; } = [];
    public int? MaxReplicas { get; init; }
}
