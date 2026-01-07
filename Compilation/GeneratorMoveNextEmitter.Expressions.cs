using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Yield y:
                EmitYield(y);
                break;

            case Expr.Literal l:
                EmitLiteral(l);
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

            case Expr.Logical l:
                EmitLogical(l);
                break;

            case Expr.Unary u:
                EmitUnary(u);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Grouping g:
                EmitExpression(g.Expression);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
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

            case Expr.ArrayLiteral a:
                EmitArrayLiteral(a);
                break;

            case Expr.ObjectLiteral o:
                EmitObjectLiteral(o);
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

            case Expr.This:
                EmitThis();
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
                EmitExpression(ta.Expression);
                break;

            case Expr.Spread sp:
                EmitExpression(sp.Expression);
                break;

            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;

            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    private void EmitYield(Expr.Yield y)
    {
        int stateNumber = _currentYieldState++;
        var resumeLabel = _stateLabels[stateNumber];

        // Handle yield* delegation
        if (y.IsDelegating && y.Value != null)
        {
            EmitYieldStar(y, stateNumber, resumeLabel);
            return;
        }

        // 1. Emit the yield value (or null if no value)
        if (y.Value != null)
        {
            EmitExpression(y.Value);
            EnsureBoxed();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        // 2. Store value in <>2__current field
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // 3. Set state to the resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 4. Return true (has value)
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // 5. Mark the resume label (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // 6. yield expression evaluates to undefined (null) when resumed
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitYieldStar(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // yield* delegates to another iterable
        // We store the enumerator in a field so it survives across MoveNext calls

        var delegatedField = _builder.DelegatedEnumeratorField;
        if (delegatedField == null)
        {
            // Fallback: no field defined, just push null
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        var loopEnd = _il.DefineLabel();

        // Emit the iterable expression and get its enumerator
        EmitExpression(y.Value!);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        // Store enumerator in field
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // Fall through to check first element
        // This label is where we resume from state dispatch
        _il.MarkLabel(resumeLabel);

        // Load the delegated enumerator from field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);

        // Check if there are more elements
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current value from delegated enumerator
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Callvirt, current);

        // Store in <>2__current
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return true
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // End of delegation
        _il.MarkLabel(loopEnd);

        // Clear the delegated enumerator field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                _il.Emit(OpCodes.Ldnull);
                SetStackType(StackType.Null);
                break;
            case double d:
                _il.Emit(OpCodes.Ldc_R8, d);
                SetStackType(StackType.Double);
                break;
            case bool b:
                _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                SetStackType(StackType.Boolean);
                break;
            case string s:
                _il.Emit(OpCodes.Ldstr, s);
                SetStackType(StackType.String);
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    private void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        // Check if hoisted to state machine field
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            SetStackUnknown();
            return;
        }

        // Check if it's a local variable
        if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            SetStackUnknown();
            return;
        }

        // Check if it's a function
        if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod))
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();
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

    private void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Sub);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.STAR:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Mul);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.SLASH:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Div);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.PERCENT:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Rem);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.LESS:
                EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        SetStackUnknown();
    }

    private void EmitToDouble()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
    }

    private void EmitNumericComparison(OpCode compareOp)
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(compareOp);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonLe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonGe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitLogical(Expr.Logical l)
    {
        var endLabel = _il.DefineLabel();

        EmitExpression(l.Left);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        EmitTruthyCheck();

        if (l.Operator.Type == TokenType.AND_AND)
        {
            _il.Emit(OpCodes.Brfalse, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }
        else
        {
            _il.Emit(OpCodes.Brtrue, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitUnary(Expr.Unary u)
    {
        EmitExpression(u.Right);

        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Neg);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.BANG:
                EnsureBoxed();
                EmitTruthyCheck();
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.TYPEOF:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
                _il.Emit(OpCodes.Not);
                _il.Emit(OpCodes.Conv_R8);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            default:
                EnsureBoxed();
                break;
        }
        SetStackUnknown();
    }

    private void EmitCall(Expr.Call c)
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
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(t.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitExpression(t.ThenBranch);
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitCompoundAssign(Expr.CompoundAssign ca)
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

    private void EmitPrefixIncrement(Expr.PrefixIncrement pi)
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

    private void EmitPostfixIncrement(Expr.PostfixIncrement poi)
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

    private void EmitArrayLiteral(Expr.ArrayLiteral a)
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

    private void EmitObjectLiteral(Expr.ObjectLiteral o)
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
        // Resolve class name (may be qualified in multi-module compilation)
        string resolvedClassName = _ctx!.ResolveClassName(n.ClassName.Lexeme);

        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
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

    private void EmitThis()
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

    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var rightLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(nc.Left);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitTemplateLiteral(Expr.TemplateLiteral tl)
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

    private void EmitSet(Expr.Set s)
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

    private void EmitCompoundSet(Expr.CompoundSet cs)
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

    private void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
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

    private void EmitCompoundOperation(TokenType opType)
    {
        if (opType == TokenType.PLUS_EQUAL)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
            return;
        }

        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (opType)
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
            case TokenType.PERCENT_EQUAL:
                _il.Emit(OpCodes.Rem);
                break;
            default:
                _il.Emit(OpCodes.Add);
                break;
        }

        _il.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitArrowFunction(Expr.ArrowFunction af)
    {
        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }
}
