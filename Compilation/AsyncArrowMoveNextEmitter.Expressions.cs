using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
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
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

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
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
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
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
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
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

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
                throw new Exception($"Internal Error: Unexpected static property key type: {key.GetType().Name}");
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
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        // Start with first string
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        // Interleave expressions and remaining strings
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);

            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
            }
        }
        SetStackType(StackType.String);
    }

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // 1. Emit the tag function reference
        EmitExpression(ttl.Tag);
        EnsureBoxed();

        // 2. Create cooked strings array
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

        // 3. Create raw strings array
        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 4. Create expressions array
        _il.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(ttl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 5. Call runtime helper
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
        throw new NotImplementedException($"Private field '{gp.Name.Lexeme}' access not supported in compiled async arrow functions.");
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        throw new NotImplementedException($"Private field '{sp.Name.Lexeme}' assignment not supported in compiled async arrow functions.");
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        throw new NotImplementedException($"Private method '{cp.Name.Lexeme}' call not supported in compiled async arrow functions.");
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
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
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
