using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmOperationInputReceiptTests
{
    [Fact]
    public void ReturningTheActualInputFromARecordedCallbackDoesNotUpgradeItsOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSObject(new Dictionary<string, object?> { ["value"] = 7d });
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var callback = VmModuleInterpreter.ExecuteInNewContext("()=>input", context, caller)!;
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Same(input, VmModuleInterpreter.InvokeVmCallable(callback, SharpTSUndefined.Instance, []));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(input, "value"));
    }

    [Fact]
    public void ReadingTheActualInputFromARecordedObjectDoesNotUpgradeItsOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSObject(new Dictionary<string, object?> { ["value"] = 7d });
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var container = VmModuleInterpreter.ExecuteInNewContext("({input})", context, caller)!;
        Assert.True(VmModuleInterpreter.IsVmValue(container));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Same(input, VmModuleInterpreter.ReadVmValueProperty(container, "input"));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
    }

    [Fact]
    public void APreviouslyProducedPropertyResultKeepsItsOriginalOwner()
    {
        using var first = new Interpreter(new StringWriter(), new StringWriter());
        using var second = new Interpreter(new StringWriter(), new StringWriter());
        var input = VmModuleInterpreter.ExecuteInNewContext("({value:7})", null, first)!;
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        var container = VmModuleInterpreter.ExecuteInNewContext("({input})", context, second)!;
        Assert.Same(input, VmModuleInterpreter.ReadVmValueProperty(container, "input"));
        second.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(7d, VmModuleInterpreter.ReadVmValueProperty(input, "value"));
        first.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(input, "value"));
    }
}