using System.IO;
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

        if (name == "globalThis")
        {
            EmitNullConstant(); // globalThis is handled specially in property access
            return;
        }

        // JavaScript global constants
        if (name == "NaN")
        {
            EmitDoubleConstant(double.NaN);
            return;
        }

        if (name == "Infinity")
        {
            EmitDoubleConstant(double.PositiveInfinity);
            return;
        }

        if (name == "undefined")
        {
            EmitUndefinedConstant();
            return;
        }

        // Global fetch function - wrap as TSFunction
        if (name == "fetch")
        {
            IL.Emit(OpCodes.Ldnull); // target (static method)
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.Fetch);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            EmitNewobjUnknown(_ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Check for Node.js module globals (__dirname, __filename)
        if (name == "__filename")
        {
            IL.Emit(OpCodes.Ldstr, _ctx.CurrentModulePath ?? "");
            SetStackUnknown();
            return;
        }

        if (name == "__dirname")
        {
            string dirname = string.IsNullOrEmpty(_ctx.CurrentModulePath)
                ? ""
                : Path.GetDirectoryName(_ctx.CurrentModulePath) ?? "";
            IL.Emit(OpCodes.Ldstr, dirname);
            SetStackUnknown();
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
            // Use two-parameter GetMethodFromHandle with declaring type for proper token resolution in persisted assemblies
            if (_ctx.ProgramType != null)
            {
                IL.Emit(OpCodes.Ldtoken, _ctx.ProgramType);
                IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle, _ctx.Types.RuntimeTypeHandle));
            }
            else
            {
                IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
            }
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

            // Use constructor with cached name/length to avoid reflection issues with MethodBuilder tokens
            // Compute function arity at compile time
            int arity = 0;
            foreach (var param in methodBuilder.GetParameters())
            {
                if (param.IsOptional) continue;
                if (param.ParameterType == typeof(List<object>)) continue;
                if (param.Name?.StartsWith("__") == true) continue;
                arity++;
            }
            IL.Emit(OpCodes.Ldstr, name);  // function name
            IL.Emit(OpCodes.Ldc_I4, arity);  // function length
            EmitNewobjUnknown(_ctx.Runtime!.TSFunctionCtorWithCache);
            return;
        }

        // Check if it's a namespace - load the static field
        if (_ctx.NamespaceFields?.TryGetValue(name, out var nsField) == true)
        {
            IL.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        // Check if it's a top-level variable - load from static field
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            IL.Emit(OpCodes.Ldsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Unknown variable - throw ReferenceError at runtime
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ThrowUndefinedVariable);
        // Emit unreachable null to satisfy IL verification (method never returns but stack must balance)
        EmitNullConstant();
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        EmitExpression(a.Value);

        // 1. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage
        if (_ctx.CapturedFunctionLocals?.Contains(a.Name.Lexeme) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(a.Name.Lexeme, out var funcDCField) == true)
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);

            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body
                IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // Access from arrow body - go through $functionDC field
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
            }
            else
            {
                // Fallback - just discard the temp and leave value on stack
                IL.Emit(OpCodes.Pop);
                SetStackUnknown();
                return;
            }

            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, funcDCField);
            SetStackUnknown();
            return;
        }

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
        else if (_ctx.CapturedTopLevelVars?.Contains(a.Name.Lexeme) == true &&
                 _ctx.EntryPointDisplayClassFields?.TryGetValue(a.Name.Lexeme, out var entryPointField) == true)
        {
            // Captured top-level variable in entry-point display class
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);

            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point
                IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            }
            else
            {
                // Fallback - just discard the temp and leave value on stack
                IL.Emit(OpCodes.Pop);
                SetStackUnknown();
                return;
            }

            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, entryPointField);
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
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brfalse
            EnsureBoxed();
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

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // Check for String.raw special case
        if (IsStringRawTag(ttl.Tag))
        {
            EmitStringRawTaggedTemplate(ttl);
            return;
        }

        // 1. Emit the tag function reference
        EmitExpression(ttl.Tag);
        EmitBoxIfNeeded(ttl.Tag);

        // 2. Create cooked strings array (object?[] to allow null for invalid escapes)
        IL.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
            {
                IL.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull); // null for invalid escape sequences
            }
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 3. Create raw strings array
        IL.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.String);
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 4. Create expressions array
        IL.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(ttl.Expressions[i]);
            EmitBoxIfNeeded(ttl.Expressions[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 5. Call runtime helper: InvokeTaggedTemplate(tag, cooked, raw, exprs)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    /// <summary>
    /// Checks if the tag expression is String.raw.
    /// </summary>
    private static bool IsStringRawTag(Expr tag)
    {
        return tag is Expr.Get get
            && get.Name.Lexeme == "raw"
            && get.Object is Expr.Variable v
            && v.Name.Lexeme == "String";
    }

    /// <summary>
    /// Emits optimized code for String.raw tagged template literals.
    /// Directly calls RuntimeTypes.StringRaw instead of going through InvokeTaggedTemplate.
    /// </summary>
    private void EmitStringRawTaggedTemplate(Expr.TaggedTemplateLiteral ttl)
    {
        // 1. Create raw strings array
        IL.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.String);
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 2. Create expressions array
        IL.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(ttl.Expressions[i]);
            EmitBoxIfNeeded(ttl.Expressions[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 3. Call RuntimeTypes.StringRaw(rawStrings, expressions)
        var stringRawMethod = typeof(RuntimeTypes).GetMethod("StringRaw", [typeof(string[]), typeof(object?[])])!;
        IL.Emit(OpCodes.Call, stringRawMethod);
        SetStackType(StackType.String);
    }

    protected override void EmitRegexLiteral(Expr.RegexLiteral re)
    {
        IL.Emit(OpCodes.Ldstr, re.Pattern);
        IL.Emit(OpCodes.Ldstr, re.Flags);
        EmitCallUnknown(_ctx.Runtime!.CreateRegExpWithFlags);
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

    protected override void EmitDelete(Expr.Delete del)
    {
        // delete operator: returns boolean
        // - delete obj.prop: removes property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete obj[key]: removes computed property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete variable: throws SyntaxError in strict mode, returns false in sloppy mode
        switch (del.Operand)
        {
            case Expr.Get get:
                // delete obj.prop - use static runtime helper with strict mode
                EmitExpression(get.Object);
                EmitBoxIfNeeded(get.Object);
                IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
                if (_ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    EmitCallUnknown(_ctx.Runtime!.DeletePropertyStrict);
                }
                else
                {
                    EmitCallUnknown(_ctx.Runtime!.DeleteProperty);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.GetIndex getIndex:
                // delete obj[key] - use DeleteIndex with strict mode
                EmitExpression(getIndex.Object);
                EmitBoxIfNeeded(getIndex.Object);
                EmitExpression(getIndex.Index);
                EmitBoxIfNeeded(getIndex.Index);
                if (_ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    EmitCallUnknown(_ctx.Runtime!.DeleteIndexStrict);
                }
                else
                {
                    EmitCallUnknown(_ctx.Runtime!.DeleteIndex);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.Variable v:
                if (_ctx.IsStrictMode)
                {
                    // Strict mode: throw SyntaxError
                    IL.Emit(OpCodes.Ldstr, $"Delete of unqualified identifier '{v.Name.Lexeme}' in strict mode");
                    EmitCallUnknown(_ctx.Runtime!.ThrowStrictSyntaxError);
                    // ThrowStrictSyntaxError throws, but we need a value on stack for IL verification
                    EmitBoolConstant(false);
                }
                else
                {
                    // Sloppy mode: warn and return false
                    IL.Emit(OpCodes.Ldstr, v.Name.Lexeme);
                    EmitCallUnknown(_ctx.Runtime!.WarnSloppyDeleteVariable);
                }
                SetStackType(StackType.Boolean);
                break;

            default:
                // delete on other expressions: returns true but does nothing
                // Still need to evaluate for side effects
                EmitExpression(del.Operand);
                IL.Emit(OpCodes.Pop);
                EmitBoolConstant(true);
                break;
        }
    }
}
