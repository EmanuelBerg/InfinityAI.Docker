using InfinityAI.Docker.Dtos.Commands;

namespace InfinityAI.Docker.Services;

public interface IDockerCommandExecutor
{
    Task RestartServiceAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task UpgradeServiceAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task StartLogStreamAsync(DockerCommandMessage cmd, CancellationToken workerCt);
    void StopLogStream(string subscriptionId);

    // Cleanup operations (Phase 5)
    Task PruneImagesAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task PruneContainersAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task PruneVolumesAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task PruneSystemAsync(DockerCommandMessage cmd, CancellationToken ct);
}
