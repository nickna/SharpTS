using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

/// <summary>
/// Async expression and statement evaluation for async/await support.
/// </summary>
public partial class Interpreter
{
    // ===================== Async Statement Execution =====================

    /// <summary>
    /// Asynchronously executes a block of statements.
    /// Uses registry-based dispatch via ExecuteStatementAsync.
    /// </summary>
    /// <summary>
    /// Awaits a guest task while preserving the interpreter's ambient
    /// environment across the suspension (#207). The resume may run on a
    /// later event-loop turn — after other callbacks ran, or after the
    /// module-init/top-level frame that was active at suspension time
    /// finished and restored <c>_environment</c> to an outer scope. The
    /// tree-walk resolves variables through the ambient environment, so a
    /// resumed async frame that doesn't re-assert its scope sees the wrong
    /// chain ("Undefined variable 'x'" for closure/module bindings).
    /// Every await of a guest-pending task must route through here.
    /// </summary>
    internal async Task<object?> AwaitPreservingEnvironment(Task<object?> task)
    {
        var saved = _environment;
        // Also preserve the active async generator: a generator body whose yielded expression awaits
        // must resume with its own generator (and scope) restored, even if interleaved event-loop work
        // ran another generator in the meantime (#752).
        var savedGen = CurrentAsyncGenerator;
        // While suspended at this await the body is off the call stack, so a request issued during the
        // gap is a legit concurrent next(), not re-entrancy. Clear the active generator's running guard
        // and re-set it on resume (success or rejection — a body unwinding a rejected await is still
        // synchronously on the stack), so a re-entrant next() after a genuinely-pending await is still
        // caught (#771). No-op when no async generator is active.
        savedGen?.MarkBodySuspended();
        try
        {
            return await task;
        }
        finally
        {
            _environment = saved;
            CurrentAsyncGenerator = savedGen;
            savedGen?.MarkBodyResumed();
        }
    }

    internal async Task<ExecutionResult> ExecuteBlockAsync(List<Stmt> statements, RuntimeEnvironment environment)
    {
        using (PushScope(environment))
        {
            // JS hoists function declarations to the top of their enclosing function/block scope.
            // The sync ExecuteBlock does the same; without it an async body never sees a function
            // declared after a reference to it — e.g. the `async function* __genArrow_N` the
            // GeneratorArrowLifter appends at body END to lift an async-generator expression that
            // closes over an enclosing local, which the `const g = __genArrow_N` reference precedes
            // (#924, the async analog of #534).
            HoistFunctionDeclarations(statements);

            foreach (Stmt statement in statements)
            {
                var result = await ExecuteStatementAsync(statement);
                if (result.IsAbrupt) return result;
            }
            return ExecutionResult.Success();
        }
    }

