namespace InfinityAI.Docker.Dtos;

public sealed class DockerNetworkDto
{
    public string NetworkId { get; init; } = string.Empty;
    public string NetworkName { get; init; } = string.Empty;
    public string Driver { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public bool Internal { get; init; }
    public bool Attachable { get; init; }
    public IReadOnlyList<string> ConnectedServiceIds { get; init; } = [];
}
