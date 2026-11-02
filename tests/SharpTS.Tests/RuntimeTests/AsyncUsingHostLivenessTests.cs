using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

// These controls inspect interpreter-owned CLR disposal tasks and handle counts.
public sealed class AsyncUsingHostLivenessTests
{
    [Fact]
    public async Task PendingClrDisposalKeepsTheOwnerAliveUntilCompletion()
        => await CheckPendingResourceAsync(
            "declare const input:any;async function run(){try{await using held=input;await 0;}finally{}}run();",
            resource => resource.Complete(), _ => { });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedClrDisposalReleasesItsHandle(bool cancelled)
        => await CheckPendingResourceAsync(
            "declare const input:any;let caught=false;async function run(){try{await using held=input;await 0;}catch(error){caught=true}}run();",
            resource => resource.Fail(cancelled),
            interpreter => Assert.Equal(true, interpreter.Environment.Get("caught").ToObject()));

    [Fact]
    public async Task AGuestOnlyPendingDisposalStillAllowsQuiescentExit()
    {
        const string source = "let calls=0,after=false;async function run(){try{await using held={[Symbol.asyncDispose](){calls++;return new Promise<void>(()=>{})}};}finally{after=true}}run();";
        using var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        await Task.Factory.StartNew(() => interpreter.Interpret(statements, types),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(interpreter.LastUncaughtError);
        Assert.Equal(1d, interpreter.Environment.Get("calls").ToObject());
        Assert.Equal(false, interpreter.Environment.Get("after").ToObject());
        Assert.Equal(0, interpreter.ActiveHandleCount);
    }

    private static async Task CheckPendingResourceAsync(string source,
        Action<ControlledResource> complete, Action<Interpreter> assertCompletion)
    {
        var resource = new ControlledResource();
        using var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        interpreter.Environment.Define("input", resource);
        var run = Task.Factory.StartNew(() => interpreter.Interpret(statements, types),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(resource.Started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => interpreter.ActiveHandleCount != 0 || run.IsCompleted,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(1, interpreter.ActiveHandleCount);
            Assert.False(run.IsCompleted);
            complete(resource);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(interpreter.LastUncaughtError);
            Assert.Equal(0, interpreter.ActiveHandleCount);
            Assert.Equal(1, resource.Calls);
            assertCompletion(interpreter);
        }
        finally
        {
            resource.Complete();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            resource.Started.Dispose();
        }
    }

    private sealed class ControlledResource : IAsyncDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Started { get; } = new();
        internal int Calls { get; private set; }
        public ValueTask DisposeAsync()
        {
            Calls++;
            Started.Set();
            return new ValueTask(_completion.Task);
        }
        internal void Complete() => _completion.TrySetResult();
        internal void Fail(bool cancelled)
        {
            if (cancelled) _completion.TrySetCanceled();
            else _completion.TrySetException(new InvalidOperationException("cleanup"));
        }
    }
}
