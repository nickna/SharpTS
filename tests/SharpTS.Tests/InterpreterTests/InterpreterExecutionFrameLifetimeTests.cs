using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

[Collection(GarbageCollectionTestsCollection.Name)]
public sealed class InterpreterExecutionFrameLifetimeTests
{
    private sealed class GuestPayload(Interpreter owner)
    {
        public Interpreter Owner { get; } = owner;
    }

    [Theory]
    [InlineData("scope-unwind")]
    [InlineData("environment")]
    [InlineData("yield-callback")]
    [InlineData("async-generator")]
    [InlineData("cleared-callbacks")]
    public void DisposedInterpreterIsCollectibleAfterScopeAndFacadeRestoration(string restoration)
    {
        var references = DisposeInsideScope(restoration);

        // The helper's stack frame has returned, leaving only the creating
        // execution context as a possible owner of this otherwise closed graph.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] DisposeInsideScope(string restoration)
    {
        var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var root = interpreter.Environment;
        var payload = new GuestPayload(interpreter);
        root.Define("payload", payload);
        var scope = new RuntimeEnvironment(root);
        Func<object?, bool, object?> callback = (_, _) => payload;
        var generator = new SharpTSAsyncGenerator([], scope, interpreter, "retained", new object());
        interpreter.YieldCallback = callback;
        interpreter.CurrentAsyncGenerator = generator;
        scope.Define("disposeNow", BuiltInMethod.CreateV2("disposeNow", 0, (owner, _, _) =>
        {
            owner.Dispose();
            return RuntimeValue.Undefined;
        }));

        // ExecuteBlock enters a synchronous scope. Its scope guard restores the
        // previous environment after the callback has already disposed its owner.
        var result = interpreter.ExecuteBlock(TestHarness.ParseOrThrow("disposeNow();"), scope);
        Assert.False(result.IsAbrupt);
        Assert.True(interpreter.IsDisposed);

        switch (restoration)
        {
            case "scope-unwind": break;
            case "environment": interpreter.SetEnvironment(scope); break;
            case "yield-callback": interpreter.YieldCallback = callback; break;
            case "async-generator": interpreter.CurrentAsyncGenerator = generator; break;
            case "cleared-callbacks":
                interpreter.YieldCallback = null;
                interpreter.CurrentAsyncGenerator = null;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(restoration));
        }

        return [new(interpreter), new(root), new(payload), new(scope), new(generator)];
    }

    [Fact]
    public void RepeatedDisposeClearsAnInheritedExecutionContext()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var root = interpreter.Environment;
        var scope = new RuntimeEnvironment(root);
        Func<object?, bool, object?> callback = (value, _) => value;
        var generator = new SharpTSAsyncGenerator([], scope, interpreter, "inherited", new object());
        interpreter.SetEnvironment(scope);
        interpreter.YieldCallback = callback;
        interpreter.CurrentAsyncGenerator = generator;
        using var inherited = ExecutionContext.Capture();
        Assert.NotNull(inherited);

        interpreter.Dispose();
        Assert.Same(root, interpreter.Environment);
        Assert.Null(interpreter.YieldCallback);
        Assert.Null(interpreter.CurrentAsyncGenerator);

        ExecutionContext.Run(inherited, _ =>
        {
            Assert.Same(scope, interpreter.Environment);
            Assert.Same(callback, interpreter.YieldCallback);
            Assert.Same(generator, interpreter.CurrentAsyncGenerator);
            interpreter.Dispose();
            Assert.Same(root, interpreter.Environment);
            Assert.Null(interpreter.YieldCallback);
            Assert.Null(interpreter.CurrentAsyncGenerator);
        }, null);
    }
}
