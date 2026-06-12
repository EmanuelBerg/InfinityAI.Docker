namespace InfinityAI.Docker.Dtos;

public sealed class DockerSwarmOverviewDto
{
    public string SwarmId { get; init; } = string.Empty;
    public int TotalNodes { get; init; }
    public int ManagerNodes { get; init; }
    public int WorkerNodes { get; init; }
    public int TotalServices { get; init; }
    public int TotalStacks { get; init; }
    public string OverallHealth { get; init; } = "unknown";
    public int HealthyServices { get; init; }
    public int DegradedServices { get; init; }
    public int UnhealthyServices { get; init; }
    public string DockerApiVersion { get; init; } = string.Empty;
    public string DockerEngineVersion { get; init; } = string.Empty;
    public DateTime PolledAt { get; init; }
}
