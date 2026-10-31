using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInputPropertyWriteTests
{
    private sealed class Input
    {
        internal readonly Dictionary<string, object?> Values = new() { ["0"] = 1d, ["1"] = 2d, ["value"] = 1d, ["length"] = 2d };
        internal int Writes;
        internal object? Receiver;
        internal bool Refuse;
        internal object? Abrupt;
    }
    private sealed class Carrier(object? value) : Exception { internal readonly object? Value = value; }
    private static bool Admits(object value) => value.GetType() == typeof(Input);
    private static int Role(object value) => 1;
    private static object? Read(object value, string name) => ((Input)value).Values.GetValueOrDefault(name, SharpTSUndefined.Instance);
    private static bool Write(object value, string name, object? assigned)
    {
        var input = (Input)value;
        input.Writes++;
        input.Receiver = value;
        if (input.Abrupt is not null) throw new Carrier(input.Abrupt);
        if (input.Refuse) return false;
        input.Values[name] = assigned;
        return true;
    }
    private static VmExecutionHost Configure(Interpreter caller, bool writable = true)
    {
        var host = caller.GetVmExecutionHost();
        VmModuleInterpreter.ConfigureExecutionHostInputPropertyAccess(host, typeof(Input), Admits, Read, Role);
        if (writable) VmModuleInterpreter.ConfigureExecutionHostInputPropertyWrites(host, Write);
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, error => error is Carrier, error => ((Carrier)error).Value);
        return host;
    }
    private static object? Run(Interpreter caller, Input input, string code)
    {
        using var scope = caller.GetVmExecutionHost().Enter();
        return VmModuleInterpreter.ExecuteInNewContext(code, new SharpTSObject(new Dictionary<string, object?> { ["input"] = input }), caller);
    }

    [Theory]
    [InlineData("input[0]=7;input[0]", 7d, "0")]
    [InlineData("input.value=7;input.value", 7d, "value")]
    [InlineData("input[-0]=7;input[0]", 7d, "0")]
    [InlineData("input[1.5]=7;input[1.5]", 7d, "1.5")]
    [InlineData("input.length=7;input.length", 7d, "length")]
    [InlineData("input[0]+=6", 7d, "0")]
    [InlineData("input.value+=6", 7d, "value")]
    [InlineData("++input[0]", 2d, "0")]
    [InlineData("input[0]++", 1d, "0")]
    [InlineData("++input.value", 2d, "value")]
    [InlineData("input.value++", 1d, "value")]
    [InlineData("input[0]&&=7", 7d, "0")]
    [InlineData("input.value&&=7", 7d, "value")]
    public void ActualWriterReceivesCanonicalKeyAndOriginalTarget(string source, double expected, string key)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var input = new Input();
        Assert.Equal(expected, Run(caller, input, source));
        Assert.Equal(1, input.Writes);
        Assert.Same(input, input.Receiver);
        Assert.True(input.Values.ContainsKey(key));
    }

    [Theory]
    [InlineData("input[0]||=7")]
    [InlineData("input[0]??=7")]
    [InlineData("input.value||=7")]
    [InlineData("input.value??=7")]
    public void LogicalShortCircuitDoesNotWriteOrEvaluateTheRightHandSide(string expression)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var input = new Input();
        Assert.Equal(1d, Run(caller, input, expression.Replace("7", "(()=>{throw 7;})()")));
        Assert.Equal(0, input.Writes);
    }

    [Theory]
    [InlineData("input[0]=7", 7d)]
    [InlineData("input.value=7", 7d)]
    [InlineData("input[0]+=6", 7d)]
    [InlineData("input.value+=6", 7d)]
    [InlineData("input[0]++", 1d)]
    public void SloppyRefusalRetainsAssignmentOrPostfixResult(string expression, double expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var input = new Input { Refuse = true };
        Assert.Equal(expected, Run(caller, input, expression));
        Assert.Equal(1d, input.Values["0"]);
        Assert.Equal(1d, input.Values["value"]);
        Assert.Equal(1, input.Writes);
    }

    [Theory]
    [InlineData("input[0]=7")]
    [InlineData("input.value=7")]
    [InlineData("input[0]+=6")]
    [InlineData("input.value+=6")]
    [InlineData("input[0]++")]
    [InlineData("input.value++")]
    [InlineData("input[0]&&=7")]
    [InlineData("input.value&&=7")]
    public void StrictRefusalThrowsTheVmRealmTypeError(string expression)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var input = new Input { Refuse = true };
        Assert.Equal("TypeError|true", Run(caller, input, "'use strict';let result='';try{" + expression + "}catch(error){result=error.name+'|'+(error instanceof TypeError)}result"));
        Assert.Equal(1, input.Writes);
    }

    [Theory]
    [InlineData("input[0]=7")]
    [InlineData("input.value+=6")]
    [InlineData("input[0]++")]
    [InlineData("input.value&&=7")]
    public void ActualThrowingSetterPreservesPayloadIdentityAndRunsOnce(string expression)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var marker = new object();
        var input = new Input { Abrupt = marker }; input.Values["marker"] = marker;
        Assert.Equal(true, Run(caller, input, "let same=false;try{" + expression + "}catch(error){same=error===input.marker}same"));
        Assert.Equal(1, input.Writes);
        Assert.Same(input, input.Receiver);
    }

    [Theory]
    [InlineData("input[key]=rhs()", "3|3|2|rhs,string1")]
    [InlineData("input[key]+=rhs()", "4|1|4|string1,rhs,string2")]
    [InlineData("input[key]&&=rhs()", "3|1|3|string1,rhs,string2")]
    [InlineData("input[key]++", "1|1|2|string1,string2")]
    public void GetAndPutRetainRawKeysAndTheirSeparateObservableConversions(string expression, string expected)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller);
        var input = new Input();
        var source = "let n=0;const events=[];const key={[Symbol.toPrimitive](hint){events.push(hint+(++n));return n===1?'0':'1'}};function rhs(){events.push('rhs');return 3}const result=(" + expression + ");[result,input[0],input[1],events.join(',')].join('|')";
        Assert.Equal(expected, Run(caller, input, source));
    }

    [Fact]
    public void RightHandValueUsesTheActualFromVmBoundaryOnceAndRetainsIdentity()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = Configure(caller); int conversions = 0; var original = new object(); var adapted = new object();
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => { if (ReferenceEquals(value, original)) { conversions++; return adapted; } return value; }, value => ReferenceEquals(value, adapted) ? original : value);
        var input = new Input(); input.Values["marker"] = original;
        Assert.Equal(true, Run(caller, input, "input[0]=input.marker;input[0]===input.marker"));
        Assert.Same(adapted, input.Values["0"]);
        Assert.Equal(1, conversions);
    }

    [Fact]
    public void FreshVmCallableHasActualOperationReceiptBeforeLeavingTheBoundary()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        bool recorded = false;
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value => { if (value is SharpTSArrowFunction) recorded = VmModuleInterpreter.IsVmValue(value); return value; }, value => value);
        var input = new Input();
        Run(caller, input, "input[0]=()=>7");
        Assert.True(recorded);
        Assert.True(VmModuleInterpreter.IsVmValue(input.Values["0"]));
    }

    [Fact]
    public void NoWriterRetainsThePriorUnsupportedWritePath()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); Configure(caller, writable: false);
        var input = new Input();
        Assert.ThrowsAny<Exception>(() => Run(caller, input, "input[0]=7"));
        Assert.Equal(0, input.Writes);
        Assert.Equal(1d, input.Values["0"]);
    }

    [Fact]
    public void ADataViewCannotWriteWithoutItsMatchingActiveHost()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter()); var host = Configure(caller);
        var input = new Input(); var view = Assert.IsType<VmInputPropertyView>(host.ToVm(input));
        Assert.Throws<InvalidOperationException>(() => view.Write("0", 7d));
        using (host.Enter()) Assert.True(view.Write("0", 7d));
        host.Dispose();
        Assert.Throws<InvalidOperationException>(() => view.Write("0", 8d));
        Assert.Equal(7d, input.Values["0"]);
    }
}
