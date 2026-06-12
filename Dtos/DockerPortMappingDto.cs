namespace InfinityAI.Docker.Dtos;

public sealed class DockerPortMappingDto
{
    public int? PublishedPort { get; init; }
    public int TargetPort { get; init; }
    public string Protocol { get; init; } = "tcp";
    public string PublishMode { get; init; } = "ingress";
}