    private async Task<ExecutionResult> ExecuteForOfAsync(Stmt.ForOf forOf)
    {
        // Drain any labels parked by ExecuteLabeledStatementAsyncVT before evaluating the iterable, so a
        // `break`/`continue <label>` targeting this loop (including via the async-iterator path below) is
        // matched here rather than escaping — and so a labeled loop produced by the iterable expression
        // can't steal this loop's label. TakePendingLoopLabels returns empty when none are parked, so the
        // unlabeled path is unchanged (#728).
        var labels = TakePendingLoopLabels();
        object? iterable = (await EvaluateAsync(forOf.Iterable)).ToObject();

        // For 'for await...of', check for async iterator protocol first
        if (forOf.IsAsync)
        {
            var asyncIterator = TryGetAsyncIterator(iterable);
            if (asyncIterator != null)
            {
                return await IterateAsyncIterator(asyncIterator, forOf, labels);
            }
            // Fall through to sync iterator with async unwrap
        }

        // Check for Symbol.iterator protocol first (works for both sync and async for...of)
        var syncIterator = TryGetSymbolIterator(iterable);
        if (syncIterator != null)
        {
            foreach (var item in syncIterator)
            {
                // For 'for await...of', unwrap promises from sync iterators
                object? value = forOf.IsAsync && item is Task<object?> t ? await AwaitPreservingEnvironment(t) : item;

                var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
                var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
                if (shouldBreak) return ExecutionResult.Success();
                if (shouldContinue) continue;
                if (abruptResult.HasValue) return abruptResult.Value;

                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
            return ExecutionResult.Success();
        }

        // Get elements based on iterable type
        IEnumerable<object?> items = iterable switch
        {
            SharpTSArray arr => arr,
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new InterpreterException("for...of requires an iterable (array, Map, Set, or iterator).")
        };

        foreach (var item in items)
        {
            // For 'for await...of' with sync iterables, unwrap promises
            object? value = forOf.IsAsync && item is Task<object?> t ? await t : item;

            var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;

            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        }

        return ExecutionResult.Success();
    }

    private async Task<ExecutionResult> ExecuteLoopBodyAsync(string varName, object? value, Stmt body)
    {
        RuntimeEnvironment loopEnv = new(_environment);
        loopEnv.Define(varName, value);

        RuntimeEnvironment prev = _environment;
        _environment = loopEnv;
        try
        {
            return await ExecuteStatementAsync(body);
        }
        finally
        {
            _environment = prev;
        }
    }

    /// <summary>
    /// Tries to get an async iterator from an object via Symbol.asyncIterator.
    /// Async generators are their own async iterators.
    /// </summary>
    private object? TryGetAsyncIterator(object? iterable)
    {
        // Async generators are their own async iterators
        if (iterable is SharpTSAsyncGenerator asyncGen)
        {
            return asyncGen;
        }

        // Web Streams ReadableStream: wrap in an async iterator that
        // delegates next() to a default reader's read(). Matches Node 18+
        // behaviour where `for await (const chunk of rs)` works natively.
        if (iterable is SharpTSReadableStream rs)
        {
            if (rs.Locked)
            {
                throw new InterpreterException("TypeError: ReadableStream is already locked to a reader");
            }
            var reader = new SharpTSReadableStreamDefaultReader(rs);
            rs.Reader = reader;
            return new SharpTSReadableStreamAsyncIterator(rs, reader);
        }

        // node:stream Readable (incl. Duplex/Transform/PassThrough) — wrap in an async
        // iterator that pulls buffered chunks and parks on a slow producer (#1024).
        if (iterable is SharpTSReadable readable)
        {
            return new SharpTSReadableAsyncIterator(readable);
        }

        if (iterable is SharpTSObject obj)
        {
            var asyncIteratorFn = obj.GetBySymbol(SharpTSSymbol.AsyncIterator);
            if (asyncIteratorFn != null)
            {
                // Bind 'this' if it's an arrow function
                if (asyncIteratorFn is SharpTSArrowFunction arrowFunc)
                    asyncIteratorFn = arrowFunc.Bind(obj);

                // Call the async iterator function
                if (asyncIteratorFn is ISharpTSCallable callable)
                    return callable.Call(this, []);
            }
        }
        else if (iterable is SharpTSInstance inst)
        {
            var asyncIteratorFn = inst.GetBySymbol(SharpTSSymbol.AsyncIterator);
            // Fall back to a declared symbol-keyed method on the class chain
            // (`class C { async *[Symbol.asyncIterator]() {...} }`).
            if (asyncIteratorFn == null && inst.GetClass().FindSymbolMethod(SharpTSSymbol.AsyncIterator) is { } symMethod)
            {
                asyncIteratorFn = SharpTSClass.BindMethod(symMethod, inst);
            }
            if (asyncIteratorFn != null)
            {
                if (asyncIteratorFn is SharpTSArrowFunction arrowFunc)
                    asyncIteratorFn = arrowFunc.Bind(inst);
                if (asyncIteratorFn is ISharpTSCallable callable)
                    return callable.Call(this, []);
            }
        }
        return null;
    }

    /// <summary>
    /// Iterates an async iterator by repeatedly calling .next() and awaiting results.
    /// </summary>
    private async Task<ExecutionResult> IterateAsyncIterator(object asyncIterator, Stmt.ForOf forOf, IReadOnlyList<string>? labels = null)
    {
        // The loop body must run in the for-await statement's own lexical scope, regardless of how the
        // iterator's next() mutates the interpreter's ambient environment. The eager-drain async generator
        // transiently repoints that environment at its own closure while draining, and a body shape whose
        // await is nested in a delegated expression can leave it repointed across the suspension; without
        // re-asserting the loop scope here, the body (and code after the loop) would resolve outer bindings
        // against the wrong scope — "Undefined variable" (#689). Capturing once and restoring after each
        // next() also hardens for-await against any custom async iterator with environment side effects.
        RuntimeEnvironment hostEnv = _environment;
        while (true)
        {
            // Call iterator.next()
            var nextResult = CallMethodOnObject(asyncIterator, "next", []);

            // Await the result if it's a promise/task
            if (nextResult is SharpTSPromise promise)
                nextResult = await AwaitPreservingEnvironment(promise.Task);
            else if (nextResult is Task<object?> task)
                nextResult = await AwaitPreservingEnvironment(task);

            // Re-assert the loop's lexical scope (see above) before consuming the result or running the body.
            _environment = hostEnv;

            // Check if the result is an iterator result object
            bool done = false;
            object? value = null;

            if (nextResult is SharpTSObject resultObj)
            {
                var doneVal = resultObj.GetProperty("done");
                done = IsTruthy(doneVal);
                value = resultObj.GetProperty("value");
            }
            else if (nextResult is SharpTSIteratorResult iterResult)
            {
                done = iterResult.Done;
                value = iterResult.Value;
            }
            // Plain Dictionary<string, object?> — used by runtime helpers like
            // Web Streams iterator results returned from ReadableStream.read().
            else if (nextResult is IDictionary<string, object?> dict)
            {
                if (dict.TryGetValue("done", out var d)) done = IsTruthy(d);
                if (dict.TryGetValue("value", out var v)) value = v;
            }

            if (done) break;

            var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
            // An early exit (break, or a return/throw out of the loop body) closes the iterator before
            // leaving: ECMA-262 AsyncIteratorClose calls return() and awaits it, so a suspended async
            // generator runs its finally blocks (#697 / cleanup). A lazy generator is otherwise simply
            // abandoned at its yield and its finally never runs. The labels (#728) match a labeled
            // break/continue that targets this for-await loop rather than escaping it.
            if (shouldBreak)
            {
                await CloseAsyncIteratorOnEarlyExit(asyncIterator);
                return ExecutionResult.Success();
            }
            if (shouldContinue) continue;
            if (abruptResult.HasValue)
            {
                await CloseAsyncIteratorOnEarlyExit(asyncIterator);
                return abruptResult.Value;
            }

            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Closes an async iterator when a <c>for await…of</c> exits early (break, or a return/throw out of
    /// the loop body) — ECMA-262 AsyncIteratorClose. Calls <c>return()</c> if the iterator provides one
    /// and awaits it, so a suspended async generator runs its <c>finally</c> blocks before the loop
    /// leaves. Cleanup is best-effort: a missing <c>return()</c> is skipped, and a rejection from the
    /// <c>return()</c> itself is swallowed so the loop's own completion (the break / return / throw that
    /// triggered the close) takes precedence.
    /// </summary>
    private async Task CloseAsyncIteratorOnEarlyExit(object asyncIterator)
    {
        object? result;
        try
        {
            result = CallMethodOnObject(asyncIterator, "return", []);
        }
        catch
        {
            // No return() method (or it threw synchronously) — nothing to close.
            return;
        }

        try
        {
            if (result is SharpTSPromise promise)
                await AwaitPreservingEnvironment(promise.Task);
            else if (result is Task<object?> task)
                await AwaitPreservingEnvironment(task);
        }
        catch
        {
            // The iterator's return() rejected during cleanup; the loop's own completion wins.
        }
    }

    /// <summary>
    /// Calls a method on an object by name.
    /// </summary>
    private object? CallMethodOnObject(object target, string methodName, List<object?> args)
    {
        if (target is SharpTSObject obj)
        {
            var method = obj.GetProperty(methodName);
            if (method != null)
            {
                if (method is SharpTSArrowFunction arrowFunc)
                    method = arrowFunc.Bind(obj);
                if (method is ISharpTSCallable callable)
                    return callable.Call(this, args);
            }
        }
        else if (target is SharpTSInstance inst)
        {
            // Try to find the method in the class
            var method = inst.GetClass().FindMethod(methodName);
            if (method != null)
            {
                var bound = SharpTSClass.BindMethod(method, inst);
                return bound.Call(this, args);
            }
        }
        else if (target is SharpTSGenerator gen)
        {
            // Handle generator methods
            return methodName switch
            {
                "next" => gen.Next(args.Count > 0 ? args[0] : SharpTSUndefined.Instance),
                "return" => gen.Return(args.Count > 0 ? args[0] : null),
                "throw" => gen.Throw(args.Count > 0 ? args[0] : null),
                _ => throw new InterpreterException($"Generator does not have method '{methodName}'.")
            };
        }
        else if (target is SharpTSAsyncGenerator asyncGen)
        {
            // Handle async generator methods
            return methodName switch
            {
                "next" => asyncGen.Next(),
                "return" => asyncGen.Return(args.Count > 0 ? args[0] : null),
                "throw" => asyncGen.Throw(args.Count > 0 ? args[0] : null),
                _ => throw new InterpreterException($"AsyncGenerator does not have method '{methodName}'.")
            };
        }

        throw new InterpreterException($"Cannot call method '{methodName}' on {target?.GetType().Name ?? "null"}.");
    }

    private async Task<ExecutionResult> ExecuteForInAsync(Stmt.ForIn forIn)
    {
        var labels = TakePendingLoopLabels();
        object? obj = (await EvaluateAsync(forIn.Object)).ToObject();

        IEnumerable<string> keys = obj switch
        {
            // Own enumerable keys only, hiding boxed-primitive internal slots and
            // honoring enumerability — consistent with Object.keys (#475).
            SharpTSObject o => o.OwnEnumerableKeys(),
            SharpTSInstance inst => inst.GetFieldNames(),
            // for...in skips holes per ECMA-262.
            SharpTSArray arr => Enumerable.Range(0, arr.Length).Where(arr.HasIndex).Select(i => i.ToString()),
            // Plain Dictionary<string, object?> from runtime helpers (e.g.,
            // Web Streams iterator results) — see SharpTSReadableStream.MakeReadResult.
            IDictionary<string, object?> d => d.Keys,
            _ => throw new InterpreterException("for...in requires an object.")
        };

        foreach (var key in keys)
        {
            var result = await ExecuteLoopBodyAsync(forIn.Variable.Lexeme, key, forIn.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;

            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        }

        return ExecutionResult.Success();
    }

    private async Task<ExecutionResult> ExecuteSwitchAsync(Stmt.Switch switchStmt)
    {
        // Use async context with unified core
        return await ExecuteSwitchCore(_asyncContext, switchStmt);
    }

    private async Task<ExecutionResult> ExecuteTryCatchAsync(Stmt.TryCatch tryCatch)
    {
        // Use async context with unified core
        return await ExecuteTryCatchCore(_asyncContext, tryCatch);
    }

    // ===================== Async Statement Handlers for Registry =====================
    // These methods return ValueTask<ExecutionResult> for use with DispatchStmtAsync.
    // They wrap the existing async execution logic in ValueTask.

    internal async ValueTask<ExecutionResult> ExecuteBlockAsyncVT(Stmt.Block block)
    {
        return await ExecuteBlockAsync(block.Statements, new RuntimeEnvironment(_environment));
    }

    internal async ValueTask<ExecutionResult> ExecuteSequenceAsyncVT(Stmt.Sequence seq)
    {
        foreach (var s in seq.Statements)
        {
            var result = await ExecuteStatementAsync(s);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    internal async ValueTask<ExecutionResult> ExecuteExpressionAsyncVT(Stmt.Expression exprStmt)
    {
        await EvaluateAsync(exprStmt.Expr);
        return ExecutionResult.Success();
    }

    internal async ValueTask<ExecutionResult> ExecuteIfAsyncVT(Stmt.If ifStmt)
    {
        if (IsTruthy(await EvaluateAsync(ifStmt.Condition)))
        {
            return await ExecuteStatementAsync(ifStmt.ThenBranch);
        }
        else if (ifStmt.ElseBranch != null)
        {
            return await ExecuteStatementAsync(ifStmt.ElseBranch);
        }
        return ExecutionResult.Success();
    }

    internal ValueTask<ExecutionResult> ExecuteWhileAsyncVT(Stmt.While whileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteWhileCore(_asyncContext, whileStmt, labels);
    }

    internal ValueTask<ExecutionResult> ExecuteDoWhileAsyncVT(Stmt.DoWhile doWhileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteDoWhileCore(_asyncContext, doWhileStmt, labels);
    }

    internal ValueTask<ExecutionResult> ExecuteForAsyncVT(Stmt.For forStmt)
    {
        // Drain labels parked for this loop so a `break`/`continue <label>` targeting it (e.g. from an
        // inner `for await`) is matched here instead of escaping. Empty when unlabeled (#728).
        var labels = TakePendingLoopLabels();
        return ExecuteForCore(_asyncContext, forStmt, labels);
    }

    internal async ValueTask<ExecutionResult> ExecuteForOfAsyncVT(Stmt.ForOf forOf)
    {
        return await ExecuteForOfAsync(forOf);
    }

    internal async ValueTask<ExecutionResult> ExecuteForInAsyncVT(Stmt.ForIn forIn)
    {
        return await ExecuteForInAsync(forIn);
    }

    /// <summary>
    /// Async analog of <see cref="ExecuteLabeledStatement"/>. Parks the chain's labels for the loop it
    /// wraps (drained by the async loop at entry, so a matching <c>continue</c>/<c>break</c> targets that
    /// loop) and executes the inner statement through the <em>async</em> path. Registering this is what
    /// routes a labeled <c>for await</c> to the async-iterator lowering instead of the synchronous
    /// executor — the latter throws "requires an iterable" on an async iterator (#728).
    /// </summary>
    internal async ValueTask<ExecutionResult> ExecuteLabeledStatementAsyncVT(Stmt.LabeledStatement labeledStmt)
    {
        // Flatten a chain of labels (a: b: stmt) down to the statement they wrap.
        List<string> labels = [];
        Stmt inner = labeledStmt;
        while (inner is Stmt.LabeledStatement ls)
        {
            labels.Add(ls.Label.Lexeme);
            inner = ls.Statement;
        }

        bool isLoop = inner is Stmt.While or Stmt.DoWhile or Stmt.For or Stmt.ForOf or Stmt.ForIn;

        if (isLoop)
        {
            // Park the labels for the loop; it drains them at entry and handles a matching
            // continue/break itself. Restore on the way out so an undrained label can't leak.
            int baseCount = _pendingLoopLabels.Count;
            _pendingLoopLabels.AddRange(labels);
            ExecutionResult result;
            try
            {
                result = await ExecuteStatementAsync(inner);
            }
            finally
            {
                if (_pendingLoopLabels.Count > baseCount)
                    _pendingLoopLabels.RemoveRange(baseCount, _pendingLoopLabels.Count - baseCount);
            }
            // The loop absorbs continue/break for its labels; guard a matching break defensively.
            if (result.Type == ExecutionResult.ResultType.Break &&
                result.TargetLabel != null && labels.Contains(result.TargetLabel))
                return ExecutionResult.Success();
            return result;
        }

        // Non-loop labeled statement: only `break <label>` is meaningful.
        var r = await ExecuteStatementAsync(inner);
        if (r.Type == ExecutionResult.ResultType.Break &&
            r.TargetLabel != null && labels.Contains(r.TargetLabel))
            return ExecutionResult.Success();
        return r;
    }

    internal async ValueTask<ExecutionResult> ExecuteSwitchAsyncVT(Stmt.Switch switchStmt)
    {
        return await ExecuteSwitchCore(_asyncContext, switchStmt);
    }

    internal async ValueTask<ExecutionResult> ExecuteTryCatchAsyncVT(Stmt.TryCatch tryCatch)
    {
        return await ExecuteTryCatchCore(_asyncContext, tryCatch);
    }

    internal async ValueTask<ExecutionResult> ExecuteThrowAsyncVT(Stmt.Throw throwStmt)
    {
        return ExecutionResult.Throw((await EvaluateAsync(throwStmt.Value)).ToObject());
    }

    internal async ValueTask<ExecutionResult> ExecuteVarAsyncVT(Stmt.Var varStmt)
    {
        object? value = null;
        if (varStmt.Initializer != null)
        {
            value = (await EvaluateAsync(varStmt.Initializer)).ToObject();
        }
        _environment.Define(varStmt.Name.Lexeme, value);
        return ExecutionResult.Success();
    }

    internal async ValueTask<ExecutionResult> ExecuteConstAsyncVT(Stmt.Const constStmt)
    {
        object? constValue = (await EvaluateAsync(constStmt.Initializer)).ToObject();
        _environment.Define(constStmt.Name.Lexeme, constValue);
        return ExecutionResult.Success();
    }

    internal async ValueTask<ExecutionResult> ExecuteReturnAsyncVT(Stmt.Return returnStmt)
    {
        // Bare `return;` completes with `undefined`, not null — see VisitReturn (#480).
        if (returnStmt.Value == null)
            return ExecutionResult.Return(RuntimeValue.Undefined);
        return ExecutionResult.Return((await EvaluateAsync(returnStmt.Value)).ToObject());
    }

    internal async ValueTask<ExecutionResult> ExecutePrintAsyncVT(Stmt.Print printStmt)
    {
        Out.WriteLine(Stringify((await EvaluateAsync(printStmt.Expr)).ToObject()));
        return ExecutionResult.Success();
    }

    // ===================== Async Expression Helpers =====================

    private async Task<RuntimeValue> EvaluateBinaryAsync(Expr.Binary binary)
    {
        var leftRV = await EvaluateAsync(binary.Left);
        var rightRV = await EvaluateAsync(binary.Right);

        // Fast path: both operands are numbers
        if (leftRV.IsNumber && rightRV.IsNumber)
        {
            double l = leftRV.AsNumber(), r = rightRV.AsNumber();
            var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);
            switch (desc)
            {
                case OperatorDescriptor.Plus:
                    return RuntimeValue.FromNumber(l + r);
                case OperatorDescriptor.Arithmetic:
                    return RuntimeValue.FromNumber(EvaluateArithmetic(binary.Operator.Type, l, r));
                case OperatorDescriptor.Power:
                    return RuntimeValue.FromNumber(Math.Pow(l, r));
                case OperatorDescriptor.Comparison:
                    return RuntimeValue.FromBoolean(EvaluateComparison(binary.Operator.Type, l, r));
                case OperatorDescriptor.Equality eq:
                    // ECMA-262 7.2.16: NaN is never strictly equal to anything
                    // (including itself). Use IEEE 754 `==` which returns false
                    // for NaN comparisons; Double.Equals is .NET-specific and
                    // treats NaN as equal to itself. Mirrors the sync fast path
                    // in EvaluateBinary (Interpreter.Calls.cs).
                    bool equal = l == r;
                    return RuntimeValue.FromBoolean(eq.IsNegated ? !equal : equal);
                case OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift:
                    return RuntimeValue.FromNumber(EvaluateBitwise(binary.Operator.Type, (int)l, (int)r));
                case OperatorDescriptor.UnsignedRightShift:
                    return RuntimeValue.FromNumber((double)((uint)(int)l >> ((int)r & 0x1F)));
            }
        }

        return EvaluateBinaryOperationRV(binary.Operator, leftRV, rightRV);
    }

    private ValueTask<RuntimeValue> EvaluateLogicalAsync(Expr.Logical logical) =>
        EvaluateLogicalCoreAsync(
            logical.Operator.Type,
            EvaluateAsync(logical.Left),
            () => EvaluateAsync(logical.Right));

    private ValueTask<RuntimeValue> EvaluateNullishCoalescingAsync(Expr.NullishCoalescing nc) =>
        EvaluateNullishCoalescingCoreAsync(
            EvaluateAsync(nc.Left),
            () => EvaluateAsync(nc.Right));

    private ValueTask<RuntimeValue> EvaluateTernaryAsync(Expr.Ternary ternary) =>
        EvaluateTernaryCoreAsync(
            EvaluateAsync(ternary.Condition),
            () => EvaluateAsync(ternary.ThenBranch),
            () => EvaluateAsync(ternary.ElseBranch));

    private async Task<RuntimeValue> EvaluateUnaryAsync(Expr.Unary unary)
    {
        // typeof never throws on undeclared variables
        if (unary.Operator.Type == TokenType.TYPEOF && unary.Right is Expr.Variable)
        {
            RuntimeValue right;
            try { right = await EvaluateAsync(unary.Right); }
            catch (InterpreterException) { right = RuntimeValue.Undefined; }
            return RuntimeValue.FromString(right.TypeofString());
        }

        var rv = await EvaluateAsync(unary.Right);
        return EvaluateUnaryOperationRV(unary.Operator, rv);
    }

    private async ValueTask<RuntimeValue> EvaluateAssignAsync(Expr.Assign assign)
    {
        var rv = await EvaluateAsync(assign.Value);
        object? value = rv.ToObject();

        if (_locals.TryGetValue(assign, out int distance))
        {
            _environment.AssignAt(distance, assign.Name, value);
        }
        else
        {
            _environment.Assign(assign.Name, value);
        }

        return rv;
    }

    private async Task<RuntimeValue> EvaluateCallAsync(Expr.Call call)
    {
        // Use async context with unified core - handles all special cases
        return RuntimeValue.FromBoxed(await EvaluateCallCore(_asyncContext, call));
    }

    private async Task<RuntimeValue> EvaluateNewAsync(Expr.New newExpr)
    {
        // Use async context with unified core - handles all built-in types
        return RuntimeValue.FromBoxed(await EvaluateNewCore(_asyncContext, newExpr));
    }

    private async Task<RuntimeValue> EvaluateArrayAsync(Expr.ArrayLiteral array)
    {
        // Use async context with unified core
        return RuntimeValue.FromBoxed(await EvaluateArrayCore(_asyncContext, array));
    }

    private async Task<RuntimeValue> EvaluateObjectAsync(Expr.ObjectLiteral obj)
    {
        // Use async context with unified core
        return RuntimeValue.FromBoxed(await EvaluateObjectCore(_asyncContext, obj));
    }

    private async Task<RuntimeValue> EvaluateTemplateLiteralAsync(Expr.TemplateLiteral template)
    {
        var evaluatedExprs = new List<object?>();
        foreach (var expr in template.Expressions)
        {
            // ToPrimitive (string hint) so boxed wrappers render their primitive (#708).
            evaluatedExprs.Add(ToPrimitive((await EvaluateAsync(expr)).ToObject(), PrimitiveHint.String));
        }
        return RuntimeValue.FromString(BuildTemplateLiteralString(template.Strings, evaluatedExprs));
    }

    private async Task<RuntimeValue> EvaluateTaggedTemplateLiteralAsync(Expr.TaggedTemplateLiteral tagged)
    {
        object? tag = (await EvaluateAsync(tagged.Tag)).ToObject();

        if (tag is not Runtime.Types.ISharpTSCallable callable)
            throw new InterpreterException("Tagged template tag must be a function.");

        var cookedList = tagged.CookedStrings.Cast<object?>().ToList();
        var stringsArray = new Runtime.Types.SharpTSTemplateStringsArray(cookedList, tagged.RawStrings);

        List<object?> args = [stringsArray];
        foreach (var expr in tagged.Expressions)
            args.Add((await EvaluateAsync(expr)).ToObject());

        return RuntimeValue.FromBoxed(callable.Call(this, args));
    }

}
