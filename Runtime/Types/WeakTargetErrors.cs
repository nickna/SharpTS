namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared helpers for weak-collection error messages. One implementation
/// replaces the byte-identical GetTypeName copies in SharpTSWeakMap /
/// SharpTSWeakSet / SharpTSWeakRef (2026-07 cleanup) so a future Symbol/BigInt
/// conformance fix cannot drift across the weak constructs.
/// </summary>
internal static class WeakTargetErrors
{
    /// <summary>JS-flavored type name for "must be an object" error messages.</summary>
    public static string TypeNameOf(object value) => value switch
    {
        string => "string",
        double or int or long or float or decimal => "number",
        bool => "boolean",
        _ => value.GetType().Name
    };
}
