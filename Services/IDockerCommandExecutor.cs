using InfinityAI.Docker.Dtos.Commands;

namespace InfinityAI.Docker.Services;

public interface IDockerCommandExecutor
{
    Task RestartServiceAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task UpgradeServiceAsync(DockerCommandMessage cmd, CancellationToken ct);
    Task StartLogStreamAsync(DockerCommandMessage cmd, CancellationToken workerCt);
    void StopLogStream(string subscriptionId);
}
