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
                containersByImage.TryGetValue(img.ID ?? string.Empty, out var containerCount);

                // Prefer a repo:tag entry; fall back to the first digest reference.
                // Images in Swarm pulled by digest have empty RepoTags but non-empty RepoDigests.
                var firstTag    = img.RepoTags?.FirstOrDefault(t => t != "<none>:<none>");
                var firstDigest = img.RepoDigests?.FirstOrDefault(d => d != "<none>@<none>");
                var reference   = firstTag ?? firstDigest ?? "<none>:<none>";

                string repo, tag;
                if (firstTag is not null)
                {
                    var colon = reference.LastIndexOf(':');
                    repo = colon >= 0 ? reference[..colon]       : reference;
                    tag  = colon >= 0 ? reference[(colon + 1)..] : string.Empty;
                }
                else if (firstDigest is not null)
                {
                    // Display as repo@sha256:short
                    var at   = firstDigest.LastIndexOf('@');
                    repo = at >= 0 ? firstDigest[..at] : firstDigest;
                    tag  = at >= 0 ? firstDigest[(at + 1)..] : string.Empty; // e.g. "sha256:abcd1234"
                }
                else
                {
                    repo = "<none>";
                    tag  = "<none>";
                }

                // An image is truly dangling only when it has no tag, no digest reference,
                // and no running container uses it. Swarm images pulled by digest are NOT dangling.
                var isDangling = firstTag is null
                              && firstDigest is null
                              && containerCount == 0;

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

            // Total size includes all images on disk. Reclaimable = only truly dangling ones.
            long totalImageBytes = imageDtos.Sum(i => i.SizeBytes);
            long reclaimable     = imageDtos.Where(i => i.IsDangling).Sum(i => i.SizeBytes);
            var  danglingImages  = imageDtos.Where(i => i.IsDangling).ToList();

            return new DockerStorageAnalysisDto
            {
                CollectedAt           = DateTime.UtcNow,
                Images                = imageDtos, // all images, including digest-only Swarm images
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
