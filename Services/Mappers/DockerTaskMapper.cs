using Docker.DotNet.Models;
using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services.Mappers;

public static class DockerTaskMapper
{
    // restartCount: number of previous tasks for this slot (computed by caller from task history).
    // NOT the container ExitCode — that field was incorrectly used here previously.
    public static DockerTaskDto Map(TaskResponse task, IDictionary<string, string> nodeHostnames, long restartCount = 0)
    {
        nodeHostnames.TryGetValue(task.NodeID ?? string.Empty, out var hostname);

        return new DockerTaskDto
        {
            TaskId = task.ID ?? string.Empty,
            ServiceId = task.ServiceID ?? string.Empty,
            NodeId = task.NodeID ?? string.Empty,
            NodeHostname = hostname ?? task.NodeID ?? string.Empty,
            State = task.Status != null ? task.Status.State.ToString().ToLowerInvariant() : string.Empty,
            DesiredState = task.DesiredState.ToString().ToLowerInvariant(),
            Image = task.Spec?.ContainerSpec?.Image ?? string.Empty,
            Slot = (int)task.Slot,
            ErrorMessage = task.Status?.Err,
            StartedAt = task.Status?.Timestamp,
            UpdatedAt = task.UpdatedAt,
            RestartCount = restartCount
        };
    }
}
