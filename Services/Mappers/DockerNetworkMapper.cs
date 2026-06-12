using Docker.DotNet.Models;
using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services.Mappers;

public static class DockerNetworkMapper
{
    public static DockerNetworkDto Map(NetworkResponse network, IReadOnlyList<string> connectedServiceIds)
    {
        return new DockerNetworkDto
        {
            NetworkId = network.ID ?? string.Empty,
            NetworkName = network.Name ?? string.Empty,
            Driver = network.Driver ?? string.Empty,
            Scope = network.Scope ?? string.Empty,
            Internal = network.Internal,
            Attachable = network.Attachable,
            ConnectedServiceIds = connectedServiceIds
        };
    }
}
