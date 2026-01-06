using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Basic expression emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitLiteral(Expr.Literal lit)
    {
        switch (lit.Value)
        {
            case double d:
                EmitDoubleConstant(d);
                break;
            case string s:
                EmitStringConstant(s);
                break;
            case bool b:
                EmitBoolConstant(b);
                break;
            case System.Numerics.BigInteger bi:
                // Emit BigInteger by parsing from string at runtime
                IL.Emit(OpCodes.Ldstr, bi.ToString());
                IL.Emit(OpCodes.Call, typeof(System.Numerics.BigInteger).GetMethod("Parse", [typeof(string)])!);
                IL.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));
                SetStackUnknown();
                break;
            case null:
                EmitNullConstant();
                break;
            default:
                EmitNullConstant();
                break;
        }
    }

    private void EmitVariable(Expr.Variable v)
    {
        var name = v.Name.Lexeme;

        // Check if it's a parameter
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            EmitLdargUnknown(argIndex);
            return;
        }

        // Check if it's a local
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(name);
            EmitLdloc(local, localType!);
            return;
        }

        // Check if it's a captured variable (in closure)
        if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue(name, out var field))
        {
            IL.Emit(OpCodes.Ldarg_0); // this (display class instance)
            EmitLdfldUnknown(field);
            return;
        }

        // Check if it's Math
        if (name == "Math")
        {
            EmitNullConstant(); // Math is handled specially in property access
            return;
        }

        // Check if it's a class - load the Type object
        if (_ctx.Classes.TryGetValue(name, out var classType))
        {
            // Load the Type object using typeof(ClassName)
            IL.Emit(OpCodes.Ldtoken, classType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
            SetStackUnknown();
            return;
        }

        // Check if it's a top-level function - wrap as TSFunction
        if (_ctx.Functions.TryGetValue(name, out var methodBuilder))
        {
            // Create TSFunction(null, methodInfo)
            IL.Emit(OpCodes.Ldnull); // target (static method)
            IL.Emit(OpCodes.Ldtoken, methodBuilder);
            IL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod(
                "GetMethodFromHandle",
                [typeof(RuntimeMethodHandle)])!);
            IL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
            EmitNewobjUnknown(_ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Unknown variable - push null
        EmitNullConstant();
    }

    private void EmitAssign(Expr.Assign a)
    {
        EmitExpression(a.Value);

        var local = _ctx.Locals.GetLocal(a.Name.Lexeme);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(a.Name.Lexeme);
            if (localType == typeof(double))
            {
                // Typed local - ensure unboxed double
                EnsureDouble();
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, local);
                SetStackType(StackType.Double);
            }
            else
            {
                // Object local - ensure boxed
                EmitBoxIfNeeded(a.Value);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, local);
                SetStackUnknown();
            }
        }
        else if (_ctx.TryGetParameter(a.Name.Lexeme, out var argIndex))
        {
            // Parameters are always object type
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Starg, argIndex);
            SetStackUnknown();
        }
        else
        {
            // Unknown target - box for safety
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            SetStackUnknown();
        }
    }

    private void EmitThis()
    {
        // If we're inside a capturing arrow function, 'this' is stored in a captured field
        if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue("this", out var thisField))
        {
            // Load 'this' from the display class field
            // arg_0 is the display class instance
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, thisField);
        }
        else if (_ctx.IsInstanceMethod)
        {
            IL.Emit(OpCodes.Ldarg_0);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        SetStackUnknown();
    }

    private void EmitSuper(Expr.Super s)
    {
        // Load this and prepare for base method call
        // Note: super() constructor calls are handled in EmitCall, not here
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        EmitCallUnknown(_ctx.Runtime!.GetSuperMethod);
    }

    private void EmitTernary(Expr.Ternary t)
    {
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        EmitExpression(t.Condition);
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(t.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, typeof(bool));
        }
        else if (t.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else
        {
            // For other expressions, apply truthy check
            EnsureBoxed(); // Ensure it's boxed first
            EmitTruthyCheck();
        }
        IL.Emit(OpCodes.Brfalse, elseLabel);

        EmitExpression(t.ThenBranch);
        EmitBoxIfNeeded(t.ThenBranch);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EmitBoxIfNeeded(t.ElseBranch);

        IL.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var endLabel = IL.DefineLabel();

        EmitExpression(nc.Left);
        EmitBoxIfNeeded(nc.Left);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brtrue, endLabel);

        IL.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EmitBoxIfNeeded(nc.Right);

        IL.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    private void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // Build array of parts
        var totalParts = tl.Strings.Count + tl.Expressions.Count;
        IL.Emit(OpCodes.Ldc_I4, totalParts);
        IL.Emit(OpCodes.Newarr, typeof(object));

        int partIndex = 0;
        for (int i = 0; i < tl.Strings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, partIndex++);
            IL.Emit(OpCodes.Ldstr, tl.Strings[i]);
            IL.Emit(OpCodes.Stelem_Ref);

            if (i < tl.Expressions.Count)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, partIndex++);
                EmitExpression(tl.Expressions[i]);
                EmitBoxIfNeeded(tl.Expressions[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }

        EmitCallString(_ctx.Runtime!.ConcatTemplate);
    }

    private void EmitRegexLiteral(Expr.RegexLiteral re)
    {
        IL.Emit(OpCodes.Ldstr, re.Pattern);
        IL.Emit(OpCodes.Ldstr, re.Flags);
        EmitCallUnknown(_ctx.Runtime!.CreateRegExpWithFlags);
    }
}
