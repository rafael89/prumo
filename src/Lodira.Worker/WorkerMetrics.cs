using System.Diagnostics.Metrics;
using Lodira.Domain.Enums;

namespace Lodira.Worker;

/// <summary>Built-in metrics that can be consumed by a configured OpenTelemetry listener.</summary>
public sealed class WorkerMetrics
{
    private readonly Meter meter = new("Lodira.Worker", "1.0");
    private readonly Counter<long> completed;
    private readonly Counter<long> failed;
    private readonly Histogram<double> durationMilliseconds;
    private readonly Dictionary<string, long> backlog = [];
    private readonly object backlogLock = new();

    public WorkerMetrics()
    {
        completed = meter.CreateCounter<long>("lodira.worker.jobs.completed");
        failed = meter.CreateCounter<long>("lodira.worker.jobs.failed");
        durationMilliseconds = meter.CreateHistogram<double>("lodira.worker.job.duration", "ms");
        meter.CreateObservableGauge("lodira.worker.jobs.backlog", ObserveBacklog);
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

    private void Record(Counter<long> counter, string type, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("job_type", type);
        counter.Add(1, tag);
        durationMilliseconds.Record(duration.TotalMilliseconds, tag);
    }

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        lock (backlogLock)
        {
            return backlog.Select(item => new Measurement<long>(item.Value,
                new KeyValuePair<string, object?>("job_type", item.Key))).ToArray();
        }
    }
}
