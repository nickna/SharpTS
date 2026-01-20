using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Abstract base class for statement emission across different emitter types.
/// Provides unified dispatch logic and shared control flow implementations.
/// </summary>
/// <remarks>
/// This base class centralizes the statement dispatch switch and control flow
/// logic that was duplicated across ILEmitter, AsyncMoveNextEmitter,
/// GeneratorMoveNextEmitter, and AsyncGeneratorMoveNextEmitter.
///
/// Abstract methods must be implemented by subclasses for emitter-specific behavior:
/// - EmitVarDeclaration: Variable storage differs (locals vs fields)
/// - EmitReturn: Return semantics differ (void, bool, ValueTask&lt;bool&gt;)
/// - EmitTryCatch: Async emitters need await-aware exception handling
/// - Loop label management: ILEmitter uses CompilationContext, state machines use stack
///
/// Virtual methods provide default implementations that subclasses may override
/// for optimization (e.g., ILEmitter's dead code elimination and stack type tracking).
/// </remarks>
public abstract class StatementEmitterBase : ExpressionEmitterBase
{
    protected StatementEmitterBase(StateMachineEmitHelpers helpers)
        : base(helpers)
    {
    }

    #region Abstract Methods - Emitter-specific behavior

    /// <summary>
    /// Declares and initializes a variable.
    /// Different emitters handle variable storage differently:
    /// - ILEmitter: locals + top-level static fields, unboxed double optimization
    /// - State machines: hoisted fields + non-hoisted locals
    /// </summary>
    protected abstract void EmitVarDeclaration(Stmt.Var v);

