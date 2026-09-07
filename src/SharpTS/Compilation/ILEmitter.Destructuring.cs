using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Stable destructuring-source caches. The parser marks only the synthetic source
/// temporary, allowing positional/property binding reads to reuse one guarded typed
/// receiver and to keep numeric values native through their consuming local.
/// </summary>
public partial class ILEmitter
{
    private sealed record StableArrayDestructureBinding(
        LocalBuilder ObjectLocal,
        LocalBuilder ArrayLocal);

    private sealed record StableRecordDestructureBinding(
        LocalBuilder ObjectLocal,
        LocalBuilder TypedLocal,
        string Fingerprint,
        JsonSerializationShape.Record Shape,
        bool IsExact,
        bool IsProvenStable);

    private readonly Dictionary<string, StableArrayDestructureBinding>
        _stableArrayDestructureBindings = [];
    private readonly Dictionary<string, StableRecordDestructureBinding>
        _stableRecordDestructureBindings = [];
    private readonly HashSet<LocalBuilder> _stableCompactRecordLocals = [];

    private void RegisterStableCompactRecordLocal(Stmt.Var declaration, LocalBuilder local)
    {
        if (declaration.Initializer is Expr.ObjectLiteral literal &&
            _ctx.RuntimeFeatures?.CompactObjectRecordStableLocalLiterals.Contains(literal) == true)
        {
            _stableCompactRecordLocals.Add(local);
        }
    }

    private void RegisterStableDestructuringSource(Stmt.Var declaration, LocalBuilder objectLocal)
    {
        if (objectLocal.LocalType != _ctx.Types.Object || declaration.Initializer is null)
            return;

        switch (declaration.DestructuringSource)
        {
            case DestructuringSourceKind.Array:
                RegisterStableArrayDestructuringSource(declaration, objectLocal);
                break;
            case DestructuringSourceKind.Object:
                RegisterStableRecordDestructuringSource(declaration, objectLocal);
                break;
        }
    }

    private void RegisterStableArrayDestructuringSource(
        Stmt.Var declaration, LocalBuilder objectLocal)
    {
        if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true ||
            _ctx.RuntimeFeatures?.UsesArrayPrototypeMutation == true ||
            declaration.Initializer is not Expr.Call
            {
                Callee: Expr.Variable { Name.Lexeme: "__arrayDestructure" },
                Arguments.Count: 1
            } call ||
            _ctx.TypeMap?.Get(call.Arguments[0]) is not (TypeInfo.Array or TypeInfo.Tuple))
            return;

