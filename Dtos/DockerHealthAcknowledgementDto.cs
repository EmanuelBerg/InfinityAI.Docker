namespace InfinityAI.Docker.Dtos;

public sealed class DockerHealthAcknowledgementDto
{
    public Guid Id { get; init; }
    public string ServiceId { get; init; } = string.Empty;
    public DateTime ClearedBeforeUtc { get; init; }
    public string? Reason { get; init; }
    public DateTime CreatedUtc { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByDisplayName { get; init; }
}
