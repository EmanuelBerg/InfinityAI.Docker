using Docker.DotNet;
using InfinityAI.Docker.Configuration;
using Microsoft.Extensions.Options;

namespace InfinityAI.Docker.Services;

public sealed class DockerClientFactory(IOptions<DockerOptions> options) : IDisposable
{
    private readonly DockerClient _client = new DockerClientConfiguration(
        new Uri(options.Value.DockerSocketPath))
        .CreateClient();

    public DockerClient GetClient() => _client;

    public void Dispose() => _client.Dispose();
}
