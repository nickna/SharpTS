using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    #region Category-Based Built-in Type Property Access

    /// <summary>
    /// Attempts to emit IL for built-in type property access using TypeCategoryResolver.
    /// Returns true if the property was handled, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitBuiltInTypePropertyGet(Expr.Get g, TypeInfo objType)
    {
        var category = TypeCategoryResolver.Classify(objType);
        string propName = g.Name.Lexeme;

        return category switch
        {
            TypeCategory.Map => TryEmitMapPropertyGet(g, propName),
            TypeCategory.Set => TryEmitSetPropertyGet(g, propName),
            TypeCategory.RegExp => TryEmitRegExpPropertyGet(g, propName),
            TypeCategory.Error => TryEmitErrorPropertyGet(g, propName),
            TypeCategory.Timeout => TryEmitTimeoutPropertyGet(g, propName),
            TypeCategory.Buffer => TryEmitBufferPropertyGet(g, propName),
            TypeCategory.Symbol => TryEmitSymbolPropertyGet(g, propName),
            TypeCategory.Function => TryEmitFunctionPropertyGet(g, propName),
            _ => false
        };
    }

    private bool TryEmitMapPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "size") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSize);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitSetPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "size") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetSize);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitRegExpPropertyGet(Expr.Get g, string propName)
    {
        switch (propName)
        {
            case "source":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetSource);
                SetStackType(StackType.String);
                return true;
            case "flags":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetFlags);
                SetStackType(StackType.String);
                return true;
            case "global":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetGlobal);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "ignoreCase":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetIgnoreCase);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "multiline":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetMultiline);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "sticky":
            case "unicode":
            case "dotAll":
            case "hasIndices":
            case "unicodeSets":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, propName switch
                {
                    "sticky" => _ctx.Runtime!.RegExpGetSticky,
                    "unicode" => _ctx.Runtime!.RegExpGetUnicode,
                    "dotAll" => _ctx.Runtime!.RegExpGetDotAll,
                    "hasIndices" => _ctx.Runtime!.RegExpGetHasIndices,
                    _ => _ctx.Runtime!.RegExpGetUnicodeSets,
                });
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "lastIndex":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Ldstr, propName);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
                SetStackUnknown();
                return true;
            default:
                return false;
        }
    }

    private bool TryEmitErrorPropertyGet(Expr.Get g, string propName)
    {
        switch (propName)
        {
            case "name":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetName);
                SetStackType(StackType.String);
                return true;
            case "message":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetMessage);
                SetStackType(StackType.String);
                return true;
            case "stack":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetStack);
                SetStackType(StackType.String);
                return true;
            case "cause":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetCause);
                SetStackUnknown();
                return true;
            case "errors":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.AggregateErrorGetErrors);
                SetStackUnknown();
                return true;
            default:
                return false;
        }
    }

    private bool TryEmitTimeoutPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "hasRef") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSTimeoutType);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSTimeoutHasRefGetter);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitBufferPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "length") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSBufferType);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.TSBufferLengthGetter);
        IL.Emit(OpCodes.Conv_R8);  // Convert to double for TypeScript number
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitSymbolPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "description") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSSymbolType);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SymbolDescriptionGetter);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitFunctionPropertyGet(Expr.Get g, string propName)
    {
        switch (propName)
        {
            case "length":
                // Try to get function arity at compile time for known function references
                if (TryGetFunctionArity(g.Object, out int arity))
                {
                    // Emit the arity directly as a constant - more efficient than runtime lookup
                    IL.Emit(OpCodes.Ldc_R8, (double)arity);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return true;
                }
                // Fall back to runtime dispatch for unknown function references
                return false;

            case "name":
                // Try to get function name at compile time for known function references
                if (TryGetFunctionName(g.Object, out string? funcName) && funcName != null)
                {
                    // Check if this is a bound function (we'd need to track that separately)
                    // For now, just emit the name directly
                    IL.Emit(OpCodes.Ldstr, funcName);
                    SetStackType(StackType.String);
                    return true;
                }
                // Fall back to runtime dispatch for unknown function references
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Tries to get the arity (number of required parameters) of a known function at compile time.
    /// </summary>
    private bool TryGetFunctionArity(Expr funcExpr, out int arity)
    {
        arity = 0;

        // Handle direct function variable references
        if (funcExpr is Expr.Variable v)
        {
            string resolvedName = _ctx.ResolveFunctionName(v.Name.Lexeme);
            if (_ctx.Functions.TryGetValue(resolvedName, out var methodBuilder))
            {
                // Count parameters excluding optional, rest, and internal (__this, etc.)
                var parameters = methodBuilder.GetParameters();
                foreach (var param in parameters)
                {
                    // Skip optional parameters
                    if (param.IsOptional)
                        continue;
                    // Skip rest parameters (List<object>)
                    if (param.ParameterType == typeof(List<object>))
                        continue;
                    // Skip internal parameters starting with "__"
                    if (param.Name?.StartsWith("__") == true)
                        continue;
                    arity++;
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to get the name of a known function at compile time.
    /// </summary>
    private bool TryGetFunctionName(Expr funcExpr, out string? funcName)
    {
        funcName = null;

        // Handle direct function variable references
        if (funcExpr is Expr.Variable v)
        {
            string resolvedName = _ctx.ResolveFunctionName(v.Name.Lexeme);
            if (_ctx.Functions.ContainsKey(resolvedName))
            {
                // Use the original variable name (not the resolved/mangled name)
                funcName = v.Name.Lexeme;
                return true;
            }
        }

        return false;
    }

    #endregion
}
