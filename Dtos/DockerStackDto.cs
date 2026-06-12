namespace InfinityAI.Docker.Dtos;

public sealed class DockerStackDto
{
    public string StackName { get; init; } = string.Empty;
    public int ServiceCount { get; init; }
    public int HealthyServiceCount { get; init; }
    public int DegradedServiceCount { get; init; }
    public int UnhealthyServiceCount { get; init; }
    public string OverallHealth { get; init; } = "unknown";
    public IReadOnlyList<DockerServiceDto> Services { get; init; } = [];
}
