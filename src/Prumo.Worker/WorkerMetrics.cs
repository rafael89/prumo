using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Prumo.Domain.Enums;

namespace Prumo.Worker;

/// <summary>Built-in metrics that can be consumed by a configured OpenTelemetry listener.</summary>
public sealed class WorkerMetrics : IDisposable
{
    private readonly Meter meter = new("Prumo.Worker", "1.0");
    private readonly Counter<long> completed;
    private readonly Counter<long> failed;
    private readonly Counter<long> aiOutcomes;
    private readonly Histogram<double> durationMilliseconds;
    private readonly Dictionary<string, long> backlog = [];
    private readonly object backlogLock = new();
    private readonly Dictionary<string, (long Completed, long Failed, double Duration)> totals = [];
    private readonly Dictionary<string, long> outcomes = [];
    public WorkerMetrics()
    {
        completed = meter.CreateCounter<long>("prumo.worker.jobs.completed");
        failed = meter.CreateCounter<long>("prumo.worker.jobs.failed");
        aiOutcomes = meter.CreateCounter<long>("prumo.worker.ai.outcomes");
        durationMilliseconds = meter.CreateHistogram<double>("prumo.worker.job.duration", "ms");
        meter.CreateObservableGauge("prumo.worker.jobs.backlog", ObserveBacklog);
    }


    public void SetBacklog(ProcessingJobType type, long count)
        => SetBacklog(type.ToString(), count);

    public void SetBacklog(string type, long count)
    {
        lock (backlogLock) backlog[type] = count;
    }

    public void RecordCompleted(ProcessingJobType type, TimeSpan duration)
        => RecordCompleted(type.ToString(), duration);

    public void RecordCompleted(string type, TimeSpan duration) => Record(completed, type, duration);

    public void RecordFailed(ProcessingJobType type, TimeSpan duration)
        => RecordFailed(type.ToString(), duration);

    public void RecordFailed(string type, TimeSpan duration) => Record(failed, type, duration);

    public void RecordAiOutcome(string outcome)
    {
        lock (backlogLock) outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        aiOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    private void Record(Counter<long> counter, string type, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("job_type", type);
        counter.Add(1, tag);
        durationMilliseconds.Record(duration.TotalMilliseconds, tag);
        lock (backlogLock)
        {
            var previous = totals.GetValueOrDefault(type);
            totals[type] = (previous.Completed + (counter == completed ? 1 : 0),
                previous.Failed + (counter == failed ? 1 : 0), previous.Duration + duration.TotalMilliseconds);
        }
    }

    public string Export()
    {
        var text = new StringBuilder();
        lock (backlogLock)
        {
            foreach (var (type, value) in totals.OrderBy(x => x.Key))
            {
                var label = $"job_type=\"{Escape(type)}\"";
                text.AppendLine($"prumo_worker_jobs_completed_total{{{label}}} {value.Completed}");
                text.AppendLine($"prumo_worker_jobs_failed_total{{{label}}} {value.Failed}");
                text.AppendLine($"prumo_worker_job_duration_ms_sum{{{label}}} {value.Duration.ToString(CultureInfo.InvariantCulture)}");
                text.AppendLine($"prumo_worker_job_duration_ms_count{{{label}}} {value.Completed + value.Failed}");
            }
            foreach (var (type, count) in backlog.OrderBy(x => x.Key))
                text.AppendLine($"prumo_worker_jobs_backlog{{job_type=\"{Escape(type)}\"}} {count}");
            foreach (var (outcome, count) in outcomes.OrderBy(x => x.Key))
                text.AppendLine($"prumo_worker_ai_outcomes_total{{outcome=\"{Escape(outcome)}\"}} {count}");
        }
        return text.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    public void Dispose() => meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        lock (backlogLock)
        {
            return backlog.Select(item => new Measurement<long>(item.Value,
                new KeyValuePair<string, object?>("job_type", item.Key))).ToArray();
        }
    }
}
