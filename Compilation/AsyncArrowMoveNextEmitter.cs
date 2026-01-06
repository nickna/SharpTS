using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext body for an async arrow function's state machine.
/// Similar to AsyncMoveNextEmitter but handles captured variable access
/// through the outer state machine reference.
/// </summary>
public class AsyncArrowMoveNextEmitter
{
    private readonly AsyncArrowStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private ILGenerator _il = null!;
    private CompilationContext? _ctx;
    private int _currentState = 0;
    private readonly List<Label> _stateLabels = [];
    private Label _exitLabel;
    private StackType _stackType = StackType.Unknown;
    private LocalBuilder? _resultLocal;
    private LocalBuilder? _exceptionLocal;

    // Non-hoisted local variables (live within a single MoveNext invocation)
    private readonly Dictionary<string, LocalBuilder> _locals = [];

    public AsyncArrowMoveNextEmitter(
        AsyncArrowStateMachineBuilder builder,
        AsyncStateAnalyzer.AsyncFunctionAnalysis analysis)
    {
        _builder = builder;
        _analysis = analysis;
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt> body, CompilationContext ctx, Type returnType)
    {
        _il = _builder.MoveNextMethod.GetILGenerator();
        _ctx = ctx;

        // Create labels for each await state
        for (int i = 0; i < _analysis.AwaitPointCount; i++)
        {
            _stateLabels.Add(_il.DefineLabel());
        }
        _exitLabel = _il.DefineLabel();

        // Declare locals
        _resultLocal = _il.DeclareLocal(typeof(object));
        _exceptionLocal = _il.DeclareLocal(typeof(Exception));

        // Begin try block
        _il.BeginExceptionBlock();

        // State dispatch switch
        EmitStateDispatch();

        // Emit the body
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Default return null
        EmitReturnNull();

        // Catch block
        _il.BeginCatchBlock(typeof(Exception));
        EmitCatchBlock();

        _il.EndExceptionBlock();

        // Exit label and return
        _il.MarkLabel(_exitLabel);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitStateDispatch()
    {
        if (_stateLabels.Count == 0)
        {
            // No awaits, just run through
            return;
        }

        var defaultLabel = _il.DefineLabel();

        // Load state
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Switch on state
        _il.Emit(OpCodes.Switch, [.. _stateLabels]);

        // Default case - continue with normal execution
        _il.MarkLabel(defaultLabel);
    }

    private void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // Pop unused value
                if (_stackType != StackType.Unknown || true) // Always pop expression results
                    _il.Emit(OpCodes.Pop);
                _stackType = StackType.Unknown;
                break;

            case Stmt.Var v:
                if (v.Initializer != null)
                {
                    EmitExpression(v.Initializer);
                    EnsureBoxed();
                    StoreVariable(v.Name.Lexeme);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                    StoreVariable(v.Name.Lexeme);
                }
                break;

            case Stmt.Return r:
                if (r.Value != null)
                {
                    EmitExpression(r.Value);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                EmitSetResult();
                _il.Emit(OpCodes.Leave, _exitLabel);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.Block b:
                foreach (var s in b.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            // Add more statement types as needed
            default:
                // For unhandled statements, emit a placeholder
                break;
        }
    }

    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;

            case Expr.Variable v:
                LoadVariable(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                EmitExpression(a.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Dup);
                StoreVariable(a.Name.Lexeme);
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

            default:
                // For unhandled expressions, push null
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
                break;
        }
    }

