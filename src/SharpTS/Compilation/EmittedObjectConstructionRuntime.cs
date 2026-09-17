using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required object construction, spread, enumerable projection and rest helpers for one compilation.</summary>
public sealed class EmittedObjectConstructionRuntime
{
    internal EmittedObjectConstructionRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _mergeIntoDictionary;
    public MethodBuilder MergeIntoDictionary
    {
        get => Require(_mergeIntoDictionary);
        internal set => SetHandle(ref _mergeIntoDictionary, value);
    }

    private MethodBuilder? _mergeIntoObject;
    public MethodBuilder MergeIntoObject
    {
        get => Require(_mergeIntoObject);
        internal set => SetHandle(ref _mergeIntoObject, value);
    }

    private MethodBuilder? _getEnumerableFields;
    public MethodBuilder GetEnumerableFields
    {
        get => Require(_getEnumerableFields);
        internal set => SetHandle(ref _getEnumerableFields, value);
    }

    private MethodBuilder? _defineSymbolAccessor;
    public MethodBuilder DefineSymbolAccessor
    {
        get => Require(_defineSymbolAccessor);
        internal set => SetHandle(ref _defineSymbolAccessor, value);
    }

    private MethodBuilder? _rest;
    public MethodBuilder Rest
    {
        get => Require(_rest);
        internal set => SetHandle(ref _rest, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object construction metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object construction metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object construction metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = MergeIntoDictionary;
        _ = MergeIntoObject;
        _ = GetEnumerableFields;
        _ = DefineSymbolAccessor;
        _ = Rest;
        IsComplete = true;
    }
}
