using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    // Interface member order is not object-literal slot order. Select an existing
    // carrier by its named fields, then guard that actual CLR type at the read.
    private bool TryEmitInterfaceRecordGet(Expr.Get get, bool numericConsumer)
    {
        if (get.Optional || _ctx.RuntimeFeatures is not { } features ||
            _ctx.Runtime is not { } runtime ||
            _ctx.TypeMap?.Get(get.Object) is not TypeInfo.Interface type ||
            !JsonSerializationShapeAnalyzer.TryMatchCompactInterface(
                type, features.CompactObjectRecordShapes.Values, out var shape))
            return false;

        int slot = -1;
        for (int i = 0; i < shape.Fields.Count; i++)
            if (shape.Fields[i].Key == get.Name.Lexeme) slot = i;
        string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
        if (slot < 0 || !runtime.CompactObjectRecordTypes.TryGetValue(fingerprint, out var carrier) ||
            !runtime.CompactObjectRecordValueFields.TryGetValue((fingerprint, slot), out var field) ||
            !runtime.CompactObjectRecordIsMaterializedGetters.TryGetValue(fingerprint, out var materialized) ||
            (numericConsumer && field.FieldType != _ctx.Types.Double))
            return false;

        EmitExpression(get.Object);
        EmitBoxIfNeeded(get.Object);
        var receiver = IL.DeclareLocal(_ctx.Types.Object);
        var exact = IL.DeclareLocal(carrier);
        var fallback = IL.DefineLabel();
        var end = IL.DefineLabel();
        IL.Emit(OpCodes.Stloc, receiver);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Isinst, carrier);
        IL.Emit(OpCodes.Stloc, exact);
        IL.Emit(OpCodes.Ldloc, exact);
        IL.Emit(OpCodes.Brfalse, fallback);
        // Interface aliases can hide mutations from exact-local analysis.
        IL.Emit(OpCodes.Ldloc, exact);
        IL.Emit(OpCodes.Call, materialized);
        IL.Emit(OpCodes.Brtrue, fallback);
        if (features.UsesDynamicPropertyDescriptors)
        {
            IL.Emit(OpCodes.Ldloc, exact);
            IL.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
            IL.Emit(OpCodes.Brtrue, fallback);
        }
        IL.Emit(OpCodes.Ldloc, exact);
        IL.Emit(OpCodes.Ldfld, field);
        if (!numericConsumer && field.FieldType.IsValueType)
            IL.Emit(OpCodes.Box, field.FieldType);
        IL.Emit(OpCodes.Br, end);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, receiver);
        if (!IsNullPlaceholderGlobal(get.Object))
            EmitThrowIfUndefinedReceiverOnStack(get.Name.Lexeme);
        IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
        IL.Emit(OpCodes.Call, runtime.GetProperty);
        // Only a numeric consumer may coerce the fallback. An interface annotation
        // alone must not turn a dynamically replaced string field into a number.
        if (numericConsumer) IL.Emit(OpCodes.Call, runtime.ConvertToNumber);
        IL.MarkLabel(end);
        if (numericConsumer) SetStackType(StackType.Double);
        else SetStackUnknown();
        return true;
    }
}
