using SharpTS.Execution;
using SharpTS.Execution.Debugging;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.DebugAdapter;

public sealed class InterpreterDebugControllerTests
{
    [Fact]
    public async Task StopOnEntryExposesSourceAndLexicalEnvironment()
    {
        const string source = """
            let outer = 40;
            function add(value: number): number {
                let local = 2;
                return value + local;
            }
            console.log(add(outer));
            """;
        DebugFixture fixture = CreateFixture(source);
        var stopped = new TaskCompletionSource<DebugStopSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Controller.Stopped += snapshot => stopped.TrySetResult(snapshot);
        fixture.Controller.Start(stopOnEntry: true);

        Task execution = Task.Run(() => fixture.Interpreter.InterpretModules(
            fixture.Modules, fixture.Resolver, fixture.TypeMap));
        DebugStopSnapshot snapshot = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DebugStopReason.Entry, snapshot.Reason);
        Assert.Equal(1, snapshot.Frames[0].Location.Line);
        Assert.Equal(Path.GetFullPath(fixture.EntryPath), snapshot.Frames[0].Location.Document.Path);

        fixture.Controller.Continue();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DebugExecutionState.Exited, MarkExited(fixture.Controller));
    }

    [Fact]
    public async Task BreakpointOnBlankLineMovesAndStopsAtExecutableStatement()
    {
        const string source = """
            let first = 1;

            // move to the next executable line
            let second = first + 1;
            console.log(second);
            """;
        DebugFixture fixture = CreateFixture(source);
        IReadOnlyList<DebugBreakpointBinding> bindings = fixture.Controller.SetBreakpoints(
            fixture.EntryPath, [(2, 1)]);
        DebugBreakpointBinding binding = Assert.Single(bindings);
        Assert.True(binding.Verified);
        Assert.Equal(4, binding.Line);
        Assert.Contains("moved", binding.Message, StringComparison.OrdinalIgnoreCase);

        var stopped = new TaskCompletionSource<DebugStopSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Controller.Stopped += snapshot => stopped.TrySetResult(snapshot);
        fixture.Controller.Start(stopOnEntry: false);
        Task execution = Task.Run(() => fixture.Interpreter.InterpretModules(
            fixture.Modules, fixture.Resolver, fixture.TypeMap));

        DebugStopSnapshot snapshot = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DebugStopReason.Breakpoint, snapshot.Reason);
        Assert.Equal(4, snapshot.Frames[0].Location.Line);
        Assert.Equal(1d, snapshot.Frames[0].Environment.Get("first").ToObject());

        fixture.Controller.Continue();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        MarkExited(fixture.Controller);
    }

    [Fact]
    public async Task TerminateReleasesStoppedInterpreter()
    {
        DebugFixture fixture = CreateFixture("let value = 1;\nconsole.log(value);");
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Controller.Stopped += _ => stopped.TrySetResult();
        fixture.Controller.Start(stopOnEntry: true);
        Task execution = Task.Run(() => fixture.Interpreter.InterpretModules(
            fixture.Modules, fixture.Resolver, fixture.TypeMap));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        fixture.Controller.Terminate();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static DebugExecutionState MarkExited(InterpreterDebugController controller)
    {
        controller.MarkExited();
        return controller.State;
    }

    private static DebugFixture CreateFixture(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-dap-tests");
        string entry = Path.Combine(root, $"{Guid.NewGuid():N}.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entry] = source,
        };
        var resolver = new ModuleResolver(entry, files);
        ParsedModule module = resolver.LoadProgram(entry, DecoratorMode.Stage3);
        List<ParsedModule> modules = resolver.GetRuntimeModulesInOrder(module);
        TypeMap typeMap = new TypeChecker().CheckModules(
            resolver.GetModulesInOrder(module), resolver);
        var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var controller = new InterpreterDebugController();
        controller.RegisterModules(modules);
        interpreter.DebugController = controller;
        var variableResolver = new VariableResolver(interpreter);
        foreach (ParsedModule runtimeModule in modules)
        {
            if (!runtimeModule.IsBuiltIn)
                variableResolver.Resolve(runtimeModule.Statements);
        }
        return new DebugFixture(entry, resolver, modules, typeMap, interpreter, controller);
    }

    private sealed record DebugFixture(
        string EntryPath,
        ModuleResolver Resolver,
        List<ParsedModule> Modules,
        TypeMap TypeMap,
        Interpreter Interpreter,
        InterpreterDebugController Controller);
}
