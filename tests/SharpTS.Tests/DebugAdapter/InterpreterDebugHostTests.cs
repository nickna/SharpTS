using SharpTS.Execution;
using SharpTS.Execution.Debugging;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.DebugAdapter;

[Collection("DebugAdapterTests")]
public sealed class InterpreterDebugHostTests
{
    [Fact]
    public void NonDebugWorkerDoesNotAllocateAControllerOrHost()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "sharpts-worker-debug-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(directory, "idle-worker.ts");
            File.WriteAllText(script, "setInterval(() => {}, 1000);");
            using var parent = new Interpreter(TextWriter.Null, TextWriter.Null);
            using var worker = new SharpTSWorker(script, options: null, parentInterpreter: parent);
            var interpreterField = typeof(SharpTSWorker).GetField(
                "_workerInterpreter",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            Assert.True(SpinWait.SpinUntil(
                () => interpreterField.GetValue(worker) is Interpreter,
                TimeSpan.FromSeconds(5)));
            var workerInterpreter = Assert.IsType<Interpreter>(interpreterField.GetValue(worker));
            Assert.Null(workerInterpreter.DebugHost);
            Assert.Null(workerInterpreter.DebugController);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WorkerIdsAreMonotonicAndConfigurationIsInherited()
    {
        using var host = new InterpreterDebugHost();
        using var main = new Interpreter(TextWriter.Null, TextWriter.Null);
        host.ConfigureExceptionFilters(caught: true, uncaught: false, unhandledRejection: false);
        host.ConfigureJustMyCode(enabled: false);
        host.RegisterMain(main);

        int firstId;
        using (var first = new Interpreter(TextWriter.Null, TextWriter.Null))
        using (host.RegisterWorker(first, "first", static interpreter => interpreter.Shutdown()))
        {
            InterpreterDebugThreadInfo thread = Assert.Single(
                host.Threads, candidate => candidate.Id != 1);
            firstId = thread.Id;
            Assert.True(thread.Controller.BreakOnCaughtException);
            Assert.False(thread.Controller.BreakOnUncaughtException);
        }

        using var second = new Interpreter(TextWriter.Null, TextWriter.Null);
        using (host.RegisterWorker(second, "second", static interpreter => interpreter.Shutdown()))
        {
            InterpreterDebugThreadInfo thread = Assert.Single(
                host.Threads, candidate => candidate.Id != 1);
            Assert.True(thread.Id > firstId);
            Assert.NotEqual(firstId, thread.Id);
        }
    }

    [Fact]
    public void LateWorkerSourceRebindsStableBreakpointId()
    {
        using var host = new InterpreterDebugHost();
        using var main = new Interpreter(TextWriter.Null, TextWriter.Null);
        host.RegisterMain(main);
        ParsedModule module = ParseModule("C:/debug/late-worker.ts", "let workerValue = 42;");

        DebugBreakpointBinding pending = Assert.Single(host.SetBreakpoints(module.Path, [(1, 1)]));
        Assert.False(pending.Verified);
        DebugBreakpointBinding? changed = null;
        host.BreakpointChanged += binding => changed = binding;

        using var worker = new Interpreter(TextWriter.Null, TextWriter.Null);
        IDisposable registration = host.RegisterWorker(
            worker, "worker", static interpreter => interpreter.Shutdown());
        worker.DebugController!.RegisterModule(module);

        Assert.NotNull(changed);
        Assert.Equal(pending.Id, changed.Id);
        Assert.True(changed.Verified);
        Assert.Equal(1, changed.Line);
        int sourceId = Assert.Single(host.Sources).Id;

        changed = null;
        registration.Dispose();
        Assert.Empty(host.Sources);
        Assert.NotNull(changed);
        Assert.Equal(pending.Id, changed.Id);
        Assert.False(changed.Verified);
        Assert.True(sourceId > 0);
    }

    [Fact]
    public async Task CoordinatedPauseReportsEachParkedThreadAndResumeReleasesAll()
    {
        using var host = new InterpreterDebugHost();
        using var main = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var worker = new Interpreter(TextWriter.Null, TextWriter.Null);
        InterpreterDebugThreadInfo mainThread = host.RegisterMain(main);
        using IDisposable registration = host.RegisterWorker(
            worker, "worker", static interpreter => interpreter.Shutdown());
        InterpreterDebugThreadInfo workerThread = host.Threads.Single(thread => thread.Id != 1);
        ParsedModule mainModule = ParseModule("C:/debug/main.ts", "let mainValue = 1;");
        ParsedModule workerModule = ParseModule("C:/debug/worker.ts", "let workerValue = 2;");
        mainThread.Controller.RegisterModule(mainModule);
        workerThread.Controller.RegisterModule(workerModule);
        host.StartMain(stopOnEntry: false);

        var stopped = new List<InterpreterDebugStopEvent>();
        var allStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Stopped += stop =>
        {
            lock (stopped)
                stopped.Add(stop);
            if (stop.AllThreadsStopped)
                allStopped.TrySetResult();
        };

        host.RequestPause(mainThread.Id);
        Task mainPark = Task.Run(() => mainThread.Controller.OnSafePoint(
            main, mainModule.Statements[0], main.Environment, mainModule));
        Task workerPark = Task.Run(() => workerThread.Controller.OnSafePoint(
            worker, workerModule.Statements[0], worker.Environment, workerModule));
        await allStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (stopped)
        {
            Assert.Contains(stopped, stop => stop.ThreadId == mainThread.Id);
            Assert.Contains(stopped, stop => stop.ThreadId == workerThread.Id);
            Assert.True(stopped[^1].AllThreadsStopped);
            Assert.Single(stopped.Select(stop => stop.Epoch).Distinct());
        }

        host.Continue(workerThread.Id, DebugStepKind.Over);
        await Task.WhenAll(mainPark, workerPark).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(host.Threads, thread => Assert.Null(thread.CurrentStop));
    }

    [Fact]
    public async Task TerminationWakesEveryControllerAndWaitsForWorkerUnregistration()
    {
        using var host = new InterpreterDebugHost();
        using var main = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var worker = new Interpreter(TextWriter.Null, TextWriter.Null);
        InterpreterDebugThreadInfo mainThread = host.RegisterMain(main);
        var workerShutdown = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable registration = host.RegisterWorker(worker, "worker", interpreter =>
        {
            interpreter.Shutdown();
            workerShutdown.TrySetResult();
        });
        host.StartMain(stopOnEntry: false);

        Task termination = host.Terminate();
        await workerShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(termination.IsCompleted);
        Assert.Equal(DebugExecutionState.Terminating, mainThread.Controller.State);

        registration.Dispose();
        await termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(host.Threads, thread => thread.Id != 1);
    }

    [Fact]
    public async Task WorkerExitCanCompleteAnInProgressStopConvergence()
    {
        using var host = new InterpreterDebugHost();
        using var main = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var parkedWorker = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var exitingWorker = new Interpreter(TextWriter.Null, TextWriter.Null);
        InterpreterDebugThreadInfo mainThread = host.RegisterMain(main);
        using IDisposable parkedRegistration = host.RegisterWorker(
            parkedWorker, "parked", static interpreter => interpreter.Shutdown());
        IDisposable exitingRegistration = host.RegisterWorker(
            exitingWorker, "exiting", static interpreter => interpreter.Shutdown());
        InterpreterDebugThreadInfo parkedThread = host.Threads.Single(thread =>
            ReferenceEquals(thread.Interpreter, parkedWorker));
        ParsedModule mainModule = ParseModule("C:/debug/convergence-main.ts", "let mainValue = 1;");
        ParsedModule workerModule = ParseModule("C:/debug/convergence-worker.ts", "let workerValue = 2;");
        mainThread.Controller.RegisterModule(mainModule);
        parkedThread.Controller.RegisterModule(workerModule);
        host.StartMain(stopOnEntry: false);

        int parkedCount = 0;
        var twoParked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Stopped += stop =>
        {
            if (Interlocked.Increment(ref parkedCount) == 2)
                twoParked.TrySetResult();
            if (stop.AllThreadsStopped)
                allStopped.TrySetResult();
        };

        host.RequestPause(mainThread.Id);
        Task mainPark = Task.Run(() => mainThread.Controller.OnSafePoint(
            main, mainModule.Statements[0], main.Environment, mainModule));
        Task workerPark = Task.Run(() => parkedThread.Controller.OnSafePoint(
            parkedWorker, workerModule.Statements[0], parkedWorker.Environment, workerModule));
        await twoParked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(allStopped.Task.IsCompleted);

        exitingRegistration.Dispose();
        await allStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Continue(mainThread.Id);
        await Task.WhenAll(mainPark, workerPark).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ParsedModule ParseModule(string path, string source)
    {
        var document = new SourceDocument(path, source);
        var parser = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document);
        var result = parser.Parse();
        Assert.True(result.IsSuccess);
        return new ParsedModule(path, result.Statements) { Document = document };
    }
}
