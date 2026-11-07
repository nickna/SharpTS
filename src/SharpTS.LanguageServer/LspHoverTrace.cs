using System.Diagnostics;

namespace SharpTS.LanguageServer;

/// <summary>Bounded phase markers for investigating interop hover timeouts over stdio.</summary>
internal sealed class LspHoverTrace : IDisposable
{
    private const int EventLimit = 512;
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("SHARPTS_LSP_HOVER_TRACE") == "1";
    private static readonly AsyncLocal<LspHoverTrace?> Current = new();
    private static int _nextOperation;
    private static int _events;

    private readonly string _operation;
    private readonly int _id = Interlocked.Increment(ref _nextOperation);
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly LspHoverTrace? _previous;

    private LspHoverTrace(string operation)
    {
        _operation = operation;
        _previous = Current.Value;
        Current.Value = this;
        Write("enter");
    }

    public static LspHoverTrace? Begin(string operation) =>
        Enabled && Volatile.Read(ref _events) < EventLimit ? new LspHoverTrace(operation) : null;

    public static void Mark(string phase)
    {
        if (Enabled) Current.Value?.Write(phase);
    }

    private void Write(string phase)
    {
        if (Volatile.Read(ref _events) >= EventLimit || Interlocked.Increment(ref _events) > EventLimit)
            return;

        try
        {
            Console.Error.WriteLine(FormattableString.Invariant(
                $"[sharpts-lsp-hover] utc={DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} op={_id} kind={_operation} phase={phase} elapsedMs={Stopwatch.GetElapsedTime(_start).TotalMilliseconds:F3}"));
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Write("exit");
        Current.Value = _previous;
    }
}