        var arrayLocal = IL.DeclareLocal(_ctx.Runtime!.TSArrayType);
        IL.Emit(OpCodes.Ldloc, objectLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Runtime.TSArrayType);
        IL.Emit(OpCodes.Stloc, arrayLocal);
        _stableArrayDestructureBindings[declaration.Name.Lexeme] =
            new StableArrayDestructureBinding(objectLocal, arrayLocal);
    }

    private void RegisterStableRecordDestructuringSource(
        Stmt.Var declaration, LocalBuilder objectLocal)
    {
        if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true ||
            _ctx.RuntimeFeatures is not { } features ||
            _ctx.TypeMap?.Get(declaration.Initializer!) is not { } sourceType ||
            !ILCompiler.TryGetCompactRecordShape(sourceType, out var shape))
            return;

        string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
        if (!_ctx.Runtime!.CompactObjectRecordTypes.TryGetValue(
                fingerprint, out var carrierType) ||
            !_ctx.Runtime.CompactObjectRecordIsMaterializedGetters.ContainsKey(fingerprint))
            return;

        LocalBuilder typedLocal;
        bool isExact = false;
        if (declaration.Initializer is Expr.Variable sourceVariable &&
            _ctx.HoistedCompactRecordParameters.TryGetValue(
                sourceVariable.Name.Lexeme, out var hoisted) &&
            hoisted.Fingerprint == fingerprint)
        {
            typedLocal = hoisted.TypedLocal;
            isExact = hoisted.IsExact;
        }
        else
        {
            typedLocal = IL.DeclareLocal(carrierType);
            IL.Emit(OpCodes.Ldloc, objectLocal);
            IL.Emit(OpCodes.Isinst, carrierType);
            IL.Emit(OpCodes.Stloc, typedLocal);
        }

        _stableRecordDestructureBindings[declaration.Name.Lexeme] =
            new StableRecordDestructureBinding(
                objectLocal,
                typedLocal,
                fingerprint,
                shape,
                isExact,
                declaration.Initializer is Expr.Variable source &&
                _ctx.Locals.GetLocal(source.Name.Lexeme) is { } sourceLocal &&
                _stableCompactRecordLocals.Contains(sourceLocal));
    }

    private bool TryEmitStableArrayDestructureGet(Expr.GetIndex expression)
    {
        if (expression.Optional ||
            expression.Object is not Expr.Variable source ||
            expression.Index is not Expr.Literal { Value: double index } ||
            index < 0 || index != Math.Truncate(index) ||
            !_stableArrayDestructureBindings.TryGetValue(
                source.Name.Lexeme, out var binding))
            return false;

        var fallback = IL.DefineLabel();
        var end = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Ldc_I8, checked((long)index));
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayGetLong);
        IL.Emit(OpCodes.Br, end);

        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, binding.ObjectLocal);
        EmitDoubleConstant(index);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetIndex);
        IL.MarkLabel(end);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitStableArrayDestructureGetAsDouble(Expr.GetIndex expression)
    {
        if (expression.Optional ||
            expression.Object is not Expr.Variable source ||
            expression.Index is not Expr.Literal { Value: double index } ||
            index < 0 || index != Math.Truncate(index) || index > int.MaxValue ||
            !_stableArrayDestructureBindings.TryGetValue(
                source.Name.Lexeme, out var binding))
            return false;

        int intIndex = (int)index;
        var genericReceiver = IL.DefineLabel();
        var boxedArrayValue = IL.DefineLabel();
        var end = IL.DefineLabel();
        var result = IL.DeclareLocal(_ctx.Types.Double);

        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Brfalse, genericReceiver);
        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Ldc_I4, intIndex);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayCanGetDouble);
        IL.Emit(OpCodes.Brfalse, boxedArrayValue);
        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Ldc_I4, intIndex);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime.TSArrayGetDouble);
        IL.Emit(OpCodes.Stloc, result);
        IL.Emit(OpCodes.Br, end);

        // Holes and non-numeric backing modes still avoid boxing the literal key.
        IL.MarkLabel(boxedArrayValue);
        IL.Emit(OpCodes.Ldloc, binding.ArrayLocal);
        IL.Emit(OpCodes.Ldc_I8, (long)intIndex);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime.TSArrayGetLong);
        IL.Emit(OpCodes.Call, _ctx.Runtime.ConvertToNumber);
        IL.Emit(OpCodes.Stloc, result);
        IL.Emit(OpCodes.Br, end);

        IL.MarkLabel(genericReceiver);
        IL.Emit(OpCodes.Ldloc, binding.ObjectLocal);
        EmitDoubleConstant(index);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetIndex);
        IL.Emit(OpCodes.Call, _ctx.Runtime.ConvertToNumber);
        IL.Emit(OpCodes.Stloc, result);

        IL.MarkLabel(end);
        IL.Emit(OpCodes.Ldloc, result);
        SetStackType(StackType.Double);
        return true;
    }

    private bool TryEmitStableRecordDestructureGet(Expr.Get expression)
    {
        if (TryEmitNumericDestructuringLoad(expression, boxed: true))
            return true;
        if (expression.Optional ||
            expression.Object is not Expr.Variable source ||
            !_stableRecordDestructureBindings.TryGetValue(
                source.Name.Lexeme, out var binding) ||
            !TryGetStableRecordField(binding, expression.Name.Lexeme, out var field))
            return false;

        if (binding.IsExact)
        {
            IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
            IL.Emit(OpCodes.Ldfld, field);
            SetStackTypeForFieldType(field.FieldType);
            return true;
        }

        var fallback = IL.DefineLabel();
        var end = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
        IL.Emit(OpCodes.Brfalse, fallback);
        if (!binding.IsProvenStable &&
            !_ctx.RuntimeFeatures!.CanAssumeCompactObjectRecordIsUnmaterialized(
                binding.Fingerprint))
        {
            IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
            IL.Emit(OpCodes.Call,
                _ctx.Runtime!.CompactObjectRecordIsMaterializedGetters[
                    binding.Fingerprint]);
            IL.Emit(OpCodes.Brtrue, fallback);
        }
        IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
        IL.Emit(OpCodes.Ldfld, field);
        if (field.FieldType.IsValueType)
            IL.Emit(OpCodes.Box, field.FieldType);
        IL.Emit(OpCodes.Br, end);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, binding.ObjectLocal);
        IL.Emit(OpCodes.Ldstr, expression.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        IL.MarkLabel(end);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitStableRecordDestructureGetAsDouble(Expr.Get expression)
    {
        if (TryEmitNumericDestructuringLoad(expression, boxed: false))
            return true;
        if (expression.Optional ||
            expression.Object is not Expr.Variable source ||
            !_stableRecordDestructureBindings.TryGetValue(
                source.Name.Lexeme, out var binding) ||
            !TryGetStableRecordField(binding, expression.Name.Lexeme, out var field) ||
            field.FieldType != _ctx.Types.Double)
            return false;

        if (binding.IsExact)
        {
            IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
            IL.Emit(OpCodes.Ldfld, field);
            SetStackType(StackType.Double);
            return true;
        }

        var fallback = IL.DefineLabel();
        var end = IL.DefineLabel();
        var result = IL.DeclareLocal(_ctx.Types.Double);
        IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
        IL.Emit(OpCodes.Brfalse, fallback);
        if (!binding.IsProvenStable &&
            !_ctx.RuntimeFeatures!.CanAssumeCompactObjectRecordIsUnmaterialized(
                binding.Fingerprint))
        {
            IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
            IL.Emit(OpCodes.Call,
                _ctx.Runtime!.CompactObjectRecordIsMaterializedGetters[
                    binding.Fingerprint]);
            IL.Emit(OpCodes.Brtrue, fallback);
        }
        IL.Emit(OpCodes.Ldloc, binding.TypedLocal);
        IL.Emit(OpCodes.Ldfld, field);
        IL.Emit(OpCodes.Stloc, result);
        IL.Emit(OpCodes.Br, end);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, binding.ObjectLocal);
        IL.Emit(OpCodes.Ldstr, expression.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        IL.Emit(OpCodes.Call, _ctx.Runtime.ConvertToNumber);
        IL.Emit(OpCodes.Stloc, result);
        IL.MarkLabel(end);
        IL.Emit(OpCodes.Ldloc, result);
        SetStackType(StackType.Double);
        return true;
    }

    private bool TryGetStableRecordField(
        StableRecordDestructureBinding binding,
        string property,
        out FieldBuilder field)
    {
        for (int index = 0; index < binding.Shape.Fields.Count; index++)
        {
            if (binding.Shape.Fields[index].Key != property)
                continue;
            return _ctx.Runtime!.CompactObjectRecordValueFields.TryGetValue(
                (binding.Fingerprint, index), out field!);
        }

        field = null!;
        return false;
    }
}
