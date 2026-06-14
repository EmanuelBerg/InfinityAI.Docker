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

    public const string HealthAckPrefix = "infinity:docker:health:ack:";
    public static string HealthAck(string serviceId) => $"{HealthAckPrefix}{serviceId}";

    // Host-level resource metrics (Phase 2+)
    public const string HostMetrics        = "infinity:docker:host:metrics";
    public const string HostMetricsHistory = "infinity:docker:host:metrics:history";

    // Docker storage analysis (Phase 4)
    public const string StorageAnalysis = "infinity:docker:storage:analysis";

    // Maximum history samples to retain: 24h ÷ 30s = 2880 entries
    public const int HostMetricsHistoryMaxEntries = 2880;
}
