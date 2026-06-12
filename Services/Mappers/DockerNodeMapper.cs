using Docker.DotNet.Models;
using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services.Mappers;

public static class DockerNodeMapper
{
    private static readonly string[] RedactedSubstrings =
        ["secret", "password", "token", "key", "credential", "auth"];

    public static DockerNodeDto Map(NodeListResponse node)
    {
        var rawLabels = node.Spec?.Labels ?? new Dictionary<string, string>();

        return new DockerNodeDto
        {
            NodeId = node.ID ?? string.Empty,
            Hostname = node.Description?.Hostname ?? string.Empty,
            Role = node.Spec?.Role ?? string.Empty,
            Availability = node.Spec?.Availability ?? string.Empty,
            State = node.Status?.State ?? string.Empty,
            EngineVersion = node.Description?.Engine?.EngineVersion ?? string.Empty,
            OsType = node.Description?.Platform?.OS ?? string.Empty,
            Architecture = node.Description?.Platform?.Architecture ?? string.Empty,
            CpuCount = (int)(node.Description?.Resources?.NanoCPUs / 1_000_000_000 ?? 0),
            MemoryBytes = (long)(node.Description?.Resources?.MemoryBytes ?? 0),
            Labels = RedactLabels(rawLabels),
            UpdatedAt = node.UpdatedAt,
            IsLeader = node.ManagerStatus?.Leader ?? false
        };
    }

    private static IReadOnlyDictionary<string, string> RedactLabels(IDictionary<string, string> labels)
    {
        var result = new Dictionary<string, string>(labels.Count);
        foreach (var (key, value) in labels)
        {
            bool shouldRedact = RedactedSubstrings.Any(s =>
                key.Contains(s, StringComparison.OrdinalIgnoreCase));
            result[key] = shouldRedact ? "[redacted]" : value;
        }
        return result;
    }
}
