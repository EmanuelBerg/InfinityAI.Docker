namespace InfinityAI.Docker.Configuration;

public sealed class DockerOptions
{
    public string DockerSocketPath { get; init; } = "unix:///var/run/docker.sock";
    public int PollIntervalSeconds { get; init; } = 30;
    public int StatsIntervalSeconds { get; init; } = 60;
    public StatsCollectionMode StatsCollectionMode { get; init; } = StatsCollectionMode.OnDemand;
    public string[] RedactedLabelSubstrings { get; init; } = ["secret", "password", "token", "key", "credential", "auth"];

    /// <summary>
    /// When non-empty, only stacks whose names start with one of these prefixes are included in cache and API responses.
    /// Empty array means all stacks are allowed (default dev/test behavior).
    /// Set DOCKER_ALLOWED_STACK_PREFIXES (comma-separated) or configure via DockerOptions:AllowedStackPrefixes.
    /// </summary>
    public string[] AllowedStackPrefixes { get; init; } = [];
}

public enum StatsCollectionMode
{
    Disabled,
    OnDemand,
    Continuous
}
