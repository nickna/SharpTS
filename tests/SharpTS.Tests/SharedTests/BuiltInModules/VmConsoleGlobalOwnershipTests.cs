using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmConsoleGlobalOwnershipTests
{
    [Fact]
    public void PersistentConsoleDataDoesNotRetainItsEvaluator()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var (context, owner) = CreateRealmData(caller);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(owner.IsAlive);
        Assert.Equal(0, caller.ActiveHandleCount);
        GC.KeepAlive(context);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (SharpTSObject Context, WeakReference Owner) CreateRealmData(Interpreter caller)
    {
        WeakReference? observed = null;
        var context = new SharpTSObject(new Dictionary<string, object?>
        {
            ["capture"] = BuiltInMethod.CreateV2("capture", 0, (owner, _, _) =>
            {
                observed = new WeakReference(owner);
                return RuntimeValue.Undefined;
            })
        });
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        Assert.Equal("object", run.Call(caller, ["capture();typeof console", context]));
        return (context, Assert.IsType<WeakReference>(observed));
    }

    [Fact]
    public void TheConsoleValueUsesTheExistingMethodAndActualCallingWriter()
    {
        using var output = new StringWriter();
        using var caller = new Interpreter(output, new StringWriter());
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var console = Assert.IsType<SharpTSObject>(run.Call(caller, ["console"]));
        var log = Assert.IsAssignableFrom<ISharpTSCallable>(console.GetProperty("log"));
        log.Call(caller, [7d]);
        Assert.Equal("7\n", output.ToString().Replace("\r\n", "\n"));
    }
}
