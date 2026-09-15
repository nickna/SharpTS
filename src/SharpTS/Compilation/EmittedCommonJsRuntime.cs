using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>CommonJS declarations for one compilation, checked during emission and frozen at completion.</summary>
public sealed class EmittedCommonJsRuntime
{
    internal EmittedCommonJsRuntime() { }

    public bool IsComplete { get; private set; }

    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorInfo? _ctor;
    public ConstructorInfo Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodInfo? _exportsSetter;
    public MethodInfo ExportsSetter
    {
        get => Require(_exportsSetter);
        internal set => Set(ref _exportsSetter, value);
    }

    private FieldBuilder? _exportsFieldInfoField;
    public FieldBuilder ExportsFieldInfoField
    {
        get => Require(_exportsFieldInfoField);
        internal set => Set(ref _exportsFieldInfoField, value);
    }

    private FieldBuilder? _idField;
    public FieldBuilder IdField
    {
        get => Require(_idField);
        internal set => Set(ref _idField, value);
    }

    private FieldBuilder? _filenameField;
    public FieldBuilder FilenameField
    {
        get => Require(_filenameField);
        internal set => Set(ref _filenameField, value);
    }

    private FieldBuilder? _loadedField;
    public FieldBuilder LoadedField
    {
        get => Require(_loadedField);
        internal set => Set(ref _loadedField, value);
    }

    private FieldBuilder? _pathsField;
    public FieldBuilder PathsField
    {
        get => Require(_pathsField);
        internal set => Set(ref _pathsField, value);
    }

    private FieldBuilder? _childrenField;
    public FieldBuilder ChildrenField
    {
        get => Require(_childrenField);
        internal set => Set(ref _childrenField, value);
    }

    private FieldBuilder? _parentField;
    public FieldBuilder ParentField
    {
        get => Require(_parentField);
        internal set => Set(ref _parentField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"CommonJS metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("CommonJS metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = Type;
        _ = Ctor;
        _ = ExportsSetter;
        _ = ExportsFieldInfoField;
        _ = IdField;
        _ = FilenameField;
        _ = LoadedField;
        _ = PathsField;
        _ = ChildrenField;
        _ = ParentField;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        IsComplete = true;
    }
}
