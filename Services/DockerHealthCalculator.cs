using InfinityAI.Docker.Dtos;

namespace InfinityAI.Docker.Services;

public static class DockerHealthCalculator
{
    public const int Version = 1;

    public static (int Score, string Status, List<string> Flags) Calculate(DockerServiceDto service)
    {
        var flags = new List<string>();
        int score = 100;

        // Factor 1: Replica ratio (40 points)
        if (service.ReplicasDesired > 0)
        {
            double ratio = (double)service.ReplicasRunning / service.ReplicasDesired;
            if (ratio < 1.0)
            {
                int penalty = (int)Math.Round((1.0 - ratio) * 40);
                score -= penalty;
                if (service.ReplicasRunning == 0)
                    flags.Add("no_running_replicas");
                else
                    flags.Add("partial_replicas");
            }
        }
        else if (service.Mode == "replicated")
        {
            score -= 40;
            flags.Add("zero_desired_replicas");
        }

        // Factor 2: Task failure states (20 points)
        var failedTasks = service.Tasks.Count(t =>
            t.State is "failed" or "rejected" or "orphaned");
        if (failedTasks > 0)
        {
            int penalty = Math.Min(20, failedTasks * 5);
            score -= penalty;
            flags.Add("task_failures");
        }

        // Factor 3: High restart count (15 points)
        long maxRestarts = service.Tasks.Any() ? service.Tasks.Max(t => t.RestartCount) : 0;
        if (maxRestarts >= 10)
        {
            score -= 15;
            flags.Add("high_restart_count");
        }
        else if (maxRestarts >= 3)
        {
            score -= 7;
            flags.Add("elevated_restart_count");
        }

        // Factor 4: Tasks in non-running desired=running state (10 points)
        var stuckTasks = service.Tasks.Count(t =>
            t.DesiredState == "running" && t.State is "starting" or "preparing" or "assigned" or "accepted");
        if (stuckTasks > 0)
        {
            score -= Math.Min(10, stuckTasks * 3);
            flags.Add("tasks_not_running");
        }

        // Factor 5: Image not pinned by digest (10 points)
        if (string.IsNullOrEmpty(service.ImageDigest))
        {
            score -= 10;
            flags.Add("image_not_pinned");
        }

        // Factor 6: Global mode with no placement constraints (5 points)
        if (service.Mode == "global" && (service.Placement == null || service.Placement.Constraints.Count == 0))
        {
            score -= 5;
            flags.Add("global_no_constraints");
        }

        score = Math.Max(0, score);

        string status = score switch
        {
            >= 90 => "healthy",
            >= 60 => "degraded",
            _ => "unhealthy"
        };

        return (score, status, flags);
    }
}