    private void EmitLiteral(Expr.Literal lit)
    {
        if (lit.Value == null)
        {
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Null;
        }
        else if (lit.Value is double d)
        {
            _il.Emit(OpCodes.Ldc_R8, d);
            _il.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
        }
        else if (lit.Value is string s)
        {
            _il.Emit(OpCodes.Ldstr, s);
            _stackType = StackType.String;
        }
        else if (lit.Value is bool b)
        {
            _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Box, typeof(bool));
            _stackType = StackType.Unknown;
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Null;
        }
    }

    private void LoadVariable(string name)
    {
        // Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's captured from outer scope
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            // Load through outer reference
            // Use Unbox (not Unbox_Any) to get a pointer to the boxed struct, then load field
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture (needs extra indirection through parent's outer)
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                // First unbox to parent, then load parent's outer reference
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            _il.Emit(OpCodes.Ldfld, outerField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check for non-hoisted local variable
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a global function
        if (_ctx?.Functions.TryGetValue(name, out var funcMethod) == true)
        {
            // Load function reference
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Fallback: null
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    private void StoreVariable(string name)
    {
        // Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, paramField);
            return;
        }

        // Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, localField);
            return;
        }

        // Check if it's captured from outer scope - store back to outer
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            // Store value to outer state machine's field through the boxed reference
            // Stack has: value
            // We need to: store to temp, get outer ptr, load temp, store to field
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);

            // Get pointer to the boxed outer state machine
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture (needs extra indirection through parent's outer)
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                // First unbox to parent, then load parent's outer reference
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            // Load value and store to field
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, outerField);
            return;
        }

        // Non-hoisted local variable - use IL local
        // Create or get the local
        if (!_locals.TryGetValue(name, out var local))
        {
            local = _il.DeclareLocal(typeof(object));
            _locals[name] = local;
        }
        _il.Emit(OpCodes.Stloc, local);
    }

    private void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        // Handle binary operations based on operator type
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                // Use runtime Add for string concatenation support
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                EmitNumericOp(OpCodes.Sub);
                break;
            case TokenType.STAR:
                EmitNumericOp(OpCodes.Mul);
                break;
            case TokenType.SLASH:
                EmitNumericOp(OpCodes.Div);
                break;
            case TokenType.PERCENT:
                EmitNumericOp(OpCodes.Rem);
                break;
            case TokenType.LESS:
            case TokenType.LESS_EQUAL:
            case TokenType.GREATER:
            case TokenType.GREATER_EQUAL:
                EmitComparisonOp(op);
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
                // Fallback: pop both and push null
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitNumericOp(OpCode opcode)
    {
        // Convert to double and apply operation
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(opcode);
        _il.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitComparisonOp(TokenType op)
    {
        // Convert to double for comparison
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (op)
        {
            case TokenType.LESS:
                _il.Emit(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                _il.Emit(OpCodes.Cgt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
            case TokenType.GREATER:
                _il.Emit(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                _il.Emit(OpCodes.Clt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
        }
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitCall(Expr.Call c)
    {
        // Emit callee
        EmitExpression(c.Callee);
        EnsureBoxed();

        // Create args array
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

        // Call InvokeValue (object callee, object[] args) -> object
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        _stackType = StackType.Unknown;
    }

    private void EmitAwait(Expr.Await aw)
    {
        int stateNum = _currentState++;
        var continueLabel = _il.DefineLabel();

        if (!_builder.AwaiterFields.TryGetValue(stateNum, out var awaiterField))
        {
            throw new InvalidOperationException($"No awaiter field for state {stateNum}");
        }

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(aw.Expression);
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
        // this.<>1__state = stateNum
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNum);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethod());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _exitLabel);

        // 7. Resume point (jumped to from state switch)
        if (stateNum < _stateLabels.Count)
            _il.MarkLabel(_stateLabels[stateNum]);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());

        // Result is now on stack
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
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
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
                _stackType = StackType.Unknown;
                return;
            }
        }

        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    private void EmitIf(Stmt.If i)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(i.Condition);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        _il.MarkLabel(endLabel);
    }

    private void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        _il.MarkLabel(startLabel);

        EmitExpression(w.Condition);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _il.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
    }

    private void EmitReturnNull()
    {
        _il.Emit(OpCodes.Ldnull);
        EmitSetResult();
        _il.Emit(OpCodes.Leave, _exitLabel);
    }

    private void EmitSetResult()
    {
        // Store result
        _il.Emit(OpCodes.Stloc, _resultLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetResult(result)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _resultLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetResultMethod());
    }

    private void EmitCatchBlock()
    {
        // Store exception
        _il.Emit(OpCodes.Stloc, _exceptionLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetException(exception)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _exceptionLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetExceptionMethod());
    }

    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case StackType.Boolean:
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check if it's an async arrow (nested async arrow)
        if (af.IsAsync)
        {
            EmitNestedAsyncArrow(af);
            return;
        }

        // Get the method for this arrow function (pre-compiled)
        if (_ctx?.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            EmitCapturingArrowInAsyncArrow(af, method, displayClass);
        }
        else
        {
            EmitNonCapturingArrowInAsyncArrow(af, method);
        }
    }

    private void EmitCapturingArrowInAsyncArrow(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Create display class instance
        _il.Emit(OpCodes.Newobj, displayCtor);

        // Get captured variables field mapping
        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            _il.Emit(OpCodes.Ldtoken, method);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Populate captured fields from async arrow state machine context
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup); // Keep display class instance on stack

            // Load the captured variable using the same logic as LoadVariable
            LoadVariableForCapture(capturedVar);

            _il.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        _stackType = StackType.Unknown;
    }

    private void EmitNonCapturingArrowInAsyncArrow(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method: new TSFunction(null, method)
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Loads a variable value for populating a capture in a non-async arrow's display class.
    /// This is similar to LoadVariable but designed for capture population.
    /// </summary>
    private void LoadVariableForCapture(string name)
    {
        // Check if it's a parameter of this async arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a hoisted local of this async arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's captured from outer scope (parent async function/arrow)
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            _il.Emit(OpCodes.Ldfld, outerField);
            _stackType = StackType.Unknown;
            return;
        }

        // Check for non-hoisted local variable
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            _stackType = StackType.Unknown;
            return;
        }

        // Handle 'this' capture - in async arrows, 'this' is captured from outer scope
        if (name == "this" && _builder.IsCaptured("this") && _builder.CapturedFieldMap.TryGetValue("this", out var thisField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            _il.Emit(OpCodes.Ldfld, thisField);
            _stackType = StackType.Unknown;
            return;
        }

        // Fallback: null
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    private void EmitNestedAsyncArrow(Expr.ArrowFunction af)
    {
        // Get the nested async arrow's state machine builder
        if (_ctx?.AsyncArrowBuilders == null ||
            !_ctx.AsyncArrowBuilders.TryGetValue(af, out var nestedBuilder))
        {
            throw new InvalidOperationException(
                "Nested async arrow function not registered with state machine builder.");
        }

        // For nested arrows, we need to pass the current arrow's boxed state machine
        // as the "outer" reference for the nested arrow.
        // The nested arrow's stub expects (outer state machine boxed, params...)

        // Load the current arrow's self-boxed reference
        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            // Fallback: this shouldn't happen if hasNestedAsyncArrows was set correctly
            throw new InvalidOperationException(
                "Async arrow with nested arrows does not have SelfBoxedField set.");
        }

        // Load the stub method for the nested arrow
        _il.Emit(OpCodes.Ldtoken, nestedBuilder.StubMethod);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Create TSFunction(target: self boxed, method: stub)
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);

        _stackType = StackType.Unknown;
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

        _stackType = StackType.Unknown;
    }
}
