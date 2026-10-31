using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmReturnedObjectReceiptTests
{
    [Theory]
    [InlineData("({value:7})")]
    [InlineData("[7]")]
    [InlineData("new (class Box{constructor(){this.value=7}})()")]
    public void ActualResultObjectsKeepTheirRealOwnerAcrossCollection(string source)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var result = VmModuleInterpreter.ExecuteInNewContext(source, null, caller)!;
        Assert.True(VmModuleInterpreter.IsVmValue(result));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(7d, VmModuleInterpreter.ReadVmValueProperty(result, result is SharpTSArray ? "0" : "value"));
        caller.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(result, "value"));
    }

    [Fact]
    public void ReturnedCompanionKeepsNestedPromiseAndCallableReceipts()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var companion = VmModuleInterpreter.ExecuteInNewContext("({promise:Promise.resolve(7),readLog:()=>8})", null, caller)!;
        var promise = VmModuleInterpreter.ReadVmValueProperty(companion, "promise")!;
        var readLog = VmModuleInterpreter.ReadVmValueProperty(companion, "readLog")!;
        Assert.True(VmModuleInterpreter.IsVmPromiseValue(promise));
        Assert.True(VmModuleInterpreter.IsVmValue(promise));
        Assert.Equal(8d, VmModuleInterpreter.InvokeVmCallable(readLog, companion, []));
    }

    [Fact]
    public void ActualInputRecordDoesNotAcquireTheEvaluatorsOwnerReceipt()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSObject(new Dictionary<string, object?> { ["value"] = 7d });
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = input });
        Assert.Same(input, VmModuleInterpreter.ExecuteInNewContext("input", context, caller));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(input, "value"));
    }

    [Fact]
    public void PreviouslyProducedObjectKeepsItsOriginalOwnerWhenPassedBackAsInput()
    {
        using var first = new Interpreter(new StringWriter(), new StringWriter());
        using var second = new Interpreter(new StringWriter(), new StringWriter());
        var original = VmModuleInterpreter.ExecuteInNewContext("({value:7})", null, first)!;
        var context = new SharpTSObject(new Dictionary<string, object?> { ["input"] = original });
        Assert.Same(original, VmModuleInterpreter.ExecuteInNewContext("input", context, second));
        second.Dispose();
        Assert.Equal(7d, VmModuleInterpreter.ReadVmValueProperty(original, "value"));
        first.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ReadVmValueProperty(original, "value"));
    }
}