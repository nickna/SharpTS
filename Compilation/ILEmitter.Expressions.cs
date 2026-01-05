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
                IL.Emit(OpCodes.Ldc_R8, d);
                _stackType = StackType.Double;
                break;
            case string s:
                IL.Emit(OpCodes.Ldstr, s);
                _stackType = StackType.String;
                break;
            case bool b:
                IL.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _stackType = StackType.Boolean;
                break;
            case System.Numerics.BigInteger bi:
                // Emit BigInteger by parsing from string at runtime
                IL.Emit(OpCodes.Ldstr, bi.ToString());
                IL.Emit(OpCodes.Call, typeof(System.Numerics.BigInteger).GetMethod("Parse", [typeof(string)])!);
                IL.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));
                _stackType = StackType.Unknown;
                break;
            case null:
                IL.Emit(OpCodes.Ldnull);
                _stackType = StackType.Null;
                break;
            default:
                IL.Emit(OpCodes.Ldnull);
                _stackType = StackType.Null;
                break;
        }
    }

    private void EmitVariable(Expr.Variable v)
    {
        var name = v.Name.Lexeme;

        // Check if it's a parameter
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            IL.Emit(OpCodes.Ldarg, argIndex);
            // Parameters are currently always object type
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a local
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            IL.Emit(OpCodes.Ldloc, local);
            // Track stack type based on local's actual CLR type
            var localType = _ctx.Locals.GetLocalType(name);
            _stackType = localType == typeof(double) ? StackType.Double
                       : localType == typeof(bool) ? StackType.Boolean
                       : StackType.Unknown;
            return;
        }

        // Check if it's a captured variable (in closure)
        if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue(name, out var field))
        {
            IL.Emit(OpCodes.Ldarg_0); // this (display class instance)
            IL.Emit(OpCodes.Ldfld, field);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's Math
        if (name == "Math")
        {
            IL.Emit(OpCodes.Ldnull); // Math is handled specially in property access
            _stackType = StackType.Null;
            return;
        }

        // Check if it's a class - load the Type object
        if (_ctx.Classes.TryGetValue(name, out var classType))
        {
            // Load the Type object using typeof(ClassName)
            IL.Emit(OpCodes.Ldtoken, classType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
            _stackType = StackType.Unknown;
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
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Unknown variable - push null
        IL.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
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
                _stackType = StackType.Double;
            }
            else
            {
                // Object local - ensure boxed
                EmitBoxIfNeeded(a.Value);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, local);
                _stackType = StackType.Unknown;
            }
        }
        else if (_ctx.TryGetParameter(a.Name.Lexeme, out var argIndex))
        {
            // Parameters are always object type
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Starg, argIndex);
            _stackType = StackType.Unknown;
        }
        else
        {
            // Unknown target - box for safety
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
    }

    private void EmitSuper(Expr.Super s)
    {
        // Load this and prepare for base method call
        // Note: super() constructor calls are handled in EmitCall, not here
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetSuperMethod);
        _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.ConcatTemplate);
        // Template literal produces a string
        _stackType = StackType.String;
    }
}
