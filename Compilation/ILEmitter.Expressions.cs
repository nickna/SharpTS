using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Basic expression emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitLiteral(Expr.Literal lit)
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
                if (bi >= long.MinValue && bi <= long.MaxValue)
                {
                    // Optimization: Use BigInteger(long) constructor for small values
                    IL.Emit(OpCodes.Ldc_I8, (long)bi);
                    IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.BigInteger, _ctx.Types.Int64));
                }
                else
                {
                    // Fallback: Parse from string for large values
                    IL.Emit(OpCodes.Ldstr, bi.ToString());
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.BigInteger, "Parse", _ctx.Types.String));
                }
                IL.Emit(OpCodes.Box, _ctx.Types.BigInteger);
                SetStackUnknown();
                break;
            case Runtime.Types.SharpTSUndefined:
                EmitUndefinedConstant();
                break;
            case null:
                EmitNullConstant();
                break;
            default:
                EmitNullConstant();
                break;
        }
    }

    protected override void EmitVariable(Expr.Variable v)
    {
        var name = v.Name.Lexeme;

        // Try resolver first (user-defined variables: parameters, locals, captured)
        var stackType = _resolver.TryLoadVariable(name);
        if (stackType.HasValue)
        {
            SetStackType(stackType.Value);
            return;
        }

        // Fallback: pseudo-variables (Math, process, classes, functions, namespaces)
        if (name == "Math")
        {
            EmitNullConstant(); // Math is handled specially in property access
            return;
        }

        if (name == "process")
        {
            EmitNullConstant(); // process is handled specially in property access
            return;
        }

        // Check if it's a class - load the Type object
        if (_ctx.Classes.TryGetValue(_ctx.ResolveClassName(name), out var classType))
        {
            IL.Emit(OpCodes.Ldtoken, classType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            SetStackUnknown();
            return;
        }

        // Check if it's a top-level function - wrap as TSFunction
        if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var methodBuilder))
        {
            IL.Emit(OpCodes.Ldnull); // target (static method)
            IL.Emit(OpCodes.Ldtoken, methodBuilder);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            EmitNewobjUnknown(_ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Check if it's a namespace - load the static field
        if (_ctx.NamespaceFields?.TryGetValue(name, out var nsField) == true)
        {
            IL.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        // Unknown variable - push null
        EmitNullConstant();
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        EmitExpression(a.Value);

        var local = _ctx.Locals.GetLocal(a.Name.Lexeme);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(a.Name.Lexeme);
            if (localType != null && _ctx.Types.IsDouble(localType))
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
        else if (_ctx.CapturedFields?.TryGetValue(a.Name.Lexeme, out var field) == true)
        {
            // Captured field in display class (closure)
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);  // Load display class instance
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, field);
            SetStackUnknown();
        }
        else if (_ctx.TopLevelStaticVars?.TryGetValue(a.Name.Lexeme, out var topLevelField) == true)
        {
            // Top-level static variable
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stsfld, topLevelField);
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

    protected override void EmitThis()
    {
        _resolver.LoadThis();
        SetStackUnknown();
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Load this and prepare for base method call
        // Note: super() constructor calls are handled in EmitCall, not here
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        EmitCallUnknown(_ctx.Runtime!.GetSuperMethod);
    }

    protected override void EmitTernary(Expr.Ternary t)
    {
        var builder = _ctx.ILBuilder;
        var elseLabel = builder.DefineLabel("ternary_else");
        var endLabel = builder.DefineLabel("ternary_end");

        EmitExpression(t.Condition);
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(t.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
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
        builder.Emit_Brfalse(elseLabel);

        EmitExpression(t.ThenBranch);
        EmitBoxIfNeeded(t.ThenBranch);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EmitBoxIfNeeded(t.ElseBranch);

        builder.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    protected override void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("nullish_end");
        var useRightLabel = builder.DefineLabel("nullish_use_right");

        EmitExpression(nc.Left);
        EmitBoxIfNeeded(nc.Left);
        IL.Emit(OpCodes.Dup);

        // If left is null, use right
        builder.Emit_Brfalse(useRightLabel);

        // If left is undefined, use right
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
        builder.Emit_Brtrue(useRightLabel);

        // Left is neither null nor undefined - use it
        builder.Emit_Br(endLabel);

        builder.MarkLabel(useRightLabel);
        IL.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EmitBoxIfNeeded(nc.Right);

        builder.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // Build array of parts
        var totalParts = tl.Strings.Count + tl.Expressions.Count;
        IL.Emit(OpCodes.Ldc_I4, totalParts);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

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

    protected override void EmitRegexLiteral(Expr.RegexLiteral re)
    {
        IL.Emit(OpCodes.Ldstr, re.Pattern);
        IL.Emit(OpCodes.Ldstr, re.Flags);
        EmitCallUnknown(_ctx.Runtime!.CreateRegExpWithFlags);
    }

    protected override void EmitUnknownExpression(Expr expr)
    {
        // Fallback: push null
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitClassExpression(Expr.ClassExpr ce)
    {
        // Class expressions evaluate to the Type object at runtime.
        // The type has been pre-defined during collection phase.
        if (_ctx.ClassExprBuilders != null && _ctx.ClassExprBuilders.TryGetValue(ce, out var typeBuilder))
        {
            // Load the Type object using ldtoken + GetTypeFromHandle
            IL.Emit(OpCodes.Ldtoken, typeBuilder);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            SetStackUnknown();
        }
        else
        {
            // Fallback: push null (should not happen if collection worked)
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
    }
}
