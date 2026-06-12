namespace InfinityAI.Docker.Dtos;

public sealed class DockerServiceDto
{
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string StackName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string ImageDigest { get; init; } = string.Empty;
    public int ReplicasDesired { get; init; }
    public int ReplicasRunning { get; init; }
    public string Mode { get; init; } = "replicated";
    public string HealthStatus { get; init; } = "unknown";
    public int HealthScore { get; init; }
    public int HealthScoreVersion { get; init; } = 1;
    public IReadOnlyList<string> HealthFlags { get; init; } = [];
    public IReadOnlyList<DockerPortMappingDto> Ports { get; init; } = [];
    public IReadOnlyList<DockerVolumeDto> Volumes { get; init; } = [];
    public IReadOnlyList<string> NetworkIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ContainerLabels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public DockerPlacementDto? Placement { get; init; }
    public IReadOnlyList<DockerTaskDto> Tasks { get; init; } = [];
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public int DisplayOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdateParallelism { get; init; }
    public string? UpdateFailureAction { get; init; }
    public string? RestartCondition { get; init; }
    public long? RestartMaxAttempts { get; init; }
    public string? PlacementDelayFormatted { get; init; }
}
