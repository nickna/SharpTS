using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Global object declarations, singleton initialization and helper bodies for one assembly.</summary>
public sealed class EmittedGlobalObjectRuntime
{
    internal EmittedGlobalObjectRuntime() { }
    public bool IsDeclared { get; private set; }
    public bool IsComplete { get; private set; }
    private bool _initializerEmitted;
    internal bool HasSingletonField => _singletonField is not null;

    private FieldBuilder? _singletonField;
    public FieldBuilder SingletonField
    {
        get => Require(_singletonField);
        internal set => SetDeclaration(ref _singletonField, value);
    }

    private MethodBuilder? _getProperty;
    public MethodBuilder GetProperty
    {
        get => Require(_getProperty);
        internal set => SetDeclaration(ref _getProperty, value);
    }

    private MethodBuilder? _setProperty;
    public MethodBuilder SetProperty
    {
        get => Require(_setProperty);
        internal set => SetDeclaration(ref _setProperty, value);
    }

    private FieldBuilder? _properties;
    public FieldBuilder Properties
    {
        get => Require(_properties);
        internal set => SetDeclaration(ref _properties, value);
    }

    private MethodBuilder? _indirectEval;
    public MethodBuilder IndirectEval
    {
        get => Require(_indirectEval);
        internal set => SetLateHandle(ref _indirectEval, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Global object metadata '" + name + "' has not been declared.");

    private void SetDeclaration<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        if (IsDeclared)
            throw new InvalidOperationException("Global object declarations are already complete.");
        SetHandle(ref field, value, name);
    }

    private void SetLateHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        RequireDeclarations();
        SetHandle(ref field, value, name);
    }

    private static void SetHandle<T>(ref T? field, T value, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Global object metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Global object metadata emission is already complete.");
    }

    private void RequireDeclarations()
    {
        if (!IsDeclared)
            throw new InvalidOperationException("Global object declarations are not complete.");
    }

    internal void CompleteDeclarations()
    {
        EnsureMutable();
        if (IsDeclared)
            throw new InvalidOperationException("Global object declarations are already complete.");
        _ = SingletonField;
        _ = GetProperty;
        _ = SetProperty;
        _ = Properties;
        IsDeclared = true;
    }

    internal void MarkInitializerEmitted()
    {
        EnsureMutable();
        RequireDeclarations();
        if (_initializerEmitted)
            throw new InvalidOperationException("Global object initialization has already been emitted.");
        _initializerEmitted = true;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        RequireDeclarations();
        _ = IndirectEval;
        if (!_initializerEmitted)
            throw new InvalidOperationException("Global object initialization has not been emitted.");
        IsComplete = true;
    }
}
