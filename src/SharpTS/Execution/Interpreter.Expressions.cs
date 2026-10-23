using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    /// <summary>
    /// Dispatches an expression to the appropriate evaluator using the registry.
    /// </summary>
    /// <param name="expr">The expression AST node to evaluate.</param>
    /// <returns>The runtime value produced by evaluating the expression.</returns>
    /// <remarks>
    /// Central dispatch point for all expression types. Handles literals, operators,
    /// function calls, property access, and control flow expressions.
    /// For async expressions (await), this will block synchronously. Use EvaluateAsync for fully async evaluation.
    /// </remarks>
    /// <summary>
    /// Evaluates an expression, returning a boxed object for compatibility with existing code.
    /// Prefer EvaluateRV() for new code to avoid boxing overhead.
    /// </summary>
    internal object? Evaluate(Expr expr)
    {
        return DispatchExpr(expr).ToObject();
    }

    /// <summary>
    /// Evaluates an expression, returning a RuntimeValue without boxing.
    /// This is the fast path — use this for new code.
    /// </summary>
    internal RuntimeValue EvaluateRV(Expr expr)
    {
        return DispatchExpr(expr);
    }

    // Expression handlers - called by the dispatch switch via RuntimeValue dispatch.
    // All Evaluate* methods return RuntimeValue directly — no FromBoxed in dispatch.

    internal RuntimeValue VisitComma(Expr.Comma comma) { Evaluate(comma.Left); return EvaluateRV(comma.Right); }
    internal RuntimeValue VisitDestructuringAssign(Expr.DestructuringAssign d)
    {
        // Run the lowered assignment statements (temp binding + per-target writes), then yield the
        // original rhs the temp holds — an assignment expression evaluates to its right-hand side (#754).
        foreach (var stmt in d.Assignments)
            Execute(stmt);
        return EvaluateRV(d.ResultValue);
    }
    internal RuntimeValue VisitBinary(Expr.Binary binary) => EvaluateBinary(binary);
    internal RuntimeValue VisitLogical(Expr.Logical logical) => EvaluateLogical(logical);
    internal RuntimeValue VisitNullishCoalescing(Expr.NullishCoalescing nc) => EvaluateNullishCoalescing(nc);
    internal RuntimeValue VisitTernary(Expr.Ternary ternary) => EvaluateTernary(ternary);
    internal RuntimeValue VisitGrouping(Expr.Grouping grouping) => EvaluateRV(grouping.Expression);
    internal RuntimeValue VisitLiteral(Expr.Literal literal) => EvaluateLiteral(literal);
    internal RuntimeValue VisitUnary(Expr.Unary unary) => EvaluateUnary(unary);
    internal RuntimeValue VisitDelete(Expr.Delete delete) => EvaluateDelete(delete);
    internal RuntimeValue VisitVariable(Expr.Variable variable) => LookupVariableRV(variable.Name, variable);
    internal RuntimeValue VisitAssign(Expr.Assign assign) => EvaluateAssign(assign);
    internal RuntimeValue VisitAssertedAssignment(Expr.AssertedAssignment assignment) => EvaluateAssertedAssignmentCore(_syncContext, assignment).GetAwaiter().GetResult();
    internal RuntimeValue VisitCall(Expr.Call call) => EvaluateCall(call);
    internal RuntimeValue VisitGet(Expr.Get get) => EvaluateGet(get);
    internal RuntimeValue VisitSet(Expr.Set set) => EvaluateSet(set);
    internal RuntimeValue VisitGetPrivate(Expr.GetPrivate gp) => EvaluateGetPrivate(gp);
    internal RuntimeValue VisitSetPrivate(Expr.SetPrivate sp) => EvaluateSetPrivate(sp);
    internal RuntimeValue VisitCallPrivate(Expr.CallPrivate cp) => EvaluateCallPrivate(cp);
    internal RuntimeValue VisitThis(Expr.This thisExpr) => EvaluateThis(thisExpr);
    internal RuntimeValue VisitNew(Expr.New newExpr) => EvaluateNew(newExpr);
    internal RuntimeValue VisitNewTarget(Expr.NewTarget newTarget) => _environment.NewTarget;
    internal RuntimeValue VisitArrayLiteral(Expr.ArrayLiteral array) => EvaluateArray(array);
    internal RuntimeValue VisitObjectLiteral(Expr.ObjectLiteral obj) => EvaluateObject(obj);
    internal RuntimeValue VisitGetIndex(Expr.GetIndex getIndex) => EvaluateGetIndex(getIndex);
    internal RuntimeValue VisitSetIndex(Expr.SetIndex setIndex) => EvaluateSetIndex(setIndex);
    internal RuntimeValue VisitSuper(Expr.Super super) => EvaluateSuper(super);
    internal RuntimeValue VisitSetSuper(Expr.SetSuper setSuper) => EvaluateSetSuper(setSuper);
    internal RuntimeValue VisitCompoundAssign(Expr.CompoundAssign compound) => EvaluateCompoundAssign(compound);
    internal RuntimeValue VisitCompoundSet(Expr.CompoundSet compoundSet) => EvaluateCompoundSet(compoundSet);
    internal RuntimeValue VisitCompoundSetPrivate(Expr.CompoundSetPrivate compoundSet) => EvaluateCompoundSetPrivateCore(_syncContext, compoundSet).GetAwaiter().GetResult();
    internal RuntimeValue VisitCompoundSetIndex(Expr.CompoundSetIndex compoundSetIndex) => EvaluateCompoundSetIndex(compoundSetIndex);
    internal RuntimeValue VisitLogicalAssign(Expr.LogicalAssign logical) => EvaluateLogicalAssign(logical);
    internal RuntimeValue VisitLogicalSet(Expr.LogicalSet logicalSet) => EvaluateLogicalSet(logicalSet);
    internal RuntimeValue VisitLogicalSetPrivate(Expr.LogicalSetPrivate logicalSet) => EvaluateLogicalSetPrivateCore(_syncContext, logicalSet).GetAwaiter().GetResult();
    internal RuntimeValue VisitLogicalSetIndex(Expr.LogicalSetIndex logicalSetIndex) => EvaluateLogicalSetIndex(logicalSetIndex);
    internal RuntimeValue VisitPrefixIncrement(Expr.PrefixIncrement prefix) => EvaluatePrefixIncrement(prefix);
    internal RuntimeValue VisitPostfixIncrement(Expr.PostfixIncrement postfix) => EvaluatePostfixIncrement(postfix);
    internal RuntimeValue VisitArrowFunction(Expr.ArrowFunction arrow) => EvaluateArrowFunction(arrow);
    internal RuntimeValue VisitTemplateLiteral(Expr.TemplateLiteral template) => EvaluateTemplateLiteral(template);
    internal RuntimeValue VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral tagged) => EvaluateTaggedTemplateLiteral(tagged);
    internal RuntimeValue VisitSpread(Expr.Spread spread) => EvaluateRV(spread.Expression);
    internal RuntimeValue VisitInstantiation(Expr.Instantiation expression) => EvaluateRV(expression.Expression);
    internal RuntimeValue VisitTypeAssertion(Expr.TypeAssertion ta) => EvaluateRV(ta.Expression);
    internal RuntimeValue VisitSatisfies(Expr.Satisfies sat) => EvaluateRV(sat.Expression);
    internal RuntimeValue VisitNonNullAssertion(Expr.NonNullAssertion nna) => EvaluateRV(nna.Expression);
    internal RuntimeValue VisitAwait(Expr.Await awaitExpr) => throw new InterpreterException("'await' can only be used inside async functions.");
    internal RuntimeValue VisitDynamicImport(Expr.DynamicImport di) => EvaluateDynamicImport(di);
    internal RuntimeValue VisitImportMeta(Expr.ImportMeta im) => EvaluateImportMeta(im);
    internal RuntimeValue VisitYield(Expr.Yield yieldExpr) => EvaluateYield(yieldExpr);
    internal RuntimeValue VisitRegexLiteral(Expr.RegexLiteral regex) => RuntimeValue.FromObject(new SharpTSRegExp(regex.Pattern, regex.Flags));
    internal RuntimeValue VisitClassExpr(Expr.ClassExpr classExpr) => EvaluateClassExpression(classExpr);

    /// <summary>
    /// Asynchronously dispatches an expression to the appropriate evaluator.
    /// Routes through DispatchExprAsync (Interpreter.Dispatch.cs) for exhaustiveness-checked
    /// dispatch, mirroring the synchronous EvaluateRV path (#930).
    /// </summary>
    /// <param name="expr">The expression AST node to evaluate.</param>
    /// <returns>A ValueTask that resolves to the runtime value produced by evaluating the expression.</returns>
    /// <remarks>
    /// Async version of EvaluateRV that properly handles await expressions without blocking.
    /// Used by async functions and arrow functions.
    /// </remarks>
    internal ValueTask<RuntimeValue> EvaluateAsync(Expr expr)
    {
        return DispatchExprAsync(expr);
    }

    // Async expression handlers — wired in DispatchExprAsync (Interpreter.Dispatch.cs).
    // Each handler returns ValueTask<RuntimeValue>.
    // Sync-safe nodes wrap the existing VisitXxx / EvaluateXxx directly;
    // async nodes delegate to the existing EvaluateXxxAsync helpers.

    internal async ValueTask<RuntimeValue> VisitCommaAsync(Expr.Comma comma)
    {
        await EvaluateAsync(comma.Left);
        return await EvaluateAsync(comma.Right);
    }

    internal async ValueTask<RuntimeValue> VisitDestructuringAssignAsync(Expr.DestructuringAssign d)
    {
        // Lowered statements may contain `await` in the rhs; run them on the async path, then
        // yield the temp holding the original rhs (#754).
        foreach (var stmt in d.Assignments)
            await ExecuteStatementAsync(stmt);
        return await EvaluateAsync(d.ResultValue);
    }

    internal ValueTask<RuntimeValue> VisitBinaryAsync(Expr.Binary binary) => new(EvaluateBinaryAsync(binary));
    internal ValueTask<RuntimeValue> VisitLogicalAsync(Expr.Logical logical) => EvaluateLogicalAsync(logical);
    internal ValueTask<RuntimeValue> VisitNullishCoalescingAsync(Expr.NullishCoalescing nc) => EvaluateNullishCoalescingAsync(nc);
    internal ValueTask<RuntimeValue> VisitTernaryAsync(Expr.Ternary ternary) => EvaluateTernaryAsync(ternary);
    internal ValueTask<RuntimeValue> VisitGroupingAsync(Expr.Grouping grouping) => EvaluateAsync(grouping.Expression);
    internal ValueTask<RuntimeValue> VisitLiteralAsync(Expr.Literal literal) => new(EvaluateLiteral(literal));
    internal ValueTask<RuntimeValue> VisitUnaryAsync(Expr.Unary unary) => new(EvaluateUnaryAsync(unary));
    internal async ValueTask<RuntimeValue> VisitDeleteAsync(Expr.Delete delete) => RuntimeValue.FromBoolean(await DeleteCore(_asyncContext, delete));
    internal ValueTask<RuntimeValue> VisitVariableAsync(Expr.Variable variable) => new(LookupVariableRV(variable.Name, variable));
    internal ValueTask<RuntimeValue> VisitAssignAsync(Expr.Assign assign) => EvaluateAssignAsync(assign);
    internal ValueTask<RuntimeValue> VisitCallAsync(Expr.Call call) => new(EvaluateCallAsync(call));
    internal ValueTask<RuntimeValue> VisitGetAsync(Expr.Get get) => EvaluateGetCore(_asyncContext, get);
    internal ValueTask<RuntimeValue> VisitSetAsync(Expr.Set set) => EvaluateSetCore(_asyncContext, set);
    internal ValueTask<RuntimeValue> VisitGetPrivateAsync(Expr.GetPrivate gp) => new(EvaluateGetPrivateAsync(gp));
    internal ValueTask<RuntimeValue> VisitSetPrivateAsync(Expr.SetPrivate sp) => new(EvaluateSetPrivateAsync(sp));
    internal ValueTask<RuntimeValue> VisitCallPrivateAsync(Expr.CallPrivate cp) => new(EvaluateCallPrivateAsync(cp));
    internal ValueTask<RuntimeValue> VisitThisAsync(Expr.This thisExpr) => new(EvaluateThis(thisExpr));
    internal ValueTask<RuntimeValue> VisitNewAsync(Expr.New newExpr) => new(EvaluateNewAsync(newExpr));
    internal ValueTask<RuntimeValue> VisitNewTargetAsync(Expr.NewTarget newTarget) => new(_environment.NewTarget);
    internal ValueTask<RuntimeValue> VisitArrayLiteralAsync(Expr.ArrayLiteral array) => new(EvaluateArrayAsync(array));
    internal ValueTask<RuntimeValue> VisitObjectLiteralAsync(Expr.ObjectLiteral obj) => new(EvaluateObjectAsync(obj));
    internal ValueTask<RuntimeValue> VisitGetIndexAsync(Expr.GetIndex getIndex) => EvaluateGetIndexCore(_asyncContext, getIndex);
    internal ValueTask<RuntimeValue> VisitSetIndexAsync(Expr.SetIndex setIndex) => EvaluateSetIndexCore(_asyncContext, setIndex);
    internal ValueTask<RuntimeValue> VisitSuperAsync(Expr.Super super) => new(EvaluateSuper(super));
    internal ValueTask<RuntimeValue> VisitSetSuperAsync(Expr.SetSuper setSuper) => EvaluateSetSuperAsync(setSuper);
    internal ValueTask<RuntimeValue> VisitCompoundAssignAsync(Expr.CompoundAssign compound) => EvaluateCompoundAssignCore(_asyncContext, compound);
    internal ValueTask<RuntimeValue> VisitCompoundSetAsync(Expr.CompoundSet compoundSet) => EvaluateCompoundSetCore(_asyncContext, compoundSet);
    internal ValueTask<RuntimeValue> VisitCompoundSetPrivateAsync(Expr.CompoundSetPrivate compoundSet) => EvaluateCompoundSetPrivateCore(_asyncContext, compoundSet);
    internal ValueTask<RuntimeValue> VisitCompoundSetIndexAsync(Expr.CompoundSetIndex compoundSetIndex) => EvaluateCompoundSetIndexCore(_asyncContext, compoundSetIndex);
    internal ValueTask<RuntimeValue> VisitLogicalAssignAsync(Expr.LogicalAssign logical) => EvaluateLogicalAssignCore(_asyncContext, logical);
    internal ValueTask<RuntimeValue> VisitLogicalSetAsync(Expr.LogicalSet logicalSet) => EvaluateLogicalSetCore(_asyncContext, logicalSet);
    internal ValueTask<RuntimeValue> VisitLogicalSetPrivateAsync(Expr.LogicalSetPrivate logicalSet) => EvaluateLogicalSetPrivateCore(_asyncContext, logicalSet);
    internal ValueTask<RuntimeValue> VisitLogicalSetIndexAsync(Expr.LogicalSetIndex logicalSetIndex) => EvaluateLogicalSetIndexCore(_asyncContext, logicalSetIndex);
    internal ValueTask<RuntimeValue> VisitPrefixIncrementAsync(Expr.PrefixIncrement prefix) => new(EvaluatePrefixIncrementAsync(prefix));
    internal ValueTask<RuntimeValue> VisitPostfixIncrementAsync(Expr.PostfixIncrement postfix) => new(EvaluatePostfixIncrementAsync(postfix));
    internal ValueTask<RuntimeValue> VisitArrowFunctionAsync(Expr.ArrowFunction arrow) => new(EvaluateArrowFunction(arrow));
    internal ValueTask<RuntimeValue> VisitTemplateLiteralAsync(Expr.TemplateLiteral template) => new(EvaluateTemplateLiteralAsync(template));
    internal ValueTask<RuntimeValue> VisitTaggedTemplateLiteralAsync(Expr.TaggedTemplateLiteral tagged) => new(EvaluateTaggedTemplateLiteralAsync(tagged));
    internal ValueTask<RuntimeValue> VisitSpreadAsync(Expr.Spread spread) => EvaluateAsync(spread.Expression);
    internal ValueTask<RuntimeValue> VisitAssertedAssignmentAsync(Expr.AssertedAssignment assignment) => EvaluateAssertedAssignmentCore(_asyncContext, assignment);
    internal ValueTask<RuntimeValue> VisitInstantiationAsync(Expr.Instantiation expression) => EvaluateAsync(expression.Expression);
    internal ValueTask<RuntimeValue> VisitTypeAssertionAsync(Expr.TypeAssertion ta) => EvaluateAsync(ta.Expression);
    internal ValueTask<RuntimeValue> VisitSatisfiesAsync(Expr.Satisfies sat) => EvaluateAsync(sat.Expression);
    internal ValueTask<RuntimeValue> VisitNonNullAssertionAsync(Expr.NonNullAssertion nna) => EvaluateAsync(nna.Expression);
    internal ValueTask<RuntimeValue> VisitAwaitAsync(Expr.Await awaitExpr) => new(EvaluateAwaitAsync(awaitExpr));
    internal ValueTask<RuntimeValue> VisitDynamicImportAsync(Expr.DynamicImport di) => new(EvaluateDynamicImport(di));
    internal ValueTask<RuntimeValue> VisitImportMetaAsync(Expr.ImportMeta im) => new(EvaluateImportMeta(im));
    internal ValueTask<RuntimeValue> VisitYieldAsync(Expr.Yield yieldExpr) => new(EvaluateYieldAsync(yieldExpr));
    internal ValueTask<RuntimeValue> VisitRegexLiteralAsync(Expr.RegexLiteral regex) => new(RuntimeValue.FromObject(new SharpTSRegExp(regex.Pattern, regex.Flags)));
    internal ValueTask<RuntimeValue> VisitClassExprAsync(Expr.ClassExpr classExpr) => EvaluateClassExpressionCore(_asyncContext, classExpr);

    /// <summary>
    /// Evaluates a yield expression, throwing YieldException for control flow.
    /// </summary>
    /// <param name="yieldExpr">The yield expression AST node.</param>
    /// <returns>Never returns normally - always throws YieldException.</returns>
    /// <remarks>
    /// Yield expressions suspend generator execution by throwing YieldException,
    /// which is caught by SharpTSGenerator.Next() to extract the yielded value.
    /// For yield*, the IsDelegating flag indicates delegation to another iterable.
    /// </remarks>
    private RuntimeValue EvaluateYield(Expr.Yield yieldExpr)
    {
        object? value = yieldExpr.Value != null ? Evaluate(yieldExpr.Value) : null;

        // If a yield callback is set (coroutine-based generator), call it directly
        // instead of throwing. This suspends the worker thread without unwinding
        // the interpreter's call stack, preserving environment state.
        if (YieldCallback != null)
        {
            var result = YieldCallback(value, yieldExpr.IsDelegating);
            // For a plain `yield`, the callback returns the exact value passed to the
            // resuming next(v) — delivered verbatim so `next(null)` yields null and a
            // bare next() yields undefined (the generator normalizes that to undefined).
            // For `yield*`, the completion value of a non-generator delegate is undefined;
            // coalesce null → undefined to preserve that.
            if (yieldExpr.IsDelegating)
                return RuntimeValue.FromBoxed(result ?? SharpTSUndefined.Instance);
            return RuntimeValue.FromBoxed(result);
        }

        throw new YieldException(value, yieldExpr.IsDelegating);
    }

    /// <summary>
    /// Evaluates a <c>yield</c>/<c>yield*</c> inside an async generator body. The body runs as an
    /// ordinary interpreter async execution, so the yielded expression is evaluated asynchronously
    /// (supporting <c>yield await x</c>) and the suspension is delegated to the active async generator
    /// (<see cref="Runtime.Types.SharpTSAsyncGenerator.OnYieldAsync"/>), which hands the value to the
    /// driving <c>next()</c> and awaits the resume. Falls back to the synchronous <c>yield</c> path when
    /// no async generator is active (a misplaced yield reached via the async dispatcher).
    /// </summary>
    private async Task<RuntimeValue> EvaluateYieldAsync(Expr.Yield yieldExpr)
    {
        var activeGenerator = CurrentAsyncGenerator;
        if (activeGenerator == null)
            return EvaluateYield(yieldExpr);

        object? value = yieldExpr.Value != null
            ? (await EvaluateAsync(yieldExpr.Value)).ToObject()
            : SharpTSUndefined.Instance;

        object? result = await activeGenerator.OnYieldAsync(value, yieldExpr.IsDelegating);

        // Both a sent value and a delegate completion preserve explicit null.
        // Iterables without a completion value already return the undefined sentinel.
        return RuntimeValue.FromBoxed(result);
    }

    /// <summary>
    /// Evaluates an await expression, unwrapping the Promise value.
    /// </summary>
    private async Task<RuntimeValue> EvaluateAwaitAsync(Expr.Await awaitExpr)
    {
        object? value = (await EvaluateAsync(awaitExpr.Expression)).ToObject();

        // Await reuses a native Promise only when its live constructor has
        // intrinsic Promise identity; subclass promises otherwise assimilate.
        if (value is SharpTSPromise promise)
        {
            return RuntimeValue.FromBoxed(await AwaitPreservingEnvironment(QueueNativeAwait(NormalizePromiseForAwait(promise))));
        }

        // Raw Task<object?> — returned by runtime methods (e.g., Web Streams
        // read()) that need to be awaitable in both interpreter and compiled
        // modes. Compiled mode's await already recognises Task<object>; adding
        // the interpreter branch lets a single BuiltInMethod body work in
        // both without wrapping in SharpTSPromise (which compiled await does
        // not unwrap).
        if (value is Task<object?> task)
        {
            return RuntimeValue.FromBoxed(await AwaitPreservingEnvironment(QueueNativeAwait(task)));
        }

        // Thenable adoption (ECMA-262 await → PromiseResolve, §25.6.4.5 step 8 /
        // §27.2.1.3.2): an ordinary object exposing a callable `then` is adopted
        // by invoking then(resolve, reject) and awaiting the captured capability.
        // SharpTSPromise is handled above; only plain guest thenables reach here —
        // including the general non-Promise then/catch/finally species results
        // produced by NewPromiseCapability (#349).
        if (TryGetThenable(value, out var thenFn))
        {
            return RuntimeValue.FromBoxed(await AwaitPreservingEnvironment(AdoptThenable(value!, thenFn)));
        }

        // Even an already available value resumes Await in a Promise job.
        // Preserve the async frame's environment and generator state through
        // the same suspension path as a genuinely pending guest promise.
        var completion = new TaskCompletionSource<object?>();
        EnqueuePromiseJob(() => completion.TrySetResult(value));
        return RuntimeValue.FromBoxed(await AwaitPreservingEnvironment(completion.Task));
    }

    private Task<object?> QueueNativeAwait(Task<object?> task)
    {
        // A native Await reaction becomes eligible only when its input settles.
        // Queue it behind existing jobs even when settlement happens inside a
        // different Await job. Resume inline within this one queued reaction.
        var completion = new TaskCompletionSource<object?>();
        if (task.IsCompleted)
            EnqueuePromiseJob(() => TransferCompletion(task, completion));
        else
            _ = task.ContinueWith(
                settled => EnqueuePromiseJob(() => TransferCompletion(settled, completion)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        return completion.Task;
    }

    /// <summary>
    /// Detects an ordinary guest thenable: a <see cref="SharpTSObject"/> or
    /// <see cref="SharpTSInstance"/> with a callable <c>then</c> member.
    /// Promise/Task values are recognised by the caller before this is reached.
    /// </summary>
    private bool TryGetThenable(object? value, [NotNullWhen(true)] out ISharpTSCallable? thenFn)
    {
        thenFn = null;
        if (value is not (SharpTSObject or SharpTSInstance))
            return false;
        if (GetProperty(value, "then") is ISharpTSCallable fn)
        {
            thenFn = fn;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Adopts a guest thenable into a host task: invokes <c>then(resolve, reject)</c>
    /// with the receiver bound, settling the returned task from whichever callback
    /// fires first. A synchronous throw from <c>then</c> rejects the task
    /// (ECMA-262 §27.2.1.3.2 — a throw after resolve/reject is ignored).
    /// </summary>
    private Task<object?> AdoptThenable(object thenable, ISharpTSCallable thenFn)
    {
        var tcs = new TaskCompletionSource<object?>();
        var resolve = new PromiseResolveCallback(v =>
        {
            // Flatten a promise resolution value the same way the executor
            // resolve callback does, so `await` yields the eventual value. Routed
            // through the event loop (not the thread pool) so the loop can't exit
            // before the adoption settles — see Interpreter.AdoptInnerPromise.
            if (v is SharpTSPromise inner)
                AdoptInnerPromise(inner.Task, tcs);
            else
                tcs.TrySetResult(v);
        });
        var reject = new PromiseRejectCallback(r => tcs.TrySetException(new SharpTSPromiseRejectedException(r)));

        try
        {
            FunctionBuiltIns.CallWithThis(this, thenFn, thenable, [resolve, reject]);
        }
        catch (ThrowException tex)
        {
            tcs.TrySetException(new SharpTSPromiseRejectedException(tex.Value));
        }
        return tcs.Task;
    }

    /// <summary>
    /// Evaluates a literal expression, wrapping BigInteger values in SharpTSBigInt.
    /// </summary>
    /// <param name="literal">The literal expression AST node.</param>
    /// <returns>The literal value, with BigInteger wrapped in SharpTSBigInt.</returns>
    private RuntimeValue EvaluateLiteral(Expr.Literal literal)
    {
        return literal.Value switch
        {
            null => RuntimeValue.Null,
            double d => RuntimeValue.FromNumber(d),
            int i => RuntimeValue.FromNumber(i),
            string s => RuntimeValue.FromString(s),
            bool b => RuntimeValue.FromBoolean(b),
            BigInteger bi => RuntimeValue.FromBigInt(new SharpTSBigInt(bi)),
            _ => RuntimeValue.FromBoxed(literal.Value)
        };
    }

    /// <summary>
    /// Evaluates a template literal (backtick string) with embedded expressions.
    /// </summary>
    /// <param name="template">The template literal expression AST node.</param>
    /// <returns>The interpolated string result.</returns>
    /// <remarks>
    /// Alternates between static string parts and evaluated expressions,
    /// stringifying each expression result before concatenation.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Template_literals">MDN Template Literals</seealso>
    private RuntimeValue EvaluateTemplateLiteral(Expr.TemplateLiteral template)
    {
        // ToString coercion of an interpolated value goes through ToPrimitive
        // (string hint), so a boxed wrapper / object with own toString renders
        // its primitive (`${new String("ab")}` → "ab") rather than "[object Object]" (#708).
        var evaluatedExprs = new List<object?>(template.Expressions.Count);
        foreach (var e in template.Expressions)
            evaluatedExprs.Add(ToPrimitive(Evaluate(e), PrimitiveHint.String));
        return RuntimeValue.FromString(BuildTemplateLiteralString(template.Strings, evaluatedExprs));
    }

    /// <summary>
    /// Evaluates a tagged template literal, invoking the tag function with strings and values.
    /// </summary>
    /// <param name="tagged">The tagged template literal AST node.</param>
    /// <returns>The result of calling the tag function.</returns>
    private RuntimeValue EvaluateTaggedTemplateLiteral(Expr.TaggedTemplateLiteral tagged)
    {
        object? tag = EvaluateMemberCallReference(_syncContext, tagged.Tag).GetAwaiter().GetResult();

        // Create template strings array with raw property
        // Cooked values: null becomes undefined (or just null in our runtime)
        var cookedList = tagged.CookedStrings.Cast<object?>().ToList();
        var stringsArray = new SharpTSTemplateStringsArray(cookedList, tagged.RawStrings);

        // Evaluate all expression arguments
        List<object?> args = [stringsArray];
        foreach (var expr in tagged.Expressions)
            args.Add(Evaluate(expr));

        if (tag is not ISharpTSCallable callable)
            throw new ThrowException(new SharpTSTypeError($"{GetTypeofString(tag)} is not a function"));

        return RuntimeValue.FromBoxed(callable.Call(this, args));
    }

    /// <summary>
    /// Evaluates a lexical super property access with the current receiver.
    /// </summary>
    /// <param name="expr">The super expression AST node.</param>
    /// <returns>The property value, or a constructor callable for super().</returns>
    /// <remarks>
    /// Retrieves the superclass from the lexical environment and reads its live
    /// prototype chain. Accessors use the actual <c>this</c> receiver; method
    /// data properties retain their canonical identity until invocation.
    /// Constructor calls capture their lexical invocation and execute every parent
    /// class level before installing this owner's instance elements.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#super-calls">TypeScript super Calls</seealso>
    private RuntimeValue EvaluateSuper(Expr.Super expr)
    {
        SharpTSClass superclass = _environment.Get(expr.Keyword).AsObject<SharpTSClass>()!;
        if (expr.Method is null)
        {
            // Capture the lexical invocation before arguments run. Nested new or
            // callbacks must not replace this pending constructor boundary.
            var frame = _environment.ConstructionFrame
                ?? throw new ThrowException(new SharpTSReferenceError("super() is not available outside a constructor"));
            object? parent = frame.Owner.HasExplicitPrototype
                ? ObjectBuiltIns.PrototypeOf(this, frame.Owner) : superclass;
            return RuntimeValue.FromObject(new SuperConstructorCallable(parent, frame, frame.Owner.CanReuseSuperAllocation(parent)));
        }
        // `this` is usually a SharpTSInstance, but built-in-backed instances
        // (Array subclass instances) are SharpTSArrays — bind generically.
        _environment.TryGetThis(out var thisValue);
        object? thisReceiver = thisValue.ToObject();

        if (expr.Method is { } property)
        {
            if (_environment.StaticSuper)
            {
                object? parent = _environment.PrivateClass is { } staticOwner
                    ? ObjectBuiltIns.PrototypeOf(this, staticOwner) : superclass;
                if (parent is null) ThrowCannotReadProperty(parent, property.Lexeme);
                return RuntimeValue.FromBoxed(GetPropertyValueFromChain(parent, property.Lexeme, thisReceiver));
            }

            // The lexical method owner remains fixed when a method is inherited
            // or borrowed, while the home prototype's parent is read live.
            object? superPrototype = _environment.PrivateClass is { } owner
                ? ObjectBuiltIns.PrototypeOf(this, owner.Prototype)
                : superclass.Prototype;
            if (superPrototype is null)
                ThrowCannotReadProperty(superPrototype, property.Lexeme);
            return RuntimeValue.FromBoxed(GetPropertyValueFromChain(
                superPrototype, property.Lexeme, thisReceiver));
        }

        throw new InvalidOperationException("Expected a super property.");
    }

    private sealed class SuperConstructorCallable(
        object? superclass, ClassConstructionFrame frame, bool reuseAllocation) : ISharpTSNonConstructorCallable
    {
        public int Arity() => (superclass as ISharpTSCallable)?.Arity() ?? 0;
        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            var allocation = frame.BeginSuperAttempt(interpreter);
            var parentResult = SharpTSClass.ExecuteSuperConstructor(
                interpreter, superclass, allocation, arguments, frame.NewTarget, reuseAllocation);
            return frame.CompleteSuper(interpreter, parentResult);
        }
    }

    /// <summary>
    /// Evaluates an arrow function expression, creating a callable closure.
    /// </summary>
    /// <param name="arrow">The arrow function expression AST node.</param>
    /// <returns>A <see cref="SharpTSArrowFunction"/> or <see cref="SharpTSAsyncArrowFunction"/> that captures the current environment.</returns>
    /// <remarks>
    /// Arrow functions capture their lexical environment at creation time,
    /// enabling closures over outer variables. Async arrow functions return a Promise.
    /// For named function expressions, the function name is visible inside the function body
    /// for recursion, but not outside.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/functions.html#arrow-functions">TypeScript Arrow Functions</seealso>
    private RuntimeValue EvaluateArrowFunction(Expr.ArrowFunction arrow)
    {
        // Named function expressions (e.g. `var f = function myFn(n) { return myFn(n-1); }`)
        // bind the self-reference inside the function's own call environment (alongside
        // parameters), not in an enclosing wrapper scope. This matches how the resolver
        // tracks the name — pushing ONE scope that contains the name plus params — so
        // the locals cache's distance values remain correct for outer-variable captures.
        // See SharpTSArrowFunction.Call for where the self-binding is installed.
        RuntimeEnvironment closure = _environment;

        ISharpTSCallable func;
        if (arrow.IsAsync && arrow.IsGenerator)
        {
            // Async generator function expressions (async function*) — wrap in an async-generator-
            // creating function. Dispatched before the plain-async branch so an async generator
            // expression that closes over a block-scoped binding (left in place by GeneratorArrowLifter)
            // runs natively rather than being mishandled as a plain async function (#734).
            func = new SharpTSAsyncArrowGeneratorFunction(arrow, closure, arrow.HasOwnThis, creationRealm: this);
        }
        else if (arrow.IsAsync)
        {
            func = new SharpTSAsyncArrowFunction(arrow, closure, arrow.HasOwnThis);
        }
        else if (arrow.IsGenerator)
        {
            // Generator function expressions - wrap in a generator-creating function
            // Note: This uses a different wrapper than Stmt.Function generators
            func = new SharpTSArrowGeneratorFunction(arrow, closure, arrow.HasOwnThis, creationRealm: this);
        }
        else
        {
            func = new SharpTSArrowFunction(arrow, closure, arrow.HasOwnThis);
        }

        return RuntimeValue.FromObject(func);
    }

    /// <summary>
    /// Gets the string name from a property key (sync version).
    /// </summary>
    private object GetPropertyKeyName(Expr.PropertyKey key)
    {
        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            Expr.ComputedKey ck => Evaluate(ck.Expression) switch
            {
                SharpTSSymbol symbol => symbol,
                var value => ToPropertyKeyString(value),
            },
            _ => throw new InterpreterException("Invalid property key for accessor.")
        };
    }

    /// <summary>
    /// Gets the string name from a property key using an evaluation context.
    /// Shared between sync and async paths via IEvaluationContext.
    /// </summary>
    private async ValueTask<object> GetPropertyKeyNameCore(IEvaluationContext ctx, Expr.PropertyKey key)
    {
        if (key is Expr.ComputedKey computed)
        {
            object? value = (await ctx.EvaluateExprAsync(computed.Expression)).ToObject();
            return value is SharpTSSymbol symbol ? symbol : ToPropertyKeyString(value);
        }

        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            _ => throw new InterpreterException("Invalid property key for accessor.")
        };
    }

    /// <summary>
    /// Applies a property key-value pair to the target fields dictionaries (sync version).
    /// </summary>
    private Dictionary<SharpTSSymbol, object?>? ApplyPropertyToFields(
        Expr.PropertyKey key,
        object? value,
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?>? symbolFields)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                stringFields[ik.Name.Lexeme] = value;
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                stringFields[(string)lk.Literal.Literal!] = value;
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                stringFields[lk.Literal.Literal!.ToString()!] = value;
                break;
            case Expr.ComputedKey ck:
                object? keyValue = Evaluate(ck.Expression);
                if (keyValue is SharpTSSymbol sym)
                    (symbolFields ??= [])[sym] = value;
                else if (keyValue is double numKey)
                    stringFields[numKey.ToString()] = value;
                else
                    stringFields[keyValue?.ToString() ?? "undefined"] = value;
                break;
        }
        return symbolFields;
    }

    /// <summary>
    /// Applies a property key-value pair to the target fields dictionaries using an evaluation context.
    /// Shared between sync and async paths via IEvaluationContext.
    /// </summary>
    private async ValueTask<Dictionary<SharpTSSymbol, object?>?>
        ApplyPropertyToFieldsCore(
        IEvaluationContext ctx,
        Expr.PropertyKey key,
        object? value,
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?>? symbolFields)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                stringFields[ik.Name.Lexeme] = value;
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                stringFields[(string)lk.Literal.Literal!] = value;
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                stringFields[lk.Literal.Literal!.ToString()!] = value;
                break;
            case Expr.ComputedKey ck:
                object? keyValue = (await ctx.EvaluateExprAsync(ck.Expression)).ToObject();
                if (keyValue is SharpTSSymbol sym)
                    (symbolFields ??= [])[sym] = value;
                else if (keyValue is double numKey)
                    stringFields[numKey.ToString()] = value;
                else
                    stringFields[keyValue?.ToString() ?? "undefined"] = value;
                break;
        }
        return symbolFields;
    }

    /// <summary>
    /// Core implementation for evaluating object literals, shared between sync and async paths.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating property values and keys.</param>
    /// <param name="obj">The object literal expression AST node.</param>
    /// <returns>A ValueTask containing the evaluated object.</returns>
    private async ValueTask<object?> EvaluateObjectCore(IEvaluationContext ctx, Expr.ObjectLiteral obj)
    {
        Dictionary<string, object?> stringFields = [];
        Dictionary<SharpTSSymbol, object?>? symbolFields = null;
        List<(object key, ISharpTSCallable getter)>? getters = null;
        List<(object key, ISharpTSCallable setter)>? setters = null;

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = (await ctx.EvaluateExprAsync(prop.Value)).ToObject();
                symbolFields = ApplySpreadToFields(spreadValue, stringFields, symbolFields);
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter)
            {
                object name = await GetPropertyKeyNameCore(ctx, prop.Key!);
                var getter = CreateAccessorFunction(prop.Value);
                getters ??= [];
                getters.Add((name, getter));
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Setter)
            {
                object name = await GetPropertyKeyNameCore(ctx, prop.Key!);
                var setter = CreateSetterFunction(prop.Value, prop.SetterParam!);
                setters ??= [];
                setters.Add((name, setter));
            }
            else
            {
                object? value = (await ctx.EvaluateExprAsync(prop.Value)).ToObject();
                symbolFields = await ApplyPropertyToFieldsCore(
                    ctx, prop.Key!, value, stringFields, symbolFields);
            }
        }

        var result = BuildObjectFromFields(stringFields, symbolFields);

        // Apply getters and setters
        if (getters != null)
        {
            foreach (var (name, getter) in getters)
            {
                if (name is SharpTSSymbol symbol)
                {
                    result.TryGetSymbolAccessor(symbol, out _, out var setter);
                    result.DefineSymbolAccessor(symbol, getter, setter);
                }
                else
                    result.DefineGetter((string)name, getter);
            }
        }
        if (setters != null)
        {
            foreach (var (name, setter) in setters)
            {
                if (name is SharpTSSymbol symbol)
                {
                    result.TryGetSymbolAccessor(symbol, out var getter, out _);
                    result.DefineSymbolAccessor(symbol, getter, setter);
                }
                else
                    result.DefineSetter((string)name, setter);
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates an object literal expression, creating a runtime object.
    /// </summary>
    /// <param name="obj">The object literal expression AST node.</param>
    /// <returns>A <see cref="SharpTSObject"/> containing the evaluated properties.</returns>
    /// <remarks>
    /// Supports spread properties (<c>...obj</c>) which copy all enumerable properties
    /// from the source object or instance.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    private RuntimeValue EvaluateObject(Expr.ObjectLiteral obj)
    {
        Dictionary<string, object?> stringFields = [];
        Dictionary<SharpTSSymbol, object?>? symbolFields = null;
        List<(object key, ISharpTSCallable getter)>? getters = null;
        List<(object key, ISharpTSCallable setter)>? setters = null;

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = Evaluate(prop.Value);
                symbolFields = ApplySpreadToFields(spreadValue, stringFields, symbolFields);
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter)
            {
                object name = GetPropertyKeyName(prop.Key!);
                var getter = CreateAccessorFunction(prop.Value);
                getters ??= [];
                getters.Add((name, getter));
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Setter)
            {
                object name = GetPropertyKeyName(prop.Key!);
                var setter = CreateSetterFunction(prop.Value, prop.SetterParam!);
                setters ??= [];
                setters.Add((name, setter));
            }
            else
            {
                object? value = Evaluate(prop.Value);
                symbolFields = ApplyPropertyToFields(
                    prop.Key!, value, stringFields, symbolFields);
            }
        }

        var result = BuildObjectFromFields(stringFields, symbolFields);

        // Apply getters and setters
        if (getters != null)
        {
            foreach (var (name, getter) in getters)
            {
                if (name is SharpTSSymbol symbol)
                {
                    result.TryGetSymbolAccessor(symbol, out _, out var setter);
                    result.DefineSymbolAccessor(symbol, getter, setter);
                }
                else
                    result.DefineGetter((string)name, getter);
            }
        }
        if (setters != null)
        {
            foreach (var (name, setter) in setters)
            {
                if (name is SharpTSSymbol symbol)
                {
                    result.TryGetSymbolAccessor(symbol, out var getter, out _);
                    result.DefineSymbolAccessor(symbol, getter, setter);
                }
                else
                    result.DefineSetter((string)name, setter);
            }
        }

        return RuntimeValue.FromObject(result);
    }

    /// <summary>
    /// Creates an accessor function (getter) from the function expression.
    /// </summary>
    private SharpTSArrowFunction CreateAccessorFunction(Expr body)
    {
        // The body should be a function or arrow function expression
        if (body is Expr.ArrowFunction arrow)
        {
            return EvaluateArrowFunction(arrow).ToObject() as SharpTSArrowFunction
                   ?? throw new InterpreterException("Failed to create getter function.");
        }
        throw new InterpreterException("Getter must be a function expression.");
    }

    /// <summary>
    /// Creates a setter function from the function expression and parameter.
    /// </summary>
    private SharpTSArrowFunction CreateSetterFunction(Expr body, Stmt.Parameter setterParam)
    {
        // The body should be a function expression
        if (body is Expr.ArrowFunction arrow)
        {
            return EvaluateArrowFunction(arrow).ToObject() as SharpTSArrowFunction
                   ?? throw new InterpreterException("Failed to create setter function.");
        }
        throw new InterpreterException("Setter must be a function expression.");
    }

    /// <summary>
    /// Applies a spread value's properties to the target fields dictionary.
    /// Shared between sync and async object evaluation paths.
    /// </summary>
    private Dictionary<SharpTSSymbol, object?>? ApplySpreadToFields(
        object? spreadValue,
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?>? symbolFields)
    {
        // CopyDataProperties skips null/undefined. Other primitives are
        // ToObject-coerced: only strings contribute enumerable index keys;
        // number/boolean/symbol/bigint wrappers have none.
        if (spreadValue is null or SharpTSUndefined)
            return symbolFields;
        if (spreadValue is string text)
        {
            for (int i = 0; i < text.Length; i++)
                stringFields[i.ToString()] = text[i].ToString();
            return symbolFields;
        }
        if (spreadValue is bool or double or int or long or float or decimal
            or SharpTSSymbol or SharpTSBigInt or System.Numerics.BigInteger)
            return symbolFields;

        if (spreadValue is ISharpTSOwnPropertyObject ownPropertyObject)
        {
            var properties = ownPropertyObject.OwnProperties;
            var stringKeys = properties.OwnVisibleStringKeys().ToList();
            var symbolKeys = properties.GetSymbolPropertyNames().ToList();
            foreach (var key in stringKeys)
                if (properties.GetOwnPropertyDescriptor(key) is { Enumerable: true })
                    stringFields[key] = GetPropertyValue(spreadValue, key);
            foreach (var symbol in symbolKeys)
                if (properties.GetOwnPropertyDescriptor(symbol) is { Enumerable: true })
                    (symbolFields ??= [])[symbol] = GetSymbolPropertyValue(spreadValue, symbol);
        }
        else if (spreadValue is SharpTSObject spreadObj)
        {
            if (spreadObj.TryCopyPlainSpreadFields(stringFields))
                return symbolFields;
            // CopyDataProperties obtains one complete [[OwnPropertyKeys]] snapshot before
            // consulting descriptors or invoking getters. Descriptor and value reads remain
            // live, but getter-driven string/Symbol additions cannot join this spread.
            var stringKeys = spreadObj.OwnVisibleStringKeys().ToList();
            var symbolKeys = spreadObj.GetSymbolPropertyNames().ToList();
            foreach (string key in stringKeys)
            {
                if (!spreadObj.IsOwnEnumerableProperty(key))
                    continue;
                stringFields[key] = GetPropertyValue(spreadObj, key);
            }
            foreach (SharpTSSymbol symbol in symbolKeys)
            {
                if (!spreadObj.IsOwnEnumerableProperty(symbol))
                    continue;
                (symbolFields ??= [])[symbol] = GetSymbolPropertyValue(spreadObj, symbol);
            }
        }
        else if (spreadValue is SharpTSClassPrototype prototype)
        {
            foreach (var key in ObjectBuiltIns.GetClassPrototypeOwnKeys(prototype))
            {
                if (ObjectBuiltIns.GetClassPrototypeOwnDescriptor(prototype, key)?.Enumerable != true)
                    continue;
                if (key is SharpTSSymbol symbol)
                    (symbolFields ??= [])[symbol] = GetSymbolPropertyValue(prototype, symbol);
                else
                    stringFields[(string)key] = GetPropertyValue(prototype, (string)key);
            }
        }
        else if (spreadValue is SharpTSInstance inst)
        {
            var stringKeys = inst.GetFieldNames().ToList();
            var symbolKeys = inst.GetSymbolPropertyNames().ToList();
            foreach (string key in stringKeys)
            {
                if (inst.GetOwnPropertyDescriptor(key)?.Enumerable != true)
                    continue;
                stringFields[key] = GetPropertyValue(inst, key);
            }
            foreach (SharpTSSymbol symbol in symbolKeys)
                (symbolFields ??= [])[symbol] = GetSymbolPropertyValue(inst, symbol);
        }
        else if (spreadValue is SharpTSProxy proxy)
        {
            // Keep the Proxy algorithm unified: one mixed [[OwnPropertyKeys]] snapshot, then an
            // observable descriptor query and [[Get]] for each still-enumerable key.
            foreach (object? key in proxy.TrapOwnPropertyKeys(this))
            {
                object? descriptor = key switch
                {
                    string name => proxy.TrapGetOwnPropertyDescriptor(name, this),
                    SharpTSSymbol symbol => proxy.TrapGetOwnPropertyDescriptor(symbol, this),
                    _ => null
                };
                if (descriptor is null or SharpTSUndefined
                    || !SharpTSPropertyDescriptor.FromAnyObject(descriptor).Enumerable)
                    continue;

                if (key is SharpTSSymbol symbolKey)
                    (symbolFields ??= [])[symbolKey] = proxy.TrapGet(symbolKey, this);
                else
                    stringFields[(string)key!] = proxy.TrapGet((string)key!, this);
            }
        }
        // Plain Dictionary<string, object?> — used by runtime helpers like
        // Web Streams iterator results. Compiled mode already handles this in
        // MergeIntoObject; this branch keeps the interpreter at parity.
        else if (spreadValue is IDictionary<string, object?> dict)
        {
            foreach (var kv in dict)
            {
                stringFields[kv.Key] = kv.Value;
            }
        }
        else
        {
            throw new InterpreterException("Spread in object literal requires an object.");
        }
        return symbolFields;
    }

    /// <summary>
    /// Core implementation for evaluating array literals, shared between sync and async paths.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating elements.</param>
    /// <param name="array">The array literal expression AST node.</param>
    /// <returns>A ValueTask containing the evaluated array.</returns>
    private async ValueTask<object?> EvaluateArrayCore(IEvaluationContext ctx, Expr.ArrayLiteral array)
    {
        var evaluated = new List<(bool isSpread, object? value)>();
        for (int i = 0; i < array.Elements.Count; i++)
        {
            // Elided positions ([1, , 3]) become true holes, not undefined elements.
            if (array.IsHole(i))
            {
                evaluated.Add((false, ArrayHole.Instance));
                continue;
            }
            var e = array.Elements[i];
            var isSpread = e is Expr.Spread;
            var value = (await ctx.EvaluateExprAsync(isSpread ? ((Expr.Spread)e).Expression : e)).ToObject();
            evaluated.Add((isSpread, value));
        }
        return BuildArrayFromElements(evaluated);
    }

    /// <summary>
    /// Evaluates an array literal expression, creating a runtime array.
    /// </summary>
    /// <param name="array">The array literal expression AST node.</param>
    /// <returns>A <see cref="SharpTSArray"/> containing the evaluated elements.</returns>
    /// <remarks>
    /// Supports spread elements (<c>...arr</c>) which expand array contents inline.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#arrays">TypeScript Arrays</seealso>
    private RuntimeValue EvaluateArray(Expr.ArrayLiteral array)
    {
        var evaluated = new List<(bool isSpread, object? value)>();
        for (int i = 0; i < array.Elements.Count; i++)
        {
            // Elided positions ([1, , 3]) become true holes, not undefined elements.
            if (array.IsHole(i))
            {
                evaluated.Add((false, ArrayHole.Instance));
                continue;
            }
            var e = array.Elements[i];
            var isSpread = e is Expr.Spread;
            var value = Evaluate(isSpread ? ((Expr.Spread)e).Expression : e);
            evaluated.Add((isSpread, value));
        }
        return RuntimeValue.FromObject(BuildArrayFromElements(evaluated));
    }

    /// <summary>
    /// Resolves an (object, index) pair to a typed IndexTarget for dispatch.
    /// </summary>
    /// <param name="obj">The object being indexed.</param>
    /// <param name="index">The index value.</param>
    /// <returns>An IndexTarget discriminated union representing the resolved target.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private IndexTarget ResolveIndexTarget(object? obj, object? index)
    {
        if (obj is SharpTSArray array)
        {
            if (index is SharpTSSymbol symbol)
                return new IndexTarget.ArraySymbol(array, symbol);
            string key = ToPropertyKeyString(index);
            if (long.TryParse(key, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long parsed)
                && parsed >= 0 && parsed <= SharpTSArray.MaxWriteIndex)
            {
                return new IndexTarget.Array(array, parsed);
            }
            return new IndexTarget.ArrayString(array, key);
        }

        return (obj, index) switch
        {
        (SharpTSTypedArray typedArray, double typedIdx) => new IndexTarget.TypedArray(typedArray, (int)typedIdx),
        (SharpTSBuffer buffer, double bufIdx) => new IndexTarget.Buffer(buffer, (int)bufIdx),
        (SharpTSEnum enumObj, double enumIdx) => new IndexTarget.EnumReverse(enumObj, enumIdx),
        (ConstEnumValues constEnum, _) => new IndexTarget.ConstEnumError(constEnum),
        // Symbol keys keep their identity through the symbol-dict path.
        (SharpTSObject symbolObj, SharpTSSymbol symbol) => new IndexTarget.ObjectSymbol(symbolObj, symbol),
        (SharpTSInstance symInstance, SharpTSSymbol symKey) => new IndexTarget.InstanceSymbol(symInstance, symKey),
        // Non-Symbol keys go through ECMA-262 §7.1.19 ToPropertyKey so `obj[-0]`
        // and `obj["0"]` resolve identically (and undefined/null/bool keys
        // stringify to "undefined"/"null"/"true"/"false" rather than landing in
        // the Unsupported bucket).
        (SharpTSObject sharpObj, _) => new IndexTarget.ObjectString(sharpObj, ToPropertyKeyString(index)),
        (SharpTSInstance instance, _) => new IndexTarget.InstanceString(instance, ToPropertyKeyString(index)),
        (SharpTSGlobalThis globalThis, string globalKey) => new IndexTarget.GlobalThis(globalThis, globalKey),
        (SharpTSClassPrototype prototype, SharpTSSymbol symbol) => new IndexTarget.ClassPrototypeSymbol(prototype, symbol),
        (ISharpTSMutableBuiltIn builtInPrototype, _) =>
            new IndexTarget.BuiltInPrototypeString(
                builtInPrototype, ToPropertyKeyString(index)),
        (SharpTSHeaders headers, string headerKey) => new IndexTarget.HeadersString(headers, headerKey),
        // Class constructors accept expando statics (Node: `C["foo"] = 1`,
        // `C[Symbol.species] = P`). #262.
        (SharpTSClass cls, SharpTSSymbol clsSym) => new IndexTarget.ClassSymbol(cls, clsSym),
        (SharpTSClass cls, _) => new IndexTarget.ClassString(cls, ToPropertyKeyString(index)),
        (string str, double strIdx)
            when double.IsFinite(strIdx)
                && strIdx >= 0
                && strIdx <= int.MaxValue
                && Math.Truncate(strIdx) == strIdx
            => new IndexTarget.StringChar(str, (int)strIdx),
        (string str, double) => new IndexTarget.StringChar(str, -1),
            _ => new IndexTarget.Unsupported(obj, index)
        };
    }

    /// <summary>
    /// Evaluates an index access expression (bracket notation).
    /// </summary>
    /// <param name="getIndex">The index access expression AST node.</param>
    /// <returns>The value at the specified index.</returns>
    /// <remarks>
    /// Supports array element access and enum reverse mapping (numeric value to name).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Property_accessors#bracket_notation">MDN Bracket Notation</seealso>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RuntimeValue EvaluateGetIndex(Expr.GetIndex getIndex)
    {
        if (getIndex.Object is Expr.Super { Method: null } super)
        {
            var environment = _environment;
            environment.TryGetThis(out var receiver);
            object? key = EvaluateRV(getIndex.Index).ToObject();
            return EvaluateComputedSuperProperty(super, environment, key, receiver.ToObject());
        }

        // Avoid an async state machine for synchronous indexed reads. This is
        // the inner path for record-array traversal such as back[i].value.
        RuntimeValue objectValue = EvaluateRV(getIndex.Object);
        object? obj = objectValue.ToObject();
        if (getIndex.Optional
            && obj is null or Runtime.Types.SharpTSUndefined)
        {
            return RuntimeValue.Undefined;
        }

        RuntimeValue indexValue = EvaluateRV(getIndex.Index);
        if (obj is SharpTSArray directArray
            && indexValue.Kind == ValueKind.Number)
        {
            double directNumber = indexValue.AsNumberUnsafe();
            if (double.IsFinite(directNumber)
                && directNumber >= 0
                && directNumber <= SharpTSArray.MaxWriteIndex
                && Math.Truncate(directNumber) == directNumber
                && directArray.TryGetPresentDataIndexRV(
                    (long)directNumber, out RuntimeValue directValue))
            {
                return directValue;
            }
        }

        return PerformIndexGet(
            getIndex.Object,
            obj,
            indexValue.ToObject());
    }

    /// <summary>
    /// Core indexed-read logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateGetIndexCore(IEvaluationContext ctx, Expr.GetIndex getIndex)
    {
        if (getIndex.Object is Expr.Super { Method: null } super)
        {
            var environment = ctx.Environment;
            environment.TryGetThis(out var receiver);
            object? key = (await ctx.EvaluateExprAsync(getIndex.Index)).ToObject();
            return EvaluateComputedSuperProperty(super, environment, key, receiver.ToObject());
        }

        RuntimeValue objectValue = await ctx.EvaluateExprAsync(getIndex.Object);
        object? obj = objectValue.ToObject();

        // Optional bracket access: return undefined if object is nullish
        if (getIndex.Optional && (obj == null || obj is Runtime.Types.SharpTSUndefined))
        {
            return RuntimeValue.Undefined;
        }

        RuntimeValue indexValue = await ctx.EvaluateExprAsync(getIndex.Index);
        if (obj is SharpTSArray directArray
            && indexValue.Kind == ValueKind.Number)
        {
            double directNumber = indexValue.AsNumberUnsafe();
            if (double.IsFinite(directNumber)
                && directNumber >= 0
                && directNumber <= SharpTSArray.MaxWriteIndex
                && Math.Truncate(directNumber) == directNumber
                && directArray.TryGetPresentDataIndexRV(
                    (long)directNumber, out RuntimeValue directValue))
            {
                return directValue;
            }
        }

        return PerformIndexGet(
            getIndex.Object,
            obj,
            indexValue.ToObject());
    }

    private RuntimeValue EvaluateComputedSuperProperty(
        Expr.Super super, RuntimeEnvironment environment, object? key, object? receiver)
    {
        // Convert before reading the live home prototype: key conversion may
        // run guest code, but must not replace the captured this receiver.
        object propertyKey = ToPropertyKey(key);
        var superclass = environment.Get(super.Keyword).AsObject<SharpTSClass>()!;
        object? superBase = environment.StaticSuper
            ? environment.PrivateClass is { } staticOwner
                ? ObjectBuiltIns.PrototypeOf(this, staticOwner) : superclass
            : environment.PrivateClass is { } owner
                ? ObjectBuiltIns.PrototypeOf(this, owner.Prototype) : superclass.Prototype;
        if (superBase is null)
            ThrowCannotReadProperty(superBase, propertyKey.ToString()!);
        return RuntimeValue.FromBoxed(propertyKey is SharpTSSymbol symbol
            ? GetSymbolPropertyValue(superBase!, symbol, receiver!)
            : GetPropertyValueFromChain(superBase, (string)propertyKey, receiver));
    }

    /// <summary>
    /// Performs an index read on already-evaluated operands. Shared by the sync
    /// evaluator and the async evaluator (Interpreter.Async.cs) so both contexts
    /// dispatch identically. <paramref name="objectExpr"/> is the unevaluated
    /// receiver expression, needed only to synthesize a Get for the dot-notation
    /// fallback path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RuntimeValue PerformIndexGet(Expr objectExpr, object? obj, object? index)
        => LocalizeBuiltInValue(PerformIndexGetCore(objectExpr, obj, index));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RuntimeValue PerformIndexGetCore(Expr objectExpr, object? obj, object? index)
    {
        // ECMA-262 §13.3.3 RequireObjectCoercible: a bracket read on a nullish base
        // throws a guest TypeError. The optional `?.[]` case is short-circuited by
        // callers (EvaluateGetIndex/EvaluateGetIndexAsync) before reaching here, so
        // any nullish receiver at this point is a non-optional access that must throw.
        // Mirrors the dot-access guard in EvaluateGetOnObject (#676).
        if (obj == null || obj is SharpTSUndefined)
        {
            // Format the key for the message WITHOUT invoking a guest toString:
            // RequireObjectCoercible (this throw) runs before ToPropertyKey, so a
            // throwing key.toString() must not pre-empt the TypeError. Primitive keys
            // (the realistic case) format directly; the object-key fallback uses the
            // host ToString, which does not dispatch the guest toString.
            string key = index switch
            {
                null => "null",
                SharpTSUndefined => "undefined",
                SharpTSSymbol sym => sym.ToString(),
                string s => s,
                bool bk => bk ? "true" : "false",
                double d => Stringify(d),
                _ => index.ToString() ?? ""
            };
            ThrowCannotReadProperty(obj, key);
        }

        // Proxy: intercept index access via get trap
        if (obj is SharpTSProxy proxy)
        {
            if (index is SharpTSSymbol symbol)
                return RuntimeValue.FromBoxed(proxy.TrapGet(symbol, this));
            string key = index?.ToString() ?? "";
            return proxy.TrapGetRV(key, this);
        }

        // A present ordinary array element shadows every prototype property.
        // Take that exact case before the generic symbol/property-key target
        // resolver (which otherwise allocates and walks multiple dispatch
        // layers). Holes and indexed accessors deliberately fall through.
        if (obj is SharpTSArray directArray
            && index is double directNumber
            && double.IsFinite(directNumber)
            && directNumber >= 0
            && directNumber <= SharpTSArray.MaxWriteIndex
            && Math.Truncate(directNumber) == directNumber)
        {
            long directIndex = (long)directNumber;
            if (directArray.TryGetPresentDataIndex(
                    directIndex, out object? directValue))
            {
                return RuntimeValue.FromBoxed(directValue);
            }
        }

        if (obj is ISharpTSOwnPropertyObject)
        {
            object key = ToPropertyKey(index);
            return RuntimeValue.FromBoxed(key is SharpTSSymbol symbol
                ? GetSymbolPropertyValue(obj, symbol)
                : GetPropertyValue(obj, (string)key));
        }

        if (obj is ISharpTSFunctionObject functionObject)
            return RuntimeValue.FromBoxed(index is SharpTSSymbol functionSymbol
                ? GetSymbolPropertyValue(functionObject, functionSymbol)
                : GetPropertyValue(functionObject, ToPropertyKeyString(index)));

        if (obj is ISharpTSPrimitiveConstructor primitiveConstructor && index is SharpTSSymbol constructorSymbol)
            return RuntimeValue.FromBoxed(GetPrimitiveConstructorSymbolValue(
                primitiveConstructor, constructorSymbol, primitiveConstructor));

        if (obj is ISharpTSSymbolPropertyBag symbolBag
            && index is SharpTSSymbol bagSymbol)
        {
            if (symbolBag.TryGetSymbolAccessor(bagSymbol, out var getter, out _))
            {
                return getter is null
                    ? RuntimeValue.Undefined
                    : RuntimeValue.FromBoxed(
                        BindAccessorToObject(getter, obj).Call(this, []));
            }
            if (symbolBag.HasSymbolProperty(bagSymbol))
                return RuntimeValue.FromBoxed(symbolBag.GetBySymbol(bagSymbol));
        }

        if (obj is ISharpTSGeneratorFunctionValue or SharpTSGenerator or SharpTSAsyncGenerator
            && index is SharpTSSymbol generatorSymbol)
            return RuntimeValue.FromBoxed(GetSymbolPropertyValue(obj, generatorSymbol));

        // Ordinary functions use the same descriptor-aware Get for own and
        // inherited members, with one guest property-key conversion.
        if (obj is SharpTSFunction or SharpTSArrowFunction)
        {
            object key = ToPropertyKey(index);
            return RuntimeValue.FromBoxed(key is SharpTSSymbol symbol
                ? GetSymbolPropertyValue(obj, symbol)
                : GetPropertyValue(obj, (string)key));
        }
        if (FunctionObjectProperties.TryGetExplicitPrototype(obj, this, out _))
            return RuntimeValue.FromBoxed(index is SharpTSSymbol inheritedSymbol
                ? GetSymbolPropertyValue(obj!, inheritedSymbol)
                : GetPropertyValue(obj, ToPropertyKeyString(index)));

        // Bound functions and native callable values inherit the same Symbol
        // protocol surface, while classes first consult their static descriptors.
        if (obj is ISharpTSCallable callable && index is SharpTSSymbol callableSymbol)
            return RuntimeValue.FromBoxed(GetSymbolPropertyValue(callable, callableSymbol));

        // Array Symbol reads share the presence-aware protocol lookup, including
        // subclass descriptors and an explicitly replaced prototype chain.
        if (obj is SharpTSArray array && index is SharpTSSymbol arraySymbol)
            return RuntimeValue.FromBoxed(
                GetSymbolPropertyValue(array, arraySymbol));

        if (obj is SharpTSObject symbolObject && index is SharpTSSymbol objectSymbol)
        {
            if (symbolObject.TryGetSymbolAccessor(objectSymbol, out var getter, out _))
            {
                return getter is null
                    ? RuntimeValue.Undefined
                    : RuntimeValue.FromBoxed(
                        BindAccessorToObject(getter, symbolObject).Call(this, []));
            }
            return RuntimeValue.FromBoxed(
                GetSymbolPropertyValue(symbolObject, objectSymbol));
        }

        // CLR indexers are real properties with parameters. Route them before the string-key
        // synthetic dot-property fallback, which would otherwise look for a member named "0".
        if (obj is DotNetInstance { HasReadableIndexer: true } dotNetInstance)
        {
            return RuntimeValue.FromBoxed(dotNetInstance.GetIndex(index, this));
        }

        if (obj is SharpTSMath symbolMath && index is SharpTSSymbol mathSymbol)
        {
            return RuntimeValue.FromBoxed(
                symbolMath.GetBySymbol(mathSymbol) ?? SharpTSUndefined.Instance);
        }
        if (obj is SharpTSStringPrototype stringPrototype
            && index is SharpTSSymbol stringSymbol)
        {
            return RuntimeValue.FromBoxed(
                stringPrototype.GetBySymbol(stringSymbol) ?? SharpTSUndefined.Instance);
        }

        // Built-in namespace singletons and prototype objects resolve dot-notation access via
        // BuiltInRegistry.GetInstanceMember or hand-written fallbacks in EvaluateGetOnFallback
        // (SharpTSArrayGlobal, SharpTSArrayPrototype, SharpTSBuiltInConstructor, etc.).
        // ResolveIndexTarget has no switch cases for them, so bracket access
        // (`Object['create']`, `Array.prototype['pop']`) would otherwise throw
        // "Index access not supported". Lodash relies on these idioms when walking
        // constructors through a lookup table. Mirror the dot-notation path by
        // synthesizing a Get expression and reusing EvaluateGetOnObject.
        if (obj != null && index is string strIndexKey)
        {
            var syntheticName = new Token(TokenType.IDENTIFIER, strIndexKey, null, 0);
            var syntheticGet = new Expr.Get(objectExpr, syntheticName, Optional: false);
            return EvaluateGetOnObject(syntheticGet, obj);
        }

        // Math[n] (numeric index) should behave the same as Math.n — strings
        // and doubles are both legal keys on an extensible object.
        if (obj is SharpTSMath math)
        {
            var key = Stringify(index);
            return RuntimeValue.FromBoxed(math.GetMember(key));
        }

        // Number/Boolean/String.prototype bracket read — extras first, then built-in methods.
        if (obj is SharpTSNumberPrototype numProto)
        {
            var key = PropertyKeyConverter.ToPropertyKeyString(index);
            return RuntimeValue.FromBoxed(numProto.GetMember(key) ?? SharpTSUndefined.Instance);
        }
        if (obj is SharpTSBooleanPrototype boolProto)
        {
            var key = PropertyKeyConverter.ToPropertyKeyString(index);
            return RuntimeValue.FromBoxed(boolProto.GetMember(key) ?? SharpTSUndefined.Instance);
        }
        if (obj is SharpTSStringPrototype strProto)
        {
            var key = PropertyKeyConverter.ToPropertyKeyString(index);
            return RuntimeValue.FromBoxed(strProto.GetMember(key) ?? SharpTSUndefined.Instance);
        }
        if (obj is SharpTSArrayPrototype arrayProto)
        {
            if (index is SharpTSSymbol prototypeSymbol)
                return RuntimeValue.FromBoxed(GetSymbolPropertyValue(arrayProto, prototypeSymbol));
            var key = PropertyKeyConverter.ToPropertyKeyString(index);
            if (arrayProto.GetExtraGetter(key) is { } getter)
                return BindAccessorToObject(getter, arrayProto).CallV2(
                    this, ReadOnlySpan<RuntimeValue>.Empty);
            return RuntimeValue.FromBoxed(
                arrayProto.GetMember(key) ?? SharpTSUndefined.Instance);
        }

        if (obj is ISharpTSSymbolPropertyBag && index is SharpTSSymbol)
            return RuntimeValue.Undefined;

        // ECMA-262 §22.2.5: RegExp.prototype has well-known-symbol-keyed methods
        // (@@match, @@matchAll, @@replace, @@search, @@split). These are bracket-only
        // and don't appear in ResolveIndexTarget — dispatch them here.
        if (obj is SharpTSRegExp regexObj && index is SharpTSSymbol regexSym)
        {
            // A user-set own symbol property (`re[Symbol.match] = false`) shadows
            // the inherited RegExp.prototype well-known-symbol method — IsRegExp
            // depends on this override winning.
            if (regexObj.TryGetSymbolAccessor(regexSym, out var ownGetter, out _))
            {
                return ownGetter is null
                    ? RuntimeValue.Undefined
                    : RuntimeValue.FromBoxed(
                        BindAccessorToObject(ownGetter, regexObj).Call(this, []));
            }
            if (regexObj.TryGetSymbolProperty(regexSym, out var ownSym))
                return RuntimeValue.FromBoxed(ownSym ?? SharpTSUndefined.Instance);
            var prototype = GetRegExpPrototype();
            if (prototype.TryGetSymbolAccessor(regexSym, out var getter, out _))
            {
                return getter is null
                    ? RuntimeValue.Undefined
                    : RuntimeValue.FromBoxed(
                        BindAccessorToObject(getter, regexObj).Call(this, []));
            }
            var member = prototype.HasSymbolProperty(regexSym)
                ? prototype.GetBySymbol(regexSym)
                : null;
            // Property Get returns the exact data-property value. Method-call
            // evaluation binds the receiver later; binding here creates a new
            // wrapper and breaks observable function identity.
            return RuntimeValue.FromBoxed(member ?? SharpTSUndefined.Instance);
        }

        return RuntimeValue.FromBoxed(ResolveIndexTarget(obj, index) switch
        {
            IndexTarget.Array t => GetArrayIndexValue(t.Target, t.Index),
            IndexTarget.ArrayString t => t.Target.HasNamedProperty(t.Key)
                ? GetArrayNamedProperty(t.Target, t.Key, t.Target)
                : SharpTSUndefined.Instance,
            IndexTarget.ArraySymbol t => t.Target.GetBySymbol(t.Key)
                ?? SharpTSUndefined.Instance,
            IndexTarget.TypedArray t => t.Target[t.Index],
            IndexTarget.Buffer t => t.Target[t.Index],
            IndexTarget.EnumReverse t => t.Target.GetReverse(t.Index),
            IndexTarget.ConstEnumError t => throw new InterpreterException(
                $"Runtime Error: Cannot use index access on const enum '{t.Target.Name}'. Const enum members can only be accessed by name."),
            // Route through the same implementation the dotted path uses. A bare
            // GetProperty here skipped getters, the __proto__ walk and the
            // Object.prototype fallback, so `obj[k]` and `obj.k` disagreed — an
            // inherited property was visible to one and not the other.
            IndexTarget.ObjectString t => EvaluateGetOnRecordRV(t.Target, t.Key).ToObject(),
            IndexTarget.ObjectSymbol t => t.Target.GetBySymbol(t.Key) ?? SharpTSUndefined.Instance,
            IndexTarget.InstanceString t => t.Target.Get(new Token(TokenType.IDENTIFIER, t.Key, null, 0)),
            IndexTarget.InstanceSymbol t => GetInstanceSymbolValue(t.Target, t.Key),
            // String keys on classes are normally intercepted by the synthetic-Get
            // path above; this arm catches non-string keys (numbers, booleans)
            // after ToPropertyKey normalization.
            IndexTarget.ClassString t => EvaluateGetOnClass(t.Target, t.Key),
            IndexTarget.ClassSymbol t => GetClassSymbolValue(t.Target, t.Key),
            IndexTarget.ClassPrototypeSymbol t => GetSymbolPropertyValue(t.Target, t.Key),
            IndexTarget.GlobalThis t => ResolveGlobalThisRead(t.Target, t.Key),
            IndexTarget.BuiltInPrototypeString t => ReadBuiltInPrototypeIndex(t.Target, t.Key),
            IndexTarget.HeadersString t => (object?)t.Target.Get(t.Key) ?? SharpTSUndefined.Instance,
            IndexTarget.StringChar t when t.Index >= 0 => t.Index < t.Target.Length
                ? (object)t.Target[t.Index].ToString()
                : GetPropertyValue(t.Target, t.Index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            IndexTarget.StringChar => SharpTSUndefined.Instance,
            IndexTarget.Unsupported => throw new InterpreterException("Index access not supported on this type."),
            _ => throw new InterpreterException("Index access not supported on this type.")
        });
    }

    /// <summary>
    /// Indexed read on a built-in prototype singleton (<c>Object.prototype[1]</c>). Own
    /// extras — including accessors installed via <c>Object.defineProperty</c> — win, then
    /// the type's own built-in member table.
    /// </summary>
    private object? ReadBuiltInPrototypeIndex(ISharpTSMutableBuiltIn prototype, string key)
    {
        if (prototype is SharpTSObjectPrototype objectPrototype)
        {
            if (objectPrototype.GetExtraGetter(key) is { } getter)
                return BindAccessorToObject(getter, objectPrototype).CallBoxed(this, []);
            return objectPrototype.GetMember(key) ?? SharpTSUndefined.Instance;
        }
        return EvaluateGetOnFallback(prototype, key);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RuntimeValue GetArrayIndexValue(SharpTSArray array, long index)
    {
        // Arguments objects are array-like, not Array exotic objects. Adding an
        // index beyond their initial argument count creates an ordinary indexed
        // property without changing the independent `length` data property.
        if (array is SharpTSArguments
            && !array.HasIndex(index)
            && array.HasNamedProperty(index.ToString(
                System.Globalization.CultureInfo.InvariantCulture)))
        {
            return RuntimeValue.FromBoxed(array.GetNamedProperty(index.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (array.TryGetIndexAccessor(index, out var getter, out _))
        {
            if (getter == null) return RuntimeValue.Undefined;
            return BindAccessorToObject(getter, array).CallV2(
                this, ReadOnlySpan<RuntimeValue>.Empty);
        }
        if (!array.HasIndex(index))
        {
            string key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return RuntimeValue.FromBoxed(GetPropertyValueFromChain(
                ObjectBuiltIns.PrototypeOf(this, array), key, array));
        }
        return RuntimeValue.FromBoxed(array.Get(index));
    }

    /// <summary>
    /// Reads a symbol-keyed member from an instance: own symbol data property
    /// first, then a declared symbol-keyed getter (`get [Symbol.x]()`) from the
    /// class chain.
    /// </summary>
    private object? GetInstanceSymbolValue(SharpTSInstance instance, SharpTSSymbol symbol)
    {
        if (TryReadDescriptor(instance.GetOwnPropertyDescriptor(symbol), instance, out var ownValue))
            return ownValue;
        if (instance.UsesDynamicPrototypeChain)
            return instance.Prototype is { } prototype
                ? GetSymbolPropertyValue(prototype, symbol, instance)
                : SharpTSUndefined.Instance;
        var descriptor = instance.GetClass().FindInstanceSymbolPropertyDescriptor(symbol);
        if (!TryReadDescriptor(descriptor, instance, out var value)) return SharpTSUndefined.Instance;
        // A method is an ordinary data-property value. Member calls provide
        // the dynamic receiver separately, preserving identity on every read.
        return value;
    }

    /// <summary>
    /// Reads the nearest symbol-keyed static descriptor, including replacements
    /// of declared methods and own properties shadowing inherited members.
    /// </summary>
    private object? GetClassSymbolValue(SharpTSClass klass, SharpTSSymbol symbol)
        => GetSymbolPropertyValue(klass, symbol, klass);

    /// <summary>
    /// Evaluates an index assignment expression (bracket notation with assignment).
    /// </summary>
    /// <param name="setIndex">The index assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Currently only supports array element assignment.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Property_accessors#bracket_notation">MDN Bracket Notation</seealso>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RuntimeValue EvaluateSetIndex(Expr.SetIndex setIndex)
    {
        RuntimeValue objectValue = EvaluateRV(setIndex.Object);
        RuntimeValue indexValue = EvaluateRV(setIndex.Index);
        RuntimeValue value = EvaluateRV(setIndex.Value);

        if (objectValue.TryAsObject<SharpTSArray>(out var array)
            && array is not SharpTSArguments
            && indexValue.Kind == ValueKind.Number)
        {
            double number = indexValue.AsNumberUnsafe();
            if (double.IsFinite(number)
                && number >= 0
                && number <= SharpTSArray.MaxWriteIndex
                && Math.Truncate(number) == number
                && array.TrySetDenseDataIndex((long)number, value))
            {
                return value;
            }
        }

        return PerformIndexSet(
            objectValue.ToObject(), indexValue.ToObject(), value.ToObject());
    }

    /// <summary>
    /// Core indexed-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateSetIndexCore(IEvaluationContext ctx, Expr.SetIndex setIndex)
    {
        RuntimeValue objectValue = await ctx.EvaluateExprAsync(setIndex.Object);
        RuntimeValue indexValue = await ctx.EvaluateExprAsync(setIndex.Index);
        RuntimeValue value = await ctx.EvaluateExprAsync(setIndex.Value);

        if (objectValue.TryAsObject<SharpTSArray>(out var array)
            && array is not SharpTSArguments
            && indexValue.Kind == ValueKind.Number)
        {
            double number = indexValue.AsNumberUnsafe();
            if (double.IsFinite(number)
                && number >= 0
                && number <= SharpTSArray.MaxWriteIndex
                && Math.Truncate(number) == number
                && array.TrySetDenseDataIndex((long)number, value))
            {
                return value;
            }
        }

        return PerformIndexSet(
            objectValue.ToObject(), indexValue.ToObject(), value.ToObject());
    }

    /// <summary>
    /// Performs an index assignment on already-evaluated operands. Shared by the
    /// sync evaluator and the async evaluator (Interpreter.Async.cs) so both
    /// contexts dispatch identically.
    /// </summary>
    private RuntimeValue PerformIndexSet(object? obj, object? index, object? value)
    {
        bool strictMode = _environment.IsStrictMode;

        // ECMA-262 PutValue: RequireObjectCoercible throws a guest TypeError on a
        // null/undefined base before any setter dispatch (#733). Operands (obj,
        // index, value) are already evaluated by EvaluateSetIndex, so RHS side
        // effects have run — matching PutValue-after-RHS ordering.
        if (obj == null || obj is SharpTSUndefined)
        {
            ThrowCannotSetProperty(obj, index?.ToString() ?? "");
        }

        // Proxy: intercept index assignment via set trap
        if (obj is SharpTSProxy proxy)
        {
            string key = index?.ToString() ?? "";
            bool assigned = proxy.TrapSetProperty(key, value, this, proxy);
            if (!assigned && strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Proxy set trap rejected property '{key}'"));
            return RuntimeValue.FromBoxed(value);
        }

        if (obj is ISharpTSOwnPropertyObject)
        {
            SetOwnPropertyObjectValue(obj, ToPropertyKey(index), value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        // Promise computed string writes share dot assignment's own descriptors,
        // subclass setters and integrity checks. Coerce the evaluated key once.
        if (obj is SharpTSPromise && ToPropertyKey(index) is string promiseKey)
        {
            var name = new Token(TokenType.IDENTIFIER, promiseKey, null, 0);
            EvaluateSetOnObject(new Expr.Set(null!, name, null!), obj, value);
            return RuntimeValue.FromBoxed(value);
        }

        if (UsesMutableFunctionPrototype(obj))
        {
            SetOwnPropertyObjectValue(obj!, ToPropertyKey(index), value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        // OrdinarySet enters an inherited Proxy's [[Set]] with the original
        // function as Receiver. A descriptor-only walk cannot replace that
        // protocol without either skipping the trap or exposing other traps.
        if (index is SharpTSSymbol functionSymbol
            && FunctionObjectProperties.TryGetStorage(obj, this, out _))
        {
            bool assigned = SharpTSProxy.OrdinarySet(this, obj, functionSymbol, value, obj);
            if (!assigned && strictMode)
                throw StrictModeErrors.TypeError(
                    $"Cannot assign to symbol property '{functionSymbol}'");
            return RuntimeValue.FromBoxed(value);
        }

        if (obj is ISharpTSFunctionObject functionObject)
        {
            SetFunctionObjectProperty(functionObject, ToPropertyKeyString(index), value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        if (index is not SharpTSSymbol
            && FunctionObjectProperties.TryGetExplicitPrototype(obj, this, out _))
        {
            string key = ToPropertyKeyString(index);
            if (!TrySetExplicitFunctionPrototypeProperty(obj!, key, value, strictMode))
            {
                FunctionObjectProperties.TryGetStorage(obj, this, out var storage);
                storage.SetPropertyStrict(key, value, strictMode);
            }
            return RuntimeValue.FromBoxed(value);
        }

        if (obj is ISharpTSSymbolPropertyBag symbolBag
            && index is SharpTSSymbol bagSymbol)
        {
            var descriptor = obj switch
            {
                ISharpTSPrimitiveConstructor symbolConstructor => symbolConstructor.GetOwnPropertyDescriptor(bagSymbol)
                    ?? FindInheritedSymbolDescriptor(symbolConstructor, bagSymbol),
                SharpTSFunctionPrototype functionPrototype => functionPrototype.GetOwnPropertyDescriptor(bagSymbol),
                _ => null
            };
            if (!TryAssignThroughDescriptor(descriptor, obj, bagSymbol.ToString(), value, strictMode))
                symbolBag.SetBySymbolStrict(bagSymbol, value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        // JS functions are objects — support bracket property assignment.
        if (obj is ISharpTSGeneratorFunctionValue generatorFunction)
        {
            var properties = generatorFunction.FunctionProperties.GetProperties(this);
            if (index is SharpTSSymbol symbol)
            {
                if (!TryAssignThroughDescriptor(properties.GetOwnPropertyDescriptor(symbol)
                        ?? FindInheritedSymbolDescriptor(obj, symbol), obj, symbol.ToString(), value, strictMode))
                    properties.SetBySymbolStrict(symbol, value, strictMode);
            }
            else
            {
                string key = ToPropertyKeyString(index);
                if (!TryAssignThroughDescriptor(properties.GetOwnPropertyDescriptor(key), obj, key, value, strictMode))
                    properties.SetPropertyStrict(key, value, strictMode);
            }
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSFunction fn)
        {
            SetOwnPropertyObjectValue(fn, index?.ToString() ?? "", value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSArrowFunction afn)
        {
            SetOwnPropertyObjectValue(afn, index?.ToString() ?? "", value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        if (obj is DotNetInstance { HasWritableIndexer: true } dotNetInstance)
        {
            dotNetInstance.SetIndex(index, value, this);
            return RuntimeValue.FromBoxed(value);
        }

        // Math is an extensible object — allow Math[n] = v alongside Math.foo = v.
        if (obj is SharpTSMath math)
        {
            if (index is SharpTSSymbol mathSymbol)
                math.SetBySymbolStrict(mathSymbol, value, strictMode);
            else
                math.SetExtra(index?.ToString() ?? "", value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSJSON or SharpTSDate or BoundFunction)
        {
            if (obj is BoundFunction && index is SharpTSSymbol inheritedSymbol
                && TryAssignThroughDescriptor(FindInheritedSymbolDescriptor(obj, inheritedSymbol),
                    obj, inheritedSymbol.ToString(), value, strictMode))
                return RuntimeValue.FromBoxed(value);
            string propertyName = PropertyKeyConverter.ToPropertyKeyString(index);
            var name = new Token(TokenType.IDENTIFIER, propertyName, null, 0);
            var set = new Expr.Set(null!, name, null!);
            EvaluateSetOnObject(set, obj, value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSObjectNamespace objectNamespace)
        {
            SetObjectNamespaceProperty(objectNamespace, PropertyKeyConverter.ToPropertyKeyString(index), value, strictMode);
            return RuntimeValue.FromBoxed(value);
        }

        // Number/Boolean/String.prototype are mutable ordinary objects — allow
        // bracket assignment (`Number.prototype[0] = 1`, `Number.prototype.length = 1`).
        if (obj is SharpTSNumberPrototype numProto)
        {
            numProto.SetExtra(PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSBooleanPrototype boolProto)
        {
            boolProto.SetExtra(PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSStringPrototype strProto)
        {
            if (index is SharpTSSymbol stringSymbol)
                strProto.SetBySymbolStrict(stringSymbol, value, strictMode);
            else
                strProto.SetExtra(PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSArrayPrototype arrayProto)
        {
            arrayProto.SetExtra(PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSFunctionPrototype functionProto)
        {
            functionProto.SetExtra(PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }

        // RegExp symbol-keyed assignment (`re[Symbol.match] = false`) stores an
        // own symbol property that shadows the inherited prototype method —
        // IsRegExp (§22.2.7.2) reads it via Get(re, @@match). Without this branch
        // the symbol key falls through to the Unsupported bucket and throws.
        if (obj is SharpTSRegExp regexSet && index is SharpTSSymbol regexSetSym)
        {
            regexSet.SetBySymbol(regexSetSym, value);
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSRegExp regexStringSet)
        {
            string key = PropertyKeyConverter.ToPropertyKeyString(index);
            var ownDescriptor = regexStringSet.GetOwnPropertyDescriptor(key);
            if (ownDescriptor is { HasGet: true } or { HasSet: true }
                && TryAssignThroughDescriptor(ownDescriptor, regexStringSet, key, value, strictMode))
                return RuntimeValue.FromBoxed(value);
            regexStringSet.SetProperty(key, value);
            return RuntimeValue.FromBoxed(value);
        }

        // Built-in callables expose `name` / `length` as non-writable own
        // properties (ECMA-262 §17). Index assignment to those should silently
        // no-op in sloppy mode and throw TypeError in strict mode — test262's
        // verifyProperty / isWritable rely on the TypeError shape to classify
        // writability. Other keys on a callable (user-stored fn[k] = v) aren't
        // supported here either; let them fall through to the spec-conformant
        // error.
        // Class metadata follows the descriptor-aware ClassString path below.
        if (obj is ISharpTSCallable && obj is not (SharpTSFunction or ISharpTSPrimitiveConstructor or SharpTSClass)
            && index?.ToString() is "name" or "length")
        {
            if (strictMode)
                throw new ThrowException(new Runtime.Types.SharpTSTypeError(
                    $"Cannot assign to read only property '{index}' of function"));
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSBuiltInConstructor
            { Name: BuiltInNames.Promise or BuiltInNames.RegExp }
            && index?.ToString() == "prototype")
        {
            if (strictMode)
                throw new ThrowException(new Runtime.Types.SharpTSTypeError(
                    "Cannot assign to read only property 'prototype' of function"));
            return RuntimeValue.FromBoxed(value);
        }
        if (obj is SharpTSBuiltInConstructor builtInConstructor)
        {
            SetBuiltInConstructorProperty(
                builtInConstructor, PropertyKeyConverter.ToPropertyKeyString(index), value);
            return RuntimeValue.FromBoxed(value);
        }

        var target = ResolveIndexTarget(obj, index);

        if (target is IndexTarget.EnumReverse or IndexTarget.ConstEnumError)
            throw new InterpreterException("Index assignment not supported on enum types.");

        switch (target)
        {
            case IndexTarget.Array t:
                if (t.Target is not SharpTSArguments)
                {
                    // Simple writes share compound assignment's OrdinarySet
                    // policy, including present-but-undefined accessors.
                    string key = t.Index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    SetOwnPropertyObjectValue(t.Target, key, value, strictMode);
                    break;
                }
                if (t.Target is SharpTSArguments && t.Index >= t.Target.LongLength)
                {
                    t.Target.SetNamedProperty(t.Index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture), value);
                    break;
                }
                if (!t.Target.HasIndex(t.Index)
                    && t.Target.HasExplicitPrototype
                    && t.Target.ExplicitPrototype is SharpTSProxy arrayPrototypeProxy)
                {
                    string propertyKey = t.Index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    bool assigned = arrayPrototypeProxy.TrapSetProperty(
                        propertyKey, value, this, t.Target);
                    if (!assigned && strictMode)
                        throw new ThrowException(new SharpTSTypeError(
                            $"Proxy set trap rejected property '{propertyKey}'"));
                    break;
                }
                if (t.Target.TryGetIndexAccessor(
                    t.Index, out _, out var indexSetter))
                {
                    if (indexSetter != null)
                    {
                        BindAccessorToObject(indexSetter, t.Target).CallBoxed(this, [value]);
                    }
                    else if (strictMode)
                    {
                        throw new ThrowException(new SharpTSTypeError(
                            $"Cannot set array index '{t.Index}' which has only a getter."));
                    }
                }
                else if (strictMode)
                {
                    t.Target.SetStrict(t.Index, value, strictMode);
                }
                else
                {
                    t.Target.Set(t.Index, value);
                }
                break;

            case IndexTarget.ArrayString t:
                SetArrayNamedProperty(t.Target, t.Key, value, strictMode);
                break;

            case IndexTarget.ArraySymbol t:
                if (t.Target.TryGetSymbolAccessor(
                    t.Key, out _, out var arraySymbolSetter))
                {
                    if (arraySymbolSetter != null)
                        BindAccessorToObject(arraySymbolSetter, t.Target)
                            .CallBoxed(this, [value]);
                    else if (strictMode)
                        throw new InterpreterException(
                            $"Cannot set symbol property '{t.Key}' which has only a getter.");
                }
                else if (strictMode)
                    t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                else
                    t.Target.SetBySymbol(t.Key, value);
                break;

            case IndexTarget.TypedArray t:
                t.Target[t.Index] = value;
                break;

            case IndexTarget.Buffer t:
                t.Target[t.Index] = value is double d ? d : Convert.ToDouble(value);
                break;

            case IndexTarget.ObjectString t:
                // Bracket assignment uses the same [[Set]] path as dot assignment:
                // invoke an own setter before considering the descriptor's writable
                // flag. Object.defineProperty accessors have writable=false in their
                // stored flags, but that flag applies only to data descriptors.
                if (t.Target.GetSetter(t.Key) is { } setter)
                {
                    BindAccessorToObject(setter, t.Target).CallBoxed(this, [value]);
                }
                else if (t.Target.HasGetter(t.Key))
                {
                    if (strictMode)
                        throw new InterpreterException($"Cannot set property '{t.Key}' which has only a getter.");
                }
                else if (t.Target.GetOwnPropertyDescriptor(t.Key) is null
                    && t.Target.Prototype is SharpTSProxy prototypeProxy)
                {
                    bool assigned = prototypeProxy.TrapSetProperty(
                        t.Key, value, this, t.Target);
                    if (!assigned && strictMode)
                        throw new ThrowException(new SharpTSTypeError(
                            $"Proxy set trap rejected property '{t.Key}'"));
                }
                else if (TrySetOrdinaryInheritedProperty(t.Target, t.Key, value, strictMode)
                    || TrySetBoxedPrimitiveInheritedProperty(t.Target, t.Key, value, strictMode))
                {
                    // The inherited setter handled the write, or an inherited
                    // non-writable/getter-only descriptor blocked it.
                }
                else if (strictMode)
                {
                    t.Target.SetPropertyStrict(t.Key, value, strictMode);
                }
                else
                {
                    t.Target.SetProperty(t.Key, value);
                }
                break;

            case IndexTarget.ObjectSymbol t:
                if (t.Target.TryGetSymbolAccessor(t.Key, out _, out var symbolSetter))
                {
                    if (symbolSetter != null)
                        BindAccessorToObject(symbolSetter, t.Target).CallBoxed(this, [value]);
                    else if (strictMode)
                        throw new InterpreterException(
                            $"Cannot set symbol property '{t.Key}' which has only a getter.");
                }
                else if (strictMode)
                    t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                else
                    t.Target.SetBySymbol(t.Key, value);
                break;

            case IndexTarget.InstanceString t:
                // Installed own accessors take precedence without changing the declared or
                // raw-data paths below. The descriptor lookup never invokes its getter.
                var ownDescriptor = t.Target.GetOwnPropertyDescriptor(t.Key);
                if (ownDescriptor is { HasGet: true } or { HasSet: true }
                    && TryAssignThroughDescriptor(ownDescriptor, t.Target, t.Key, value, strictMode))
                    break;
                // Bracket assignment must invoke a declared setter / auto-accessor, mirroring
                // dot assignment (SharpTSInstance.Set) and JS [[Set]] semantics. Without this the
                // write lands in _fields while reads keep hitting the getter, silently
                // desynchronizing the two (#290). Plain data writes stay on SetRawField so the
                // defineProperty `writable:false` flag continues to be honored.
                if (t.Target.HasExplicitPrototype
                    || t.Target.GetClass().HasInstanceAccessorDeclaration(t.Key)
                    || t.Target.GetClass().FindSetter(t.Key) != null
                    || t.Target.GetClass().HasInstanceAutoAccessor(t.Key))
                {
                    // Set/SetStrict invoke the setter only when the instance has an interpreter
                    // reference; the dot-assignment path primes it the same way (cold instances
                    // created by `new` don't carry one until first member access).
                    t.Target.SetInterpreter(this);
                    var setToken = new Token(TokenType.IDENTIFIER, t.Key, null, 0);
                    if (strictMode) t.Target.SetStrict(setToken, value, strictMode);
                    else t.Target.Set(setToken, value);
                }
                else if (strictMode) t.Target.SetRawFieldStrict(t.Key, value, strictMode);
                else t.Target.SetRawField(t.Key, value);
                break;

            case IndexTarget.InstanceSymbol t:
                if (TryAssignThroughDescriptor(t.Target.GetOwnPropertyDescriptor(t.Key),
                        t.Target, t.Key.ToString(), value, strictMode)) break;
                if (t.Target.HasExplicitPrototype)
                {
                    if (!t.Target.HasSymbolProperty(t.Key)
                        && TryAssignThroughDescriptor(FindInheritedSymbolDescriptor(t.Target, t.Key),
                            t.Target, t.Key.ToString(), value, strictMode)) break;
                    if (strictMode) t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                    else t.Target.SetBySymbol(t.Key, value);
                    break;
                }
                if (!t.Target.HasSymbolProperty(t.Key))
                {
                    var descriptor = t.Target.GetClass().FindInstanceSymbolPropertyDescriptor(t.Key);
                    if (TryAssignThroughDescriptor(descriptor, t.Target, t.Key.ToString(), value, strictMode))
                        break;
                    // A removed declared accessor must not reappear through
                    // the immutable declaration table below.
                    if (t.Target.GetClass().HasInstanceAccessorDeclaration(t.Key))
                    {
                        if (strictMode) t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                        else t.Target.SetBySymbol(t.Key, value);
                        break;
                    }
                }
                // A declared symbol-keyed setter (`set [Symbol.x](v)`) intercepts
                // assignment unless shadowed by an own symbol data property.
                if (!t.Target.HasSymbolProperty(t.Key)
                    && t.Target.GetClass().FindSymbolSetter(t.Key) is { } symSetter)
                {
                    symSetter.Bind(t.Target).Call(this, [value]);
                    break;
                }
                if (strictMode) t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                else t.Target.SetBySymbol(t.Key, value);
                break;

            case IndexTarget.ClassString t:
                if (!TryAssignThroughClassDescriptor(t.Target, t.Key, value, strictMode))
                    t.Target.SetStaticProperty(t.Key, value, strictMode);
                break;

            case IndexTarget.ClassSymbol t:
                SetOwnPropertyObjectValue(t.Target, t.Key, value, strictMode);
                break;

            case IndexTarget.ClassPrototypeSymbol t:
                if (!TryAssignThroughDescriptor(t.Target.GetOwnPropertyDescriptor(t.Key),
                        t.Target, t.Key.ToString(), value, strictMode))
                    t.Target.SetBySymbol(t.Key, value);
                break;

            case IndexTarget.GlobalThis t:
                t.Target.SetProperty(t.Key, value);
                break;

            case IndexTarget.BuiltInPrototypeString t:
                if (t.Target is SharpTSClassPrototype classPrototype)
                    SetClassPrototypeProperty(classPrototype, t.Key, value, strictMode);
                else if (t.Target is not ISharpTSPrimitiveConstructor indexedConstructor
                    || !TryAssignThroughDescriptor(indexedConstructor.GetOwnPropertyDescriptor(t.Key),
                        indexedConstructor, t.Key, value, strictMode))
                    t.Target.SetExtra(t.Key, value);
                break;

            default:
                throw new InterpreterException("Index assignment not supported on this type.");
        }
        return RuntimeValue.FromBoxed(value);
    }

    /// <summary>
    /// Evaluates a dynamic import expression, returning a Promise of the module namespace.
    /// </summary>
    /// <param name="di">The dynamic import expression AST node.</param>
    /// <returns>A <see cref="SharpTSPromise"/> that resolves to the module namespace object.</returns>
    /// <remarks>
    /// Dynamic imports allow runtime module loading with expression paths.
    /// The returned Promise resolves to an object containing all module exports.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/import">MDN Dynamic Import</seealso>
    private RuntimeValue EvaluateDynamicImport(Expr.DynamicImport di)
    {
        return RuntimeValue.FromObject(new SharpTSPromise(DynamicImportAsync(di)));
    }

    /// <summary>
    /// Evaluates an import.meta expression, returning an object with module metadata.
    /// </summary>
    private RuntimeValue EvaluateImportMeta(Expr.ImportMeta im)
    {
        // Get current module path
        string path = _currentModule?.Path ?? "";
        string url = path;

        // Convert to file:// URL format if it's a file path
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            // Absolute POSIX paths already begin with '/'. Prefixing file:/// by
            // hand produced file:////tmp/... on Linux, which createRequire could
            // not map back to the in-memory module path. Let Uri apply the
            // platform-specific drive/root and escaping rules instead.
            url = Path.IsPathRooted(path)
                ? new Uri(Path.GetFullPath(path)).AbsoluteUri
                : "file:///" + path.Replace("\\", "/");
        }

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["url"] = url,
            ["filename"] = path,
            ["dirname"] = Path.GetDirectoryName(path) ?? ""
        }));
    }

    /// <summary>
    /// Asynchronously loads a module dynamically.
    /// </summary>
    private async Task<object?> DynamicImportAsync(Expr.DynamicImport di)
    {
        // Evaluate the path expression
        object? pathValue = IsHostedExecution
            ? (await EvaluateAsync(di.PathExpression)).ToObject()
            : Evaluate(di.PathExpression);
        string specifier = pathValue?.ToString()
            ?? throw new InterpreterException("Dynamic import path cannot be null.");

        // vm importModuleDynamically hook: resolve through the user-provided loader
        // instead of the file-system module resolver (vm #1156).
        if (_vmDynamicImportHook != null)
            return _vmDynamicImportHook(specifier);

        // Create resolver if needed (single-file mode without module context)
        _moduleResolver ??= new ModuleResolver(
            _currentModule?.Path ?? Directory.GetCurrentDirectory());

        string currentPath = _currentModule?.Path ?? Directory.GetCurrentDirectory();
        string absolutePath = _moduleResolver.ResolveRuntimeModulePath(specifier, currentPath);

        // dotnet: modules have no enumerable namespace object — their export surface is
        // defined by static named imports. Reject instead of returning a partial view.
        if (DotNetImports.IsDotNetSpecifier(absolutePath))
        {
            throw new InterpreterException(
                $"Dynamic import of '{specifier}' is not supported. " +
                "Use a static named import: import { TypeName } from \"" + specifier + "\";");
        }

        // Check if module is already loaded and fully initialized. A hosted
        // module registered for a static or dynamic cycle is not observable as
        // complete merely because its namespace object already exists.
        if (_loadedModules.TryGetValue(absolutePath, out var cached))
        {
            if (cached.IsExecuted)
                return cached.ExportsAsObject();
            if (IsHostedExecution && IsHostedModuleExecuting(absolutePath))
            {
                throw new InterpreterException(
                    $"Dynamic import cycle reached the evaluating module '{absolutePath}'.");
            }
        }

        if (IsHostedExecution && TryGetHostedDynamicImport(absolutePath, out Task<object?> pending))
            return await pending;

        // Load and execute the module
        ParsedModule module = _moduleResolver.LoadModule(absolutePath);

        // Type check the new module (optional - errors become Promise rejections)
        // Note: Skipping type checking for dynamic imports for flexibility

        if (IsHostedExecution)
        {
            Task<object?> initialization = ExecuteHostedDynamicImportAsync(module);
            TrackHostedDynamicImport(absolutePath, initialization);
            try
            {
                return await initialization;
            }
            finally
            {
                CompleteHostedDynamicImport(absolutePath, initialization);
            }
        }

        // The ordinary console runtime retains its synchronous module executor
        // and promise pump; only hosted execution uses resumable module jobs.
        ExecuteModule(module);

        // Return the module namespace
        return _loadedModules[absolutePath].ExportsAsObject();
    }

    // Counter for generating unique anonymous class expression names
    private int _classExprCounter = 0;

    private ValueTask<RuntimeValue> EvaluateVariableInitializerCore(
        IEvaluationContext ctx, Expr initializer, Token name)
    {
        // Destructuring lowering also uses variable statements, but its temporary
        // tokens do not denote source bindings that participate in named evaluation.
        if (name.Start < 0)
            return ctx.EvaluateExprAsync(initializer);

        Expr expression = initializer;
        while (expression is Expr.Grouping grouping)
            expression = grouping.Expression;

        // Named evaluation belongs to this initializer only. Passing it explicitly
        // keeps nested class expressions in heritage, keys and static fields independent.
        return expression is Expr.ClassExpr { Name: null } classExpr
            ? EvaluateClassExpressionCore(ctx, classExpr, name.Lexeme)
            : ctx.EvaluateExprAsync(initializer);
    }

    /// <summary>
    /// Evaluates a class expression and returns the SharpTSClass object.
    /// Unlike class declarations, the class is not added to the environment.
    /// </summary>
    private RuntimeValue EvaluateClassExpression(Expr.ClassExpr classExpr) =>
        EvaluateClassExpressionCore(_syncContext, classExpr).GetAwaiter().GetResult();

    private async ValueTask<RuntimeValue> EvaluateClassExpressionCore(
        IEvaluationContext ctx, Expr.ClassExpr classExpr, string? inferredName = null)
    {
        // Generate name for anonymous classes
        string className = classExpr.Name?.Lexeme ?? $"$ClassExpr_{++_classExprCounter}";

        // Resolve superclass if present
        object? superclass = null;
        if (classExpr.SuperclassExpr != null)
        {
            superclass = (await ctx.EvaluateExprAsync(classExpr.SuperclassExpr)).ToObject();
            // `extends Array` (#233): substitute the SharpTSArrayClass bridge,
            // mirroring VisitClass.
            if (superclass is SharpTSArrayGlobal)
            {
                superclass = SharpTSArrayClass.ArrayBase;
            }
            // `extends Promise` (#242): same substitution, mirroring VisitClass.
            if (superclass is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise })
            {
                superclass = SharpTSPromiseClass.PromiseBase;
            }
            if (superclass is not SharpTSClass)
            {
                if (superclass is SharpTSBuiltInConstructor builtInCtor)
                {
                    throw new InterpreterException(
                        $"Class expression cannot extend built-in '{builtInCtor.Name}': subclassing this built-in is not supported yet.");
                }
                throw new ThrowException(new SharpTSTypeError("Superclass must be a class constructor."));
            }
        }

        // Create environment for class body
        RuntimeEnvironment classEnv = _environment;

        // If named, define the name in class body scope for self-reference
        if (classExpr.Name != null)
        {
            classEnv = new RuntimeEnvironment(_environment);
            classEnv.Define(classExpr.Name.Lexeme, null); // Placeholder for self-reference
        }

        if (classExpr.SuperclassExpr != null)
        {
            classEnv = new RuntimeEnvironment(classEnv);
            classEnv.Define("super", superclass);
        }

        using (PushScope(classEnv))
        {
            Dictionary<string, ISharpTSCallable> methods = [];
            Dictionary<string, ISharpTSCallable> staticMethods = [];
            Dictionary<string, object?> staticProperties = [];
            List<Stmt.Field> instanceFields = [];
            List<Stmt.Field> instancePrivateFields = [];
            Dictionary<string, ISharpTSCallable> privateMethods = [];
            Dictionary<string, ISharpTSCallable> staticPrivateMethods = [];
            Dictionary<string, object?> staticPrivateFields = [];

            // Check if we have static initializers for proper ordering
            bool hasStaticInitializers = classExpr.StaticInitializers != null && classExpr.StaticInitializers.Count > 0;

            // Process fields
            // Evaluate every computed key exactly once, in source order, before
            // applying static values or symbol-keyed methods and accessors.
            var computedMemberKeys = new Dictionary<Expr, object?>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var key in Compilation.ClassDefinitionExpressions.MemberKeys(
                classExpr.Fields.Where(field => !field.IsDeclare), classExpr.Methods, classExpr.Accessors))
            {
                object? value = (await ctx.EvaluateExprAsync(key)).ToObject();
                computedMemberKeys[key] = ToPropertyKey(value);
            }

            foreach (Stmt.Field field in classExpr.Fields)
            {
                if (field.IsDeclare) continue;
                if (field.IsPrivate)
                {
                    if (!field.IsStatic) instancePrivateFields.Add(field);
                    else staticPrivateFields[field.Name.Lexeme] = !hasStaticInitializers && field.Initializer != null
                        ? EvaluateEarlyStaticInitializer(field.Initializer) : SharpTSUndefined.Instance;
                }
                else if (field.IsStatic)
                {
                    if (!hasStaticInitializers)
                    {
                        // Old behavior: evaluate immediately
                        object? fieldValue = field.Initializer != null
                            ? EvaluateEarlyStaticInitializer(field.Initializer)
                            : SharpTSUndefined.Instance;
                        staticProperties[field.Name.Lexeme] = fieldValue;
                    }
                    // else: will be evaluated via StaticInitializers with proper 'this' binding
                }
                else
                {
                    instanceFields.Add(field.ComputedKey == null ? field
                    : field with { ComputedKey = new Expr.Literal(computedMemberKeys[field.ComputedKey]) });
                }
            }

            List<ClassMemberDefinition> memberDefinitions = [];
            List<(int Position, ISharpTSCallable Function, bool? IsGetter)>? constructorProperties = null;

            // Process methods (skip overload signatures with no body)
            foreach (Stmt.Function method in classExpr.Methods.Where(m => m.Body != null))
            {
                // Create the appropriate function type based on async/generator flags
                ISharpTSCallable func;
                if (method.IsAsync && method.IsGenerator)
                    func = new SharpTSAsyncGeneratorFunction(method, _environment, creationRealm: this);
                else if (method.IsAsync)
                    func = new SharpTSAsyncFunction(method, _environment);
                else if (method.IsGenerator)
                    func = new SharpTSGeneratorFunction(method, _environment, creationRealm: this);
                else
                    func = new SharpTSFunction(method, _environment, isConstructible: false);

                // Computed method keys (`[Symbol.iterator]()`) — symbol keys land in the symbol-method
                // table; other keys fold to a string-named method (parallels the class-declaration path).
                if (method.ComputedKey != null)
                {
                    object? key = computedMemberKeys[method.ComputedKey];
                    if (key is SharpTSSymbol symbolKey)
                    {
                        memberDefinitions.Add(new(method.Name.SourceOrderOffset, symbolKey, func, method.IsStatic, null));
                        continue;
                    }
                    string keyStr = PropertyKeyConverter.ToPropertyKeyString(key);
                    SharpTSClass.SetComputedMethodName(func, keyStr);
                    if (!method.IsStatic && keyStr == "constructor")
                    {
                        (constructorProperties ??= []).Add((method.Name.SourceOrderOffset, func, null));
                        continue;
                    }
                    (method.IsStatic ? staticMethods : methods)[keyStr] = func;
                    memberDefinitions.Add(new(method.Name.SourceOrderOffset, keyStr, func, method.IsStatic, null));
                    continue;
                }

                if (method.IsPrivate)
                    (method.IsStatic ? staticPrivateMethods : privateMethods)[method.Name.Lexeme] = func;
                else if (method.IsStatic)
                {
                    staticMethods[method.Name.Lexeme] = func;
                    memberDefinitions.Add(new(method.Name.SourceOrderOffset, method.Name.Lexeme, func, true, null));
                }
                else
                {
                    methods[method.Name.Lexeme] = func;
                    if (method.Name.Lexeme != "constructor")
                        memberDefinitions.Add(new(method.Name.SourceOrderOffset, method.Name.Lexeme, func, false, null));
                }
            }

            // Create accessor functions
            Dictionary<string, SharpTSFunction> getters = [];
            Dictionary<string, SharpTSFunction> setters = [];
            Dictionary<string, SharpTSFunction> staticGetters = [];
            Dictionary<string, SharpTSFunction> staticSetters = [];
            if (classExpr.Accessors != null)
            {
                foreach (var accessor in classExpr.Accessors)
                {
                    var funcStmt = new Stmt.Function(
                        accessor.Name,
                        null,
                        null,
                        accessor.SetterParam != null ? [accessor.SetterParam] : [],
                        accessor.Body,
                        accessor.ReturnType, IsStatic: accessor.IsStatic);

                    SharpTSFunction func = new(funcStmt, _environment, isConstructible: false);
                    bool isGetter = accessor.Kind.Type == TokenType.GET;
                    string nameKey = accessor.Name.Lexeme;

                    // Computed accessor names, evaluated at class-definition time
                    // (mirrors VisitClass).
                    if (accessor.ComputedKey != null)
                    {
                        object? key = computedMemberKeys[accessor.ComputedKey];
                        if (key is SharpTSSymbol symbolKey)
                        {
                            memberDefinitions.Add(new(accessor.Name.SourceOrderOffset, symbolKey, func, accessor.IsStatic, isGetter));
                            continue;
                        }
                        nameKey = PropertyKeyConverter.ToPropertyKeyString(key);
                    }

                    if (!accessor.IsStatic && nameKey == "constructor")
                        (constructorProperties ??= []).Add((accessor.Name.SourceOrderOffset, func, isGetter));

                    if (isGetter)
                    {
                        (accessor.IsStatic ? staticGetters : getters)[nameKey] = func;
                    }
                    else
                    {
                        (accessor.IsStatic ? staticSetters : setters)[nameKey] = func;
                    }
                    if (accessor.IsStatic || nameKey != "constructor")
                        memberDefinitions.Add(new(accessor.Name.SourceOrderOffset, nameKey, func, accessor.IsStatic, isGetter));
                }
            }

            List<Stmt.AutoAccessor> instanceAutoAccessors = [];
            Dictionary<string, object?> staticAutoAccessors = [];
            foreach (var autoAccessor in classExpr.AutoAccessors ?? [])
            {
                if (autoAccessor.IsStatic)
                    staticAutoAccessors[autoAccessor.Name.Lexeme] = autoAccessor.Initializer is { } initializer
                        ? EvaluateEarlyStaticInitializer(initializer) : null;
                else
                    instanceAutoAccessors.Add(autoAccessor);
            }

            var memberProperties = NormalizeClassMemberDefinitions(memberDefinitions, methods, staticMethods,
                getters, setters, staticGetters, staticSetters);

            // Mirror VisitClass: Error/Array/Promise superclasses need their
            // dedicated SharpTSClass subtypes so instances get the right backing.
            SharpTSClass klass = superclass is SharpTSErrorClass errorSuper
                ? new SharpTSErrorClass(
                    className,
                    errorSuper,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classExpr.IsAbstract,
                    instanceFields,
                    instanceAutoAccessors: instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                    staticAutoAccessors: staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                    staticGetters: staticGetters.Count > 0 ? staticGetters : null,
                    staticSetters: staticSetters.Count > 0 ? staticSetters : null, instancePrivateFields: instancePrivateFields, privateMethods: privateMethods, staticPrivateFields: staticPrivateFields, staticPrivateMethods: staticPrivateMethods)
                : superclass is SharpTSArrayClass arraySuper
                ? new SharpTSArrayClass(
                    className,
                    arraySuper,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classExpr.IsAbstract,
                    instanceFields,
                    instanceAutoAccessors: instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                    staticAutoAccessors: staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                    staticGetters: staticGetters.Count > 0 ? staticGetters : null,
                    staticSetters: staticSetters.Count > 0 ? staticSetters : null, instancePrivateFields: instancePrivateFields, privateMethods: privateMethods, staticPrivateFields: staticPrivateFields, staticPrivateMethods: staticPrivateMethods)
                : superclass is SharpTSPromiseClass promiseSuper
                ? new SharpTSPromiseClass(
                    className,
                    promiseSuper,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classExpr.IsAbstract,
                    instanceFields,
                    instanceAutoAccessors: instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                    staticAutoAccessors: staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                    staticGetters: staticGetters.Count > 0 ? staticGetters : null,
                    staticSetters: staticSetters.Count > 0 ? staticSetters : null, instancePrivateFields: instancePrivateFields, privateMethods: privateMethods, staticPrivateFields: staticPrivateFields, staticPrivateMethods: staticPrivateMethods)
                : new SharpTSClass(
                    className,
                    (SharpTSClass?)superclass,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classExpr.IsAbstract,
                    instanceFields,
                    instanceAutoAccessors: instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                    staticAutoAccessors: staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                    staticGetters: staticGetters.Count > 0 ? staticGetters : null,
                    staticSetters: staticSetters.Count > 0 ? staticSetters : null, instancePrivateFields: instancePrivateFields, privateMethods: privateMethods, staticPrivateFields: staticPrivateFields, staticPrivateMethods: staticPrivateMethods);

            // Public name inference is available before static initialization,
            // while className remains the unique internal identity.
            klass.SetInferredName(inferredName);
            klass.BindPrivateMemberOwners();
            klass.InitializerEnvironment = classEnv;
            klass.OrderedInstanceFields = classExpr.Fields.Where(field => !field.IsStatic && !field.IsDeclare)
                .SelectMany(field => (field.IsPrivate ? instancePrivateFields : instanceFields)
                    .Where(captured => captured.Name == field.Name)).ToList();
            InitializeClassInstanceKeyOrder(klass, memberDefinitions, classExpr.AutoAccessors);
            InstallInstanceConstructorProperties(klass, constructorProperties);

            InitializeClassStaticKeyOrder(klass, memberDefinitions, classExpr.AutoAccessors);
            InstallClassSymbolMembers(klass, memberProperties);

            // Initialize the captured lexical self before static initializers
            // can invoke methods and accessors that close over it.
            if (classExpr.Name != null)
            {
                classEnv.Assign(classExpr.Name, klass);
            }

            // Execute static initializers in declaration order (if present)
            if (hasStaticInitializers)
            {
                // Create temporary environment with 'this' bound to the class
                // Also make the class name available so code like Foo.x works
                var staticEnv = new RuntimeEnvironment(_environment, strictMode: true)
                    { PrivateClass = klass, StaticSuper = true };
                staticEnv.NewTarget = RuntimeValue.Undefined;
                staticEnv.ConstructionFrame = null;
                staticEnv.Define("this", klass);
                if (classExpr.Name != null)
                {
                    staticEnv.Define(classExpr.Name.Lexeme, klass);
                }

                using (PushScope(staticEnv))
                {
                    foreach (var initializer in classExpr.StaticInitializers!)
                    {
                        switch (initializer)
                        {
                            case Stmt.Field field when field.IsStatic && !field.IsDeclare:
                                object? fieldValue = field.Initializer != null
                                    ? Evaluate(field.Initializer)
                                    : SharpTSUndefined.Instance;
                                if (field.IsPrivate) klass.InstallStaticPrivateField(field.Name.Lexeme, fieldValue);
                                else if (field.ComputedKey != null && computedMemberKeys[field.ComputedKey] is SharpTSSymbol symbol)
                                    klass.DefineStaticSymbolProperty(symbol,
                                        new SharpTSPropertyDescriptor(fieldValue, writable: true,
                                            enumerable: true, configurable: true) { HasValue = true });
                                else
                                    klass.InitializeStaticProperty(field.ComputedKey != null
                                        ? (string)computedMemberKeys[field.ComputedKey]! : field.Name.Lexeme, fieldValue);
                                break;

                            case Stmt.StaticBlock block:
                                ExecuteClassStaticBlock(block);
                                break;
                        }
                    }
                }
            }

            return RuntimeValue.FromObject(klass);
        }
    }
}
