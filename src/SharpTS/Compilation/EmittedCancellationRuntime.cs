using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required per-assembly cooperative cancellation declarations and emitted bodies.</summary>
public sealed class EmittedCancellationRuntime
{
    internal EmittedCancellationRuntime() { }
    public bool IsComplete { get; private set; }
    private FieldBuilder? _requested;
    private MethodBuilder? _check;
    private MethodBuilder? _buildException;
    private bool _checkBodyEmitted;
    private bool _buildExceptionBodyEmitted;

    /// <summary>Public flag set through reflection by workers, Test262 and other embedders.</summary>
    public FieldBuilder Requested
    {
        get => Require(_requested);
        internal set => SetHandle(ref _requested, value);
    }

    /// <summary>Conditional throwing helper used by invocation guards.</summary>
    public MethodBuilder Check
    {
        get => Require(_check);
        internal set => SetHandle(ref _check, value);
    }

    /// <summary>
    /// Constructs but does not throw the exception. Loop backedges emit a separate throw,
    /// keeping numeric values live in registers across the hot path (issue #856).
    /// </summary>
    public MethodBuilder BuildException
    {
        get => Require(_buildException);
        internal set => SetHandle(ref _buildException, value);
    }

    internal void MarkCheckBodyEmitted()
    {
        EnsureMutable();
        _ = Requested;
        _ = Check;
        if (_checkBodyEmitted)
            throw new InvalidOperationException("Cancellation check body has already been emitted.");
        _checkBodyEmitted = true;
    }

    internal void MarkBuildExceptionBodyEmitted()
    {
        EnsureMutable();
        _ = BuildException;
        if (_buildExceptionBodyEmitted)
            throw new InvalidOperationException("Cancellation exception body has already been emitted.");
        _buildExceptionBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Cancellation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Cancellation metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Cancellation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Requested;
        _ = Check;
        _ = BuildException;
        if (!_checkBodyEmitted || !_buildExceptionBodyEmitted)
            throw new InvalidOperationException("Cancellation method bodies have not been emitted.");
        IsComplete = true;
    }
}
