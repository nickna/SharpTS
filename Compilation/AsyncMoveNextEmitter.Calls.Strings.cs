using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    /// <summary>
    /// Emits a string method call.
    /// </summary>
    private void EmitStringMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the string object
        EmitExpression(obj);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "charAt":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringCharAt);
                break;

            case "substring":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSubstring);
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "toUpperCase":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToUpper", Type.EmptyTypes)!);
                break;

            case "toLowerCase":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                break;

            case "trim":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Trim", Type.EmptyTypes)!);
                break;

            case "replace":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringReplace);
                break;

            case "split":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSplit);
                break;

            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "startsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringStartsWith);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "endsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringEndsWith);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSlice);
                break;

            case "repeat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringRepeat);
                break;

            case "padStart":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringPadStart);
                break;

            case "padEnd":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringPadEnd);
                break;

            case "charCodeAt":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringCharCodeAt);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "concat":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringConcat);
                break;

            case "lastIndexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringLastIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "trimStart":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimStart", Type.EmptyTypes)!);
                break;

            case "trimEnd":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimEnd", Type.EmptyTypes)!);
                break;

            case "replaceAll":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringReplaceAll);
                break;

            case "at":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringAt);
                break;
        }

        SetStackUnknown();
    }
}
