using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmExecutionOwnershipTests
{
    [Fact]
    public void CompletedPromiseAndExactMethodsKeepTheirRealOwnerAcrossCollection()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var promise = VmModuleInterpreter.ExecuteInNewContext("Promise.resolve(1)", null, parent)!;
        Assert.True(VmModuleInterpreter.IsVmValue(promise));
        var then = VmModuleInterpreter.ReadVmValueProperty(promise, "then")!;
        var constructor = VmModuleInterpreter.ReadVmValueProperty(promise, "constructor")!;
        var prototype = VmModuleInterpreter.GetVmValuePrototype(promise);
        Assert.True(VmModuleInterpreter.IsVmCallableValue(then));
        Assert.Same(prototype, VmModuleInterpreter.ReadVmValueProperty(constructor, "prototype"));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Same(then, VmModuleInterpreter.ReadVmValueProperty(promise, "then"));
        double observed = 0;
        var callback = BuiltInMethod.CreateV2("observe", 1, (_, _, args) =>
        { observed = (double)args[0].ToObject()!; return RuntimeValue.Undefined; });
        VmModuleInterpreter.InvokeVmCallable(then, promise, [callback]);
        parent.ProcessMicrotasks();
        Assert.Equal(1d, observed);
        Assert.Equal(0, parent.ActiveHandleCount);
        parent.Dispose();
        Assert.True(VmModuleInterpreter.IsVmValue(promise));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(promise, "then"));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.InvokeVmCallable(then, promise, [callback]));
    }

    [Fact]
    public void ShutdownRejectsNewWorkAndDisposesTheActiveEvaluatorAfterItsScopeUnwinds()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var child = new Interpreter(new StringWriter(), new StringWriter());
        var lease = VmEvaluationLease.Create(child, parent, static () => { });
        Assert.NotNull(lease);
        using (lease.Enter())
        {
            Exception? failure = null;
            using var finished = new ManualResetEventSlim();
            _ = Task.Run(() =>
            {
                try { parent.Shutdown(); }
                catch (Exception exception) { failure = exception; }
                finally { finished.Set(); }
            });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.True(lease.IsDisposed);
            Assert.False(child.IsDisposed);
            Assert.Throws<WorkerTerminatedException>(() => child.CheckVmExecutionCancellation());
        }
        Assert.True(child.IsDisposed);
        Assert.Equal(0, parent.GetVmExecutionHost().ActiveLeaseCount);
        Assert.Equal(0, parent.ActiveHandleCount);
    }

    [Fact]
    public void ValueAdaptersArePairedOwnedAndDoNotReplaceUnrelatedValues()
    {
        var marker = new object();
        using var host = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host,
            value => VmModuleInterpreter.IsVmUndefinedValue(value) ? marker : value,
            value => ReferenceEquals(value, marker) ? VmModuleInterpreter.GetVmUndefinedValue() : value);
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value, value => value));
        var context = new Dictionary<string, object?> { ["value"] = marker };
        using (host.Enter())
        {
            var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
            Assert.Same(marker, run.Call(null!, ["value", context]));
            Assert.Equal("undefined", run.Call(null!, ["typeof value", context]));
            Assert.Same(marker, context["value"]);
        }
    }

    [Fact]
    public void ActualEscapedErrorKeepsItsOwnerAndReadsLiveAccessors()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var context = new Dictionary<string, object?> { ["count"] = 0d };
        var error = VmModuleInterpreter.ExecuteInNewContext(
            "var error=new Error('x');Object.defineProperty(error,'stack',{get(){count++;return 'live'}});error", context, parent);
        Assert.True(VmModuleInterpreter.IsVmErrorValue(error));
        Assert.Equal(0, parent.ActiveHandleCount);
        Assert.Equal("live", VmModuleInterpreter.ReadVmErrorProperty(error!, "stack"));
        Assert.Equal(1d, context["count"]);
        Assert.Equal("live", VmModuleInterpreter.ReadVmErrorProperty(error!, "stack"));
        Assert.Equal(2d, context["count"]);
        var unrecorded = new SharpTSErrorClass("Error", null).Call(parent, ["unrecorded"]);
        Assert.False(VmModuleInterpreter.IsVmErrorValue(unrecorded));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmErrorProperty(unrecorded!, "stack"));
        parent.Dispose();
        Assert.True(VmModuleInterpreter.IsVmErrorValue(error));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmErrorProperty(error!, "stack"));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<VmEvaluationLease> StartTimer(Interpreter? parent, Action callback, bool unref, int delay)
    {
        var child = new Interpreter(new StringWriter(), new StringWriter());
        var lease = VmEvaluationLease.Create(child, parent, static () => { });
        Assert.NotNull(lease);
        using (lease.Enter())
        {
            var timer = TimerBuiltIns.SetTimeout(child,
                BuiltInMethod.CreateV2("timer", 0, (_, _, _) => { callback(); return RuntimeValue.Undefined; }), delay, []);
            if (unref) timer.Unref();
            lease.FinishInitialEvaluation(true);
        }
        return new(lease);
    }

    [Fact]
    public void ActualTimerWakeRootsItsLeaseAcrossCollectionAndRunsOnCallerThread()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        int owner = System.Environment.CurrentManagedThreadId, callbackThread = 0;
        var weak = StartTimer(parent, () => callbackThread = System.Environment.CurrentManagedThreadId, false, 50);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(weak.TryGetTarget(out _));
        Assert.Equal(1, parent.ActiveHandleCount);
        parent.RunEventLoop();
        Assert.Equal(owner, callbackThread);
        Assert.Equal(0, parent.ActiveHandleCount);
    }

    [Fact]
    public void UnrefTimerCanFireDuringOtherWorkAndShutdownCancelsItsWake()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        int calls = 0;
        StartTimer(parent, () => calls++, true, 5);
        TimerBuiltIns.SetTimeout(parent, BuiltInMethod.CreateV2("keepAlive", 0, static (_, _, _) => RuntimeValue.Undefined), 30, []);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(1, parent.ActiveHandleCount);
        parent.RunEventLoop();
        Assert.Equal(1, calls);

        using var idle = new Interpreter(new StringWriter(), new StringWriter());
        var pending = StartTimer(idle, () => calls++, true, 60_000);
        Assert.True(pending.TryGetTarget(out var lease));
        Assert.Equal(0, idle.ActiveHandleCount);
        idle.RunEventLoop();
        idle.Dispose();
        Assert.True(lease.IsDisposed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FirstRefAndCallerShutdownCannotReleaseAnUnacquiredRef()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            int refs = 0;
            using var ready = new ManualResetEventSlim();
            using var host = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { },
                () => Interlocked.Increment(ref refs),
                () => Assert.True(Interlocked.Decrement(ref refs) >= 0), () => { });
            var acquire = Task.Run(() => { ready.Wait(); if (host.TryAcquireRef()) host.ReleaseRef(); });
            var shutdown = Task.Run(() => { ready.Wait(); host.Dispose(); });
            ready.Set();
            await Task.WhenAll(acquire, shutdown);
            Assert.Equal(0, refs);
        }
    }

    [Fact]
    public void CallerCleanupContinuesWhenOneRefReleaseThrows()
    {
        int refs = 0;
        var host = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { },
            () => refs++, () => { refs--; throw new InvalidOperationException("release"); }, () => { });
        WeakReference<VmEvaluationLease> first, second;
        using (host.Enter())
        {
            first = StartTimer(null, static () => { }, false, 60_000);
            second = StartTimer(null, static () => { }, false, 60_000);
        }
        Assert.Equal(2, refs);
        Assert.True(first.TryGetTarget(out var firstLease));
        Assert.True(second.TryGetTarget(out var secondLease));
        Assert.Throws<AggregateException>(() => host.Dispose());
        Assert.Equal(0, refs);
        Assert.Equal(0, host.ActiveLeaseCount);
        Assert.True(firstLease.IsDisposed);
        Assert.True(secondLease.IsDisposed);
    }

    [Fact]
    public void ZeroWorkDisposesImmediatelyAndPendingPromiseDoesNotRefCaller()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var host = parent.GetVmExecutionHost();
        Assert.Equal(3d, VmModuleInterpreter.ExecuteInNewContext("1+2", null, parent));
        Assert.Equal(0, host.ActiveLeaseCount);
        var promise = Assert.IsType<SharpTSPromise>(VmModuleInterpreter.ExecuteInNewContext("new Promise(()=>{})", null, parent));
        Assert.False(promise.IsCompleted);
        Assert.Equal(0, parent.ActiveHandleCount);
        Assert.Equal(1, host.ActiveLeaseCount);
        parent.Dispose();
        Assert.Equal(0, host.ActiveLeaseCount);
        Assert.False(promise.IsCompleted);
    }

    [Fact]
    public void JobsUseTheActualCallerCheckpointAndShutdownDiscardsFutureJobs()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        var context = new Dictionary<string, object?> { ["flag"] = 0d };
        VmModuleInterpreter.ExecuteInNewContext("Promise.resolve().then(()=>{flag=1});0", context, parent);
        Assert.Equal(0d, context["flag"]);
        parent.ProcessMicrotasks();
        Assert.Equal(1d, context["flag"]);
        VmModuleInterpreter.ExecuteInNewContext("Promise.resolve().then(()=>{flag=2});0", context, parent);
        parent.Dispose();
        parent.ProcessMicrotasks();
        Assert.Equal(1d, context["flag"]);
    }

    [Fact]
    public void TimeoutAndPreCancelledCallerReleaseTheEvaluation()
    {
        using var parent = new Interpreter(new StringWriter(), new StringWriter());
        Assert.Throws<ThrowException>(() => VmModuleInterpreter.ExecuteInNewContext("while(true){}", null, parent, 20));
        Assert.Equal(0, parent.GetVmExecutionHost().ActiveLeaseCount);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        parent.SetWorkerTerminationToken(cancellation.Token);
        Assert.Throws<WorkerTerminatedException>(() => VmModuleInterpreter.ExecuteInNewContext("1", null, parent));
        Assert.Equal(0, parent.GetVmExecutionHost().ActiveLeaseCount);
        Assert.Equal(0, parent.ActiveHandleCount);
    }

    [Fact]
    public void NestedHostScopesRestoreOwnershipAndRejectUnownedObjects()
    {
        var first = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
        var second = (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
        Assert.Null(VmExecutionHost.Current);
        using (VmModuleInterpreter.EnterExecutionHost(first))
        {
            Assert.Same(first, VmExecutionHost.Current);
            using (VmModuleInterpreter.EnterExecutionHost(second)) Assert.Same(second, VmExecutionHost.Current);
            Assert.Same(first, VmExecutionHost.Current);
        }
        Assert.Null(VmExecutionHost.Current);
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.EnterExecutionHost(new object()));
        first.Dispose(); second.Dispose();
        Assert.Throws<ObjectDisposedException>(() => VmModuleInterpreter.EnterExecutionHost(first));
    }
}
