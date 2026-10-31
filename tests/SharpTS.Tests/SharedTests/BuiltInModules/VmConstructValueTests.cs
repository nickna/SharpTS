using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmConstructValueTests
{
    [Fact]
    public void ActualClassUsesItsRealOwnerAndRecordsTheConstructedValue()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{constructor(value){this.value=value;}})", null, caller)!;
        var result = VmModuleInterpreter.ConstructVmCallable(constructor, [7d], null)!;
        Assert.True(VmModuleInterpreter.IsVmValue(result));
        Assert.Equal(7d, VmModuleInterpreter.ReadVmValueProperty(result, "value"));
        Assert.Same(VmModuleInterpreter.ReadVmValueProperty(constructor, "prototype"), VmModuleInterpreter.GetVmValuePrototype(result));
    }

    [Fact]
    public void AlternateRecordedNewTargetKeepsItsActualPrototype()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{constructor(value){this.value=value;}})", null, caller)!;
        var newTarget = VmModuleInterpreter.ExecuteInNewContext("(class Alternate{})", null, caller)!;
        var prototype = VmModuleInterpreter.ReadVmValueProperty(newTarget, "prototype");
        var result = VmModuleInterpreter.ConstructVmCallable(constructor, [8d], newTarget)!;
        Assert.Same(prototype, VmModuleInterpreter.GetVmValuePrototype(result));
        Assert.Equal(8d, VmModuleInterpreter.ReadVmValueProperty(result, "value"));
    }

    [Theory]
    [InlineData("(()=>7)")]
    [InlineData("Promise.resolve(7)")]
    public void RecordedNonConstructorsProduceGuestTypeError(string source)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = VmModuleInterpreter.ExecuteInNewContext(source, null, caller)!;
        var exception = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ConstructVmCallable(value, [], null));
        Assert.IsType<SharpTSTypeError>(exception.Value);
    }

    [Fact]
    public void UnrecordedValuesAndNewTargetsCannotAcquireConstructionAuthority()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{})", null, caller)!;
        var ordinary = BuiltInMethod.CreateV2("ordinary", 0, (_, _, _) => RuntimeValue.Undefined);
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConstructVmCallable(ordinary, [], null));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConstructVmCallable(constructor, [], ordinary));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConstructVmCallable(new object(), [], null));
    }

    [Fact]
    public void RecordedInvalidNewTargetProducesGuestTypeError()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{})", null, caller)!;
        var newTarget = VmModuleInterpreter.ExecuteInNewContext("(()=>7)", null, caller)!;
        var exception = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ConstructVmCallable(constructor, [], newTarget));
        Assert.IsType<SharpTSTypeError>(exception.Value);
    }

    [Fact]
    public async Task ActualPromiseConstructorKeepsItsOwnerAndExecutorSemantics()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("Promise", null, caller)!;
        int calls = 0;
        var executor = BuiltInMethod.CreateV2("executor", 2, (interpreter, _, args) =>
        {
            calls++;
            FunctionBuiltIns.CallWithThis(interpreter, (ISharpTSCallable)args[0].ToObject()!, SharpTSUndefined.Instance, [7d]);
            return RuntimeValue.Undefined;
        });
        var promise = VmModuleInterpreter.ConstructVmCallable(constructor, [executor], constructor)!;
        Assert.Equal(1, calls);
        Assert.True(VmModuleInterpreter.IsVmPromiseValue(promise));
        Assert.Equal(7d, await VmModuleInterpreter.GetVmPromiseTask(promise));
        Assert.Same(constructor, VmModuleInterpreter.ReadVmValueProperty(promise, "constructor"));
    }

    [Fact]
    public void ConstructionUsesTheActiveBoundaryConversionsAndPublishesAbruptValues()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value, value => value is double number && number == 7d ? 71d : value);
        using var scope = host.Enter();
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{constructor(value){this.value=value;}})", null, caller)!;
        var result = VmModuleInterpreter.ConstructVmCallable(constructor, [7d], constructor)!;
        Assert.Equal(71d, VmModuleInterpreter.ReadVmValueProperty(result, "value"));
        var throwing = VmModuleInterpreter.ExecuteInNewContext("(class Box{constructor(value){throw value;}})", null, caller)!;
        var failure = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ConstructVmCallable(throwing, [7d], throwing));
        Assert.Equal(71d, failure.Value);
    }

    [Theory]
    [InlineData("(class Box{})", true)]
    [InlineData("Promise", true)]
    [InlineData("(function ordinary(){})", true)]
    [InlineData("(()=>7)", false)]
    [InlineData("Promise.resolve(7)", false)]
    public void ConstructorEligibilityUsesTheSameActualReceipt(string source, bool expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = VmModuleInterpreter.ExecuteInNewContext(source, null, caller)!;
        Assert.Equal(expected, VmModuleInterpreter.IsVmConstructorValue(value));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.IsVmConstructorValue(new object()));
    }
    [Fact]
    public void DisposedOwnerRejectsFurtherConstruction()
    {
        var caller = new Interpreter(new StringWriter(), new StringWriter());
        var constructor = VmModuleInterpreter.ExecuteInNewContext("(class Box{})", null, caller)!;
        caller.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConstructVmCallable(constructor, [], null));
    }
}