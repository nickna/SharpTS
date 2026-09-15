using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional readline declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedReadlineRuntime
{
    internal EmittedReadlineRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _interfaceType;
    public TypeBuilder InterfaceType
    {
        get => Require(_interfaceType);
        internal set => Set(ref _interfaceType, value);
    }

    private ConstructorBuilder? _interfaceCtor;
    public ConstructorBuilder InterfaceCtor
    {
        get => Require(_interfaceCtor);
        internal set => Set(ref _interfaceCtor, value);
    }

    private FieldBuilder? _closedField;
    public FieldBuilder ClosedField
    {
        get => Require(_closedField);
        internal set => Set(ref _closedField, value);
    }

    private FieldBuilder? _pausedField;
    public FieldBuilder PausedField
    {
        get => Require(_pausedField);
        internal set => Set(ref _pausedField, value);
    }

    private FieldBuilder? _promptField;
    public FieldBuilder PromptField
    {
        get => Require(_promptField);
        internal set => Set(ref _promptField, value);
    }

    private MethodBuilder? _questionSync;
    public MethodBuilder QuestionSync
    {
        get => Require(_questionSync);
        internal set => Set(ref _questionSync, value);
    }

    private MethodBuilder? _createInterface;
    public MethodBuilder CreateInterface
    {
        get => Require(_createInterface);
        internal set => Set(ref _createInterface, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Readline metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Readline metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = InterfaceType;
        _ = InterfaceCtor;
        _ = ClosedField;
        _ = PausedField;
        _ = PromptField;
        _ = QuestionSync;
        _ = CreateInterface;
        IsComplete = true;
    }
}
