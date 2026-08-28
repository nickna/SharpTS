using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Hosting;

/// <summary>
/// Diagnostic identifiers for SharpTS hosting APIs.
/// </summary>
public static class SharpTSHostingDiagnostics
{
    /// <summary>
    /// Experimental API identifier for hosted SharpTS runtime features.
    /// </summary>
    public const string ExperimentalId = "SHARPTS_HOSTING001";
}

/// <summary>
/// Application binary interface (ABI) version constants for hosted SharpTS programs.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAbi
{
    /// <summary>
    /// The current ABI version for hosted SharpTS programs.
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Marks an assembly as a hosted SharpTS program and specifies its factory type.
/// </summary>
/// <param name="abiVersion">The ABI version of the hosted program.</param>
/// <param name="factoryType">The factory type that creates the hosted runtime.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedProgramAttribute(
    int abiVersion,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type factoryType) : Attribute
{
    /// <summary>
    /// Gets the ABI version of the hosted program.
    /// </summary>
    public int AbiVersion { get; } = abiVersion;

    /// <summary>
    /// Gets the factory type that creates the hosted runtime.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type FactoryType { get; } = factoryType ?? throw new ArgumentNullException(nameof(factoryType));
}

/// <summary>
/// Provides dispatcher services for scheduling hosted SharpTS work on the owner thread.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostDispatcher
{
    /// <summary>
    /// Determines whether the calling thread is the owner thread.
    /// </summary>
    /// <returns>True if the calling thread has access; otherwise, false.</returns>
    bool CheckAccess();

    /// <summary>
    /// Posts an action to execute on the owner thread.
    /// </summary>
    /// <param name="hostTurn">The action to execute.</param>
    void Post(Action hostTurn);

    /// <summary>
    /// Schedules an action to execute after a specified delay on the owner thread.
    /// </summary>
    /// <param name="delay">The delay before execution.</param>
    /// <param name="hostTurn">The action to execute.</param>
    /// <returns>A handle to the scheduled work that can be canceled.</returns>
    ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn);
}

/// <summary>
/// Represents scheduled work that can be canceled.
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
/// Provides lifetime management for a hosted SharpTS program.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostLifetime
{
    /// <summary>
    /// Requests that the host terminate with the specified exit code.
    /// </summary>
    /// <param name="exitCode">The exit code for the host process.</param>
    void RequestExit(int exitCode);
}

/// <summary>
/// Provides error reporting for hosted SharpTS runtime errors.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedErrorSink
{
    /// <summary>
    /// Reports an error that occurred during hosted runtime execution.
    /// </summary>
    /// <param name="error">The error to report.</param>
    void Report(SharpTSHostedError error);
}

/// <summary>
/// Represents a hosted SharpTS runtime instance.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedRuntime : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current state of the hosted runtime.
    /// </summary>
    SharpTSHostedRuntimeState State { get; }

    /// <summary>
    /// Gets the reason for shutdown, if the runtime is stopping or stopped.
    /// </summary>
    SharpTSHostedShutdownReason? ShutdownReason { get; }

    /// <summary>
    /// Gets the managed thread ID of the owner thread, if captured.
    /// </summary>
    int? OwnerThreadId { get; }

    /// <summary>
    /// Initializes the hosted runtime asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when initialization is finished.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down the hosted runtime asynchronously.
    /// </summary>
    /// <param name="reason">The reason for shutdown.</param>
    /// <param name="exitCode">The exit code for the program.</param>
    /// <returns>A task that completes when shutdown is finished.</returns>
    Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0);

    /// <summary>
    /// Registers a cleanup action to run during shutdown.
    /// </summary>
    /// <param name="cleanup">The cleanup action to register.</param>
    void RegisterCleanup(Action cleanup);

    /// <summary>
    /// Notifies the guest runtime to execute an action as a macrotask.
    /// </summary>
    /// <param name="guestNotification">The action to execute.</param>
    void Notify(Action guestNotification);

    /// <summary>
    /// Invokes a guest callback synchronously on the owner thread.
    /// </summary>
    /// <param name="guestCallback">The callback to invoke.</param>
    void Invoke(Action guestCallback);

    /// <summary>
    /// Invokes a guest callback synchronously on the owner thread and returns its result.
    /// </summary>
    /// <param name="guestCallback">The callback to invoke.</param>
    /// <returns>The result of the callback.</returns>
    object? Invoke(Func<object?> guestCallback);
}

