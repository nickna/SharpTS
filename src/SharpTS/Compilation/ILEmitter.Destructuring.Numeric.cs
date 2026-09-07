using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private sealed record NumericDestructuringLoad(LocalBuilder Receiver, LocalBuilder Valid, LocalBuilder Value);
    private readonly Dictionary<Expr.Get, NumericDestructuringLoad> _numericDestructuringLoads =
        new(ReferenceEqualityComparer.Instance);

    private bool TryEmitNumericDestructuringSequence(Stmt.Sequence sequence)
    {
        if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != false ||
            sequence.Statements.Count < 2 ||
            sequence.Statements[0] is not Stmt.Var
            {
                Initializer: { } source,
                DestructuringSource: DestructuringSourceKind.Object
            } declaration)
            return false;

        var gets = new List<Expr.Get>();
        foreach (var statement in sequence.Statements.Skip(1))
        {
            if (statement is not Stmt.Var
                {
                    Initializer: Expr.Get
                    {
                        Object: Expr.Variable receiver, Optional: false, Defaulted: false
                    } get
                } || receiver.Name.Lexeme != declaration.Name.Lexeme)
                return false;
            gets.Add(get);
        }
        if (!TryCreateNumericRecordReadPlan(source, gets.Select(get => get.Name.Lexeme).ToArray(), out var plan))
            return false;

        EmitStatement(declaration);
        var receiverLocal = _ctx.Locals.GetLocal(declaration.Name.Lexeme);
        if (receiverLocal is null || receiverLocal.LocalType != _ctx.Types.Object)
        {
            // Top-level and captured temporaries can use static fields or cells.
            // Their ordinary emission already preserves the evaluated receiver.
            foreach (var statement in sequence.Statements.Skip(1))
                EmitStatement(statement);
            return true;
        }
        var valid = IL.DeclareLocal(_ctx.Types.Boolean);
        var values = gets.Select(_ => IL.DeclareLocal(_ctx.Types.Double)).ToArray();
        var ready = IL.DefineLabel();
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, valid);
        EmitNumericRecordSnapshot(receiverLocal, plan, values, ready);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, valid);
        IL.MarkLabel(ready);

        // Cache only across a complete fixed numeric pattern. A computed key,
        // default or nested binding could run guest code between property reads.
        for (int index = 0; index < gets.Count; index++)
            _numericDestructuringLoads.Add(gets[index], new(receiverLocal, valid, values[index]));
        try
        {
            foreach (var statement in sequence.Statements.Skip(1))
                EmitStatement(statement);
        }
        finally
        {
            foreach (var get in gets)
                _numericDestructuringLoads.Remove(get);
        }
        return true;
    }

    private bool TryEmitNumericDestructuringLoad(Expr.Get expression, bool boxed)
    {
        if (!_numericDestructuringLoads.TryGetValue(expression, out var load))
            return false;
        var fallback = IL.DefineLabel();
        var end = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, load.Valid);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, load.Value);
        if (boxed)
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Br, end);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, load.Receiver);
        IL.Emit(OpCodes.Ldstr, expression.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        if (!boxed)
            IL.Emit(OpCodes.Call, _ctx.Runtime.ConvertToNumber);
        IL.MarkLabel(end);
        if (boxed)
            SetStackUnknown();
        else
            SetStackType(StackType.Double);
        return true;
    }

    private sealed record NumericRecordReadPlan(
        IReadOnlyList<string> Properties,
        string Fingerprint,
        IReadOnlyList<FieldBuilder>? Fields);

    private bool TryCreateNumericRecordReadPlan(
        Expr source, IReadOnlyList<string> properties, out NumericRecordReadPlan plan)
    {
        plan = null!;
        if (_ctx.TypeMap?.Get(source) is not { } type ||
            !ILCompiler.TryGetCompactRecordShape(type, out var shape) ||
            properties.Count == 0 ||
            properties.Any(property => !shape.Fields.Any(field =>
                field.Key == property && field.Value is JsonSerializationShape.Number)))
            return false;

        string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
        List<FieldBuilder>? fields = null;
        if (_ctx.Runtime!.CompactObjectRecordTypes.TryGetValue(fingerprint, out var carrier) &&
            _ctx.Runtime.CompactObjectRecordTryGetMaterializedDictionary.ContainsKey(fingerprint))
        {
            fields = [];
            foreach (string property in properties)
            {
                int index = shape.Fields.ToList().FindIndex(field => field.Key == property);
                if (!_ctx.Runtime.CompactObjectRecordValueFields.TryGetValue((fingerprint, index), out var field) ||
                    field.DeclaringType != carrier || field.FieldType != _ctx.Types.Double)
                {
                    fields = null;
                    break;
                }
                fields.Add(field);
            }
        }
        plan = new NumericRecordReadPlan(properties, fingerprint, fields);
        return true;
    }

    /// <summary>
    /// Acquires only own numeric data properties. Every probe is free of guest
    /// effects, so partial failure can safely resume ordinary property evaluation.
    /// The descriptor key remains the original receiver after materialization.
    /// </summary>
    private void EmitNumericRecordSnapshot(
        LocalBuilder receiver, NumericRecordReadPlan plan,
        IReadOnlyList<LocalBuilder> values, Label fallback)
    {
        var runtime = _ctx.Runtime!;
        var dictionary = IL.DeclareLocal(_ctx.Types.DictionaryStringObject);
        var tryDictionary = IL.DefineLabel();
        var dictionaryReady = IL.DefineLabel();
        var ready = IL.DefineLabel();
        if (plan.Fields is { } fields)
        {
            var exact = IL.DeclareLocal(fields[0].DeclaringType!);
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Isinst, exact.LocalType);
            IL.Emit(OpCodes.Stloc, exact);
            IL.Emit(OpCodes.Ldloc, exact);
            IL.Emit(OpCodes.Brfalse, tryDictionary);
            if (!_stableCompactRecordLocals.Contains(receiver) &&
                !_ctx.RuntimeFeatures!.CanAssumeCompactObjectRecordIsUnmaterialized(plan.Fingerprint))
            {
                IL.Emit(OpCodes.Ldloc, exact);
                IL.Emit(OpCodes.Ldloca, dictionary);
                IL.Emit(OpCodes.Call, runtime.CompactObjectRecordTryGetMaterializedDictionary[plan.Fingerprint]);
                IL.Emit(OpCodes.Brtrue, dictionaryReady);
            }
            // Callers retain the program-wide descriptor restriction for field reads.
            for (int index = 0; index < fields.Count; index++)
            {
                IL.Emit(OpCodes.Ldloc, exact);
                IL.Emit(OpCodes.Ldfld, fields[index]);
                IL.Emit(OpCodes.Stloc, values[index]);
            }
            IL.Emit(OpCodes.Br, ready);
        }

        IL.MarkLabel(tryDictionary);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryStringObject);
        IL.Emit(OpCodes.Stloc, dictionary);
        IL.Emit(OpCodes.Ldloc, dictionary);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.MarkLabel(dictionaryReady);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        IL.Emit(OpCodes.Brtrue, fallback);

        var boxed = IL.DeclareLocal(_ctx.Types.Object);
        for (int index = 0; index < plan.Properties.Count; index++)
        {
            IL.Emit(OpCodes.Ldloc, dictionary);
            IL.Emit(OpCodes.Ldstr, plan.Properties[index]);
            IL.Emit(OpCodes.Ldloca, boxed);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
                _ctx.Types.DictionaryStringObject, "TryGetValue", _ctx.Types.String,
                _ctx.Types.Object.MakeByRefType()));
            IL.Emit(OpCodes.Brfalse, fallback);
            IL.Emit(OpCodes.Ldloc, boxed);
            IL.Emit(OpCodes.Isinst, _ctx.Types.Double);
            IL.Emit(OpCodes.Brfalse, fallback);
            IL.Emit(OpCodes.Ldloc, boxed);
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
            IL.Emit(OpCodes.Stloc, values[index]);
        }
        IL.MarkLabel(ready);
    }
}
