using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace SharpTS.ProcessTreeFixture;

/// <summary>
/// Invokes the real test helper in a fresh process, without a test-project reference.
/// The artificial backlog demonstrates scheduling sensitivity, not a historical cause.
/// </summary>
internal sealed class StandaloneCaptureScheduling
{
    private const int OverallMilliseconds = 15000;
    private const int ChildMilliseconds = 15000;
    private const int BacklogWorkMilliseconds = 8000;
    private readonly ManualResetEventSlim _done = new(false);
    private readonly ConcurrentQueue<string> _cleanup = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _directory;
    private readonly int _backlog;
    private readonly MethodInfo _helper;
    private string PidPath => Path.Combine(_directory, "owned-child.pid");
    private string DiagnosticDirectory => Path.Combine(_directory, "helper-diagnostics");
    private string? _output, _errorOutput, _error, _diagnosticPath;
    private JsonElement? _diagnostic;
    private bool _callerIsPoolThread;
    private int _queued, _started, _finished;
    private string _outcome = "not-started";
    private int _exitCode = 2;

    private StandaloneCaptureScheduling(string testsPath, string directory, int backlog)
    {
        _directory = directory;
        _backlog = backlog;
        string testsDirectory = Path.GetDirectoryName(testsPath)!;
        var resolver = new AssemblyDependencyResolver(testsPath);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? path = resolver.ResolveAssemblyToPath(name);
            string adjacent = Path.Combine(testsDirectory, name.Name + ".dll");
            if (path is null && File.Exists(adjacent)) path = adjacent;
            return path is null ? null : context.LoadFromAssemblyPath(path);
        };
        Assembly tests = AssemblyLoadContext.Default.LoadFromAssemblyPath(testsPath);
        RuntimeHelpers.RunModuleConstructor(tests.ManifestModule.ModuleHandle);
        _helper = tests.GetType("SharpTS.Tests.CompilerTests.StandaloneDllTests", throwOnError: true)!
            .GetMethod("ExecuteProcessIsolated", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("ExecuteProcessIsolated");
        _ = _helper.GetParameters();
        RuntimeHelpers.PrepareMethod(_helper.MethodHandle);
        Directory.CreateDirectory(DiagnosticDirectory);
        Environment.SetEnvironmentVariable("SHARPTS_STANDALONE_DIAGNOSTICS_DIR", DiagnosticDirectory);
    }

    public static int Run(string testsPath, string directory, int backlog)
    {
        // The test module initializer replaces Console writers. Keep reporting
        // independent of its muted, unscoped console proxies.
        TextWriter report = Console.Out;
        TextWriter errors = Console.Error;
        try
        {
            if (backlog is not (0 or 128)) throw new ArgumentOutOfRangeException(nameof(backlog));
            var probe = new StandaloneCaptureScheduling(Path.GetFullPath(testsPath), directory, backlog);
            ThreadPool.GetMinThreads(out _, out int io);
            if (!ThreadPool.SetMinThreads(4, io)) throw new InvalidOperationException("Cannot set fixture worker minimum.");
            ThreadPool.UnsafeQueueUserWorkItem(static state => state.Invoke(), probe, preferLocal: false);
            bool completed = probe._done.Wait(probe.RemainingMilliseconds);
            if (!completed) probe.StopChild();
            report.WriteLine(JsonSerializer.Serialize(new
            {
                outcome = completed ? probe._outcome : "overall-deadline",
                processId = Environment.ProcessId,
                callerIsThreadPoolThread = probe._callerIsPoolThread,
                backlogRequested = backlog,
                backlogQueued = Volatile.Read(ref probe._queued),
                backlogStarted = Volatile.Read(ref probe._started),
                backlogFinished = Volatile.Read(ref probe._finished),
                childBudgetMilliseconds = ChildMilliseconds,
                drainBudgetMilliseconds = 5000,
                overallBudgetMilliseconds = OverallMilliseconds,
                elapsedMilliseconds = probe._clock.Elapsed.TotalMilliseconds,
                output = probe._output, standardError = probe._errorOutput, error = probe._error,
                diagnosticPath = probe._diagnosticPath, diagnostic = probe._diagnostic,
                cleanup = probe._cleanup.ToArray()
            }));
            report.Flush();
            // Do not join the artificial background backlog on process exit.
            Environment.Exit(completed ? probe._exitCode : 124);
            return 124;
        }
        catch (Exception exception)
        {
            errors.WriteLine(exception);
            return 2;
        }
    }

