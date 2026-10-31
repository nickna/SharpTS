using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmValueCoercionTests
{
    private static object Value(Interpreter caller, string source, object? context = null)
        => VmModuleInterpreter.ExecuteInNewContext(source, context, caller)!;

    [Theory]
    [InlineData("default")]
    [InlineData("number")]
    [InlineData("string")]
    public void ActualExoticConversionReceivesTheExactHintAndOriginalReceiver(string hint)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, "({[Symbol.toPrimitive](hint){return (this===globalThis.actual)+':'+hint}})");
        var selfChecking = Value(caller, "(()=>{const value={[Symbol.toPrimitive](hint){return (this===value)+':'+hint}};return value})()");
        Assert.Equal("true:" + hint, VmModuleInterpreter.ToPrimitiveVmValue(selfChecking, hint));
        Assert.Equal("false:" + hint, VmModuleInterpreter.ToPrimitiveVmValue(value, hint));
    }

    [Theory]
    [InlineData("string", "string-first")]
    [InlineData("number", 7d)]
    [InlineData("default", 7d)]
    public void OrdinaryConversionUsesTheActualHintOrder(string hint, object expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, "({toString(){return 'string-first'},valueOf(){return 7}})");
        Assert.Equal(expected, VmModuleInterpreter.ToPrimitiveVmValue(value, hint));
    }

    [Theory]
    [InlineData("new Error('boom')", "Error: boom")]
    [InlineData("new TypeError('boom')", "TypeError: boom")]
    [InlineData("(()=>{const error=new Error('boom');error.toString=function(){return 'overridden'};return error})()", "overridden")]
    [InlineData("[1,2]", "1,2")]
    public void ActualVmObjectsUseTheirJavaScriptConversionProtocol(string source, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Assert.Equal(expected, VmModuleInterpreter.ToPrimitiveVmValue(Value(caller, source), "string"));
    }

    [Fact]
    public void ActualRuntimeErrorReceiptSupportsConversionWithoutGeneralValueAdmission()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var failure = Assert.Throws<ThrowException>(() => Value(caller, "future;let future=1"));
        Assert.True(VmModuleInterpreter.IsVmErrorValue(failure.Value));
        Assert.False(VmModuleInterpreter.IsVmValue(failure.Value));
        Assert.StartsWith("ReferenceError:", Assert.IsType<string>(VmModuleInterpreter.ToPrimitiveVmValue(failure.Value!, "string")));
        Assert.False(VmModuleInterpreter.IsVmValue(failure.Value));
    }

    [Theory]
    [InlineData("({[Symbol.toPrimitive](){return {}}})")]
    [InlineData("({[Symbol.toPrimitive]:7})")]
    [InlineData("({toString(){return {}},valueOf(){return {}}})")]
    public void InvalidConversionResultsAndMethodsThrowOwnedGuestTypeErrors(string source)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, source);
        var failure = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ToPrimitiveVmValue(value, "string"));
        Assert.IsType<SharpTSTypeError>(failure.Value);
        Assert.True(VmModuleInterpreter.IsVmErrorValue(failure.Value));
    }

    [Fact]
    public void ExoticGetterAndCallExecuteOnceAndRecordTheAbruptPayload()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, "(()=>{const marker={};const events=[];return {marker,events,get [Symbol.toPrimitive](){events.push('get');return function(hint){events.push(hint);throw marker}}}})()");
        var marker = VmModuleInterpreter.ReadVmValueProperty(value, "marker");
        var failure = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ToPrimitiveVmValue(value, "string"));
        Assert.Same(marker, failure.Value);
        Assert.True(VmModuleInterpreter.IsVmValue(failure.Value));
        var events = VmModuleInterpreter.ReadVmValueProperty(value, "events")!;
        Assert.Equal("get,string", VmModuleInterpreter.ToPrimitiveVmValue(events, "string"));
    }

    [Fact]
    public void ConversionUsesItsOriginalOwnerAndPublishesActualWrites()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var context = new SharpTSObject(new Dictionary<string, object?> { ["label"] = "first" });
        var value = Value(caller, "({toString(){globalThis.written=7;return label}})", context);
        using var other = new Interpreter(new StringWriter(), new StringWriter());
        var received = Value(other, "input", new SharpTSObject(new Dictionary<string, object?> { ["input"] = value, ["label"] = "second" }));
        Assert.Same(value, received);
        Assert.Equal("first", VmModuleInterpreter.ToPrimitiveVmValue(received, "string"));
        Assert.Equal(7d, context.GetProperty("written"));
    }

    [Fact]
    public void ResultsAndThrownPayloadsUseTheExistingActiveBoundaryAdapter()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost(); var mapped = new object();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => value is double number && number == 7d ? mapped : value, value => value);
        var value = Value(caller, "({valueOf(){return 7}})");
        Assert.Same(mapped, VmModuleInterpreter.ToPrimitiveVmValue(value, "number"));
        var throwing = Value(caller, "({[Symbol.toPrimitive](){throw 7}})");
        var failure = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ToPrimitiveVmValue(throwing, "number"));
        Assert.Equal(7d, failure.Value);
        Assert.Same(mapped, failure.Data["__tsValue"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("other")]
    [InlineData(null)]
    public void HintSchemaIsValidatedBeforeExecutingGuestConversion(string? hint)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, "({[Symbol.toPrimitive](){throw 7}})");
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ToPrimitiveVmValue(value, hint!));
    }

    [Theory]
    [InlineData("new Error('boom')", "Error: boom")]
    [InlineData("({[Symbol.toPrimitive](){return 7n}})", "7")]
    [InlineData("({[Symbol.toPrimitive](){return false}})", "false")]
    [InlineData("({[Symbol.toPrimitive](){return null}})", "null")]
    [InlineData("({[Symbol.toPrimitive](){return undefined}})", "undefined")]
    public void TypedStringEndpointRetainsTheActualPrimitiveKind(string source, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Assert.Equal(expected, VmModuleInterpreter.ToStringVmValue(Value(caller, source)));
    }

    [Fact]
    public void StringEndpointRejectsAnExoticSymbolResultWithTheOwnersTypeError()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var value = Value(caller, "({[Symbol.toPrimitive](){return Symbol('vm')}})");
        Assert.IsType<SharpTSSymbol>(VmModuleInterpreter.ToPrimitiveVmValue(value, "string"));
        var failure = Assert.Throws<ThrowException>(() => VmModuleInterpreter.ToStringVmValue(value));
        Assert.IsType<SharpTSTypeError>(failure.Value);
        Assert.True(VmModuleInterpreter.IsVmErrorValue(failure.Value));
    }

    [Fact]
    public void TypedStringResultIsNotRewrittenByTheObjectValueAdapter()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var mapped = new object();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(caller.GetVmExecutionHost(), value => value is string ? mapped : value, value => value);
        var value = Value(caller, "({toString(){return 'vm-string'}})");
        Assert.Same(mapped, VmModuleInterpreter.ToPrimitiveVmValue(value, "string"));
        Assert.Equal("vm-string", VmModuleInterpreter.ToStringVmValue(value));
    }
    [Fact]
    public void UnrecordedInputsAndDisposedOwnersNeverAcquireCoercionAuthority()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var input = new SharpTSObject(new Dictionary<string, object?>());
        Assert.Same(input, Value(caller, "input", new SharpTSObject(new Dictionary<string, object?> { ["input"] = input })));
        Assert.False(VmModuleInterpreter.IsVmValue(input));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ToPrimitiveVmValue(input, "string"));
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ToPrimitiveVmValue(new object(), "string"));
        var owned = Value(caller, "({toString(){return 'owned'}})");
        caller.Dispose();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ToPrimitiveVmValue(owned, "string"));
    }
}
