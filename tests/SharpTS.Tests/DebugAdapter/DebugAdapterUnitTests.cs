using SharpTS.DebugAdapter;
using SharpTS.DebugAdapter.Adapter;
using SharpTS.DebugAdapter.Protocol;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.DebugAdapter;

[Collection("DebugAdapterTests")]
public sealed class DebugAdapterUnitTests
{
    [Fact]
    public async Task DiagnosticFileLogIsReplacedAndBoundedPerSession()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "sharpts-dap-log", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "adapter.log");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "stale-session-content");

            await using (var writer = new BoundedFileLogWriter(path))
            {
                await writer.WriteAsync(new string(
                    'x', BoundedFileLogWriter.MaximumCharacters + 100));
                await writer.WriteAsync("must-not-be-written");
            }

            string content = await File.ReadAllTextAsync(path);
            Assert.Equal(BoundedFileLogWriter.MaximumCharacters, content.Length);
            Assert.DoesNotContain("stale-session-content", content);
            Assert.DoesNotContain("must-not-be-written", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelRequestCanCancelQueuedLaunchWithoutBreakingSessionFraming()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "sharpts-dap-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(program, "let value = 1;");
        byte[] requests = DapProtocolConnectionTests.Frame(
                """{"seq":1,"type":"request","command":"initialize","arguments":{"adapterID":"sharpts"}}""")
            .Concat(DapProtocolConnectionTests.Frame(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    seq = 2,
                    type = "request",
                    command = "launch",
                    arguments = new { program },
                })))
            .Concat(DapProtocolConnectionTests.Frame(
                """{"seq":3,"type":"request","command":"cancel","arguments":{"requestId":2}}"""))
            .ToArray();

        await using var output = new MemoryStream();
        await using var connection = new DapProtocolConnection(new MemoryStream(requests), output);
        await using var session = new DapAdapterSession(connection, TextWriter.Null);
        await session.RunAsync(default);

        List<System.Text.Json.JsonElement> messages =
            await DapProtocolConnectionTests.ReadServerMessagesAsync(output.ToArray());
        System.Text.Json.JsonElement launch = messages.Single(message =>
            message.GetProperty("type").GetString() == "response"
            && message.GetProperty("request_seq").GetInt32() == 2);
        Assert.False(launch.GetProperty("success").GetBoolean());
        Assert.Contains("cancelled", launch.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(messages, message =>
            message.GetProperty("type").GetString() == "response"
            && message.GetProperty("request_seq").GetInt32() == 3
            && message.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void VariableHandlesCannotAliasAcrossStops()
    {
        var handles = new DebugHandleStore();
        handles.Reset(1);
        int oldHandle = handles.Add(new object());

        handles.Reset(2);
        int currentHandle = handles.Add(new object());

        Assert.NotEqual(oldHandle, currentHandle);
        Assert.ThrowsAny<Exception>(() => handles.Get<object>(oldHandle));
        Assert.NotNull(handles.Get<object>(currentHandle));
    }

    [Fact]
    public void VariableHandleStoreHasHardPerStopLimit()
    {
        var handles = new DebugHandleStore();
        handles.Reset(1);
        for (int index = 0; index < 10_000; index++)
            handles.Add(new object());

        Exception exception = Assert.ThrowsAny<Exception>(() => handles.Add(new object()));
        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstanceExpansionDoesNotInvokeGetter()
    {
        var klass = new SharpTSClass("Inspectable", null, [], [], []);
        var instance = new SharpTSInstance(klass);
        instance.SetRawField("value", 42d);
        instance.DefineProperty("computed", new SharpTSPropertyDescriptor(
            getter: new ThrowingCallable(), enumerable: true, configurable: true));

        IReadOnlyList<DebugVariableValue> children =
            DebugValueInspector.EnumerateChildren(instance, 0, count: null);

        Assert.Contains(children, child => child.Name == "value" && child.Value == "42");
        Assert.Contains(children, child => child.Name == "computed" && child.Value == "<accessor>");
    }

    [Fact]
    public void ValuePreviewsDoNotInvokeHostConversionMethods()
    {
        DebugVariableValue callable = DebugValueInspector.Describe("callback", new ThrowingCallable());
        Assert.Equal("<function>", callable.Value);

        var klass = new SharpTSClass("Inspectable", null, [], [], []);
        DebugVariableValue instance = DebugValueInspector.Describe("instance", new ThrowingInstance(klass));
        Assert.Equal("Inspectable instance", instance.Value);
        Assert.Equal("42n", DebugValueInspector.Describe("bigint", new ThrowingBigInt()).Value);
        Assert.Equal("Symbol(key)", DebugValueInspector.Describe("symbol", new ThrowingSymbol()).Value);
        Assert.True(DebugValueInspector.Describe("large", new SharpTSBigInt(new string('9', 1_000))).Value.Length < 270);
        Assert.True(DebugValueInspector.Describe("large", new SharpTSSymbol(new string('a', 1_000))).Value.Length < 270);
    }

    [Fact]
    public void ArrayFiltersApplyBeforePagingAndZeroCountReturnsAllChildren()
    {
        var array = new SharpTSArray([10d, 20d, 30d]);
        array.SetNamedProperty("z", 42d);
        array.SetNamedProperty("a", 40d);

        DebugVariableValue named = Assert.Single(DebugValueInspector.EnumerateChildren(
            array, start: 1, count: 1, filter: "named"));
        Assert.Equal("z", named.Name);
        DebugVariableValue indexed = Assert.Single(DebugValueInspector.EnumerateChildren(
            array, start: 1, count: 1, filter: "indexed"));
        Assert.Equal("1", indexed.Name);
        Assert.Equal("20", indexed.Value);
        Assert.Equal(5, DebugValueInspector.EnumerateChildren(array, 0, count: 0).Count);
        Assert.Equal(5, DebugValueInspector.EnumerateChildren(array, 0, count: null).Count);
    }

    [Fact]
    public void OversizedVariableRequestsFailClearlyAndCanBePaged()
    {
        var array = new SharpTSArray(Enumerable.Range(0, 1_001).Select(index => (object?)(double)index).ToList());
        DapRequestException failure = Assert.Throws<DapRequestException>(() =>
            DebugValueInspector.EnumerateChildren(array, 0, count: 0));
        Assert.Contains("smaller page", failure.Message);
        Assert.Equal(1_000, DebugValueInspector.EnumerateChildren(array, 0, count: 1_000).Count);
        Assert.Equal("1000", Assert.Single(DebugValueInspector.EnumerateChildren(
            array, 1_000, count: 1_000)).Value);

        var scope = new RuntimeEnvironment();
        for (int index = 0; index < 1_001; index++)
            scope.Define($"value{index}", (double)index);
        var handle = new DebugScopeHandle(scope);
        Assert.Throws<DapRequestException>(() => DebugValueInspector.EnumerateChildren(handle, 0, count: 0));
        Assert.Equal(1_000, DebugValueInspector.EnumerateChildren(handle, 0, count: 1_000).Count);
        Assert.Empty(DebugValueInspector.EnumerateChildren(handle, 0, count: 0, filter: "indexed"));
    }

    [Fact]
    public async Task SparseArrayPagesSeekDirectlyToHighIndicesAndThenNamedProperties()
    {
        var array = new SharpTSArray([]);
        array.Set(1_000_000_000, 42d);
        array.SetNamedProperty("label", "sparse");

        IReadOnlyList<DebugVariableValue> page = await Task.Run(() =>
            DebugValueInspector.EnumerateChildren(
                array, start: 1_000_000_000, count: 1, filter: "indexed"))
            .WaitAsync(TimeSpan.FromSeconds(5));
        DebugVariableValue indexed = Assert.Single(page);
        Assert.Equal("1000000000", indexed.Name);
        Assert.Equal("42", indexed.Value);
        Assert.Equal("label", Assert.Single(DebugValueInspector.EnumerateChildren(
            array, start: 1_000_000_001, count: 1)).Name);
        Assert.Empty(DebugValueInspector.EnumerateChildren(
            array, start: 1_000_000_001, count: 1, filter: "indexed"));
    }

    [Fact]
    public void DiagnosticRedactionRemovesEnvironmentValues()
    {
        const string name = "SHARPTS_DAP_REDACTION_TEST";
        const string secret = "not-a-real-secret-value";
        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, secret);
            string redacted = DapAdapterSession.Redact($"failure included {secret}");
            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
            Assert.Contains("<redacted>", redacted, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public void ObjectExpansionIsDeterministicPagedAndCycleSafe()
    {
        var fields = new Dictionary<string, object?>
        {
            ["z"] = 3d,
            ["a"] = 1d,
        };
        var value = new SharpTSObject(fields);
        value.SetProperty("self", value);

        IReadOnlyList<DebugVariableValue> page =
            DebugValueInspector.EnumerateChildren(value, start: 1, count: 1);

        DebugVariableValue child = Assert.Single(page);
        Assert.Equal("self", child.Name);
        Assert.Same(value, child.ExpandableValue);
    }

    [Fact]
    public void DebuggerEvaluationAllowsPureExpressionsAndRejectsMutationAndCalls()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var environment = new RuntimeEnvironment();
        environment.Define("value", 40d);
        environment.Define("record", new SharpTSObject(new Dictionary<string, object?>
        {
            ["answer"] = 42d,
        }));

        Assert.Equal(42d, interpreter.EvaluateDebuggerExpression(
            "value + 2", environment, allowPropertyAccess: true, default));
        Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
            "value = 0", environment, allowPropertyAccess: true, default));
        Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
            "console.log(value)", environment, allowPropertyAccess: true, default));
        Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
            "record.answer", environment, allowPropertyAccess: false, default));
    }

    [Fact]
    public void HoverCoercionAndSpreadCannotExecuteGuestCode()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var environment = new RuntimeEnvironment();
        var conversion = new CountingCallable();
        var record = new SharpTSObject(new Dictionary<string, object?>
        {
            ["valueOf"] = conversion,
            ["toString"] = conversion,
        });
        record.DefineProperty("answer", new SharpTSPropertyDescriptor(
            getter: conversion, enumerable: true, configurable: true));
        environment.Define("record", record);
        environment.Define("seed", 40d);

        foreach (string expression in new[] { "record + 2", "`${record}`", "({ [record]: 1 })", "[...record]", "({...record})" })
            Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
                expression, environment, allowPropertyAccess: false, default));
        foreach (string expression in new[] { "[...record]", "({...record})" })
            Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
                expression, environment, allowPropertyAccess: true, default));
        Assert.Equal(0, conversion.Calls);
        Assert.Same(record, interpreter.EvaluateDebuggerExpression(
            "record", environment, allowPropertyAccess: false, default));
        Assert.Equal(42d, interpreter.EvaluateDebuggerExpression(
            "seed + 2", environment, allowPropertyAccess: true, default));
        Assert.Equal(42d, interpreter.EvaluateDebuggerExpression(
            "record.answer", environment, allowPropertyAccess: true, default));
        Assert.Equal(1, conversion.Calls);
    }

    [Fact]
    public void HoverLogicalAndTypeInspectionCannotInvokeClrOperators()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var environment = new RuntimeEnvironment();
        var host = new CountingLogicalOperator();
        environment.Define("host", new DotNetInstance(host, typeof(CountingLogicalOperator)));

        Assert.Throws<InvalidOperationException>(() => interpreter.EvaluateDebuggerExpression(
            "!host", environment, allowPropertyAccess: false, default));
        Assert.Equal("object", interpreter.EvaluateDebuggerExpression(
            "typeof host", environment, allowPropertyAccess: false, default));
        Assert.Equal(42d, interpreter.EvaluateDebuggerExpression(
            "host ? 42 : 0", environment, allowPropertyAccess: false, default));
        Assert.Equal(42d, interpreter.EvaluateDebuggerExpression(
            "host && 42", environment, allowPropertyAccess: false, default));
        Assert.Equal(0, host.Calls);
    }

    private sealed class ThrowingCallable : ISharpTSCallable
    {
        public int Arity() => 0;
        public object? Call(SharpTS.Execution.Interpreter interpreter, List<object?> arguments) =>
            throw new InvalidOperationException("Getter must not run during debugger expansion.");
        public override string ToString() =>
            throw new InvalidOperationException("Host conversion must not run during debugger expansion.");
    }

    private sealed class ThrowingInstance(SharpTSClass klass) : SharpTSInstance(klass)
    {
        public override string ToString() =>
            throw new InvalidOperationException("Host conversion must not run during debugger expansion.");
    }

    private sealed class ThrowingBigInt() : SharpTSBigInt(42d)
    {
        public override string ToString() =>
            throw new InvalidOperationException("Host conversion must not run during debugger expansion.");
    }

    private sealed class ThrowingSymbol() : SharpTSSymbol("key")
    {
        public override string ToString() =>
            throw new InvalidOperationException("Host conversion must not run during debugger expansion.");
    }

    private sealed class CountingCallable : ISharpTSCallable
    {
        public int Calls { get; private set; }
        public int Arity() => 0;
        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            Calls++;
            return 42d;
        }
    }

    private sealed class CountingLogicalOperator
    {
        public int Calls { get; private set; }
        public static bool operator !(CountingLogicalOperator value)
        {
            value.Calls++;
            return false;
        }
    }
}
