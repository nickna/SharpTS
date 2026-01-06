using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await a:
                EmitAwait(a);
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
                // Type assertions are compile-time only, just emit the inner expression
                EmitExpression(ta.Expression);
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

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.Super s:
                EmitSuper(s);
                break;

            default:
                // Unsupported expression - push null
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
                break;
        }
    }

    private void EmitAwait(Expr.Await a)
    {
        int stateNumber = _currentAwaitState++;
        var resumeLabel = _stateLabels[stateNumber];
        var continueLabel = _il.DefineLabel();
        var awaiterField = _builder.AwaiterFields[stateNumber];

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));

        // 3. Get awaiter: task.GetAwaiter()
        _il.Emit(OpCodes.Call, _builder.GetTaskGetAwaiterMethod());

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_builder.AwaiterType);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, awaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterIsCompletedGetter());
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend
        // this.<>1__state = stateNumber
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethod());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _endLabel);

        // 7. Resume point (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        // If we're inside a try block with awaits, wrap GetResult in try/catch
        if (_currentTryCatchExceptionLocal != null)
        {
            var getResultDoneLabel = _il.DefineLabel();

            _il.BeginExceptionBlock();

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());

            // Store result temporarily
            var resultTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.BeginCatchBlock(typeof(Exception));
            // Wrap and store exception
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, _currentTryCatchExceptionLocal);
            // Push null as result
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.EndExceptionBlock();

            _il.MarkLabel(getResultDoneLabel);
            _il.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());
        }

        // Result is now on stack
        _stackType = StackType.Unknown;
    }

    private void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Null;
                break;
            case double d:
                _il.Emit(OpCodes.Ldc_R8, d);
                _stackType = StackType.Double;
                break;
            case bool b:
                _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _stackType = StackType.Boolean;
                break;
            case string s:
                _il.Emit(OpCodes.Ldstr, s);
                _stackType = StackType.String;
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
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
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a local variable
        if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a function
        if (_ctx.Functions.TryGetValue(name, out var funcMethod))
        {
            // Create TSFunction wrapper
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Not found - push null
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Unknown;
    }

    private void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();

        // Duplicate for return value
        _il.Emit(OpCodes.Dup);

        // Check if hoisted
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

        _stackType = StackType.Unknown;
    }

    private void EmitSuper(Expr.Super s)
    {
        // Load hoisted 'this' from state machine field
        // In async methods, 'this' is hoisted to the state machine struct
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);  // State machine ref
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
        }
        else
        {
            // Fallback - shouldn't happen if 'this' hoisting is working properly
            _il.Emit(OpCodes.Ldnull);
        }

        // Load the method name
        _il.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");

        // Call GetSuperMethod(instance, methodName) to get a callable wrapper (TSFunction)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetSuperMethod);
        _stackType = StackType.Unknown;
    }

    private void EmitBinary(Expr.Binary b)
    {
        // Emit left and right operands
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        // Use runtime Add method for + since it handles string concatenation
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                // Convert to double and subtract
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
                // Equals returns unboxed bool, need to box it
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                // Negate the result (Equals returns unboxed bool)
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitToDouble()
    {
        // Stack has: left, right (both boxed)
        // Need to convert left to double, then convert right to double
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
    }

    private void EmitNumericComparison(OpCode compareOp)
    {
        // Convert both to double and compare
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
        // a <= b is equivalent to !(a > b)
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
        // a >= b is equivalent to !(a < b)
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
            // Short-circuit: if left is falsy, return left
            _il.Emit(OpCodes.Brfalse, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }
        else // OR
        {
            // Short-circuit: if left is truthy, return left
            _il.Emit(OpCodes.Brtrue, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }

        _il.MarkLabel(endLabel);
        _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
    }

    private void EmitCall(Expr.Call c)
    {
        // Handle console.log specially (parser converts console.log to Expr.Variable("console.log"))
        if (c.Callee is Expr.Variable consoleVar && consoleVar.Name.Lexeme == "console.log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Also handle the Get pattern in case it's used differently
        if (c.Callee is Expr.Get g && g.Object is Expr.Variable v && v.Name.Lexeme == "console" && g.Name.Lexeme == "log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Handle Promise.xxx() static calls - returns Task<object?> without synchronously awaiting
        if (c.Callee is Expr.Get promiseGet &&
            promiseGet.Object is Expr.Variable promiseVar &&
            promiseVar.Name.Lexeme == "Promise")
        {
            EmitPromiseStaticCall(promiseGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Handle Promise instance methods: promise.then(onFulfilled?, onRejected?)
        // promise.catch(onRejected), promise.finally(onFinally)
        if (c.Callee is Expr.Get methodGet)
        {
            string methodName = methodGet.Name.Lexeme;
            if (methodName is "then" or "catch" or "finally")
            {
                EmitPromiseInstanceMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle string-only methods
            if (methodName is "charAt" or "substring" or "toUpperCase" or "toLowerCase"
                or "trim" or "replace" or "split" or "startsWith" or "endsWith"
                or "repeat" or "padStart" or "padEnd" or "charCodeAt" or "lastIndexOf"
                or "trimStart" or "trimEnd" or "replaceAll" or "at")
            {
                EmitStringMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle array-only methods
            if (methodName is "pop" or "shift" or "unshift" or "map" or "filter" or "forEach"
                or "push" or "find" or "findIndex" or "some" or "every" or "reduce" or "join"
                or "reverse" or "concat")
            {
                EmitArrayMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle ambiguous methods (slice, concat, includes, indexOf) that exist on both string and array
            if (methodName is "slice" or "concat" or "includes" or "indexOf")
            {
                // Try to get type info for better dispatch
                var objType = _ctx?.TypeMap?.Get(methodGet.Object);
                if (objType is TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                {
                    EmitStringMethodCall(methodGet.Object, methodName, c.Arguments);
                    return;
                }
                if (objType is TypeSystem.TypeInfo.Array)
                {
                    EmitArrayMethodCall(methodGet.Object, methodName, c.Arguments);
                    return;
                }
                // Fallback: runtime dispatch for any/unknown types
                EmitAmbiguousMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Try direct dispatch for known class instance methods
            if (TryEmitDirectMethodCall(methodGet.Object, methodName, c.Arguments))
                return;
        }

        // Check if it's a direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(funcVar.Name.Lexeme, out var funcMethod))
        {
            // Direct call to known function
            // IMPORTANT: In async context, await can happen in arguments
            // Emit all arguments first and store to temps
            var parameters = funcMethod.GetParameters();
            var directArgTemps = new List<LocalBuilder>();

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
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                directArgTemps.Add(temp);
            }

            // Now load all args from temps and call
            foreach (var temp in directArgTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }
            _il.Emit(OpCodes.Call, funcMethod);
            _stackType = StackType.Unknown;
            return;
        }

        // Generic call through TSFunction
        // IMPORTANT: In async context, await can happen in callee or arguments
        // Emit all parts that may contain await first and store to temps

        // Emit callee first and save to temp
        EmitExpression(c.Callee);
        EnsureBoxed();
        var calleeTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, calleeTemp);

        // Emit all arguments and save to temps
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in c.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Now build the call with saved values (no awaits can happen here)
        _il.Emit(OpCodes.Ldloc, calleeTemp);

        // Build arguments array from temps
        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        _stackType = StackType.Unknown;
    }

    private void EmitGet(Expr.Get g)
    {
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
    }

    private void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        // IMPORTANT: Emit the new value first (may contain await which clears stack)
        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Now load current value (after await if any)
        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();

        // Load the value back
        _il.Emit(OpCodes.Ldloc, valueTemp);

        // Apply operation
        var op = ca.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            default:
                // For other compound ops, convert to double
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
                break;
        }

        // Store result
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

        _stackType = StackType.Unknown;
    }

    private void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load, increment, store, return new value
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
        _stackType = StackType.Unknown;
    }

    private void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load original value
            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            // Increment and store
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

            // Original value is on stack
        }
        _stackType = StackType.Unknown;
    }
}
