namespace InfinityAI.Docker.Services;

// Physical Redis key constants for the Docker Swarm inventory cache.
//
// Naming convention: infinity:docker:{logical-key}
//   infinity:  platform-wide Redis namespace prefix (all InfinityAI keys use this)
//   docker:    sub-namespace for the Docker Swarm management domain
//
// These constants are the single source of truth within InfinityAI.Docker.
// InfinityAI.Api (DockerCacheReader) holds its own copy of these strings —
// controlled duplication across project boundaries is the established pattern.
public static class DockerCacheKeys
{
    public const string SchemaVersion = "infinity:docker:schema:version";
    public const string StacksAll = "infinity:docker:stacks:all";
    public const string StackPrefix = "infinity:docker:stack:";
    public const string ServicesFlat = "infinity:docker:services:flat";
    public const string ServicePrefix = "infinity:docker:service:";
    public const string NodesAll = "infinity:docker:nodes:all";
    public const string SwarmOverview = "infinity:docker:swarm:overview";
    public const string StatsAll = "infinity:docker:stats:all";
    public const string StatsPrefix = "infinity:docker:stats:";

    public static string Stack(string stackName) => $"{StackPrefix}{stackName}";
    public static string Service(string serviceId) => $"{ServicePrefix}{serviceId}";
    public static string Stats(string serviceId) => $"{StatsPrefix}{serviceId}";
}
