using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional source-execution declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedSourceExecutionRuntime
{
    internal EmittedSourceExecutionRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _runJson;
    public MethodBuilder RunJson
    {
        get => Require(_runJson);
        internal set => Set(ref _runJson, value);
    }

    private MethodBuilder? _configureUntrustedProcess;
    public MethodBuilder ConfigureUntrustedProcess
    {
        get => Require(_configureUntrustedProcess);
        internal set => Set(ref _configureUntrustedProcess, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Source-execution metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Source-execution metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = RunJson;
        _ = ConfigureUntrustedProcess;
        IsComplete = true;
    }
}
