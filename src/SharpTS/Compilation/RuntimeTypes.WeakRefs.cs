namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region WeakRef Support

    /// <summary>
    /// Creates a WeakRef wrapping the given target.
    /// </summary>
    public static object CreateWeakRef(object? target)
    {
        if (target == null)
        {
            throw new Exception("Runtime Error: WeakRef target cannot be null or undefined.");
        }

        ValidateWeakRefTarget(target);
        return new WeakReference<object>(target);
    }

    /// <summary>
    /// Dereferences a WeakRef. Returns the target if still alive, or null.
    /// </summary>
    public static object? WeakRefDeref(object? weakRef)
    {
        if (weakRef is WeakReference<object> wr && wr.TryGetTarget(out var target))
        {
            return target;
        }
        return null;
    }

    /// <summary>
    /// Validates that the target is not a primitive type.
    /// </summary>
    private static void ValidateWeakRefTarget(object target)
    {
        if (target is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used as weak reference target. WeakRef target must be an object, not '{GetWeakRefTypeName(target)}'.");
        }
    }

    private static string GetWeakRefTypeName(object value) => value switch
    {
        string => "string",
        double or int or long or float or decimal => "number",
        bool => "boolean",
        _ => value.GetType().Name
    };

    #endregion
}
