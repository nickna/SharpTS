using System.Reflection;
using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class NativeAwaitPublicationTests
{
    [Fact]
    public void SettledReadableRejectionDoesNotRequireWorkerContextRestoration()
    {
        const string source = """
            async function run() {
                const reason = ({ token: 1 });
                const stream = new ReadableStream({
                    async pull(controller: any) { await Promise.resolve(); throw reason; }
                });
                const reader = stream.getReader();
                try { await reader.read(); }
                catch (error: any) { console.log(error === reason); }
                try { await reader.read(); }
                catch (error: any) { console.log(error === reason); }
            }
            run();
            """;
        var marker = new object();
        using var releaseWorkers = new ManualResetEventSlim();
        using var contextsExited = new ManualResetEventSlim(true);
        int activeContexts = 0;
        var slot = new AsyncLocal<object?>(change =>
        {
            if (!change.ThreadContextChanged || !Thread.CurrentThread.IsThreadPoolThread) return;
            if (ReferenceEquals(change.CurrentValue, marker))
            {
                contextsExited.Reset();
                Interlocked.Increment(ref activeContexts);
                releaseWorkers.Wait();
            }
            else if (ReferenceEquals(change.PreviousValue, marker)
                && Interlocked.Decrement(ref activeContexts) == 0)
                contextsExited.Set();
        });
        try
        {
            // Hold only this execution's worker-context restoration. The
            // interpreter's dedicated thread and synchronous Post remain free.
            // No pool limits, sleeps, or runtime deadlines are changed.
            slot.Value = marker;
            var output = TestHarness.Run(source, ExecutionMode.Interpreted);
            Assert.False(releaseWorkers.IsSet);
            Assert.Equal("true\ntrue\n", output);
        }
        finally
        {
            releaseWorkers.Set();
            slot.Value = null;
            Assert.True(contextsExited.Wait(TimeSpan.FromSeconds(2)), "Gated worker context did not exit after release.");
            Assert.Equal(0, Volatile.Read(ref activeContexts));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingRegistrationRestoresAmbientContextAsync(bool hasAmbientContext)
    {
        using var interpreter = new Interpreter();
        var prior = SynchronizationContext.Current;
        var ambient = hasAmbientContext ? new SynchronizationContext() : null;
        SynchronizationContext.SetSynchronizationContext(ambient);
        try
        {
            var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var awaited = RegisterAsync(interpreter, source.Task);
            Assert.Same(ambient, SynchronizationContext.Current);
            var value = new object();
            source.SetResult(value);
            Assert.False(awaited.IsCompleted);
            interpreter.ProcessMicrotasks();
            Assert.True(awaited.IsCompletedSuccessfully);
            Assert.Same(value, await awaited);
            Assert.Same(ambient, SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadyAndRacingSettlementTransferInOneOwnerJobAsync(bool alreadyReady)
    {
        using var interpreter = new Interpreter();
        for (int iteration = 0; iteration < 16; iteration++)
        {
            var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var value = new object();
            var order = new List<string>();
            interpreter.EnqueuePromiseJob(() => order.Add("before"));
            Task<object?> awaited;
            if (alreadyReady)
            {
                source.SetResult(value);
                awaited = RegisterAsync(interpreter, source.Task);
            }
            else
            {
                using var start = new ManualResetEventSlim();
                var worker = new Thread(() => { start.Wait(); source.SetResult(value); });
                worker.Start();
                try
                {
                    start.Set();
                    awaited = RegisterAsync(interpreter, source.Task);
                    Assert.True(worker.Join(TestHarness.DefaultTimeout));
                }
                finally
                {
                    start.Set();
                    Assert.True(worker.Join(TestHarness.DefaultTimeout));
                }
            }
            Assert.False(awaited.IsCompleted);
            interpreter.EnqueuePromiseJob(() => order.Add(awaited.IsCompleted ? "after:settled" : "after:pending"));
            interpreter.ProcessMicrotasks();
            Assert.Equal(["before", "after:settled"], order);
            Assert.True(awaited.IsCompletedSuccessfully);
            Assert.Same(value, await awaited);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettlementInsideOwnerJobDoesNotResumeInsideProducerAsync(bool forceAsynchronousContinuations)
    {
        using var interpreter = new Interpreter();
        var source = new TaskCompletionSource<object?>(forceAsynchronousContinuations
            ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
        var awaited = RegisterAsync(interpreter, source.Task);
        var order = new List<string>();
        interpreter.EnqueuePromiseJob(() =>
        {
            awaited.GetAwaiter().OnCompleted(() => order.Add("await"));
            order.Add("before");
            source.SetResult(7.0);
            order.Add(awaited.IsCompleted ? "after:settled" : "after:pending");
        });
        interpreter.EnqueuePromiseJob(() => order.Add(awaited.IsCompleted ? "next:settled" : "next:pending"));
        interpreter.RunEventLoop();
        Assert.True(awaited.IsCompletedSuccessfully);
        Assert.Equal(["before", "after:pending", "next:pending", "await"], order);
        Assert.Equal(7.0, await awaited);
    }

    // This tests the interpreter's private native-Task publication seam. Guest
    // semantics and the existing rejection fixture remain shared in both modes.
    private static Task<object?> RegisterAsync(Interpreter interpreter, Task<object?> task)
        => (Task<object?>)typeof(Interpreter).GetMethod("QueueNativeAwait", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(interpreter, [task])!;
}
