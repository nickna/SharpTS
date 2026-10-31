using System.Reflection;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCallbackThrownTransportTests
{
    private static VmExecutionHost NewHost() => (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(
        _ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });

    [Fact]
    public void TypedPredicateProtectsTheGetterAndDisposeClearsBothDelegates()
    {
        var host = NewHost();
        var carrier = new Exception();
        int reads = 0;
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, value => ReferenceEquals(value, carrier), _ => { reads++; return null; });
        Assert.False(host.TryGetThrownValue(new Exception(), out _));
        Assert.Equal(0, reads);
        Assert.True(host.TryGetThrownValue(carrier, out var value));
        Assert.Null(value);
        Assert.Equal(1, reads);
        host.Dispose();
        Assert.False(host.TryGetThrownValue(carrier, out _));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ConfigurationRequiresTheActualUnstartedHostAndIsSingleUse()
    {
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(new object(), _ => false, _ => null));
        using var host = NewHost();
        Assert.Throws<ArgumentNullException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, _ => false, null!));
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, _ => false, _ => null);
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, _ => false, _ => null));
        using var started = NewHost();
        using (started.Enter()) { }
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(started, _ => false, _ => null));
        var disposed = NewHost();
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(disposed, _ => false, _ => null));
    }

    [Fact]
    public void AdmittedCarrierUsesTheExistingValueAdapterWithoutLosingNullOrUndefined()
    {
        foreach (var original in new object?[] { new object(), null, SharpTSUndefined.Instance })
        {
            using var host = NewHost();
            var carrier = new Exception();
            var converted = original is null or SharpTSUndefined ? original : new object();
            VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value,
                value => ReferenceEquals(value, original) ? converted : value);
            VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, value => ReferenceEquals(value, carrier), _ => original);
            CompiledCallableAdapter adapter;
            using (host.Enter()) adapter = new(new ThrowingCallable(carrier), typeof(ThrowingCallable).GetMethod(nameof(ThrowingCallable.Invoke))!);
            var thrown = Assert.Throws<ThrowException>(() => adapter.Call(null!, []));
            Assert.Same(converted, thrown.Value);
        }
    }

    [Fact]
    public void UnprovedCallerExceptionRetainsItsOriginalReflectionExceptionAndNeverReadsPayload()
    {
        using var host = NewHost();
        var original = new InvalidOperationException("caller");
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, _ => false, _ => throw new InvalidOperationException("getter must not run"));
        CompiledCallableAdapter adapter;
        using (host.Enter()) adapter = new(new ThrowingCallable(original), typeof(ThrowingCallable).GetMethod(nameof(ThrowingCallable.Invoke))!);
        var thrown = Assert.Throws<TargetInvocationException>(() => adapter.Call(null!, []));
        Assert.Same(original, thrown.InnerException);
    }

    public sealed class ThrowingCallable(Exception exception)
    {
        public object Invoke(object[] args) => throw exception;
    }
}