using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Map (Dictionary) method dispatch with runtime type checking for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits a Map method call with runtime type checking.
    /// For Any-typed values that might be Maps (Dictionary&lt;object, object?&gt;).
    /// </summary>
    private void EmitMapMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        var builder = _ctx.ILBuilder;
        var isMapLabel = builder.DefineLabel("map_method_map");
        var fallbackLabel = builder.DefineLabel("map_method_fallback");
        var doneLabel = builder.DefineLabel("map_method_done");

        // Check if it's a Map (Dictionary<object, object?>)
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryObjectObject);
        builder.Emit_Brtrue(isMapLabel);

        // Fall through to dynamic dispatch
        builder.Emit_Br(fallbackLabel);

        // Map path - call the appropriate Map method from RuntimeTypes
        builder.MarkLabel(isMapLabel);

        switch (methodName)
        {
            case "get":
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapGet);
                break;

            case "set":
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSet);
                break;

            case "has":
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapHas);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "delete":
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapDelete);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "clear":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                break;

            case "keys":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapKeys);
                break;

            case "values":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapValues);
                break;

            case "entries":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
                break;

            case "forEach":
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                break;

            default:
                // Should not reach here
                IL.Emit(OpCodes.Ldnull);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
    }
}