/// <summary>
/// Factory for creating hosted SharpTS runtime instances.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public interface ISharpTSHostedProgramFactory
{
    /// <summary>
    /// Gets the ABI version of the hosted program factory.
    /// </summary>
    int AbiVersion { get; }

    /// <summary>
    /// Creates a new hosted SharpTS runtime instance.
    /// </summary>
    /// <param name="dispatcher">The dispatcher for scheduling work on the owner thread.</param>
    /// <param name="lifetime">The lifetime manager for the host process.</param>
    /// <param name="errorSink">The error sink for reporting runtime errors.</param>
    /// <returns>A new hosted runtime instance.</returns>
    ISharpTSHostedRuntime Create(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink);
}

/// <summary>
/// Exception thrown when a hosted SharpTS ABI contract is violated.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="innerException">Optional inner exception.</param>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedAbiException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// Utility methods for working with hosted SharpTS assemblies.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSHostedAssembly
{
    /// <summary>
    /// Creates a hosted runtime instance from a compiled SharpTS assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the hosted program.</param>
    /// <param name="dispatcher">The dispatcher for scheduling work on the owner thread.</param>
    /// <param name="lifetime">The lifetime manager for the host process.</param>
    /// <param name="errorSink">The error sink for reporting runtime errors.</param>
    /// <returns>A new hosted runtime instance.</returns>
    /// <exception cref="SharpTSHostedAbiException">Thrown if the assembly is not a valid hosted program.</exception>
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
/// Represents the phase of hosted runtime execution during which an error occurred.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedErrorPhase
{
    /// <summary>Runtime creation phase.</summary>
    Creation,
    /// <summary>Runtime initialization phase.</summary>
    Initialization,
    /// <summary>Runtime execution phase.</summary>
    Running,
    /// <summary>Runtime shutdown phase.</summary>
    Shutdown,
    /// <summary>Cleanup phase during shutdown.</summary>
    Cleanup,
}

/// <summary>
/// Represents the reason for hosted runtime shutdown.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedShutdownReason
{
    /// <summary>Shutdown was requested by the host.</summary>
    HostRequested,
    /// <summary>The program completed normally.</summary>
    ProgramCompleted,
    /// <summary>Process exit was requested by the guest.</summary>
    ProcessExit,
    /// <summary>Startup failed with an error.</summary>
    StartupFailure,
    /// <summary>An uncaught error occurred during execution.</summary>
    UncaughtError,
    /// <summary>The runtime was disposed.</summary>
    Disposed,
}

/// <summary>
/// Represents the current state of a hosted SharpTS runtime.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public enum SharpTSHostedRuntimeState
{
    /// <summary>The runtime has been created but not initialized.</summary>
    Created,
    /// <summary>The runtime is initializing.</summary>
    Initializing,
    /// <summary>The runtime is running.</summary>
    Running,
    /// <summary>The runtime is stopping.</summary>
    Stopping,
    /// <summary>The runtime has stopped.</summary>
    Stopped,
    /// <summary>The runtime encountered a fatal error.</summary>
    Faulted,
    /// <summary>The runtime has been disposed.</summary>
    Disposed,
}

/// <summary>
/// Represents an error that occurred during hosted runtime execution.
/// </summary>
/// <param name="Exception">The exception that was thrown.</param>
/// <param name="Phase">The execution phase during which the error occurred.</param>
/// <param name="RuntimeState">The runtime state when the error occurred.</param>
/// <param name="ShutdownReason">Optional shutdown reason if the runtime is shutting down.</param>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed record SharpTSHostedError(
    Exception Exception,
    SharpTSHostedErrorPhase Phase,
    SharpTSHostedRuntimeState RuntimeState,
    SharpTSHostedShutdownReason? ShutdownReason = null);
