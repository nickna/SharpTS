using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// String/Array ambiguous method dispatch with runtime type checking for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private new void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        // Check if it's a string
        var builder = _ctx.ILBuilder;
        var isStringLabel = builder.DefineLabel("ambiguous_string");
        var isListLabel = builder.DefineLabel("ambiguous_list");
        var doneLabel = builder.DefineLabel("ambiguous_done");

        // Take the string-fast-path if the receiver is either a CLR string
        // OR a `new String(...)` wrapper ($Object with __primitiveType="String").
        // Without the wrapper branch, methods like indexOf/concat fall through
        // to the dynamic GetProperty + InvokeMethodValue path where the bound
        // prototype method silently drops arguments like fromIndex (Test262
        // String/prototype/{indexOf,concat,...} regressions).
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.String);
        builder.Emit_Brtrue(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, "String");
        IL.Emit(OpCodes.Call, _ctx.Runtime!.IsBoxedPrimitiveOfTypeMethod);
        builder.Emit_Brtrue(isStringLabel);

        // List<object> → list path. Otherwise (Dictionary, $Object, etc.)
        // fall back to dynamic property dispatch via $Runtime.GetProperty +
        // InvokeMethodValue. Without this fallback, borrowed-prototype
        // patterns like `obj.charAt = String.prototype.charAt; obj.charAt(0)`
        // cast obj to List<object> and crash with InvalidCastException.
        var dynamicDispatchLabel = builder.DefineLabel("ambiguous_dynamic");
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.ListOfObject);
        builder.Emit_Brtrue(isListLabel);
        builder.Emit_Br(dynamicDispatchLabel);

        // String path — unwrap the receiver (handles both primitive strings
        // and $Object wrappers) before dispatching to the string-method
        // emitter.
        builder.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.UnwrapStringReceiverMethod);

        switch (methodName)
        {
            case "includes":
                // StringIncludes now takes (string, object, object) where the
                // 3rd arg is the position. Helper handles IsRegExp, ToJsString,
                // JsToInt32 internally.
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // ECMA-262 §22.1.3.8 step 3: searchString = ? ToString(searchString).
                    // Castclass-to-string would either NRE on null (→ String.IndexOf
                    // throws ArgumentNullException) or InvalidCastException on
                    // $Undefined/other objects. ToJsString handles all primitives
                    // and invokes the @@toPrimitive/toString protocol for objects.
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ToJsString);
                }
                else
                {
                    // ECMA-262 §22.1.3.8: ToString(undefined) = "undefined".
                    IL.Emit(OpCodes.Ldstr, "undefined");
                }
                // Optional 2nd arg: fromIndex. Use the from-variant when present; otherwise
                // the single-arg helper. Previously ignored any second arg entirely, which
                // silently produced wrong results for `str.indexOf(ch, pos)` (e.g. yaml's
                // buffer.indexOf('\n', this.pos)).
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOfFrom);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOf);
                }
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "lastIndexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // ECMA-262 §22.1.3.9 step 3: searchString = ? ToString(searchString).
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ToJsString);
                }
                else
                {
                    // ECMA-262 §22.1.3.{8,9}: ToString(undefined) = "undefined".
                    // \`"".lastIndexOf()\` should look for "undefined" not "".
                    IL.Emit(OpCodes.Ldstr, "undefined");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringLastIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "slice":
                // StringSlice(string str, object[] args). argCount derived from args.Length.
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSlice);
                break;

            case "concat":
                // str.concat(...strings)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringConcat);
                break;

            case "startsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringStartsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "endsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringEndsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "substring":
                // str.substring(start, end?)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSubstring);
                break;

            case "charAt":
                // str.charAt(index)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharAt);
                break;
        }
        builder.Emit_Br(doneLabel);

        // List path
        builder.MarkLabel(isListLabel);

        // A List receiver only gets the array fast path for methods that are
        // actually provided by Array.prototype. String-only names can still
        // be installed as own/prototype properties (for example,
        // `array.substring = String.prototype.substring`) and must use
        // ordinary Get + Call dispatch so the borrowed function receives the
        // original array as `this`. The old fall-through left the List itself
        // on the evaluation stack and returned it as the call result.
        if (methodName is "substring" or "charAt" or "startsWith" or "endsWith")
        {
            builder.Emit_Br(dynamicDispatchLabel);
        }

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.ArrayHoleInstance);
                }
                // ArrayIncludes already returns a boxed bool (object). The
                // earlier `Box Boolean` here double-boxed it — reinterpreting
                // the non-null object reference's bits as a bool, which yields
                // `true` almost always and flakily `false` when the pointer's
                // low byte happened to be 0. (Contrast ArrayIndexOf below, which
                // returns a native double, so its Box is correct.)
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayIncludes);
                break;

            case "indexOf":
            case "lastIndexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, methodName == "indexOf" ? _ctx.Runtime!.ArrayIndexOf : _ctx.Runtime!.ArrayLastIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "slice":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArraySlice);
                break;

            case "concat":
                // ECMA-262: concat(...items) is variadic. Pass args as object[], with any
                // `...spread` flattened first (#952) so concat appends/spreads each element.
                EmitArgsArrayWithSpread(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayConcat);
                break;

        }
        builder.Emit_Br(doneLabel);

        // Dynamic property-dispatch path: receiver is neither string nor List.
        // Look up the method via $Runtime.GetProperty (which walks PDS +
        // prototype chain), then invoke via $Runtime.InvokeMethodValue.
        // Required for borrowed-prototype patterns like
        // `obj.charAt = String.prototype.charAt; obj.charAt(0)`.
        builder.MarkLabel(dynamicDispatchLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        var fnLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, fnLocal);
        // args = new object[arguments.Count]
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        var argsLocal = IL.DeclareLocal(_ctx.Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsLocal);
        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, argsLocal);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, fnLocal);
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
    }
}
