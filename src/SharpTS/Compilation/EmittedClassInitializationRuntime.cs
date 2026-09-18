using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required class-definition initialization and exception-unwrapping metadata.</summary>
public sealed class EmittedClassInitializationRuntime
{
    internal EmittedClassInitializationRuntime() { }
    public bool IsComplete { get; private set; }
    private MethodBuilder? _runDefinition;
    private bool _bodyEmitted;

    /// <summary>Forces a CLR class initializer and rethrows its original guest exception.</summary>
    public MethodBuilder RunDefinition
    {
        get => _runDefinition ?? throw new InvalidOperationException("Class-initialization helper has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_runDefinition is not null)
                throw new InvalidOperationException("Class-initialization helper has already been declared.");
            _runDefinition = value;
        }
    }

    internal void MarkBodyEmitted()
    {
        EnsureMutable();
        _ = RunDefinition;
        if (_bodyEmitted)
            throw new InvalidOperationException("Class-initialization helper body has already been emitted.");
        _bodyEmitted = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Class-initialization metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = RunDefinition;
        if (!_bodyEmitted)
            throw new InvalidOperationException("Class-initialization helper body has not been emitted.");
        IsComplete = true;
    }
}