    /// <summary>
    /// Declares and initializes a const variable.
    /// Similar to var declaration but const always has an initializer.
    /// </summary>
    protected virtual void EmitConstDeclaration(Stmt.Const c)
    {
        // Default: treat as Var with guaranteed initializer
        EmitVarDeclaration(new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer));
    }

    /// <summary>
    /// Emits return statement. Different semantics per emitter type:
    /// - ILEmitter: void with Ret, or store + Leave inside exception blocks
    /// - Async: store result + leave to SetResult label
    /// - Generator: set state -2, return false (MoveNext contract)
    /// - AsyncGenerator: set state -2, return ValueTask&lt;bool&gt;(false)
    /// </summary>
    protected abstract void EmitReturn(Stmt.Return r);

    /// <summary>
    /// Emits try/catch/finally block.
    /// Async emitters override with await-aware exception handling.
    /// </summary>
    protected abstract void EmitTryCatch(Stmt.TryCatch t);

    #endregion

    #region Abstract Methods - Loop Label Management

    /// <summary>
    /// Registers a loop context for break/continue resolution.
    /// </summary>
    protected abstract void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null);

    /// <summary>
    /// Exits the current loop context.
    /// </summary>
    protected abstract void ExitLoop();

    /// <summary>
    /// Gets the current innermost loop context, or null if not in a loop.
    /// </summary>
    protected abstract (Label BreakLabel, Label ContinueLabel, string? LabelName)? CurrentLoop { get; }

    /// <summary>
    /// Finds a loop context by label name.
    /// </summary>
    protected abstract (Label BreakLabel, Label ContinueLabel, string? LabelName)? FindLabeledLoop(string labelName);

    #endregion

    #region Virtual Hooks - Override for customization

    /// <summary>
    /// Checks if a statement should be skipped due to dead code analysis.
    /// Default: no dead code analysis. ILEmitter overrides to use DeadCodeAnalyzer.
    /// </summary>
    protected virtual bool IsDead(Stmt stmt) => false;

    /// <summary>
    /// Emits a condition check that leaves a boolean-compatible value on stack.
    /// Default: EnsureBoxed + EmitTruthyCheck. ILEmitter overrides for stack type optimization.
    /// </summary>
    protected virtual void EmitConditionCheck(Expr condition)
    {
        EmitExpression(condition);
        EnsureBoxed();
        EmitTruthyCheck();
    }

    /// <summary>
    /// Emits a branch to a label. Override to use Leave instead of Br in exception blocks.
    /// </summary>
    protected virtual void EmitBranchToLabel(Label target)
    {
        IL.Emit(OpCodes.Br, target);
    }

    /// <summary>
    /// Emits truthy check. Default calls Runtime.IsTruthy.
    /// </summary>
    protected virtual void EmitTruthyCheck()
    {
        IL.Emit(OpCodes.Call, Ctx.Runtime!.IsTruthy);
    }

    /// <summary>
    /// Called for unknown statement types.
    /// Default: ignored. ILEmitter overrides for Namespace, Import, Export.
    /// </summary>
    protected virtual void EmitUnknownStatement(Stmt stmt)
    {
        // Default: ignore unknown statements
    }

    #endregion

    #region Core Statement Dispatch

    /// <summary>
    /// Dispatches statement emission to the appropriate handler method.
    /// </summary>
    public virtual void EmitStatement(Stmt stmt)
    {
        if (IsDead(stmt))
            return;

        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                IL.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.Const c:
                EmitConstDeclaration(c);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.DoWhile dw:
                EmitDoWhile(dw);
                break;

            case Stmt.For f:
                EmitFor(f);
                break;

            case Stmt.ForOf forOf:
                EmitForOf(forOf);
                break;

            case Stmt.ForIn fi:
                EmitForIn(fi);
                break;

            case Stmt.Block b:
                EmitBlock(b);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.Break b:
                EmitBreak(b);
                break;

            case Stmt.Continue c:
                EmitContinue(c);
                break;

            case Stmt.LabeledStatement ls:
                EmitLabeledStatement(ls);
                break;

            case Stmt.Switch s:
                EmitSwitch(s);
                break;

            case Stmt.TryCatch t:
                EmitTryCatch(t);
                break;

            case Stmt.Throw t:
                EmitThrow(t);
                break;

            case Stmt.Print p:
                EmitPrint(p);
                break;

            default:
                EmitUnknownStatement(stmt);
                break;
        }
    }

    #endregion

    #region Virtual Control Flow Methods - Default Implementations

    /// <summary>
    /// Emits an if/else statement.
    /// </summary>
    protected virtual void EmitIf(Stmt.If i)
    {
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        EmitConditionCheck(i.Condition);
        IL.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        IL.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits a while loop.
    /// </summary>
    protected virtual void EmitWhile(Stmt.While w)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);
        EmitConditionCheck(w.Condition);
        IL.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);

        IL.MarkLabel(continueLabel);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a do-while loop.
    /// </summary>
    protected virtual void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        IL.MarkLabel(continueLabel);
        EmitConditionCheck(dw.Condition);
        IL.Emit(OpCodes.Brtrue, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a for loop with proper continue handling.
    /// Continue jumps to the increment, not past it.
    /// </summary>
    protected virtual void EmitFor(Stmt.For f)
    {
        // Emit initializer (once, outside the loop)
        if (f.Initializer != null)
            EmitStatement(f.Initializer);

        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();  // Points to increment

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);

        // Check condition (if present)
        if (f.Condition != null)
        {
            EmitConditionCheck(f.Condition);
            IL.Emit(OpCodes.Brfalse, endLabel);
        }

        // Emit body
        EmitStatement(f.Body);

        // Continue target: increment goes here
        IL.MarkLabel(continueLabel);
        if (f.Increment != null)
        {
            EmitExpression(f.Increment);
            IL.Emit(OpCodes.Pop);  // Discard increment result
        }

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a for...of loop using IEnumerable pattern.
    /// Override in ILEmitter for iterator protocol support.
    /// Override in async emitters for for await...of support.
    /// </summary>
    protected virtual void EmitForOf(Stmt.ForOf f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // Emit iterable and get enumerator
        EmitExpression(f.Iterable);
        EnsureBoxed();

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        IL.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = IL.DeclareLocal(typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        EnterLoop(endLabel, continueLabel);

        // Declare loop variable
        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        IL.MarkLabel(startLabel);

        // Check MoveNext
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from Current
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, current);
        });

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a for...in loop iterating over object keys.
    /// </summary>
    protected virtual void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // Get keys from object
        EmitExpression(f.Object);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetKeys);

        var keysLocal = IL.DeclareLocal(Types.ListOfObject);
        IL.Emit(OpCodes.Stloc, keysLocal);

        // Index variable
        var indexLocal = IL.DeclareLocal(Types.Int32);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        EnterLoop(endLabel, continueLabel);

        // Loop variable
        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        IL.MarkLabel(startLabel);

        // Check index < keys.Count
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from keys[index]
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            IL.Emit(OpCodes.Ldloc, keysLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetElement);
        });

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a block statement.
    /// </summary>
    protected virtual void EmitBlock(Stmt.Block b)
    {
        if (b.Statements != null)
        {
            foreach (var stmt in b.Statements)
                EmitStatement(stmt);
        }
    }

    /// <summary>
    /// Emits a break statement.
    /// </summary>
    protected virtual void EmitBreak(Stmt.Break b)
    {
        var loop = b.Label != null
            ? FindLabeledLoop(b.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.BreakLabel);
    }

    /// <summary>
    /// Emits a continue statement.
    /// </summary>
    protected virtual void EmitContinue(Stmt.Continue c)
    {
        var loop = c.Label != null
            ? FindLabeledLoop(c.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.ContinueLabel);
    }

    /// <summary>
    /// Emits a labeled statement.
    /// </summary>
    protected virtual void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        var breakLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // Mark continue label at the start
        IL.MarkLabel(continueLabel);

        EnterLoop(breakLabel, continueLabel, ls.Label.Lexeme);

        EmitStatement(ls.Statement);

        ExitLoop();

        // Mark break label at the end
        IL.MarkLabel(breakLabel);
    }

    /// <summary>
    /// Emits a switch statement.
    /// </summary>
    protected virtual void EmitSwitch(Stmt.Switch s)
    {
        var endLabel = IL.DefineLabel();
        var defaultLabel = IL.DefineLabel();
        var caseLabels = s.Cases.Select(_ => IL.DefineLabel()).ToList();

        // Evaluate subject
        EmitExpression(s.Subject);
        EnsureBoxed();
        var subjectLocal = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Call, Ctx.Runtime!.Equals);
            IL.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        // Jump to default or end
        if (s.DefaultBody == null)
            IL.Emit(OpCodes.Br, endLabel);
        else
            IL.Emit(OpCodes.Br, defaultLabel);

        // Emit case bodies
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break breakStmt && breakStmt.Label == null)
                    IL.Emit(OpCodes.Br, endLabel);
                else
                    EmitStatement(stmt);
            }
        }

        // Default case
        if (s.DefaultBody != null)
        {
            IL.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break breakStmt && breakStmt.Label == null)
                    IL.Emit(OpCodes.Br, endLabel);
                else
                    EmitStatement(stmt);
            }
        }

        IL.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits a throw statement.
    /// </summary>
    protected virtual void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateException);
        IL.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits a print/console.log statement.
    /// </summary>
    protected virtual void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ConsoleLog);
    }

    #endregion

    #region Loop Variable Helpers

    /// <summary>
    /// Declares a loop variable. Override in state machine emitters to handle hoisted fields.
    /// Returns a LocalBuilder for non-hoisted variables, or null if hoisted to field.
    /// </summary>
    protected virtual LocalBuilder? DeclareLoopVariable(string name)
    {
        var local = IL.DeclareLocal(Types.Object);
        Ctx.Locals.RegisterLocal(name, local);
        return local;
    }

    /// <summary>
    /// Stores a value into a loop variable. Override in state machine emitters to handle hoisted fields.
    /// </summary>
    protected virtual void EmitStoreLoopVariable(LocalBuilder? local, string name, Action emitValue)
    {
        if (local != null)
        {
            emitValue();
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    #endregion

    #region Class Expressions

    /// <summary>
    /// Default implementation for class expressions.
    /// Loads the pre-defined TypeBuilder as a Type object at runtime.
    /// </summary>
    protected override void EmitClassExpression(Expr.ClassExpr ce)
    {
        if (Ctx?.ClassExprBuilders != null && Ctx.ClassExprBuilders.TryGetValue(ce, out var typeBuilder))
        {
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

    #endregion
}
