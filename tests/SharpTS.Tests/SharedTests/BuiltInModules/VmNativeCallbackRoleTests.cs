using System.Reflection;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmNativeCallbackRoleTests
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ProvedRoleCarriesLengthAndNonConstructorEligibility(int role)
    {
        var value = EmitActualCallable();
        using var host = NewHost();
        VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, actual => ReferenceEquals(actual, value) ? role : 0);
        var callable = Assert.IsType<CompiledCallableAdapter>(host.ToVm(value));
        Assert.Equal(1, callable.Arity());
        Assert.True(Interpreter.IsNonConstructorWrapper(callable));
        Assert.True(ReflectBuiltIns.IsNotConstructor(callable));
        Assert.Same(callable, host.ToVm(value));
        host.Dispose();
        Assert.Equal(1, callable.Arity());
        Assert.True(ReflectBuiltIns.IsNotConstructor(callable));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void VmMetadataAndConstructorChecksUseOnlyTheProvedRole(int role)
    {
        var value = EmitActualCallable();
        using var host = NewHost();
        VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, actual => ReferenceEquals(actual, value) ? role : 0);
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        using var scope = host.Enter();
        var context = new SharpTSObject(new Dictionary<string, object?> { ["fn"] = host.ToVm(value) });
        var result = VmModuleInterpreter.ExecuteInNewContext("[typeof fn,fn.name,fn.length,Object.prototype.hasOwnProperty.call(fn,'prototype'),(()=>{try{Reflect.construct(function(){},[],fn);return 'constructed';}catch(error){return error instanceof TypeError?'type-error':'other';}})()].join('|')", context, caller);
        Assert.Equal("function||1|false|type-error", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void UnprovedRolesKeepTheOriginalMetadataFallback(int role)
    {
        using var host = NewHost();
        VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, _ => role);
        var callable = Assert.IsType<CompiledCallableAdapter>(host.ToVm(EmitActualCallable()));
        Assert.Equal(0, callable.Arity());
        Assert.False(Interpreter.IsNonConstructorWrapper(callable));
    }

    [Fact]
    public void StructuralFallbackCannotAcquireAProducerRole()
    {
        using var host = NewHost();
        int reads = 0;
        VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, _ => { reads++; return 1; });
        var ordinary = new StructuralCallable();
        using var scope = host.Enter();
        var callable = Assert.IsType<CompiledCallableAdapter>(VmContext.WrapCompiledCallable(ordinary));
        Assert.Same(ordinary, host.ToVm(ordinary));
        Assert.Equal(0, reads);
        Assert.Equal(0, callable.Arity());
        Assert.False(Interpreter.IsNonConstructorWrapper(callable));
    }

    [Fact]
    public void ConfigurationRequiresTheActualUnstartedHostAndIsSingleUse()
    {
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(new object(), _ => 0));
        using var host = NewHost();
        Assert.Throws<ArgumentNullException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, null!));
        VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, _ => 0);
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(host, _ => 0));
        using var started = NewHost();
        using (started.Enter()) { }
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(started, _ => 0));
        var disposed = NewHost();
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(disposed, _ => 0));
        using var adapted = NewHost();
        adapted.ToVm(EmitActualCallable());
        Assert.Throws<InvalidOperationException>(() => VmModuleInterpreter.ConfigureExecutionHostNativeCallbackRoles(adapted, _ => 0));
    }

    public sealed class StructuralCallable
    {
        public object Invoke(object[] arguments) => 7d;
    }
}
