using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// FinalizationRegistry cleanup callbacks must actually fire: the GC finalizer
/// path enqueued heldValues into the registry's pending queue, but nothing ever
/// drained that queue, so registered cleanup callbacks never ran. The event
/// loop now drains enrolled registries on each tick. Real GC timing is
/// untestable, so these tests use the internal enqueue seam that matches
/// exactly what the GC-triggered finalizer does.
/// </summary>
public class FinalizationRegistryDrainTests
{
    private static (SharpTSFinalizationRegistry Registry, List<object?> Received) MakeRegistry()
    {
        var received = new List<object?>();
        var callback = BuiltInMethod.CreateV2("cleanup", 1, (interp, recv, args) =>
        {
            received.Add(args.Length > 0 ? args[0].ToObject() : null);
            return RuntimeValue.Undefined;
        });
        return (new SharpTSFinalizationRegistry(callback), received);
    }

    [Fact]
    public void PendingCleanups_DrainOnEventLoopTick()
    {
        using var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        var (registry, received) = MakeRegistry();
        interpreter.TrackFinalizationRegistry(registry);

        registry.EnqueueCleanupForTest("held-1");
        registry.EnqueueCleanupForTest("held-2");
        interpreter.ProcessPendingCallbacks();

        Assert.Equal(["held-1", "held-2"], received);
    }

    [Fact]
    public void RegisterBuiltIn_EnrollsRegistryWithEventLoop()
    {
        using var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        var (registry, received) = MakeRegistry();

        // Drive the guest-visible register() built-in; it must enroll the
        // registry with the interpreter's event loop.
        var register = (BuiltInMethod)FinalizationRegistryBuiltIns.GetMember(registry, "register")!;
        register.Bind(registry).CallV2(
            interpreter,
            [RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>())), RuntimeValue.FromString("held-guest")]);

        registry.EnqueueCleanupForTest("held-guest");
        interpreter.ProcessPendingCallbacks();

        Assert.Equal(["held-guest"], received);
    }

    [Fact]
    public void UntrackedRegistry_IsNotKeptAliveByInterpreter()
    {
        using var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        var weak = TrackCollectible(interpreter);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));

        static WeakReference<SharpTSFinalizationRegistry> TrackCollectible(Interpreter interpreter)
        {
            var (registry, _) = MakeRegistry();
            interpreter.TrackFinalizationRegistry(registry);
            return new WeakReference<SharpTSFinalizationRegistry>(registry);
        }
    }
}
