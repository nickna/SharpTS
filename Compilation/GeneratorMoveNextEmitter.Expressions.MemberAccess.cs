using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    protected override void EmitCall(Expr.Call c)
    {
        // Handle console.log specially
        if (c.Callee is Expr.Variable consoleVar && consoleVar.Name.Lexeme == "console.log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        if (c.Callee is Expr.Get g && g.Object is Expr.Variable v && v.Name.Lexeme == "console" && g.Name.Lexeme == "log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(funcVar.Name.Lexeme), out var funcMethod))
        {
            var parameters = funcMethod.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < c.Arguments.Count)
                {
                    EmitExpression(c.Arguments[i]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
            }
            _il.Emit(OpCodes.Call, funcMethod);
            SetStackUnknown();
            return;
        }

        // Generic call through runtime
        EmitExpression(c.Callee);
        EnsureBoxed();

        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < c.Arguments.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(c.Arguments[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        SetStackUnknown();
    }

    protected override void EmitGet(Expr.Get g)
    {
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    protected override void EmitSet(Expr.Set s)
    {
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

    protected override void EmitNew(Expr.New n)
    {
        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (n.NamespacePath != null && n.NamespacePath.Count > 0)
        {
            // Build qualified name for namespace classes: Namespace_SubNs_ClassName
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
            int expectedParamCount = ctorBuilder.GetParameters().Length;

            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
            }

            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Newobj, ctorBuilder);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    protected override void EmitThis()
    {
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
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
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
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
                _il.Emit(OpCodes.Ldstr, "");
                break;
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

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

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

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();
        EmitExpression(ca.Value);
        EnsureBoxed();

        var op = ca.Operator.Type;
        if (op == TokenType.PLUS_EQUAL)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
        }
        else
        {
            var rightLocal = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, rightLocal);
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldloc, rightLocal);
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

            switch (op)
            {
                case TokenType.MINUS_EQUAL:
                    _il.Emit(OpCodes.Sub);
                    break;
                case TokenType.STAR_EQUAL:
                    _il.Emit(OpCodes.Mul);
                    break;
                case TokenType.SLASH_EQUAL:
                    _il.Emit(OpCodes.Div);
                    break;
                default:
                    _il.Emit(OpCodes.Add);
                    break;
            }
            _il.Emit(OpCodes.Box, typeof(double));
        }

        _il.Emit(OpCodes.Dup);
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            _il.Emit(OpCodes.Dup);
            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        SetStackUnknown();
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        SetStackUnknown();
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        EmitExpression(cs.Value);
        EnsureBoxed();
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

        EmitExpression(csi.Value);
        EnsureBoxed();
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
}
