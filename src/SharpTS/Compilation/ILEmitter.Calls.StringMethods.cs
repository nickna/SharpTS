using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// String-only method dispatch with runtime type checking for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits a string-only method call with runtime type checking for any/unknown types.
    /// Checks if the receiver is a string at runtime and dispatches accordingly.
    /// For padEnd, padStart, trim, replace, split, etc.
    /// </summary>
    private void EmitStringOnlyMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        var builder = _ctx.ILBuilder;
        var isStringLabel = builder.DefineLabel("string_method_string");
        var fallbackLabel = builder.DefineLabel("string_method_fallback");
        var doneLabel = builder.DefineLabel("string_method_done");

        // Take the string-fast-path if the receiver is either a CLR string OR
        // a `new String(...)` wrapper ($Object with __primitiveType="String").
        // Without the wrapper check, `(new String("ABC")).indexOf("A", 1)`
        // and similar tests fall through to the dynamic GetProperty +
        // InvokeMethodValue path, where the bound prototype method silently
        // ignores the fromIndex argument.
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.String);
        builder.Emit_Brtrue(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, "String");
        IL.Emit(OpCodes.Call, _ctx.Runtime!.BoxedPrimitives.IsOfType);
        builder.Emit_Brtrue(isStringLabel);

        // Fall through to dynamic dispatch (objects with user-defined methods,
        // null/undefined, etc.).
        builder.Emit_Br(fallbackLabel);

        // String path — unwrap the receiver to its primitive string value.
        // UnwrapStringReceiver fast-paths CLR strings and walks $Object
        // wrappers' __primitiveValue. Both cases yield a string for the
        // string-method emitters below.
        builder.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.BoxedPrimitives.UnwrapStringReceiver);

        switch (methodName)
        {
            case "padEnd":
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.PadEnd);
                break;

            case "padStart":
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.PadStart);
                break;

            case "trim":
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.TrimInline);
                break;

            case "trimStart":
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.TrimInline);
                break;

            case "trimEnd":
                IL.Emit(OpCodes.Ldc_I4_2);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.TrimInline);
                break;

            case "toUpperCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToUpper"));
                break;

            case "toLowerCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToLower"));
                break;

            case "replace":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExps.RequireImplementation().StringReplace);
                break;

            case "replaceAll":
                // The string fast-path unwrapped the receiver before this
                // switch. replaceAll must preserve the original boxed value
                // until custom @@replace dispatch has received it.
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExps.RequireImplementation().StringReplaceAll);
                break;

            case "split":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    // ECMA-262 22.1.3.21 step 4: missing separator is
                    // `undefined`, not `""`. Push the $Undefined singleton so
                    // the helper's undefined-arm returns [str].
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExps.RequireImplementation().StringSplitProto);
                break;

            case "match":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExps.RequireImplementation().StringMatch);
                break;

            case "search":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExps.RequireImplementation().StringSearch);
                break;

            case "repeat":
                // StringRepeat takes (string, object); helper does ToNumber
                // internally (throws TypeError on Symbol per ECMA-262).
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.Repeat);
                break;

            case "charCodeAt":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // ECMA-262 ToIntegerOrInfinity starts with ToNumber. Raw
                    // unboxing rejects null, strings, booleans and coercible
                    // objects even though all are valid position arguments.
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.NumericCoercion.ToNumber);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.CharCodeAt);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "at":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.NumericCoercion.ToNumber);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.At);
                break;

            case "lastIndexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCoercion.ToJsString);
                }
                else
                {
                    // ECMA-262 §22.1.3.9: ToString(undefined) = "undefined".
                    IL.Emit(OpCodes.Ldstr, "undefined");
                }
                // Position coercion is observable even though the current
                // search helper still uses the default starting position.
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.NumericCoercion.ToNumber);
                    IL.Emit(OpCodes.Pop);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.LastIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "normalize":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.Normalize);
                break;

            case "localeCompare":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCoercion.ToJsString);
                }
                else
                {
                    // ECMA-262 22.1.3.10: missing arg → undefined → "undefined".
                    IL.Emit(OpCodes.Ldstr, "undefined");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Strings.LocaleCompare);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectRead.Property);

        // Create args array
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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.Invocation.Method);

        builder.MarkLabel(doneLabel);
    }
}
