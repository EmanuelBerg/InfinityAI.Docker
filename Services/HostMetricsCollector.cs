using System.Runtime.InteropServices;
using DockerModels = Docker.DotNet.Models;
using InfinityAI.Docker.Dtos;
using Microsoft.Extensions.Logging;

namespace InfinityAI.Docker.Services;

// Collects server-level resource metrics from /proc/ (Linux) and Docker Engine API.
// Never throws — individual metric failures are logged as warnings and the metric is
// left at its -1/unavailable sentinel so other metrics are unaffected.
public sealed class HostMetricsCollector(
    DockerClientFactory clientFactory,
    ILogger<HostMetricsCollector> logger)
{
    private readonly bool _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    // CPU: maintained across calls to compute delta between poll cycles.
    private long[] _prevCpuValues = [];

    public async Task<DockerHostMetricsDto> CollectAsync(CancellationToken ct)
    {
        var dto = new DockerHostMetricsDto { CollectedAt = DateTime.UtcNow };

        dto.Cpu    = CollectCpu();
        dto.Memory = CollectMemory();
        dto.Disk   = CollectDisk();
        dto.Load   = CollectLoad();
        dto.DockerCounts = await CollectDockerCountsAsync(ct);

        ComputeHostHealthScore(dto);
        return dto;
    }

    // ── CPU ──────────────────────────────────────────────────────────────────────

    private DockerCpuMetricsDto CollectCpu()
    {
        var result = new DockerCpuMetricsDto
        {
            CoreCount = Environment.ProcessorCount
        };

        if (!_isLinux)
        {
            result.UsagePercent = -1;
            return result;
        }

        try
        {
            // /proc/stat first line: cpu  user nice system idle iowait irq softirq steal guest guest_nice
            var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
            if (line is null) return result;

            var parts  = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = parts.Skip(1)
                              .Select(p => long.TryParse(p, out var v) ? v : 0L)
                              .Take(8)
                              .ToArray();

            if (values.Length < 4)
            {
                _prevCpuValues = values;
                return result;
            }

            if (_prevCpuValues.Length == values.Length)
            {
                long totalDelta = 0;
                for (int i = 0; i < values.Length; i++)
                    totalDelta += values[i] - _prevCpuValues[i];

                // Index 3 = idle, index 4 = iowait
                long idleDelta = (values[3] - _prevCpuValues[3])
                               + (values.Length > 4 ? values[4] - _prevCpuValues[4] : 0);

                if (totalDelta > 0)
                    result.UsagePercent = Math.Round((double)(totalDelta - idleDelta) / totalDelta * 100, 1);
            }

            _prevCpuValues = values;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[HOST-METRICS] Failed to read CPU stats from /proc/stat");
        }

        return result;
    }

    // ── Memory ───────────────────────────────────────────────────────────────────

    private DockerMemoryMetricsDto CollectMemory()
    {
        var result = new DockerMemoryMetricsDto();

        if (!_isLinux)
            return result;

        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long totalKb     = ParseMemInfoLine(lines, "MemTotal:");
            long availableKb = ParseMemInfoLine(lines, "MemAvailable:");

            if (totalKb <= 0) return result;

            result.TotalBytes  = totalKb * 1024;
            result.FreeBytes   = availableKb * 1024;
            result.UsedBytes   = result.TotalBytes - result.FreeBytes;
            result.UsagePercent = totalKb > 0
                ? Math.Round((double)result.UsedBytes / result.TotalBytes * 100, 1)
                : -1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[HOST-METRICS] Failed to read memory stats from /proc/meminfo");
        }

        return result;
    }

    private static long ParseMemInfoLine(string[] lines, string key)
    {
        var line = Array.Find(lines, l => l.StartsWith(key));
        if (line is null) return 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var v) ? v : 0;
    }

    // ── Disk ─────────────────────────────────────────────────────────────────────

    private DockerDiskMetricsDto CollectDisk()
    {
        var result = new DockerDiskMetricsDto();

        try
        {
            // On Linux: root drive. On Windows: C: drive. Gracefully skip if unavailable.
            var targetPath = _isLinux ? "/" : @"C:\";
            var drive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && string.Equals(
                    d.RootDirectory.FullName, targetPath, StringComparison.OrdinalIgnoreCase));

            if (drive is null)
            {
                // Fallback: largest ready drive
                drive = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .OrderByDescending(d => d.TotalSize)
                    .FirstOrDefault();
            }

            if (drive is null) return result;

            result.MountPoint  = drive.RootDirectory.FullName;
            result.TotalBytes  = drive.TotalSize;
            result.FreeBytes   = drive.AvailableFreeSpace;
            result.UsedBytes   = result.TotalBytes - result.FreeBytes;
            result.UsagePercent = result.TotalBytes > 0
                ? Math.Round((double)result.UsedBytes / result.TotalBytes * 100, 1)
                : -1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[HOST-METRICS] Failed to read disk stats");
        }

        return result;
    }

    // ── Load ─────────────────────────────────────────────────────────────────────

    private DockerLoadMetricsDto CollectLoad()
    {
        var result = new DockerLoadMetricsDto();

        if (!_isLinux)
            return result; // Windows has no /proc/loadavg

        try
        {
            var content = File.ReadAllText("/proc/loadavg");
            // Format: 0.52 0.48 0.41 2/178 12345
            var parts = content.Trim().Split(' ');
            if (parts.Length >= 3)
            {
                result.Load1  = TryParseDouble(parts[0]);
                result.Load5  = TryParseDouble(parts[1]);
                result.Load15 = TryParseDouble(parts[2]);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[HOST-METRICS] Failed to read load averages from /proc/loadavg");
        }

        return result;
    }

    // ── Docker counts ─────────────────────────────────────────────────────────────

    private async Task<DockerDockerCountsDto> CollectDockerCountsAsync(CancellationToken ct)
    {
        var result = new DockerDockerCountsDto();

        try
        {
            var client = clientFactory.GetClient();

            var imagesTask     = client.Images.ListImagesAsync(new DockerModels.ImagesListParameters { All = false }, ct);
            var containersTask = client.Containers.ListContainersAsync(
                new DockerModels.ContainersListParameters { All = true }, ct);
            var volumesTask    = client.Volumes.ListAsync(cancellationToken: ct);
            var networksTask   = client.Networks.ListNetworksAsync(new DockerModels.NetworksListParameters(), ct);

            await Task.WhenAll(imagesTask, containersTask, volumesTask, networksTask);

            var images     = await imagesTask;
            var containers = await containersTask;
            var volumes    = await volumesTask;
            var networks   = await networksTask;

            result.TotalImages      = images.Count;
            result.TotalImageBytes  = images.Sum(i => i.Size);
            result.TotalContainers  = containers.Count;
            result.RunningContainers = containers.Count(c =>
                string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase));
            result.StoppedContainers = result.TotalContainers - result.RunningContainers;
            result.TotalVolumes     = volumes.Volumes?.Count ?? 0;
            result.TotalNetworks    = networks.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[HOST-METRICS] Failed to collect Docker resource counts");
        }

        return result;
    }

    // ── Host health score ──────────────────────────────────────────────────────────

    private static void ComputeHostHealthScore(DockerHostMetricsDto dto)
    {
        var flags  = new List<string>();
        int score  = 100;

        // CPU penalties
        if (dto.Cpu.UsagePercent >= 0)
        {
            if (dto.Cpu.UsagePercent > 90) score -= 25;
            else if (dto.Cpu.UsagePercent > 80) score -= 12;
            else if (dto.Cpu.UsagePercent > 70) score -= 5;
        }

        // Memory penalties
        if (dto.Memory.UsagePercent >= 0)
        {
            if (dto.Memory.UsagePercent > 90) { score -= 25; flags.Add("memory_critical"); }
            else if (dto.Memory.UsagePercent > 85) { score -= 12; flags.Add("memory_high"); }
            else if (dto.Memory.UsagePercent > 75) score -= 5;
        }

        // Disk penalties (highest priority — disk pressure is catastrophic)
        if (dto.Disk.UsagePercent >= 0)
        {
            if (dto.Disk.UsagePercent >= 90) { score -= 30; flags.Add("disk_usage_critical"); }
            else if (dto.Disk.UsagePercent >= 80) { score -= 15; flags.Add("disk_usage_high"); }
            else if (dto.Disk.UsagePercent >= 70) score -= 5;
        }

        // Load average penalty (compare to core count)
        if (dto.Load.Load1 >= 0 && dto.Cpu.CoreCount > 0)
        {
            double loadRatio = dto.Load.Load1 / dto.Cpu.CoreCount;
            if (loadRatio > 2.0) score -= 10;
            else if (loadRatio > 1.0) score -= 5;
        }

        dto.HostHealthScore  = Math.Max(0, score);
        dto.HealthFlags      = flags;
        dto.HostHealthStatus = score >= 90 ? "healthy"
                             : score >= 65 ? "degraded"
                             : "unhealthy";
    }

    private static double TryParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1;
}
