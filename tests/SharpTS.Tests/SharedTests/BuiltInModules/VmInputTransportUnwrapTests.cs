using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInputTransportUnwrapTests
{
    private sealed class Input : List<object?> { }
    private sealed class Pretend { public object Original => throw new InvalidOperationException("A shape must never be read."); }
    private static bool Admits(object value) => value.GetType() == typeof(Input);
    private static object? Read(object target, string key) => SharpTSUndefined.Instance;
    private static int Role(object target) => 1;
    private static VmExecutionHost Configure(Interpreter caller)
    {
        var host = caller.GetVmExecutionHost();
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(host, typeof(Input), Admits, Read, Role);
        return host;
    }

    [Fact]
    public void OnlyTheExactDataViewUnwrapsAndDoesNotAcquireVmOwnership()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        var input = new Input(); var view = Assert.IsType<VmInputPropertyView>(host.ToVm(input));
        Assert.Same(input, VmModuleInterpreter.UnwrapVmInputValue(view));
        Assert.Same(input, host.FromVm(view));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.False(VmModuleInterpreter.IsVmValue(view));
        var fake = new Pretend(); Assert.Same(fake, VmModuleInterpreter.UnwrapVmInputValue(fake));
        Assert.Null(VmModuleInterpreter.UnwrapVmInputValue(null));
        Assert.Same(SharpTSUndefined.Instance, VmModuleInterpreter.UnwrapVmInputValue(SharpTSUndefined.Instance));
        host.Dispose();
        Assert.Same(input, VmModuleInterpreter.UnwrapVmInputValue(view));
    }

    [Fact]
    public void TypedThrownPayloadRestoresItsOriginalInputIdentity()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        var input = new Input(); var view = host.ToVm(input);
        var carrier = new ThrowException(view);
        Assert.Same(view, VmModuleInterpreter.GetVmExceptionValue(carrier));
        Assert.Same(input, VmModuleInterpreter.UnwrapVmInputValue(VmModuleInterpreter.GetVmExceptionValue(carrier)));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
    }

    private static object? CompileWithExtensions(Interpreter caller, object extensions)
    {
        var compile = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["compileFunction"]);
        var options = new SharpTSObject(new Dictionary<string, object?> { ["contextExtensions"] = extensions });
        var function = FunctionBuiltIns.CallWithThis(caller, compile, SharpTSUndefined.Instance,
            ["return greeting + ' ' + name", new SharpTSArray(), options]);
        return FunctionBuiltIns.CallWithThis(caller, Assert.IsAssignableFrom<ISharpTSCallable>(function), SharpTSUndefined.Instance, []);
    }

    [Fact]
    public void ConfiguredInputArrayRetainsExistingContextExtensionLayers()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        var input = new Input { new SharpTSObject(new Dictionary<string, object?> { ["greeting"] = "hello", ["name"] = "old" }), new SharpTSObject(new Dictionary<string, object?> { ["name"] = "world" }) };
        using var scope = host.Enter();
        Assert.IsType<VmInputPropertyView>(host.ToVm(input));
        Assert.Equal("hello world", CompileWithExtensions(caller, input));
    }

    [Fact]
    public void LegacyListAndCoreArrayExtensionsKeepTheirExistingCarrierChecks()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        var layer = new SharpTSObject(new Dictionary<string, object?> { ["greeting"] = "hello", ["name"] = "world" });
        using var scope = host.Enter();
        Assert.Equal("hello world", CompileWithExtensions(caller, new List<object?> { layer }));
        Assert.Equal("hello world", CompileWithExtensions(caller, new SharpTSArray(new List<object?> { layer })));
    }
}
