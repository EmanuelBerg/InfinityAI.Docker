using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services;

public static class DockerNetworkResolver
{
    public static IReadOnlyDictionary<string, List<string>> BuildNetworkToServiceMap(
        IReadOnlyList<DockerServiceDto> services)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var service in services)
        {
            foreach (var networkId in service.NetworkIds)
            {
                if (!map.TryGetValue(networkId, out var list))
                {
                    list = [];
                    map[networkId] = list;
                }
                list.Add(service.ServiceId);
            }
        }
        return map;
    }
}
