using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    protected override void EmitLiteral(Expr.Literal lit)
    {
        if (lit.Value == null)
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
        else if (lit.Value is double d)
        {
            _il.Emit(OpCodes.Ldc_R8, d);
            _il.Emit(OpCodes.Box, typeof(double));
            SetStackUnknown();
        }
        else if (lit.Value is string s)
        {
            _il.Emit(OpCodes.Ldstr, s);
            SetStackType(StackType.String);
        }
        else if (lit.Value is bool b)
        {
            _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Box, typeof(bool));
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    protected override void EmitGet(Expr.Get g)
    {
        // Special case: Symbol well-known symbols
        if (g.Object is Expr.Variable symV && symV.Name.Lexeme == "Symbol")
        {
            switch (g.Name.Lexeme)
            {
                case "iterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
                    SetStackUnknown();
                    return;
                case "asyncIterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
                    SetStackUnknown();
                    return;
                case "toStringTag":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToStringTag);
                    SetStackUnknown();
                    return;
                case "hasInstance":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolHasInstance);
                    SetStackUnknown();
                    return;
                case "isConcatSpreadable":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIsConcatSpreadable);
                    SetStackUnknown();
                    return;
                case "toPrimitive":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToPrimitive);
                    SetStackUnknown();
                    return;
                case "species":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolSpecies);
                    SetStackUnknown();
                    return;
                case "unscopables":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolUnscopables);
                    SetStackUnknown();
                    return;
            }
        }

        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    protected override void EmitThis()
    {
        // Load 'this' from outer state machine if captured
        if (_builder.Captures.Contains("this"))
        {
            // Get outer state machine's ThisField
            if (_ctx?.AsyncArrowOuterBuilders?.TryGetValue(_builder.Arrow, out var outerBuilder) == true &&
                outerBuilder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, outerBuilder.ThisField);
                SetStackUnknown();
                return;
            }
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Emit the path expression
        EmitExpression(di.PathExpression);
        EnsureBoxed();

        // Convert to string
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", [typeof(object)])!);

        // Push current module path (or empty string if not in module context)
        _il.Emit(OpCodes.Ldstr, _ctx?.CurrentModulePath ?? "");

        // Call DynamicImportModule(path, currentModulePath) -> Task<object?>
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.DynamicImportModule);

        // Wrap Task<object?> in SharpTSPromise
        _il.Emit(OpCodes.Call, _ctx.Runtime.WrapTaskAsPromise);

        SetStackUnknown();
    }

    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string path = _ctx?.CurrentModulePath ?? "";
        string url = path;
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }
        string dirname = string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path) ?? "";

        // Create Dictionary<string, object> and add properties
        _il.Emit(OpCodes.Newobj, Types.DictionaryStringObjectCtor);

        // Add "url" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Add "filename" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "filename");
        _il.Emit(OpCodes.Ldstr, path);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Add "dirname" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "dirname");
        _il.Emit(OpCodes.Ldstr, dirname);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        // Emit the new value first (may contain await which clears stack)
        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Now load current value (after await if any)
        LoadVariable(name);
        EnsureBoxed();

        // Load the value back
        _il.Emit(OpCodes.Ldloc, valueTemp);

        // Apply operation
        EmitCompoundOperation(ca.Operator.Type);

        // Store result
        _il.Emit(OpCodes.Dup);
        StoreVariable(name);

        SetStackUnknown();
    }

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        string name = la.Name.Lexeme;
        var endLabel = _il.DefineLabel();

        // Load current value
        LoadVariable(name);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        switch (la.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, endLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, endLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and keep current
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, endLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        EmitExpression(la.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        StoreVariable(name);

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSet(Expr.LogicalSet ls)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(ls.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        _il.Emit(OpCodes.Dup);

        switch (ls.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        EmitExpression(ls.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(lsi.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        EmitExpression(lsi.Index);
        EnsureBoxed();
        var indexLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        _il.Emit(OpCodes.Dup);

        switch (lsi.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        EmitExpression(lsi.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitCompoundOperation(TokenType opType)
    {
        if (opType == TokenType.PLUS_EQUAL)
        {
            _helpers.EmitCallUnknown(_ctx!.Runtime!.Add);
            return;
        }

        switch (opType)
        {
            case TokenType.MINUS_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Rem);
                break;
            default:
                _helpers.EmitArithmeticBinary(OpCodes.Add);
                break;
        }
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load, increment, store, return new value
            LoadVariable(name);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            _il.Emit(OpCodes.Dup);
            StoreVariable(name);
        }
        SetStackUnknown();
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load original value
            LoadVariable(name);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            // Increment and store
            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            StoreVariable(name);

            // Original value is on stack
        }
        SetStackUnknown();
    }

    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(a.Elements[i]);
                EnsureBoxed();
                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    EmitExpression(spread.Expression);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_I4, 1);
                    _il.Emit(OpCodes.Newarr, typeof(object));
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4_0);
                    EmitExpression(a.Elements[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
                }

                _il.Emit(OpCodes.Stelem_Ref);
            }

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
            _il.Emit(OpCodes.Ldtoken, _ctx!.Runtime!.RuntimeType);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread or computed key
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);

        if (!hasSpreads && !hasComputedKeys)
        {
            // Simple case: no spreads, no computed keys
            _il.Emit(OpCodes.Newobj, Types.DictionaryStringObjectCtor);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys, use SetIndex
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    EmitExpression(ck.Expression);
                    EnsureBoxed();
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
                }
                else
                {
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        SetStackUnknown();
    }

    private void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                _il.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                _il.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                _il.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new CompileException($"Unexpected static property key type: {key.GetType().Name}");
        }
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        SetStackUnknown();
    }

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        EmitExpression(si.Object);
        EnsureBoxed();
        EmitExpression(si.Index);
        EnsureBoxed();
        EmitExpression(si.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        SetStackUnknown();
    }

    /// <summary>
    /// Extracts a qualified class name from a callee expression.
    /// </summary>
    private static (List<string> namespaceParts, string className) ExtractQualifiedNameFromCallee(Expr callee)
    {
        List<string> parts = [];
        CollectGetChainParts(callee, parts);

        if (parts.Count == 0)
            return ([], "");

        var namespaceParts = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : [];
        var className = parts[^1];
        return (namespaceParts, className);
    }

    private static void CollectGetChainParts(Expr expr, List<string> parts)
    {
        switch (expr)
        {
            case Expr.Variable v:
                parts.Add(v.Name.Lexeme);
                break;
            case Expr.Get g:
                CollectGetChainParts(g.Object, parts);
                parts.Add(g.Name.Lexeme);
                break;
        }
    }

    protected override void EmitNew(Expr.New n)
    {
        // Extract qualified name from callee expression
        var (namespaceParts, className) = ExtractQualifiedNameFromCallee(n.Callee);

        // Resolve class name
        string resolvedClassName;
        if (namespaceParts.Count > 0)
        {
            string nsPath = string.Join("_", namespaceParts);
            resolvedClassName = $"{nsPath}_{className}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(className);
        }

        var ctorBuilder = _ctx!.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassRegistry!.GetGenericParams(resolvedClassName) != null)
            {
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();
                targetType = typeBuilder.MakeGenericType(typeArgs);
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            int expectedParamCount = ctorBuilder.GetParameters().Length;

            // Emit arguments
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
            }

            // Pad missing arguments with null
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Newobj, targetCtor);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    private Type ResolveTypeArg(string typeArg)
    {
        return typeArg switch
        {
            "number" => typeof(object),
            "string" => typeof(object),
            "boolean" => typeof(object),
            "any" => typeof(object),
            _ => typeof(object)
        };
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // TemplateLiteral has Strings (literal parts) and Expressions (interpolated parts)
        // Structure: strings[0] + expressions[0] + strings[1] + expressions[1] + ... + strings[n]
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        // In async context, expressions may contain await which clears the stack.
        // Phase 1: Evaluate all expressions to temps first (awaits happen here)
        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            exprTemps.Add(temp);
        }

        // Phase 2: Build string from temps (no awaits, stack safe)
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        for (int i = 0; i < exprTemps.Count; i++)
        {
            // Load expression value from temp and convert to string
            _il.Emit(OpCodes.Ldloc, exprTemps[i]);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, Types.StringConcat2);

            // Emit next string part
            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, Types.StringConcat2);
            }
        }
        SetStackType(StackType.String);
    }

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // In async context, expressions may contain await which clears the stack.
        // Phase 1: Evaluate tag and all expressions to temps first (awaits happen here)
        EmitExpression(ttl.Tag);
        EnsureBoxed();
        var tagTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, tagTemp);

        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            EmitExpression(ttl.Expressions[i]);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            exprTemps.Add(temp);
        }

        // Phase 2: Build arrays and make call (no awaits, stack safe)
        // Load tag function
        _il.Emit(OpCodes.Ldloc, tagTemp);

        // Create cooked strings array
        _il.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
                _il.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            else
                _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // Create raw strings array
        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // Create expressions array from temps
        _il.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < exprTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, exprTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // Call runtime helper
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    protected override void EmitSet(Expr.Set s)
    {
        // Handle static field assignment
        if (s.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.ClassRegistry!.TryGetStaticField(resolvedClassName, s.Name.Lexeme, out var staticField))
            {
                EmitExpression(s.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property assignment
        EmitExpression(s.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EnsureBoxed();

        _il.Emit(OpCodes.Dup);
        var resultTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultTemp);

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);
        _il.Emit(OpCodes.Ldloc, resultTemp);
        SetStackUnknown();
    }

    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (gp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.GetStaticPrivateField);
            SetStackUnknown();
            return;
        }

        // Instance private field access - use runtime helper
        // Stack: GetPrivateField(instance, declaringClass, fieldName)
        EmitExpression(gp.Object);
        EnsureBoxed();
        EmitDeclaringClassTypeOrGetFromObject();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetPrivateField);
        SetStackUnknown();
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (sp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitExpression(sp.Value);
            EnsureBoxed();
            var valueTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, valueTemp);

            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Ldloc, valueTemp);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.SetStaticPrivateField);

            // Leave value on stack for expression result
            _il.Emit(OpCodes.Ldloc, valueTemp);
            SetStackUnknown();
            return;
        }

        // Instance private field assignment - use runtime helper
        // Emit value first (may contain await which clears stack)
        EmitExpression(sp.Value);
        EnsureBoxed();
        var valueLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueLocal);

        // SetPrivateField(instance, declaringClass, fieldName, value)
        EmitExpression(sp.Object);
        EnsureBoxed();
        EmitDeclaringClassTypeOrGetFromObject();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Ldloc, valueLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetPrivateField);

        // Leave value on stack for expression result
        _il.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        // Check if this is static private method call (ClassName.#method())
        if (cp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async state machines, we can't directly call private static methods
            // from another class, so always use the runtime helper for static private methods
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, methodName);
            EmitArgumentArray(cp.Arguments);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.CallStaticPrivateMethod);
            SetStackUnknown();
            return;
        }

        // Instance private method call - use runtime helper
        // Emit arguments first (may contain await which clears stack)
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in cp.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // CallPrivateMethod(instance, declaringClass, methodName, args[])
        EmitExpression(cp.Object);
        EnsureBoxed();
        EmitDeclaringClassTypeOrGetFromObject();
        _il.Emit(OpCodes.Ldstr, methodName);

        // Build args array from temps
        _il.Emit(OpCodes.Ldc_I4, argTemps.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CallPrivateMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits the typeof() for the declaring class containing the private member.
    /// Used for static private member access where we know the class at compile time.
    /// </summary>
    private void EmitDeclaringClassType()
    {
        if (_ctx?.CurrentClassBuilder != null)
        {
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        }
        else
        {
            // Should not happen if called correctly - throw at runtime
            _il.Emit(OpCodes.Ldstr, "Cannot access private members outside of class context");
            _il.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            _il.Emit(OpCodes.Throw);
        }
    }

    /// <summary>
    /// Emits the declaring class type for instance private member access.
    /// When CurrentClassBuilder is available, uses typeof(). Otherwise, calls GetType()
    /// on the instance that's already on the stack (duplicates it first).
    /// Stack before: [instance]
    /// Stack after: [instance, Type]
    /// </summary>
    private void EmitDeclaringClassTypeOrGetFromObject()
    {
        if (_ctx?.CurrentClassBuilder != null)
        {
            // Known class at compile time - use typeof
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        }
        else
        {
            // Unknown class at compile time (async arrow in regular method)
            // Get the type from the instance that's on the stack
            // Stack: [instance] -> [instance, instance] -> [instance, Type]
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        }
    }

    /// <summary>
    /// Emits an object[] array from argument expressions.
    /// </summary>
    private void EmitArgumentArray(List<Expr> arguments)
    {
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
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Handle static field compound assignment
        if (cs.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.ClassRegistry!.TryGetStaticField(resolvedClassName, cs.Name.Lexeme, out var staticField))
            {
                EmitExpression(cs.Value);
                EnsureBoxed();
                var valueTemp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, valueTemp);

                _il.Emit(OpCodes.Ldsfld, staticField!);
                _il.Emit(OpCodes.Ldloc, valueTemp);
                EmitCompoundOperation(cs.Operator.Type);

                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property compound assignment
        EmitExpression(cs.Value);
        EnsureBoxed();
        var valueTemp2 = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp2);

        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        _il.Emit(OpCodes.Ldloc, valueTemp2);
        EmitCompoundOperation(cs.Operator.Type);

        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    protected override void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        EmitExpression(csi.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        EmitExpression(csi.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(csi.Index);
        EnsureBoxed();
        var indexTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);

        _il.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(csi.Operator.Type);

        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);

        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Load hoisted 'this' from outer state machine if captured
        if (_builder.Captures.Contains("this"))
        {
            if (_ctx?.AsyncArrowOuterBuilders?.TryGetValue(_builder.Arrow, out var outerBuilder) == true &&
                outerBuilder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, outerBuilder.ThisField);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        _il.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetSuperMethod);
        SetStackUnknown();
    }
}
