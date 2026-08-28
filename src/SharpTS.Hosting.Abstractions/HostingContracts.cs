using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Hosting;

/// <summary>
/// Diagnostic identifiers for SharpTS hosting APIs.
/// </summary>
public static class SharpTSHostingDiagnostics
{
    /// <summary>
    /// Diagnostic ID for experimental hosting APIs.
    /// </summary>
    public const string ExperimentalId = "SHARPTS_HOSTING001";
}

/// <summary>
/// Constants for the SharpTS hosted application binary interface (ABI).
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAbi
{
    /// <summary>
    /// The current ABI version. Hosted applications and hosts must agree on this version.
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Marks an assembly as a SharpTS hosted program, specifying the ABI version and factory type.
/// </summary>
/// <param name="abiVersion">The ABI version this program was built for.</param>
/// <param name="factoryType">The type implementing <see cref="ISharpTSHostedProgramFactory"/> that creates the runtime.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedProgramAttribute(
    int abiVersion,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type factoryType) : Attribute
{
    /// <summary>
    /// Gets the ABI version this program declares.
    /// </summary>
    public int AbiVersion { get; } = abiVersion;

    /// <summary>
    /// Gets the factory type that creates the hosted runtime.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type FactoryType { get; } = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
}

/// <summary>
/// Provides thread-affinity and scheduling services for a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostDispatcher
{
    /// <summary>
    /// Determines whether the calling thread is the runtime's owner thread.
    /// </summary>
    /// <returns>True if the caller is on the runtime's thread; otherwise false.</returns>
    bool CheckAccess();

    /// <summary>
    /// Posts an action to run on the runtime's thread.
    /// </summary>
    /// <param name="hostTurn">The action to execute.</param>
    void Post(Action hostTurn);

    /// <summary>
    /// Schedules an action to run on the runtime's thread after a delay.
    /// </summary>
    /// <param name="delay">The delay before executing the action.</param>
    /// <param name="hostTurn">The action to execute.</param>
    /// <returns>A handle that can cancel the scheduled work.</returns>
    ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn);
}

/// <summary>
/// Represents a scheduled work item that can be canceled or disposed.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSScheduledWork : IDisposable
{
    /// <summary>
    /// Cancels the scheduled work if it has not yet executed.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Provides host lifetime management for a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostLifetime
{
    /// <summary>
    /// Requests the host to exit with the specified exit code.
    /// </summary>
    /// <param name="exitCode">The exit code to return.</param>
    void RequestExit(int exitCode);
}

/// <summary>
/// Receives error reports from a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedErrorSink
{
    /// <summary>
    /// Reports an error that occurred during runtime operation.
    /// </summary>
    /// <param name="error">The error details.</param>
    void Report(SharpTSHostedError error);
}

/// <summary>
/// Represents a hosted SharpTS runtime instance with lifecycle management and guest-host communication.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedRuntime : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current state of the runtime.
    /// </summary>
    SharpTSHostedRuntimeState State { get; }

    /// <summary>
    /// Gets the reason for shutdown, if the runtime has shut down.
    /// </summary>
    SharpTSHostedShutdownReason? ShutdownReason { get; }

    /// <summary>
    /// Gets the managed thread ID that owns this runtime, or null if not yet initialized.
    /// </summary>
    int? OwnerThreadId { get; }

    /// <summary>
    /// Initializes the runtime asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort initialization.</param>
    /// <returns>A task that completes when initialization finishes.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down the runtime asynchronously.
    /// </summary>
    /// <param name="reason">The reason for shutdown.</param>
    /// <param name="exitCode">The exit code to report.</param>
    /// <returns>A task that completes when shutdown finishes.</returns>
    Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0);

    /// <summary>
    /// Registers an action to be run during cleanup.
    /// </summary>
    /// <param name="cleanup">The cleanup action.</param>
    void RegisterCleanup(Action cleanup);

    /// <summary>
    /// Posts a notification to the guest without waiting for a result (fire-and-forget).
    /// </summary>
    /// <param name="guestNotification">The action to execute on the guest.</param>
    void Notify(Action guestNotification);

    /// <summary>
    /// Invokes an action on the guest and waits for it to complete.
    /// </summary>
    /// <param name="guestCallback">The action to execute on the guest.</param>
    void Invoke(Action guestCallback);

    /// <summary>
    /// Invokes a function on the guest and returns its result.
    /// </summary>
    /// <param name="guestCallback">The function to execute on the guest.</param>
    /// <returns>The result of the guest function.</returns>
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
    /// Creates a new hosted runtime instance.
    /// </summary>
    /// <param name="dispatcher">The dispatcher for thread-affinity and scheduling.</param>
    /// <param name="lifetime">The host lifetime manager.</param>
    /// <param name="errorSink">The error reporting sink.</param>
    /// <returns>A new hosted runtime instance.</returns>
    ISharpTSHostedRuntime Create(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink);
}

