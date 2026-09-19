using Prumo.Worker;

namespace Prumo.Domain.Tests;

public sealed class OperationalHealthTests
{
    [Fact]
    public void Worker_metrics_are_available_in_process_without_starting_a_server()
    {
        using var metrics = new WorkerMetrics();
        metrics.SetBacklog("AnalyzeDocument", 3);
        metrics.RecordCompleted("AnalyzeDocument", TimeSpan.FromMilliseconds(12.5));
        metrics.RecordFailed("AnalyzeDocument", TimeSpan.FromMilliseconds(7.5));
        metrics.RecordAiOutcome("fallback");

        var output = metrics.Export();
        Assert.Contains("prumo_worker_jobs_completed_total{job_type=\"AnalyzeDocument\"} 1", output);
        Assert.Contains("prumo_worker_jobs_failed_total{job_type=\"AnalyzeDocument\"} 1", output);
        Assert.Contains("prumo_worker_job_duration_ms_sum{job_type=\"AnalyzeDocument\"} 20", output);
        Assert.Contains("prumo_worker_jobs_backlog{job_type=\"AnalyzeDocument\"} 3", output);
        Assert.Contains("prumo_worker_ai_outcomes_total{outcome=\"fallback\"} 1", output);
    }
}
