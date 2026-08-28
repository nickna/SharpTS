using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Hosting;

/// <summary>
/// Diagnostic codes for SharpTS hosting APIs.
/// </summary>
public static class SharpTSHostingDiagnostics
{
    /// <summary>
    /// Experimental API diagnostic ID for SharpTS hosting features.
    /// </summary>
    public const string ExperimentalId = "SHARPTS_HOSTING001";
}

/// <summary>
/// Defines the Application Binary Interface (ABI) version for SharpTS hosted programs.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAbi
{
    /// <summary>
    /// The current ABI version for SharpTS hosted programs.
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Assembly-level attribute marking a SharpTS hosted program and specifying its ABI version and factory type.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedProgramAttribute(
    int abiVersion,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type factoryType) : Attribute
{
    /// <summary>
    /// The ABI version this hosted program conforms to.
    /// </summary>
    public int AbiVersion { get; } = abiVersion;

    /// <summary>
    /// The factory type that creates instances of the hosted runtime.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type FactoryType { get; } = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
}

/// <summary>
/// Provides thread synchronization and scheduling services for a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostDispatcher
{
    /// <summary>
    /// Checks whether the current thread has access to the dispatcher.
    /// </summary>
    bool CheckAccess();

    /// <summary>
    /// Posts an action to be executed on the dispatcher thread.
    /// </summary>
    void Post(Action hostTurn);

    /// <summary>
    /// Schedules an action to be executed after a specified delay.
    /// </summary>
    ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn);
}

/// <summary>
/// Represents a scheduled work item that can be canceled.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSScheduledWork : IDisposable
{
    /// <summary>
    /// Cancels the scheduled work.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Provides lifetime management services for a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostLifetime
{
    /// <summary>
    /// Requests the host to exit with the specified exit code.
    /// </summary>
    void RequestExit(int exitCode);
}

/// <summary>
/// Receives and handles errors from a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedErrorSink
{
    /// <summary>
    /// Reports an error that occurred in the hosted runtime.
    /// </summary>
    void Report(SharpTSHostedError error);
}

/// <summary>
/// Represents a hosted SharpTS runtime instance with lifecycle management and guest code invocation.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedRuntime : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current state of the hosted runtime.
    /// </summary>
    SharpTSHostedRuntimeState State { get; }

    /// <summary>
    /// Gets the reason the runtime shut down, if it has shut down.
    /// </summary>
    SharpTSHostedShutdownReason? ShutdownReason { get; }

    /// <summary>
    /// Gets the ID of the thread that owns this runtime, if applicable.
    /// </summary>
    int? OwnerThreadId { get; }

    /// <summary>
    /// Initializes the hosted runtime asynchronously.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down the hosted runtime asynchronously with the specified reason and exit code.
    /// </summary>
    Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0);

    /// <summary>
    /// Registers a cleanup action to be executed during shutdown.
    /// </summary>
    void RegisterCleanup(Action cleanup);

    /// <summary>
    /// Posts a notification to the guest runtime without waiting for completion.
    /// </summary>
    void Notify(Action guestNotification);

    /// <summary>
    /// Invokes a guest callback synchronously.
    /// </summary>
    void Invoke(Action guestCallback);

    /// <summary>
    /// Invokes a guest callback synchronously and returns its result.
    /// </summary>
    object? Invoke(Func<object?> guestCallback);
}

/// <summary>
/// Factory for creating hosted SharpTS runtime instances.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedProgramFactory
{
    /// <summary>
    /// Gets the ABI version this factory supports.
    /// </summary>
    int AbiVersion { get; }

    /// <summary>
    /// Creates a hosted runtime instance with the specified host services.
    /// </summary>
    ISharpTSHostedRuntime Create(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink);
}

/// <summary>
/// Exception thrown when a hosted program violates the SharpTS hosting ABI contract.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedAbiException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// Provides utility methods for loading and creating hosted SharpTS runtimes from assemblies.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAssembly
{
    /// <summary>
    /// Creates a hosted runtime instance from the specified assembly with the provided host services.
    /// </summary>
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

/// <summary>
/// Indicates the phase of execution during which a hosted runtime error occurred.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedErrorPhase
{
    /// <summary>Error occurred during runtime creation.</summary>
    Creation,
    /// <summary>Error occurred during runtime initialization.</summary>
    Initialization,
    /// <summary>Error occurred while the runtime was running.</summary>
    Running,
    /// <summary>Error occurred during runtime shutdown.</summary>
    Shutdown,
    /// <summary>Error occurred during cleanup operations.</summary>
    Cleanup,
}

/// <summary>
/// Indicates the reason a hosted runtime shut down.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedShutdownReason
{
    /// <summary>Host requested shutdown.</summary>
    HostRequested,
    /// <summary>Guest program completed normally.</summary>
    ProgramCompleted,
    /// <summary>Process exit was initiated.</summary>
    ProcessExit,
    /// <summary>Startup failed.</summary>
    StartupFailure,
    /// <summary>An uncaught error occurred.</summary>
    UncaughtError,
    /// <summary>Runtime was disposed.</summary>
    Disposed,
}

/// <summary>
/// Represents the current state of a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedRuntimeState
{
    /// <summary>Runtime has been created but not initialized.</summary>
    Created,
    /// <summary>Runtime is currently initializing.</summary>
    Initializing,
    /// <summary>Runtime is running.</summary>
    Running,
    /// <summary>Runtime is stopping.</summary>
    Stopping,
    /// <summary>Runtime has stopped.</summary>
    Stopped,
    /// <summary>Runtime encountered a fault.</summary>
    Faulted,
    /// <summary>Runtime has been disposed.</summary>
    Disposed,
}

/// <summary>
/// Represents an error that occurred in a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed record SharpTSHostedError(
    Exception Exception,
    SharpTSHostedErrorPhase Phase,
    SharpTSHostedRuntimeState RuntimeState,
    SharpTSHostedShutdownReason? ShutdownReason = null);
