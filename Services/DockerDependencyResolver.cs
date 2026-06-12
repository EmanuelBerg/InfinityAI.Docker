using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services;

public static class DockerDependencyResolver
{
    public static IReadOnlyList<DockerServiceDto> ApplyDisplayOrder(IReadOnlyList<DockerServiceDto> services)
    {
        return [.. services.OrderBy(s => s.DisplayOrder).ThenBy(s => s.ServiceName)];
    }

    public static IReadOnlyList<string> ResolveDependencyOrder(IReadOnlyList<DockerServiceDto> services)
    {
        var nameToId = services.ToDictionary(s => s.ServiceName, s => s.ServiceId, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>();
        var ordered = new List<string>();

        foreach (var service in services)
            Visit(service.ServiceId, services, nameToId, visited, ordered, new HashSet<string>());

        return ordered;
    }

    private static void Visit(
        string serviceId,
        IReadOnlyList<DockerServiceDto> services,
        Dictionary<string, string> nameToId,
        HashSet<string> visited,
        List<string> ordered,
        HashSet<string> visiting)
    {
        if (visited.Contains(serviceId)) return;
        if (visiting.Contains(serviceId)) return; // cycle — skip

        visiting.Add(serviceId);
        var service = services.FirstOrDefault(s => s.ServiceId == serviceId);
        if (service != null)
        {
            foreach (var dep in service.DependsOn)
            {
                if (nameToId.TryGetValue(dep, out var depId))
                    Visit(depId, services, nameToId, visited, ordered, visiting);
            }
        }
        visiting.Remove(serviceId);
        visited.Add(serviceId);
        ordered.Add(serviceId);
    }
}
