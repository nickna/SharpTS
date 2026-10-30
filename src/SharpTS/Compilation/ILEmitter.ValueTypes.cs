using System.Reflection.Emit;
using SharpTS.Parsing;

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
    private bool PrepareReceiverForMemberAccess(Type type, Expr receiver)
    {
        RecordExternalInput(receiver, type, NativeClrAdmission.InputConversion.Receiver);
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
    /// Marshals a CLR result into the guest representation. Numeric primitives
    /// become boxed doubles, matching the interpreter's DotNetMarshaller boundary.
    /// Object-typed results are inspected without coercing nonnumeric payloads.
    /// </summary>
    /// <param name="type">The type of the value on the stack.</param>
    private void BoxResultIfValueType(Type type)
    {
        type = _ctx.TypeMapper.CoreAtomForEmission(type);
        if (type == _ctx.Types.Object)
            ClrNumericInteropEmitter.EmitNormalizeBoxed(IL, _ctx.Types);
        else if (type == typeof(decimal))
        {
            IL.Emit(OpCodes.Call, typeof(Convert).GetMethod(nameof(Convert.ToDouble), [typeof(decimal)])!);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
        }
        else if (type.IsValueType && type != typeof(void))
        {
            DelegateAdapterEmitter.EmitBoxForTS(IL, type);
            if (Nullable.GetUnderlyingType(type) != null)
                ClrNumericInteropEmitter.EmitNormalizeBoxed(IL, _ctx.Types);
        }
    }
}
