using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Value type handling helpers for IL emission.
/// </summary>
/// <remarks>
/// Consolidates value type operations (unboxing, address loading, result boxing)
/// that are needed when calling methods/properties on external .NET value types.
/// </remarks>
public partial class ILEmitter
{
    /// <summary>
    /// Prepares a boxed value on the stack for instance member access.
    /// For value types: unboxes to a local and loads its address.
    /// For reference types: performs castclass.
    /// </summary>
    /// <param name="type">The target type to prepare the receiver as.</param>
    /// <returns>True if the type is a value type (caller should use Call instead of Callvirt).</returns>
    private bool PrepareReceiverForMemberAccess(Type type)
    {
        if (type.IsValueType)
        {
            IL.Emit(OpCodes.Unbox_Any, type);
            var local = IL.DeclareLocal(type);
            IL.Emit(OpCodes.Stloc, local);
            IL.Emit(OpCodes.Ldloca, local);
            return true;
        }
        else
        {
            IL.Emit(OpCodes.Castclass, type);
            return false;
        }
    }

    /// <summary>
    /// Boxes a value type result if needed to maintain object semantics.
    /// No-op for void or reference types.
    /// </summary>
    /// <param name="type">The type of the value on the stack.</param>
    private void BoxResultIfValueType(Type type)
    {
        if (type.IsValueType && type != typeof(void))
        {
            IL.Emit(OpCodes.Box, type);
        }
    }
}
