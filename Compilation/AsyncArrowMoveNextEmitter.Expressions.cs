using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;

            case Expr.Variable v:
                EmitVariable(v);
                break;

            case Expr.Assign a:
                EmitAssign(a);
                break;

            case Expr.Binary b:
                EmitBinary(b);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.Await aw:
                EmitAwait(aw);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Grouping grp:
                EmitExpression(grp.Expression);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
                break;

            case Expr.This:
                EmitThis();
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.DynamicImport di:
                EmitDynamicImport(di);
                break;

            case Expr.Logical l:
                EmitLogical(l);
                break;

            case Expr.Unary u:
                EmitUnary(u);
                break;

            case Expr.CompoundAssign ca:
                EmitCompoundAssign(ca);
                break;

            case Expr.PrefixIncrement pi:
                EmitPrefixIncrement(pi);
                break;

            case Expr.PostfixIncrement poi:
                EmitPostfixIncrement(poi);
                break;

            case Expr.ArrayLiteral al:
                EmitArrayLiteral(al);
                break;

            case Expr.ObjectLiteral ol:
                EmitObjectLiteral(ol);
                break;

            case Expr.GetIndex gi:
                EmitGetIndex(gi);
                break;

            case Expr.SetIndex si:
                EmitSetIndex(si);
                break;

            case Expr.New n:
                EmitNew(n);
                break;

            case Expr.NullishCoalescing nc:
                EmitNullishCoalescing(nc);
                break;

            case Expr.TemplateLiteral tl:
                EmitTemplateLiteral(tl);
                break;

            case Expr.Set s:
                EmitSet(s);
                break;

            case Expr.TypeAssertion ta:
                // Type assertions are compile-time only, just emit the inner expression
                EmitExpression(ta.Expression);
                break;

            case Expr.NonNullAssertion nna:
                // Non-null assertions are compile-time only, just emit the inner expression
                EmitExpression(nna.Expression);
                break;

            case Expr.Spread sp:
                // Spread expressions are handled in context (arrays, objects, calls)
                // If we get here directly, just emit the inner expression
                EmitExpression(sp.Expression);
                break;

            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;

            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;

            case Expr.Super s:
                EmitSuper(s);
                break;

            default:
                // For unhandled expressions, push null
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    private void EmitLiteral(Expr.Literal lit)
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

    private void EmitGet(Expr.Get g)
    {
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    private void EmitTernary(Expr.Ternary t)
    {
        _helpers.EmitTernary(
            () => EmitExpression(t.Condition),
            () => EmitExpression(t.ThenBranch),
            () => EmitExpression(t.ElseBranch),
            _ctx!.Runtime!.IsTruthy);
    }

    private void EmitThis()
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

    private void EmitDynamicImport(Expr.DynamicImport di)
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

    private void EmitLogical(Expr.Logical l)
    {
        bool isAnd = l.Operator.Type == TokenType.AND_AND;
        _helpers.EmitLogical(
            isAnd,
            () => { EmitExpression(l.Left); EnsureBoxed(); },
            () => { EmitExpression(l.Right); EnsureBoxed(); },
            _ctx!.Runtime!.IsTruthy);
    }

    private void EmitUnary(Expr.Unary u)
    {
        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                _helpers.EmitUnaryMinus(() => EmitExpression(u.Right));
                break;
            case TokenType.BANG:
                _helpers.EmitUnaryNot(() => EmitExpression(u.Right), _ctx!.Runtime!.IsTruthy);
                break;
            case TokenType.TYPEOF:
                _helpers.EmitUnaryTypeOf(() => EmitExpression(u.Right), _ctx!.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                _helpers.EmitUnaryBitwiseNot(() => EmitExpression(u.Right));
                break;
            default:
                EmitExpression(u.Right);
                EnsureBoxed();
                break;
        }
    }

    private void EmitCompoundAssign(Expr.CompoundAssign ca)
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

    private void EmitPrefixIncrement(Expr.PrefixIncrement pi)
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

    private void EmitPostfixIncrement(Expr.PostfixIncrement poi)
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

    private void EmitArrayLiteral(Expr.ArrayLiteral a)
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

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
        SetStackUnknown();
    }

    private void EmitObjectLiteral(Expr.ObjectLiteral o)
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

    private void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        SetStackUnknown();
    }

    private void EmitSetIndex(Expr.SetIndex si)
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

    private void EmitNew(Expr.New n)
    {
        // Resolve class name
        string resolvedClassName;
        if (n.NamespacePath != null && n.NamespacePath.Count > 0)
        {
            string nsPath = string.Join("_", n.NamespacePath.Select(t => t.Lexeme));
            resolvedClassName = $"{nsPath}_{n.ClassName.Lexeme}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(n.ClassName.Lexeme);
        }

        if (_ctx!.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(resolvedClassName, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(resolvedClassName, out var _) == true)
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

    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        _helpers.EmitNullishCoalescing(
            () => EmitExpression(nc.Left),
            () => EmitExpression(nc.Right));
    }

    private void EmitTemplateLiteral(Expr.TemplateLiteral tl)
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

    private void EmitSet(Expr.Set s)
    {
        // Handle static field assignment
        if (s.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(s.Name.Lexeme, out var staticField))
            {
                EmitExpression(s.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Stsfld, staticField);
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

    private void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Handle static field compound assignment
        if (cs.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(cs.Name.Lexeme, out var staticField))
            {
                EmitExpression(cs.Value);
                EnsureBoxed();
                var valueTemp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, valueTemp);

                _il.Emit(OpCodes.Ldsfld, staticField);
                _il.Emit(OpCodes.Ldloc, valueTemp);
                EmitCompoundOperation(cs.Operator.Type);

                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Stsfld, staticField);
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

    private void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
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

    private void EmitSuper(Expr.Super s)
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
