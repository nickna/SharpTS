#pragma warning disable SHARPTS_HOSTING001

using SharpTS.Execution;
using SharpTS.Execution.Debugging;
using SharpTS.Hosting;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.DebugAdapter.Adapter;

internal sealed record DebuggeeLaunchOptions(
    string Program,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?> Environment,
    string? Project,
    IReadOnlyList<string> References,
    bool StopOnEntry,
    bool JustMyCode,
    string Diagnostics);

internal sealed record DebuggeeExit(int ExitCode, Exception? Error);

internal sealed class DebuggeeSession : IAsyncDisposable
{
    private readonly DebuggeeLaunchOptions _options;
    private readonly SharpTSProgram _program;
    private readonly Interpreter _interpreter;
    private readonly Action<string, string> _emitOutput;
    private Task<DebuggeeExit>? _execution;
    private Task _termination = Task.CompletedTask;
    private int _started;

    private DebuggeeSession(
        DebuggeeLaunchOptions options,
        SharpTSProgram program,
        Interpreter interpreter,
        InterpreterDebugHost host,
        InterpreterDebugController controller,
        Action<string, string> emitOutput)
    {
        _options = options;
        _program = program;
        _interpreter = interpreter;
        Host = host;
        Controller = controller;
        _emitOutput = emitOutput;
    }

    public InterpreterDebugHost Host { get; }
    public InterpreterDebugController Controller { get; }
    public Task<DebuggeeExit>? Execution => _execution;

    public static async Task<DebuggeeSession> PrepareAsync(
        DebuggeeLaunchOptions options,
        InterpreterDebugHost host,
        Action<string, string> emitOutput,
        CancellationToken cancellationToken)
    {
        SharpTSProgram program = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SharpTSProgramLoader.Load(options.Program, new SharpTSProgramLoadOptions
            {
                TsConfigPath = options.Project,
                DiscoverTsConfig = options.Project is null,
                References = options.References,
            });
        }, cancellationToken).ConfigureAwait(false);

        var stdout = new DapOutputWriter("stdout", emitOutput);
        var stderr = new DapOutputWriter("stderr", emitOutput);
        var interpreter = new Interpreter(stdout, stderr);
        interpreter.SetDecoratorMode(program.DecoratorMode);
        interpreter.EmitProcessLifecycleEvents = true;

        host.ConfigureJustMyCode(options.JustMyCode);
        InterpreterDebugThreadInfo mainThread = host.RegisterMain(interpreter);
        InterpreterDebugController controller = mainThread.Controller;
        controller.RegisterModules(program.RuntimeModules);

        if (options.Diagnostics == "all")
        {
            foreach (SharpTS.Diagnostics.Diagnostic diagnostic in program.Diagnostics)
                emitOutput("stderr", $"{diagnostic}{Environment.NewLine}");
        }

        var variableResolver = new VariableResolver(interpreter);
        foreach (ParsedModule module in program.RuntimeModules)
        {
            if (!module.IsBuiltIn)
                variableResolver.Resolve(module.Statements);
        }

        return new DebuggeeSession(options, program, interpreter, host, controller, emitOutput);
    }

    public Task<DebuggeeExit> Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The debuggee has already started.");

        Host.StartMain(_options.StopOnEntry);
        _execution = Task.Factory.StartNew(
            Run,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return _execution;
    }

    public void Pause() => Host.RequestPause(1);
    public void Continue(DebugStepKind step = DebugStepKind.None) => Host.Continue(1, step);

    public void Terminate()
    {
        _termination = Host.Terminate();
        _interpreter.Dispose();
    }

    private DebuggeeExit Run()
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        var previousEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Action<int> previousExit = ProcessControl.ExitHandler;
        Action<string> previousAbort = ProcessControl.AbortHandler;
        int exitCode = 0;
        Exception? failure = null;

        try
        {
            Directory.SetCurrentDirectory(_options.WorkingDirectory);
            foreach ((string name, string? value) in _options.Environment)
            {
                previousEnvironment[name] = System.Environment.GetEnvironmentVariable(name);
                System.Environment.SetEnvironmentVariable(name, value);
            }

            ProcessControl.ExitHandler = code => throw new DebuggeeExitException(code);
            ProcessControl.AbortHandler = message => throw new DebuggeeAbortException(message);
            ProcessBuiltIns.SetScriptArguments(_program.EntryPath, _options.Arguments.ToArray());

            _interpreter.EntryModulePath = _program.EntryPath;
            _interpreter.InterpretModules(
                _program.RuntimeModules.ToList(), _program.Resolver, _program.TypeMap);
            if (_interpreter.HadUnhandledRejection)
                exitCode = 1;
        }
        catch (DebuggeeExitException exception)
        {
            exitCode = exception.ExitCode;
        }
        catch (DebuggerTerminationException)
        {
            exitCode = 0;
        }
        catch (OperationCanceledException) when (Controller.State == DebugExecutionState.Terminating)
        {
            exitCode = 0;
        }
        catch (Exception exception)
        {
            exitCode = 1;
            failure = exception;
            _emitOutput("stderr", $"{exception.Message}{Environment.NewLine}");
        }
        finally
        {
            Host.MarkMainExited();
            _termination = Host.Terminate();
            try { _termination.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            _interpreter.Dispose();
            ProcessControl.ExitHandler = previousExit;
            ProcessControl.AbortHandler = previousAbort;
            foreach ((string name, string? value) in previousEnvironment)
                System.Environment.SetEnvironmentVariable(name, value);
            Directory.SetCurrentDirectory(previousDirectory);
        }

        return new DebuggeeExit(exitCode, failure);
    }

    public async ValueTask DisposeAsync()
    {
        if (Controller.State != DebugExecutionState.Exited)
            Terminate();
        if (_execution is not null || !_termination.IsCompleted)
        {
            Task owned = _execution is null
                ? _termination
                : Task.WhenAll(_execution, _termination);
            try { await owned.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        Controller.Dispose();
    }

    private sealed class DebuggeeExitException(int exitCode) : Exception
    {
        public int ExitCode { get; } = exitCode;
    }

    private sealed class DebuggeeAbortException(string message) : Exception(message);
}
