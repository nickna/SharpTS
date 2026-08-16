using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in method implementations for WeakRef instances.
/// </summary>
public static class WeakRefBuiltIns
{
    public static object? GetMember(SharpTSWeakRef receiver, string name)
    {
        return name switch
        {
            "deref" => BuiltInMethod.CreateV2("deref", 0, static (_, recv, _) =>
            {
                var weakRef = (SharpTSWeakRef)recv.ToObject()!;
                return RuntimeValue.FromBoxed(weakRef.Deref());
            }),

            _ => null
        };
    }
}
