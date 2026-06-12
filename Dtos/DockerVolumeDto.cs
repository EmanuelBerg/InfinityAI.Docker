namespace InfinityAI.Docker.Dtos;

public sealed class DockerVolumeDto
{
    public string Type { get; init; } = "volume";
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool ReadOnly { get; init; }
}
