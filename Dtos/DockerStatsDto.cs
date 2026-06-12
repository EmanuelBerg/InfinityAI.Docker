namespace InfinityAI.Docker.Dtos;

public sealed class DockerStatsDto
{
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public long MemoryUsageBytes { get; init; }
    public long MemoryLimitBytes { get; init; }
    public double MemoryPercent { get; init; }
    public long NetworkRxBytes { get; init; }
    public long NetworkTxBytes { get; init; }
    public long BlockReadBytes { get; init; }
    public long BlockWriteBytes { get; init; }
    public DateTime CollectedAt { get; init; }
}
