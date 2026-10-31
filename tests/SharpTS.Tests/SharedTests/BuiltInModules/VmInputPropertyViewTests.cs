using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInputPropertyViewTests
{
    private sealed class Input
    {
        internal object? Value = 7d;
        internal object? Method;
        internal bool Throw;
        internal int Reads;
        internal int Role;
        public override string ToString() => "input:" + Value;
    }
    private sealed class Carrier(object? value) : Exception { internal readonly object? Value = value; }
    private static bool Admits(object target) => target.GetType() == typeof(Input);
    private static object? Read(object target, string name)
    {
        var input = (Input)target;
        input.Reads++;
        if (input.Throw) throw new Carrier(input.Value);
        return name switch { "value" or "0" => input.Value, "self" => input, "method" => input.Method, _ => SharpTSUndefined.Instance };
    }
    private static int Role(object target) => ((Input)target).Role;
    private static bool Write(object target, string name, object? value) => true;
    private static bool OtherWrite(object target, string name, object? value) => false;
    private static object? OtherRead(object target, string name) => null;
    private static VmExecutionHost Host() => (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(_ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });
    private static void Configure(VmExecutionHost host) => VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(host, typeof(Input), Admits, Read, Role);
    private static object? Run(Interpreter caller, Input input, string code)
    {
        using var scope = caller.GetVmExecutionHost().Enter();
        return VmModuleInterpreter.ExecuteInNewContext(code, new SharpTSObject(new Dictionary<string, object?> { ["input"] = input }), caller);
    }

    [Fact]
    public void ActualConfiguredInputReadsUseOriginalTargetAndCurrentValues()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var input = new Input();
        Assert.Equal(7d, Run(caller, input, "input.value"));
        input.Value = 8d;
        Assert.Equal(8d, Run(caller, input, "input[0]"));
        Assert.Equal(2, input.Reads);
    }

    [Fact]
    public void StringCoercionRetainsTheOriginalClrFallbackEndpoint()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        Assert.Equal("input:7", Run(caller, new Input(), "String(input)"));
    }
    [Fact]
    public void ReturnedActualMethodReceivesTheOriginalInputObject()
    {
        var (assembly, _) = TestHarness.CompileAndRun("function make(){return function(this:any){return this;};}", DecoratorMode.None);
        var make = assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Single(method => method.Name == "make");
        var input = new Input { Method = make.Invoke(null, new object?[make.GetParameters().Length]) };
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        Assert.Equal(true, Run(caller, input, "input.method()===input"));
    }

    [Fact]
    public void DataOnlyCanonicalIdentitySurvivesFreshHostsAndExactUnwrap()
    {
        var input = new Input();
        using var first = Host();
        Configure(first);
        var view = Assert.IsType<VmInputPropertyView>(first.ToVm(input));
        Assert.Same(input, first.FromVm(view));
        first.Dispose();
        using var second = Host();
        Configure(second);
        Assert.Same(view, second.ToVm(input));
        using (second.Enter())
        {
            Assert.Same(view, view.Read("self"));
            Assert.Equal(7d, view.Read("value"));
        }
        Assert.Throws<InvalidOperationException>(() => view.Read("value"));
    }

    [Fact]
    public void SameDomainDifferentOperationsAndWrongDomainReadsAreRejected()
    {
        var input = new Input();
        using var first = Host(); Configure(first);
        var view = Assert.IsType<VmInputPropertyView>(first.ToVm(input));
        using var changed = Host();
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(changed, typeof(Input), Admits, OtherRead, Role);
        Assert.Throws<InvalidOperationException>(() => changed.ToVm(input));
        using var foreign = Host();
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(foreign, typeof(string), Admits, Read, Role);
        using (foreign.Enter()) Assert.Throws<InvalidOperationException>(() => view.Read("value"));
    }

    [Fact]
    public void InputViewsAndUnknownClrShapesDoNotAcquireVmOwnershipOrCallableAdmission()
    {
        using var host = Host(); Configure(host);
        var input = new Input();
        var view = host.ToVm(input);
        Assert.False(VmModuleInterpreter.IsVmValue(view));
        var unknown = new object();
        Assert.Same(unknown, host.ToVm(unknown));
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var returned = Run(caller, input, "input");
        Assert.Same(view, returned);
        Assert.Same(input, caller.GetVmExecutionHost().FromVm(returned));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
    }

    [Fact]
    public void AbruptGetterUsesTypedPayloadOnceAndLeavesUnrelatedExceptionsAlone()
    {
        using var host = Host(); Configure(host);
        var marker = new object();
        var input = new Input { Throw = true, Value = marker };
        int predicates = 0, extractors = 0;
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, error => { predicates++; return error is Carrier; }, error => { extractors++; return ((Carrier)error).Value; });
        var view = Assert.IsType<VmInputPropertyView>(host.ToVm(input));
        using (host.Enter())
        {
            var failure = Assert.Throws<ThrowException>(() => view.Read("value"));
            Assert.Same(marker, failure.Value);
        }
        Assert.Equal(1, predicates); Assert.Equal(1, extractors); Assert.Equal(1, input.Reads);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (VmInputPropertyView View, WeakReference Host) DetachedView()
    {
        var host = Host(); Configure(host);
        var view = Assert.IsType<VmInputPropertyView>(host.ToVm(new Input()));
        var weak = new WeakReference(host);
        host.Dispose();
        return (view, weak);
    }

    [Fact]
    public void RetainingOnlyTheDataViewDoesNotRetainItsDisposedHost()
    {
        var result = DetachedView();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(result.Host.IsAlive);
        GC.KeepAlive(result.View);
    }

    [Fact]
    public void OptionalWriterConfigurationIsImmutableAndPrecedesAnyAdmission()
    {
        using var host = Host();
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, Write));
        Configure(host);
        var input = new Input();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, (_, _, _) => input.Throw));
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, Write);
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, Write));
        host.ToVm(input);
        using var other = Host(); Configure(other);
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(other, OtherWrite);
        Assert.Throws<InvalidOperationException>(() => other.ToVm(input));
        using var admitted = Host(); Configure(admitted); admitted.ToVm(new Input());
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(admitted, Write));
        using var started = Host(); Configure(started); using (started.Enter()) { }
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(started, Write));
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 0)]
    public void ActualProducerRoleIsImmutableDataAfterAdmission(int role, int expected)
    {
        using var host = Host(); Configure(host);
        var input = new Input { Role = role };
        var view = Assert.IsType<VmInputPropertyView>(host.ToVm(input));
        Assert.Equal(expected, view.NativeRole);
        host.Dispose();
        Assert.Equal(expected, view.NativeRole);
    }
    [Fact]
    public void CapabilityConfigurationRequiresAnOwnedUnstartedHostAndStaticDelegates()
    {
        using var host = Host();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(new object(), typeof(Input), Admits, Read, Role));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(host, new object(), Admits, Read, Role));
        var input = new Input();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(host, typeof(Input), value => ReferenceEquals(value, input), Read, Role));
        Configure(host);
        Assert.Throws<InvalidOperationException>(() => Configure(host));
        using var started = Host(); using (started.Enter()) { }
        Assert.Throws<InvalidOperationException>(() => Configure(started));
        host.Dispose(); Assert.Throws<ObjectDisposedException>(() => Configure(host));
    }
}