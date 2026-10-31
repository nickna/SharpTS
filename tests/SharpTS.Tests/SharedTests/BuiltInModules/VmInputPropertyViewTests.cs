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
        internal object? Tag = SharpTSUndefined.Instance;
        internal object? TagReceiver;
        internal int TagReads;
        internal bool TagThrows;
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
    private static object? Tag(object target, object? receiver)
    {
        var input = (Input)target;
        input.TagReads++;
        input.TagReceiver = receiver;
        if (input.TagThrows) throw new Carrier(input.Tag);
        return input.Tag;
    }
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

    [Theory]
    [InlineData(1, "object|false|false|true|input:7|[object Array]")]
    [InlineData(2, "object|false|false|false|input:7|[object Error]")]
    [InlineData(0, "object|false|false|false|input:7|[object Object]")]
    public void BrandConsumersUseOnlyTheRecordedRoleAndPreserveOtherCoercions(int role, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        Assert.Equal(expected, Run(caller, new Input { Role = role }, "[typeof input,input instanceof Error,input instanceof TypeError,Array.isArray(input),String(input),Object.prototype.toString.call(input)].join('|')"));
    }

    [Theory]
    [InlineData("'Custom'", "true|[object Custom]|tag")]
    [InlineData("7", "true|[object Array]|tag")]
    public void ProxyTagLookupStillPrecedesTheProvedArrayDefault(string tag, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var code = "const log=[];const proxy=new Proxy(input,{get(target,key){if(key===Symbol.toStringTag){log.push('tag');return " + tag + ";}return Reflect.get(target,key);}});[Array.isArray(proxy),Object.prototype.toString.call(proxy),log.join(',')].join('|')";
        Assert.Equal(expected, Run(caller, new Input { Role = 1 }, code));
    }

    [Fact]
    public void AbruptTagsAndRevokedProxiesKeepExistingErrors()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        Assert.Equal(true, Run(caller, new Input { Role = 1 }, "const marker={};const proxy=new Proxy(input,{get(target,key){if(key===Symbol.toStringTag)throw marker;return Reflect.get(target,key);}});let same=false;try{Object.prototype.toString.call(proxy)}catch(error){same=error===marker}same"));
        Assert.Equal("true|true", Run(caller, new Input { Role = 1 }, "const pair=Proxy.revocable(input,{});pair.revoke();let a=false,b=false;try{Array.isArray(pair.proxy)}catch(error){a=error instanceof TypeError}try{Object.prototype.toString.call(pair.proxy)}catch(error){b=error instanceof TypeError}[a,b].join('|')"));
    }
    [Theory]
    [InlineData(1, "Custom", "[object Custom]")]
    [InlineData(1, 7, "[object Array]")]
    [InlineData(2, 7, "[object Error]")]
    public void ActualTagGetterRunsOnceBeforeTheCertifiedDefault(int role, object tag, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost(); Configure(host);
        VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag);
        var input = new Input { Role = role, Tag = tag };
        Assert.Equal(expected, Run(caller, input, "Object.prototype.toString.call(input)"));
        Assert.Equal(1, input.TagReads);
        Assert.Same(input, input.TagReceiver);
    }

    [Fact]
    public void ActualTagReceivesTheTransparentProxyAndPreservesAbruptPayload()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost(); Configure(host);
        VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag);
        var input = new Input { Role = 1, Tag = "Custom" };
        Assert.Equal("[object Custom]", Run(caller, input, "Object.prototype.toString.call(new Proxy(input,{}))"));
        Assert.IsType<SharpTSProxy>(input.TagReceiver);
        Assert.Equal(1, input.TagReads);
    }

    [Fact]
    public void ActualTagGetterPreservesTheTypedAbruptPayload()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost(); Configure(host);
        VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag);
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, error => error is Carrier, error => ((Carrier)error).Value);
        var marker = new object();
        var input = new Input { Role = 1, Tag = marker, Value = marker, TagThrows = true };
        Assert.Equal(true, Run(caller, input, "let same=false;try{Object.prototype.toString.call(input)}catch(error){same=error===input.value}same"));
        Assert.Equal(1, input.TagReads);
    }

    [Fact]
    public void TagConfigurationPreservesOtherCapabilitiesAndRejectsLateChanges()
    {
        using var host = Host();
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag));
        Configure(host);
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, Write);
        VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag);
        Assert.NotNull(host.InputPropertyAccess!.Write);
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(host, Tag));
        host.ToVm(new Input());
        using var other = Host(); Configure(other); other.ToVm(new Input());
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostInputToStringTag(other, Tag));
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