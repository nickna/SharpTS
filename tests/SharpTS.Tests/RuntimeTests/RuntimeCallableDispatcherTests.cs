using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Unit tests for <see cref="RuntimeCallableDispatcher"/> and the
/// <see cref="SharpTSEventEmitter.EmitDirect"/> regression it fixes (silent
/// drop of <see cref="ISharpTSCallable"/> listeners).
/// </summary>
public class RuntimeCallableDispatcherTests
{
    [Fact]
    public void IsCallable_RecognisesBuiltInMethod()
    {
        var bm = new BuiltInMethod("noop", 0, (_, _, _) => null);
        Assert.True(RuntimeCallableDispatcher.IsCallable(bm));
    }

    [Fact]
    public void IsCallable_RejectsNullAndPlainObject()
    {
        Assert.False(RuntimeCallableDispatcher.IsCallable(null));
        Assert.False(RuntimeCallableDispatcher.IsCallable(new object()));
        Assert.False(RuntimeCallableDispatcher.IsCallable("string"));
        Assert.False(RuntimeCallableDispatcher.IsCallable(42));
    }

    [Fact]
    public void IsCallable_RecognisesFuncBoundMethod()
    {
        Func<object?[], object?> f = args => args[0];
        Assert.True(RuntimeCallableDispatcher.IsCallable(f));
    }

    [Fact]
    public void IsCallable_RecognisesActionListener()
    {
        Action<object?[]> a = _ => { };
        Assert.True(RuntimeCallableDispatcher.IsCallable(a));
    }

    [Fact]
    public void Invoke_BuiltInMethod_ForwardsArgsAndReturnsResult()
    {
        object? observed = null;
        var bm = new BuiltInMethod("capture", 1, (_, _, args) =>
        {
            observed = args[0];
            return "received:" + args[0];
        });

        var result = RuntimeCallableDispatcher.Invoke(null, bm, "hello");

        Assert.Equal("hello", observed);
        Assert.Equal("received:hello", result);
    }

    [Fact]
    public void Invoke_FuncBoundMethod_ReceivesArgsArray()
    {
        object?[]? captured = null;
        Func<object?[], object?> f = args =>
        {
            captured = args;
            return "ok";
        };

        var result = RuntimeCallableDispatcher.Invoke(null, f, "a", "b", "c");

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Length);
        Assert.Equal("a", captured[0]);
        Assert.Equal("ok", result);
    }

    [Fact]
    public void Invoke_ActionListener_ReceivesArgs()
    {
        int callCount = 0;
        Action<object?[]> a = args =>
        {
            callCount++;
            Assert.Equal("payload", args[0]);
        };

        RuntimeCallableDispatcher.Invoke(null, a, "payload");
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Invoke_NullCallable_ReturnsNullSilently()
    {
        Assert.Null(RuntimeCallableDispatcher.Invoke(null, null));
    }

    /// <summary>
    /// Regression: prior to the shared dispatcher, <see cref="SharpTSEventEmitter.EmitDirect"/>
    /// silently dropped <see cref="ISharpTSCallable"/> listeners (the body had
    /// branches for <see cref="SharpTS.Compilation.TSFunction"/> and
    /// <see cref="BuiltInMethod"/> only, and silently <c>return</c>ed for any
    /// other <see cref="ISharpTSCallable"/>). This is the latent bug the
    /// dispatcher unification fixed.
    /// </summary>
    [Fact]
    public void EmitDirect_InvokesBuiltInMethodListener()
    {
        var emitter = new SharpTSEventEmitter();
        int callCount = 0;
        object? receivedArg = null;

        var listener = new BuiltInMethod("ping", 1, (_, _, args) =>
        {
            callCount++;
            receivedArg = args[0];
            return SharpTSUndefined.Instance;
        });

        emitter.AddListenerDirect("ping", listener);
        emitter.EmitDirect("ping", "payload");

        Assert.Equal(1, callCount);
        Assert.Equal("payload", receivedArg);
    }

    [Fact]
    public void EmitDirect_OnceListenerFiresAndRemoves()
    {
        var emitter = new SharpTSEventEmitter();
        int callCount = 0;

        var listener = new BuiltInMethod("once", 0, (_, _, _) =>
        {
            callCount++;
            return SharpTSUndefined.Instance;
        });

        emitter.AddListenerDirect("evt", listener, once: true);
        emitter.EmitDirect("evt");
        emitter.EmitDirect("evt");

        Assert.Equal(1, callCount);
    }
}
