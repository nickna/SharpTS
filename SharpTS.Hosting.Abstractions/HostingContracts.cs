using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Hosting;

public static class SharpTSHostingDiagnostics
{
    public const string ExperimentalId = "SHARPTS_HOSTING001";
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAbi
{
    public const int CurrentVersion = 1;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedProgramAttribute(int abiVersion, Type factoryType) : Attribute
{
    public int AbiVersion { get; } = abiVersion;
    public Type FactoryType { get; } = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostDispatcher
{
    bool CheckAccess();
    void Post(Action hostTurn);
    ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn);
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSScheduledWork : IDisposable
{
    void Cancel();
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostLifetime
{
    void RequestExit(int exitCode);
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedErrorSink
{
    void Report(SharpTSHostedError error);
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedRuntime : IDisposable, IAsyncDisposable
{
    SharpTSHostedRuntimeState State { get; }
    SharpTSHostedShutdownReason? ShutdownReason { get; }
    int? OwnerThreadId { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0);
    void RegisterCleanup(Action cleanup);
    void Notify(Action guestNotification);
    void Invoke(Action guestCallback);
    object? Invoke(Func<object?> guestCallback);
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedProgramFactory
{
    int AbiVersion { get; }
    ISharpTSHostedRuntime Create(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink);
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedAbiException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAssembly
{
    public static ISharpTSHostedRuntime CreateRuntime(
        Assembly assembly,
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(errorSink);

        SharpTSHostedProgramAttribute[] markers;
        try
        {
            markers = assembly.GetCustomAttributes<SharpTSHostedProgramAttribute>().ToArray();
        }
        catch (Exception exception)
        {
            throw new SharpTSHostedAbiException(
                $"Assembly '{assembly.GetName().Name}' has unreadable SharpTS hosted metadata.", exception);
        }

        if (markers.Length != 1)
        {
            throw new SharpTSHostedAbiException(
                $"Assembly '{assembly.GetName().Name}' must declare exactly one " +
                $"{nameof(SharpTSHostedProgramAttribute)}; found {markers.Length}.");
        }

        SharpTSHostedProgramAttribute marker = markers[0];
        if (marker.AbiVersion != SharpTSHostedAbi.CurrentVersion)
        {
            throw new SharpTSHostedAbiException(
                $"Assembly '{assembly.GetName().Name}' uses SharpTS hosted ABI {marker.AbiVersion}; " +
                $"this host requires ABI {SharpTSHostedAbi.CurrentVersion}.");
        }

        Type factoryType = marker.FactoryType;
        if (factoryType.Assembly != assembly ||
            !factoryType.IsClass || factoryType.IsAbstract || !factoryType.IsPublic ||
            !typeof(ISharpTSHostedProgramFactory).IsAssignableFrom(factoryType) ||
            factoryType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new SharpTSHostedAbiException(
                $"Hosted factory '{factoryType.FullName}' must belong to the marked assembly and be a public, concrete type with a " +
                $"public parameterless constructor implementing {nameof(ISharpTSHostedProgramFactory)}.");
        }

        ISharpTSHostedProgramFactory factory;
        try
        {
            factory = (ISharpTSHostedProgramFactory)Activator.CreateInstance(factoryType)!;
        }
        catch (Exception exception)
        {
            throw new SharpTSHostedAbiException(
                $"Hosted factory '{factoryType.FullName}' could not be created.", exception);
        }

        if (factory.AbiVersion != marker.AbiVersion)
        {
            throw new SharpTSHostedAbiException(
                $"Hosted factory '{factoryType.FullName}' reports ABI {factory.AbiVersion}, " +
                $"but its assembly marker reports ABI {marker.AbiVersion}.");
        }

        try
        {
            return factory.Create(dispatcher, lifetime, errorSink)
                ?? throw new SharpTSHostedAbiException(
                    $"Hosted factory '{factoryType.FullName}' returned no runtime.");
        }
        catch (SharpTSHostedAbiException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SharpTSHostedAbiException(
                $"Hosted factory '{factoryType.FullName}' failed while creating its runtime.", exception);
        }
    }
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedErrorPhase
{
    Creation,
    Initialization,
    Running,
    Shutdown,
    Cleanup,
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedShutdownReason
{
    HostRequested,
    ProgramCompleted,
    ProcessExit,
    StartupFailure,
    UncaughtError,
    Disposed,
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedRuntimeState
{
    Created,
    Initializing,
    Running,
    Stopping,
    Stopped,
    Faulted,
    Disposed,
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed record SharpTSHostedError(
    Exception Exception,
    SharpTSHostedErrorPhase Phase,
    SharpTSHostedRuntimeState RuntimeState,
    SharpTSHostedShutdownReason? ShutdownReason = null);