/// <summary>
/// Exception thrown when there is an ABI version mismatch or other hosting contract violation.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="innerException">The inner exception, if any.</param>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedAbiException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// Utilities for loading and creating hosted runtimes from compiled assemblies.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAssembly
{
    /// <summary>
    /// Creates a hosted runtime from an assembly marked with <see cref="SharpTSHostedProgramAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly containing the hosted program.</param>
    /// <param name="dispatcher">The dispatcher for thread-affinity and scheduling.</param>
    /// <param name="lifetime">The host lifetime manager.</param>
    /// <param name="errorSink">The error reporting sink.</param>
    /// <returns>A new hosted runtime instance.</returns>
    /// <exception cref="SharpTSHostedAbiException">Thrown if the assembly metadata is invalid or ABI version mismatches.</exception>
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
/// Indicates the lifecycle phase during which an error occurred.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedErrorPhase
{
    /// <summary>Error occurred during runtime creation.</summary>
    Creation,
    /// <summary>Error occurred during initialization.</summary>
    Initialization,
    /// <summary>Error occurred while the runtime was running.</summary>
    Running,
    /// <summary>Error occurred during shutdown.</summary>
    Shutdown,
    /// <summary>Error occurred during cleanup.</summary>
    Cleanup,
}

/// <summary>
/// Indicates why a hosted runtime shut down.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedShutdownReason
{
    /// <summary>The host requested shutdown.</summary>
    HostRequested,
    /// <summary>The program completed normally.</summary>
    ProgramCompleted,
    /// <summary>The process is exiting.</summary>
    ProcessExit,
    /// <summary>Startup failed.</summary>
    StartupFailure,
    /// <summary>An uncaught error occurred.</summary>
    UncaughtError,
    /// <summary>The runtime was disposed.</summary>
    Disposed,
}

/// <summary>
/// Indicates the current state of a hosted runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedRuntimeState
{
    /// <summary>Runtime has been created but not yet initialized.</summary>
    Created,
    /// <summary>Runtime is currently initializing.</summary>
    Initializing,
    /// <summary>Runtime is running normally.</summary>
    Running,
    /// <summary>Runtime is in the process of stopping.</summary>
    Stopping,
    /// <summary>Runtime has stopped.</summary>
    Stopped,
    /// <summary>Runtime encountered a fault.</summary>
    Faulted,
    /// <summary>Runtime has been disposed.</summary>
    Disposed,
}

/// <summary>
/// Contains details about an error that occurred in a hosted runtime.
/// </summary>
/// <param name="Exception">The exception that was thrown.</param>
/// <param name="Phase">The lifecycle phase when the error occurred.</param>
/// <param name="RuntimeState">The runtime state when the error occurred.</param>
/// <param name="ShutdownReason">The shutdown reason, if applicable.</param>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed record SharpTSHostedError(
    Exception Exception,
    SharpTSHostedErrorPhase Phase,
    SharpTSHostedRuntimeState RuntimeState,
    SharpTSHostedShutdownReason? ShutdownReason = null);
