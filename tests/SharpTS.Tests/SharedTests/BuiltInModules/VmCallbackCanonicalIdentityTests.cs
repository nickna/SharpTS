using System.Reflection;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCallbackCanonicalIdentityTests
{
    private static VmExecutionHost NewHost() => (VmExecutionHost)VmModuleInterpreter.CreateExecutionHost(
        _ => { }, _ => { }, _ => { }, () => { }, () => { }, () => { });

    private static object EmitActualCallable()
    {
        var (assembly, _) = TestHarness.CompileAndRun("function make(){return ()=>7;}", DecoratorMode.None);
        var make = assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "make");
        return make.Invoke(null, new object?[make.GetParameters().Length])!;
    }

    [Fact]
    public void ActualManagedFunctionKeepsIdentityAcrossSeedAndExplicitHostConversion()
    {
        var actual = EmitActualCallable();
        using var host = NewHost();
        object? seeded;
        using (host.Enter()) seeded = VmContext.WrapCompiledCallable(actual);
        Assert.IsType<CompiledCallableAdapter>(seeded);
        Assert.Same(seeded, host.ToVm(actual));
        Assert.Same(seeded, VmContext.WrapVmCompiledCallableForHost(actual, host));
        Assert.Same(actual, host.FromVm(seeded));
    }

    [Fact]
    public void EachActualHostRetainsItsOwnInvocationLifetime()
    {
        var actual = EmitActualCallable();
        using var first = NewHost();
        using var second = NewHost();
        var left = Assert.IsType<CompiledCallableAdapter>(first.ToVm(actual));
        var right = Assert.IsType<CompiledCallableAdapter>(second.ToVm(actual));
        Assert.NotSame(left, right);
        Assert.Equal(7d, left.Call(null!, []));
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => left.Call(null!, []));
        Assert.Throws<ObjectDisposedException>(() => first.ToVm(actual));
        Assert.Equal(7d, right.Call(null!, []));
    }

    [Fact]
    public void ExplicitOwnerWinsOverAnUnrelatedAmbientHost()
    {
        var actual = EmitActualCallable();
        using var first = NewHost();
        using var second = NewHost();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(first, value => value,
            value => value is double number && number == 7d ? 71d : value);
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(second, value => value,
            value => value is double number && number == 7d ? 72d : value);
        CompiledCallableAdapter adapted;
        using (first.Enter()) adapted = Assert.IsType<CompiledCallableAdapter>(second.ToVm(actual));
        Assert.Equal(72d, adapted.Call(null!, []));
        Assert.Same(adapted, second.ToVm(actual));
    }

    [Fact]
    public void ActualHostConversionPrecedesLegacyContextCallableWrapping()
    {
        var ordinary = new StructuralCallable();
        var converted = new object();
        using var host = NewHost();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value,
            value => ReferenceEquals(value, ordinary) ? converted : value);
        Dictionary<string, object?> extracted;
        using (host.Enter()) extracted = VmContext.ExtractProperties(new Dictionary<string, object?> { ["value"] = ordinary });
        Assert.Same(converted, extracted["value"]);
    }

    [Fact]
    public void StructuralCompatibilityDoesNotGainTypedAdmissionOrCaching()
    {
        var ordinary = new StructuralCallable();
        using var host = NewHost();
        Assert.Same(ordinary, host.ToVm(ordinary));
        using (host.Enter())
        {
            var first = VmContext.WrapCompiledCallable(ordinary);
            var second = VmContext.WrapCompiledCallable(ordinary);
            Assert.NotSame(first, second);
            Assert.Same(ordinary, host.FromVm(first));
        }
    }

    public sealed class StructuralCallable
    {
        public object Invoke(object[] arguments) => 7d;
        public object InvokeWithThis(object receiver, object[] arguments) => 99d;
    }
}
