namespace InfinityAI.Docker.Dtos;

public sealed class DockerTaskDto
{
    public string TaskId { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string NodeHostname { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string DesiredState { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public int Slot { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public long RestartCount { get; init; }
}
