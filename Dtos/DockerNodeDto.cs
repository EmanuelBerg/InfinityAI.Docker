namespace InfinityAI.Docker.Dtos;

public sealed class DockerNodeDto
{
    public string NodeId { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Availability { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string EngineVersion { get; init; } = string.Empty;
    public string OsType { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public int CpuCount { get; init; }
    public long MemoryBytes { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public DateTime? UpdatedAt { get; init; }
    public bool IsLeader { get; init; }
}
