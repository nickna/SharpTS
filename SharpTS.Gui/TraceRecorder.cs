using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Gui;

public sealed record GuiTraceEvent(
    long Sequence,
    double ElapsedMilliseconds,
    DateTimeOffset TimestampUtc,
    string Stage,
    int ManagedThreadId,
    string? SynchronizationContext,
    string? Detail);

public sealed class TraceRecorder
{
    private readonly object _gate = new();
    private readonly List<GuiTraceEvent> _events = [];
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly bool _enabled;
    private long _sequence;

    public TraceRecorder(int ownerThreadId, bool enabled = true)
    {
        OwnerThreadId = ownerThreadId;
        _enabled = enabled;
    }

    public int OwnerThreadId { get; }
    public event Action<GuiTraceEvent>? Recorded;

    public IReadOnlyList<GuiTraceEvent> Snapshot()
    {
        lock (_gate)
            return _events.ToArray();
    }

    public void Record(string stage, bool requireOwnerThread = true, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (!_enabled)
            return;
        int threadId = Environment.CurrentManagedThreadId;
        if (requireOwnerThread && threadId != OwnerThreadId)
        {
            throw new InvalidOperationException(
                $"Trace stage '{stage}' ran on managed thread {threadId}; owner is {OwnerThreadId}.");
        }

        var item = new GuiTraceEvent(
            Interlocked.Increment(ref _sequence),
            Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
            DateTimeOffset.UtcNow,
            stage,
            threadId,
            System.Threading.SynchronizationContext.Current?.GetType().FullName,
            detail);
        lock (_gate)
            _events.Add(item);
        Recorded?.Invoke(item);
    }

    public bool Contains(string stage)
    {
        lock (_gate)
            return _events.Any(item => item.Stage == stage);
    }

    public void WriteJson(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(
            Snapshot().ToArray(),
            GuiJsonContext.Indented.GuiTraceEventArray));
    }

    public IReadOnlyList<string> ValidateRequiredStages(bool headless)
    {
        string[] ownerStages =
        [
            "avalonia-setup", "guest-init-begin", "guest-init-end", "mount",
            "dispatcher-sentinel", "button-click-event", "guest-click",
            "coalesced-update-complete", "dependency-switch-complete",
            "reactive-update-complete", "transient-ref-cleaned",
            "guest-timer", "guest-async-resume", "unmount", "unsubscribe"
        ];
        var snapshot = Snapshot();
        var failures = new List<string>();
        foreach (string stage in ownerStages)
        {
            var matches = snapshot.Where(item => item.Stage == stage).ToArray();
            if (matches.Length == 0)
                failures.Add($"missing trace stage '{stage}'");
            else if (matches.Any(item => item.ManagedThreadId != OwnerThreadId))
                failures.Add($"trace stage '{stage}' ran off owner thread {OwnerThreadId}");
        }

        var completion = snapshot.SingleOrDefault(item => item.Stage == "task-complete-off-thread");
        if (completion == null)
            failures.Add("missing trace stage 'task-complete-off-thread'");
        else if (completion.ManagedThreadId == OwnerThreadId)
            failures.Add("task completion unexpectedly ran on the owner thread");

        string windowStage = headless ? "headless-window-shown" : "real-window-shown";
        if (!snapshot.Any(item => item.Stage == windowStage))
            failures.Add($"missing trace stage '{windowStage}'");

        return failures;
    }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(GuiTraceEvent[]))]
internal sealed partial class GuiJsonContext : JsonSerializerContext
{
    internal static GuiJsonContext Indented { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
    });
}
