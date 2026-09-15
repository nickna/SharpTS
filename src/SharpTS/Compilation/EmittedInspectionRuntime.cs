using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required inspection helper declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedInspectionRuntime
{
    internal EmittedInspectionRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _inspectValue;
    public MethodBuilder InspectValue
    {
        get => Require(_inspectValue);
        internal set => Set(ref _inspectValue, value);
    }

    private MethodBuilder? _inspectArray;
    public MethodBuilder InspectArray
    {
        get => Require(_inspectArray);
        internal set => Set(ref _inspectArray, value);
    }

    private MethodBuilder? _inspectObject;
    public MethodBuilder InspectObject
    {
        get => Require(_inspectObject);
        internal set => Set(ref _inspectObject, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Inspection metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Inspection metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = InspectValue;
        _ = InspectArray;
        _ = InspectObject;
        IsComplete = true;
    }
}
