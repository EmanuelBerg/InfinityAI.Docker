namespace InfinityAI.Docker.Dtos.Commands;

// Controlled duplication of InfinityAI.Api.Dtos.Docker.DockerCommandMessage.
public sealed class DockerCommandMessage
{
    public int SchemaVersion { get; init; } = 1;
    public Guid OperationId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string StackName { get; init; } = string.Empty;
    public Dictionary<string, string> Parameters { get; init; } = [];
    public string RequestedBy { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
}
