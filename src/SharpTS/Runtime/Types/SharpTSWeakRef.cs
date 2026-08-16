using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript WeakRef&lt;T&gt;.
/// </summary>
/// <remarks>
/// Uses WeakReference&lt;object&gt; to provide weak reference semantics matching JavaScript WeakRef:
/// - Target must be an object (not a primitive)
/// - deref() returns the target if still alive, or undefined
/// </remarks>
public class SharpTSWeakRef : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.WeakRef;

    private readonly WeakReference<object> _ref;

    public SharpTSWeakRef(object? target)
    {
        ValidateTarget(target);
        _ref = new WeakReference<object>(target!);
    }

    /// <summary>
    /// Returns the target object if still alive, or null (representing undefined).
    /// </summary>
    public object? Deref()
    {
        return _ref.TryGetTarget(out var target) ? target : null;
    }

    /// <summary>
    /// Validates that the target is a valid object type (not a primitive).
    /// </summary>
    private static void ValidateTarget(object? target)
    {
        if (target == null)
        {
            throw new Exception("Runtime Error: WeakRef target cannot be null or undefined.");
        }

        if (target is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used as weak reference target. WeakRef target must be an object, not '{WeakTargetErrors.TypeNameOf(target)}'.");
        }
    }

    public override string ToString() => "WeakRef {}";
}
