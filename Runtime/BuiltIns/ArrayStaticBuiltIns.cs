using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static methods on the Array namespace (e.g., Array.isArray())
/// </summary>
public static class ArrayStaticBuiltIns
{
    public static object? GetStaticMethod(string name)
    {
        return name switch
        {
            "isArray" => new BuiltInMethod("isArray", 1, (_, _, args) =>
            {
                return args[0] is SharpTSArray;
            }),
            _ => null
        };
    }
}
