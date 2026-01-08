using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// String method call emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitStringMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the string object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);
        IL.Emit(OpCodes.Castclass, _ctx.Types.String);

        switch (methodName)
        {
            case "charAt":
                // str.charAt(index) -> str[index].ToString() or "" if out of range
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
                return;

            case "substring":
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
                return;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            case "toUpperCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToUpper"));
                return;

            case "toLowerCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToLower"));
                return;

            case "trim":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "Trim"));
                return;

            case "replace":
                // str.replace(searchValue, replacement) - searchValue can be string or RegExp
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // Don't cast - let runtime handle string or RegExp
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // replacement is always string
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceRegExp);
                return;

            case "split":
                // str.split(separator) - separator can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // Don't cast - let runtime handle string or RegExp
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSplitRegExp);
                return;

            case "match":
                // str.match(pattern) - pattern can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringMatchRegExp);
                return;

            case "search":
                // str.search(pattern) - pattern can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSearchRegExp);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "startsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringStartsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "endsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringEndsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "slice":
                // str.slice(start, end?) - with negative index support
                // StringSlice(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSlice);
                return;

            case "repeat":
                // str.repeat(count)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringRepeat);
                return;

            case "padStart":
                // str.padStart(targetLength, padString?)
                // StringPadStart(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadStart);
                return;

            case "padEnd":
                // str.padEnd(targetLength, padString?)
                // StringPadEnd(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadEnd);
                return;

            case "charCodeAt":
                // str.charCodeAt(index) -> number
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharCodeAt);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

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
                return;

            case "lastIndexOf":
                // str.lastIndexOf(search) -> number
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringLastIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            case "trimStart":
                // str.trimStart() -> string
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "TrimStart"));
                return;

            case "trimEnd":
                // str.trimEnd() -> string
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "TrimEnd"));
                return;

            case "replaceAll":
                // str.replaceAll(search, replacement) -> string
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceAll);
                return;

            case "at":
                // str.at(index) -> string | null
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringAt);
                return;
        }
    }
}
