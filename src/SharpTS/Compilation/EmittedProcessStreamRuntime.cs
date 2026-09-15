using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional process stream singleton declarations for one compilation.
/// Declarations support forward references; completion validates every handle and freezes writes.
/// </summary>
public sealed class EmittedProcessStreamRuntime
{
    internal EmittedProcessStreamRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _getStdout;
    public MethodBuilder GetStdout
    {
        get => Require(_getStdout);
        internal set => Set(ref _getStdout, value);
    }

    private MethodBuilder? _getStderr;
    public MethodBuilder GetStderr
    {
        get => Require(_getStderr);
        internal set => Set(ref _getStderr, value);
    }

    private MethodBuilder? _getStdin;
    public MethodBuilder GetStdin
    {
        get => Require(_getStdin);
        internal set => Set(ref _getStdin, value);
    }

    private FieldBuilder? _stdoutInstance;
    public FieldBuilder StdoutInstance
    {
        get => Require(_stdoutInstance);
        internal set => Set(ref _stdoutInstance, value);
    }

    private FieldBuilder? _stderrInstance;
    public FieldBuilder StderrInstance
    {
        get => Require(_stderrInstance);
        internal set => Set(ref _stderrInstance, value);
    }

    private FieldBuilder? _stdinInstance;
    public FieldBuilder StdinInstance
    {
        get => Require(_stdinInstance);
        internal set => Set(ref _stdinInstance, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Process stream singleton metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Process stream singleton metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = GetStdout;
        _ = GetStderr;
        _ = GetStdin;
        _ = StdoutInstance;
        _ = StderrInstance;
        _ = StdinInstance;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        IsComplete = true;
    }
}