    public static int RunChild(string pidPath)
    {
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(pidPath, $"{process.Id}|{process.StartTime.ToUniversalTime().Ticks}");
        Console.Out.Write("child-start|");
        Console.Out.Flush();
        Thread.Sleep(1500);
        Console.Error.Write("child-stderr");
        Console.Error.Flush();
        Console.Out.Write("child-end");
        Console.Out.Flush();
        return 0;
    }

    private int RemainingMilliseconds => (int)Math.Clamp(
        OverallMilliseconds - _clock.ElapsedMilliseconds, 0, OverallMilliseconds);

    private void Invoke()
    {
        try
        {
            _callerIsPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            for (int index = 0; index < _backlog; index++)
            {
                Interlocked.Increment(ref _queued);
                ThreadPool.UnsafeQueueUserWorkItem(static state =>
                {
                    Interlocked.Increment(ref state._started);
                    Thread.Sleep(BacklogWorkMilliseconds);
                    Interlocked.Increment(ref state._finished);
                }, this, preferLocal: false);
            }
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = _directory
            };
            start.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
            start.ArgumentList.Add("capture-scheduling-child");
            start.ArgumentList.Add(PidPath);
            object?[] arguments = _helper.GetParameters().Select(parameter => parameter.Name switch
            {
                "psi" => (object)start,
                "timeoutMs" => Math.Min(ChildMilliseconds, RemainingMilliseconds),
                "timeoutStartsAfterOutput" => null,
                "readinessTimeoutMs" => ChildMilliseconds,
                "verifyStandardError" => (Action<string>)(value => _errorOutput = value),
                "standardInput" => null,
                "diagnosticDllPath" => typeof(ProcessTreeFixtureMarker).Assembly.Location,
                _ when parameter.HasDefaultValue => parameter.DefaultValue,
                _ => throw new InvalidOperationException("Unexpected helper parameter: " + parameter.Name)
            }).ToArray();
            _output = (string?)_helper.Invoke(null, arguments);
            _outcome = "helper-returned";
            _exitCode = 0;
        }
        catch (Exception exception)
        {
            Exception actual = exception is TargetInvocationException { InnerException: Exception inner }
                ? inner : exception;
            _error = actual.ToString();
            _diagnosticPath = actual.Data["StandaloneProcessDiagnosticPath"] as string;
            _outcome = "helper-failed";
        }
        finally
        {
            try
            {
                _diagnosticPath ??= Directory.EnumerateFiles(DiagnosticDirectory, "*.json").SingleOrDefault();
                if (_diagnosticPath is not null)
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(_diagnosticPath));
                    _diagnostic = document.RootElement.Clone();
                }
            }
            catch (Exception exception) { _error += "\nDiagnostic collection: " + exception; _exitCode = 2; }
            StopChild();
            _done.Set();
        }
    }

    private void StopChild()
    {
        if (!File.Exists(PidPath)) return;
        try
        {
            string[] identity = File.ReadAllText(PidPath).Split('|');
            using var child = Process.GetProcessById(int.Parse(identity[0]));
            if (child.StartTime.ToUniversalTime().Ticks != long.Parse(identity[1]))
            {
                _cleanup.Enqueue("PID reused; no termination requested.");
                return;
            }
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            _cleanup.Enqueue("Owned child exit observed or termination requested.");
        }
        catch (ArgumentException) { _cleanup.Enqueue("Owned child already reaped."); }
        catch (Exception exception) { _cleanup.Enqueue(exception.ToString()); }
    }
}
