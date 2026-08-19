using SharpTS.DebugAdapter;
using SharpTS.DebugAdapter.Adapter;
using SharpTS.DebugAdapter.Protocol;
using SharpTS.Execution;
using SharpTS.Runtime;
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
            && message.GetProperty("requestSeq").GetInt32() == 2);
        Assert.False(launch.GetProperty("success").GetBoolean());
        Assert.Contains("cancelled", launch.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(messages, message =>
            message.GetProperty("type").GetString() == "response"
            && message.GetProperty("requestSeq").GetInt32() == 3
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

    private sealed class ThrowingCallable : ISharpTSCallable
    {
        public int Arity() => 0;
        public object? Call(SharpTS.Execution.Interpreter interpreter, List<object?> arguments) =>
            throw new InvalidOperationException("Getter must not run during debugger expansion.");
    }
}
