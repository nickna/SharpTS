using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for JSON static method calls.
/// Handles JSON.parse() and JSON.stringify().
/// </summary>
public sealed class JSONStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a JSON static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "parse":
                // Arg 0: text — coerce via ECMA-262 ToString (JS-style "true"/"false")
                // before parsing. Without this, `JSON.parse(false)` round-trips through
                // CLR ToString → "False" → SyntaxError. Also throw TypeError early for
                // Symbol arguments (ToString throws on Symbol per spec).
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    var argLocal = il.DeclareLocal(ctx.Types.Object);
                    il.Emit(OpCodes.Stloc, argLocal);
                    // if (arg is $TSSymbol) throw TypeError
                    var notSymbolLabel = il.DefineLabel();
                    il.Emit(OpCodes.Ldloc, argLocal);
                    il.Emit(OpCodes.Isinst, ctx.Runtime!.TSSymbolType);
                    il.Emit(OpCodes.Brfalse, notSymbolLabel);
                    GuestErrorEmitter.ThrowTypeError(il, ctx.Runtime!, "Cannot convert a Symbol value to a string");
                    il.MarkLabel(notSymbolLabel);
                    il.Emit(OpCodes.Ldloc, argLocal);
                    // Use ToJsString (ECMA-262 ToString protocol) rather than
                    // Stringify so user-defined toString/valueOf on Dictionary/$Object
                    // receivers fires. JSON.parse({toString: () => '"x"'}) must
                    // coerce via the protocol then parse the resulting string.
                    il.Emit(OpCodes.Call, ctx.Runtime!.ToJsString);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "undefined");
                }

                // Arg 1: reviver (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonParseWithReviver);
                }
                else
                {
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonParse);
                }
                return true;

            case "stringify":
                JsonSerializationShape? staticShape = null;
                FieldBuilder? shapeField = null;
                if (arguments.Count == 1 && ctx.ProgramType is not null &&
                    JsonSerializationShapeAnalyzer.TryAnalyze(
                        ctx.TypeMap?.Get(arguments[0]), out var analyzedShape))
                {
                    staticShape = analyzedShape;
                    shapeField = GetOrDefineShapeField(ctx, analyzedShape);
                }

                // Arg 0: value (required)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Arg 1: replacer (optional), Arg 2: space (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);

                    if (arguments.Count > 2)
                    {
                        emitter.EmitExpression(arguments[2]);
                        emitter.EmitBoxIfNeeded(arguments[2]);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldnull);
                    }
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonStringifyFull);
                }
                else
                {
                    if (staticShape is not null && shapeField is not null)
                    {
                        bool closedShape = JsonSerializationShapeAnalyzer.IsClosed(staticShape);
                        EmitLazyShapeDescriptor(ctx, staticShape, shapeField, closedShape);
                        il.Emit(closedShape ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Call, ctx.Runtime!.JsonStringifyShaped);
                    }
                    else
                    {
                        il.Emit(OpCodes.Call, ctx.Runtime!.JsonStringify);
                    }
                }
                return true;

            case "rawJSON":
            case "isRawJSON":
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, methodName == "rawJSON"
                    ? ctx.Runtime!.JsonRawJson
                    : ctx.Runtime!.JsonIsRawJson);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Stage 4y: expose JSON.parse / JSON.stringify as values so
    /// `let p = JSON.parse; p('"x"')` works AND so test262's isConstructor
    /// harness reports them as functions (typeof check).
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var runtime = ctx.Runtime!;
        MethodInfo? method = propertyName switch
        {
            "parse"     => runtime.JsonParse,
            "stringify" => runtime.JsonStringify,
            "rawJSON"   => runtime.JsonRawJson,
            "isRawJSON" => runtime.JsonIsRawJson,
            _ => null
        };
        if (method == null) return false;

        // Route value-form reads through the singleton object. Besides keeping
        // identity stable, this makes configurable built-in deletion observable:
        // after `delete JSON.stringify`, a later `JSON.stringify` must not be
        // resurrected by this compile-time fast path.
        var il = ctx.IL;
        il.Emit(OpCodes.Call, runtime.JsonSingletonPopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        return true;
    }

    /// <summary>
    /// Canonical source of the spec-stable JSON static methods exposed in value
    /// form, with the matching <c>$Runtime</c> method and ECMA-262 §17 spec
    /// length. Consumed both by <see cref="TryEmitStaticPropertyGet"/>
    /// (<c>let p = JSON.parse</c>) and by the JSON singleton populate step that
    /// fills <c>_jsonSingleton</c> for value-form receivers
    /// (<c>const j = JSON; j.stringify(x)</c>, issue #276). The rawJSON/isRawJSON
    /// raw-value methods use the same path so aliases and singleton access are
    /// fully callable rather than metadata-only stubs.
    /// </summary>
    internal static IEnumerable<(string Name, MethodInfo? Method, int Length)> EnumerateValueFormMethods(EmittedRuntime runtime)
    {
        yield return ("parse",     runtime.JsonParse, 2);
        yield return ("stringify", runtime.JsonStringify, 3);
        yield return ("rawJSON",   runtime.JsonRawJson, 1);
        yield return ("isRawJSON", runtime.JsonIsRawJson, 1);
    }

    public bool HasStaticProperty(string memberName) =>
        memberName is "parse" or "stringify" or "rawJSON" or "isRawJSON";

    internal static FieldBuilder GetOrDefineShapeField(
        CompilationContext ctx,
        JsonSerializationShape shape)
    {
        string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
        if (ctx.Runtime!.JsonShapeFields.TryGetValue(fingerprint, out var field))
            return field;

        field = ctx.ProgramType!.DefineField(
            $"$jsonShape_{ctx.Runtime.JsonShapeFields.Count}",
            ctx.Types.Object,
            FieldAttributes.Assembly | FieldAttributes.Static);
        ctx.Runtime.JsonShapeFields.Add(fingerprint, field);
        return field;
    }

    internal static void EmitLazyShapeDescriptor(
        CompilationContext ctx,
        JsonSerializationShape shape,
        FieldBuilder field,
        bool closed)
    {
        var il = ctx.IL;
        var ready = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, ready);
        il.Emit(OpCodes.Pop);
        EmitShapeDescriptor(il, ctx, shape, closed);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, field);
        il.MarkLabel(ready);
    }

    private static void EmitShapeDescriptor(
        ILGenerator il,
        CompilationContext ctx,
        JsonSerializationShape shape,
        bool closed)
    {
        switch (shape)
        {
            case JsonSerializationShape.Generic:
                il.Emit(OpCodes.Ldstr, "$g");
                return;
            case JsonSerializationShape.Number:
                il.Emit(OpCodes.Ldstr, closed ? "$N" : "$n");
                return;
            case JsonSerializationShape.String:
                il.Emit(OpCodes.Ldstr, closed ? "$S" : "$s");
                return;
            case JsonSerializationShape.Boolean:
                il.Emit(OpCodes.Ldstr, closed ? "$B" : "$b");
                return;
            case JsonSerializationShape.Array array:
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Newarr, ctx.Types.Object);
                EmitArraySlot(il, 0, () => il.Emit(OpCodes.Ldstr, closed ? "$A" : "$a"));
                EmitArraySlot(il, 1, () => EmitShapeReference(il, ctx, array.Element, closed));
                return;
            case JsonSerializationShape.Record record:
                il.Emit(OpCodes.Ldc_I4, 1 + record.Fields.Count * 2);
                il.Emit(OpCodes.Newarr, ctx.Types.Object);
                EmitArraySlot(il, 0, () => il.Emit(OpCodes.Ldstr, closed ? "$O" : "$o"));
                for (int i = 0; i < record.Fields.Count; i++)
                {
                    var field = record.Fields[i];
                    EmitArraySlot(il, 1 + i * 2, () => il.Emit(OpCodes.Ldstr, field.Key));
                    EmitArraySlot(il, 2 + i * 2,
                        () => EmitShapeReference(il, ctx, field.Value, closed));
                }
                return;
        }

        void EmitArraySlot(ILGenerator generator, int index, Action emitValue)
        {
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Ldc_I4, index);
            emitValue();
            generator.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static void EmitShapeReference(
        ILGenerator il,
        CompilationContext ctx,
        JsonSerializationShape shape,
        bool closed)
    {
        if (shape is JsonSerializationShape.Record or JsonSerializationShape.Array)
        {
            var field = GetOrDefineShapeField(ctx, shape);
            EmitLazyShapeDescriptor(ctx, shape, field, closed);
            return;
        }

        EmitShapeDescriptor(il, ctx, shape, closed);
    }
}
