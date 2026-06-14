namespace InfinityAI.Docker.Dtos;

// Docker storage analysis: per-image and per-volume details.
public sealed class DockerStorageAnalysisDto
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CollectedAt { get; set; }

    public IReadOnlyList<DockerImageInfoDto> Images { get; set; } = [];
    public IReadOnlyList<DockerVolumeInfoDto> Volumes { get; set; } = [];

    // Aggregates
    public long TotalImageBytes { get; set; }
    public long ReclaimableImageBytes { get; set; } // dangling images
    public int DanglingImageCount { get; set; }
    public long TotalVolumeBytes { get; set; }
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public int TotalNetworks { get; set; }
}

public sealed class DockerImageInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime Created { get; set; }
    // Number of running containers using this image.
    public int ContainerCount { get; set; }
    public bool IsDangling { get; set; }
}

public sealed class DockerVolumeInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    // Size may be unavailable (null) for remote volumes or volumes that have not been inspected.
    public long? SizeBytes { get; set; }
    public int ContainerCount { get; set; }
}
