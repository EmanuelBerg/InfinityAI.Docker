using DockerModels = Docker.DotNet.Models;
using InfinityAI.Docker.Dtos;
using Microsoft.Extensions.Logging;

namespace InfinityAI.Docker.Services;

// Collects detailed Docker storage information: per-image and per-volume sizes.
// More expensive than HostMetricsCollector — runs on same poll cycle but failures
// are non-fatal and result in a null/empty analysis.
public sealed class DockerStorageAnalyzer(
    DockerClientFactory clientFactory,
    ILogger<DockerStorageAnalyzer> logger)
{
    public async Task<DockerStorageAnalysisDto?> AnalyzeAsync(CancellationToken ct)
    {
        try
        {
            var client = clientFactory.GetClient();

            var imagesTask     = client.Images.ListImagesAsync(new DockerModels.ImagesListParameters { All = true }, ct);
            var containersTask = client.Containers.ListContainersAsync(new DockerModels.ContainersListParameters { All = true }, ct);
            var volumesTask    = client.Volumes.ListAsync(cancellationToken: ct);
            var networksTask   = client.Networks.ListNetworksAsync(new DockerModels.NetworksListParameters(), ct);

            await Task.WhenAll(imagesTask, containersTask, volumesTask, networksTask);

            var images     = await imagesTask;
            var containers = await containersTask;
            var volumes    = await volumesTask;
            var networks   = await networksTask;

            // Build image → containers map (by image ID)
            var containersByImage = containers
                .Where(c => c.ImageID is not null)
                .GroupBy(c => c.ImageID!)
                .ToDictionary(g => g.Key, g => g.Count());

            var imageDtos = images.Select(img =>
            {
                var repoTags = img.RepoTags?.FirstOrDefault() ?? "<none>:<none>";
                var colon    = repoTags.LastIndexOf(':');
                var repo     = colon >= 0 ? repoTags[..colon]       : repoTags;
                var tag      = colon >= 0 ? repoTags[(colon + 1)..] : string.Empty;
                var isDangling = string.IsNullOrEmpty(img.RepoTags?.FirstOrDefault())
                              || img.RepoTags!.FirstOrDefault() == "<none>:<none>";

                containersByImage.TryGetValue(img.ID ?? string.Empty, out var containerCount);

                return new DockerImageInfoDto
                {
                    Id             = img.ID ?? string.Empty,
                    Repository     = repo,
                    Tag            = tag,
                    SizeBytes      = img.Size,
                    Created        = img.Created,
                    ContainerCount = containerCount,
                    IsDangling     = isDangling
                };
            }).OrderByDescending(i => i.SizeBytes).ToList();

            var volumeDtos = (volumes.Volumes ?? []).Select(v => new DockerVolumeInfoDto
            {
                Name           = v.Name ?? string.Empty,
                Driver         = v.Driver ?? string.Empty,
                ContainerCount = 0 // would require inspect per volume — skipped for performance
            }).ToList();

            var danglingImages   = imageDtos.Where(i => i.IsDangling).ToList();
            long totalImageBytes = imageDtos.Where(i => !i.IsDangling).Sum(i => i.SizeBytes);
            long reclaimable     = danglingImages.Sum(i => i.SizeBytes);

            return new DockerStorageAnalysisDto
            {
                CollectedAt           = DateTime.UtcNow,
                Images                = imageDtos.Where(i => !i.IsDangling).ToList(),
                Volumes               = volumeDtos,
                TotalImageBytes       = totalImageBytes,
                ReclaimableImageBytes = reclaimable,
                DanglingImageCount    = danglingImages.Count,
                TotalContainers       = containers.Count,
                RunningContainers     = containers.Count(c =>
                    string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase)),
                TotalNetworks         = networks.Count
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DOCKER-STORAGE] Failed to collect storage analysis");
            return null;
        }
    }
}
