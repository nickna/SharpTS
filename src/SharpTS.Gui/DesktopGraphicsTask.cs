namespace SharpTS.Gui;

/// <summary>A cooperative graphics job. Native raster/codec calls finish before cancellation is observed.</summary>
public sealed class DesktopGraphicsTask
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private bool _complete;
    private double _progress;
    public Task<object?> Result { get; }
    public double Progress => Volatile.Read(ref _progress);

    internal DesktopGraphicsTask(Func<string> work)
    {
        Result = Task.Run<object?>(() =>
        {
            try
            {
                using var scope = new GraphicsWorkScope(_cancellation.Token, value => Volatile.Write(ref _progress, value));
                GraphicsWorkScope.Checkpoint(0);
                string result = work();
                GraphicsWorkScope.Checkpoint(1);
                return result;
            }
            finally
            {
                lock (_gate) { _complete = true; _cancellation.Dispose(); }
            }
        });
    }

    public void Cancel()
    {
        lock (_gate) if (!_complete) _cancellation.Cancel();
    }
}

internal sealed class GraphicsWorkScope : IDisposable
{
    [ThreadStatic] private static GraphicsWorkScope? _current;
    private readonly GraphicsWorkScope? _previous = _current;
    private readonly CancellationToken _token;
    private readonly Action<double> _report;
    private double _progress;

    internal GraphicsWorkScope(CancellationToken token, Action<double> report)
    {
        _token = token;
        _report = report;
        _current = this;
    }

    internal static void Checkpoint(double? progress = null)
    {
        if (_current is not { } current) return;
        current._token.ThrowIfCancellationRequested();
        if (progress is double value && value > current._progress)
            current._report(current._progress = Math.Clamp(value, 0, 1));
    }

    public void Dispose() => _current = _previous;
}

public static partial class DesktopBridge
{
    public static DesktopGraphicsTask StartDrawingImage(string documentJson, string optionsJson)
    {
        var context = RequireContext();
        return new(() => DrawingGraphics.RenderDocumentToImageJson(context, documentJson, optionsJson));
    }

    public static DesktopGraphicsTask StartDrawingFill(string documentJson, string optionsJson)
    {
        var context = RequireContext();
        return new(() => DrawingGraphics.FloodFillDrawingJson(context, documentJson, optionsJson));
    }
}
