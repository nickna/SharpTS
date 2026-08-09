#pragma warning disable SHARPTS_HOSTING001

using System.Runtime.Loader;
using SharpTS.Gui;
using SharpTS.Hosting;

namespace SharpTS.Gui.Host;

internal interface IGuestRuntime : IDisposable
{
    SharpTSHostedShutdownReason? ShutdownReason { get; }
    Task InitializeAsync();
    void Notify(Action callback);
    void QueueMicrotask(Action callback);
    Task ShutdownAsync(SharpTSHostedShutdownReason reason, int exitCode);
}

internal sealed class InterpretedGuestRuntime : IGuestRuntime
{
    private readonly string _entryPath;
    private readonly string _tsconfigPath;
    private readonly ISharpTSHostDispatcher _dispatcher;
    private readonly ISharpTSHostLifetime _lifetime;
    private readonly ISharpTSHostedErrorSink _errorSink;
    private HostedInterpreterRuntime? _runtime;

    public SharpTSHostedShutdownReason? ShutdownReason => _runtime?.ShutdownReason;

    public InterpretedGuestRuntime(
        string entryPath,
        string tsconfigPath,
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink)
    {
        _entryPath = entryPath;
        _tsconfigPath = tsconfigPath;
        _dispatcher = dispatcher;
        _lifetime = lifetime;
        _errorSink = errorSink;
    }

    public Task InitializeAsync()
    {
        string bridgePath = typeof(DesktopBridge).Assembly.Location;
        SharpTSProgram program = SharpTSProgramLoader.Load(
            _entryPath,
            new SharpTSProgramLoadOptions
        {
            TsConfigPath = _tsconfigPath,
            References = [bridgePath],
        });
        _runtime = new HostedInterpreterRuntime(
            _dispatcher, _lifetime, _errorSink, program, Console.Out, Console.Error);
        _runtime.RegisterCleanup(DesktopBridge.DisposeAllRoots);
        return _runtime.InitializeAsync();
    }

    public void Notify(Action callback) =>
        (_runtime ?? throw new InvalidOperationException("Guest is not initialized.")).Notify(callback);

    public void QueueMicrotask(Action callback) =>
        (_runtime ?? throw new InvalidOperationException("Guest is not initialized.")).EnqueueMicrotask(callback);

    public Task ShutdownAsync(SharpTSHostedShutdownReason reason, int exitCode) =>
        _runtime?.ShutdownAsync(reason, exitCode) ?? Task.CompletedTask;

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}

internal sealed class CompiledGuestRuntime : IGuestRuntime
{
    private readonly string? _assemblyPath;
    private readonly byte[]? _assemblyBytes;
    private readonly ISharpTSHostDispatcher _dispatcher;
    private readonly ISharpTSHostLifetime _lifetime;
    private readonly ISharpTSHostedErrorSink _errorSink;
    private ISharpTSHostedRuntime? _runtime;

    public SharpTSHostedShutdownReason? ShutdownReason => _runtime?.ShutdownReason;

    public CompiledGuestRuntime(
        string assemblyPath,
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink)
    {
        _assemblyPath = assemblyPath;
        _dispatcher = dispatcher;
        _lifetime = lifetime;
        _errorSink = errorSink;
    }

    public CompiledGuestRuntime(
        byte[] assemblyBytes,
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink)
    {
        _assemblyBytes = assemblyBytes ?? throw new ArgumentNullException(nameof(assemblyBytes));
        _dispatcher = dispatcher;
        _lifetime = lifetime;
        _errorSink = errorSink;
    }

    public Task InitializeAsync()
    {
        var assembly = _assemblyBytes is null
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(_assemblyPath!))
            : AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(_assemblyBytes, writable: false));
        _runtime = SharpTSHostedAssembly.CreateRuntime(
            assembly, _dispatcher, _lifetime, _errorSink);
        _runtime.RegisterCleanup(DesktopBridge.DisposeAllRoots);
        return _runtime.InitializeAsync();
    }

    public void Notify(Action callback) =>
        (_runtime ?? throw new InvalidOperationException("Guest is not initialized.")).Notify(callback);

    public void QueueMicrotask(Action callback)
    {
        if (_runtime is not SharpTSHostedRuntimeBase runtime)
            throw new InvalidOperationException("Compiled guest runtime does not expose the hosted scheduler.");
        runtime.EnqueueMicrotask(callback);
    }

    public Task ShutdownAsync(SharpTSHostedShutdownReason reason, int exitCode) =>
        _runtime?.ShutdownAsync(reason, exitCode) ?? Task.CompletedTask;

    public void Dispose()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
