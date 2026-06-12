using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services;

public static class DockerStackBuilder
{
    public static IReadOnlyList<DockerStackDto> Build(IReadOnlyList<DockerServiceDto> services)
    {
        var grouped = services
            .GroupBy(s => string.IsNullOrEmpty(s.StackName) ? "_standalone" : s.StackName)
            .OrderBy(g => g.Key);

        return grouped.Select(g =>
        {
            var stackServices = DockerDependencyResolver.ApplyDisplayOrder(g.ToList());
            int healthy = stackServices.Count(s => s.HealthStatus == "healthy");
            int degraded = stackServices.Count(s => s.HealthStatus == "degraded");
            int unhealthy = stackServices.Count(s => s.HealthStatus == "unhealthy");

            string overallHealth = unhealthy > 0 ? "unhealthy"
                : degraded > 0 ? "degraded"
                : "healthy";

            return new DockerStackDto
            {
                StackName = g.Key,
                ServiceCount = stackServices.Count,
                HealthyServiceCount = healthy,
                DegradedServiceCount = degraded,
                UnhealthyServiceCount = unhealthy,
                OverallHealth = overallHealth,
                Services = stackServices
            };
        }).ToList();
    }
}
