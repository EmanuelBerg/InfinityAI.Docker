namespace InfinityAI.Docker.Dtos;

// Host-level resource metrics collected every poll cycle.
// All byte values are raw bytes; UI layer converts to human-readable units.
public sealed class DockerHostMetricsDto
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CollectedAt { get; set; }

    public DockerCpuMetricsDto Cpu { get; set; } = new();
    public DockerMemoryMetricsDto Memory { get; set; } = new();
    public DockerDiskMetricsDto Disk { get; set; } = new();
    public DockerLoadMetricsDto Load { get; set; } = new();
    public DockerDockerCountsDto DockerCounts { get; set; } = new();

    // Computed host health score (0-100, same convention as service health score).
    public int HostHealthScore { get; set; }
    public string HostHealthStatus { get; set; } = "unknown"; // healthy / degraded / unhealthy

    // Disk pressure flags for dashboard alerting.
    public IReadOnlyList<string> HealthFlags { get; set; } = [];
}

public sealed class DockerCpuMetricsDto
{
    // CPU utilization percentage (0-100). -1 = unavailable.
    public double UsagePercent { get; set; } = -1;
    public int CoreCount { get; set; }
}

public sealed class DockerMemoryMetricsDto
{
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    // Utilization as percent (0-100). -1 = unavailable.
    public double UsagePercent { get; set; } = -1;
}

public sealed class DockerDiskMetricsDto
{
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    // Utilization as percent (0-100). -1 = unavailable.
    public double UsagePercent { get; set; } = -1;
    // Mount point that was measured (/ on Linux).
    public string MountPoint { get; set; } = string.Empty;
}

public sealed class DockerLoadMetricsDto
{
    // /proc/loadavg values. -1 = unavailable (Windows / read error).
    public double Load1 { get; set; } = -1;
    public double Load5 { get; set; } = -1;
    public double Load15 { get; set; } = -1;
}

public sealed class DockerDockerCountsDto
{
    public int TotalImages { get; set; }
    public long TotalImageBytes { get; set; }
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public int StoppedContainers { get; set; }
    public int TotalVolumes { get; set; }
    public int TotalNetworks { get; set; }
}
