using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using System.Collections.Frozen;
using System.Threading;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    // Stack of using declaration trackers for nested scopes
    private readonly Stack<UsingTracker> _usingTrackerStack = new();

    /// <summary>
    /// Tracks resources declared with 'using' for automatic disposal at scope exit.
    /// </summary>
    private class UsingTracker
    {
        private readonly Interpreter _interpreter;
        private readonly List<(object? Resource, bool IsAsync)> _resources = new();

        public UsingTracker(Interpreter interpreter) => _interpreter = interpreter;

        public void Add(object? resource, bool isAsync) =>
            _resources.Add((resource, isAsync));

        public bool HasResources => _resources.Count > 0;

        /// <summary>
        /// Disposes all resources in reverse order, aggregating errors via SuppressedError.
        /// </summary>
        /// <param name="pendingError">Any error that occurred in the block before disposal.</param>
        /// <returns>The final error to throw (original, SuppressedError, or null if no errors).</returns>
        public object? DisposeAll(object? pendingError)
        {
            object? currentError = pendingError;

            // Dispose in reverse order (LIFO)
            for (int i = _resources.Count - 1; i >= 0; i--)
            {
                var (resource, isAsync) = _resources[i];
                try
                {
                    _interpreter.DisposeResource(resource, isAsync);
                }
                catch (Exception disposalError)
                {
                    // Wrap in SuppressedError: original error becomes 'error', disposal becomes 'suppressed'
                    currentError = new SharpTSSuppressedError(currentError, disposalError);
                }
            }

            return currentError;
        }
    }

    /// <summary>
    /// Executes an enum declaration, creating a runtime enum object with its members.
    /// </summary>
    /// <param name="enumStmt">The enum statement AST node.</param>
    /// <remarks>
    /// Supports numeric enums (auto-incrementing), string enums, and heterogeneous enums.
    /// Numeric enums support reverse mapping (value to name lookup).
    /// Const enums use ConstEnumValues which does not support reverse mapping.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/enums.html">TypeScript Enums</seealso>
    private void ExecuteEnumDeclaration(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value != null)
            {
                object? value;
                if (member.Value is Expr.Literal)
                {
                    // Literal value - evaluate directly
                    value = Evaluate(member.Value);
                }
                else if (enumStmt.IsConst)
                {
                    // Const enum computed expression - evaluate with resolved members
                    value = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                }
                else
                {
                    // Regular enum - evaluate normally
                    value = Evaluate(member.Value);
                }

                if (value is double d)
                {
                    members[member.Name.Lexeme] = d;
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (value is string s)
                {
                    members[member.Name.Lexeme] = s;
                    hasString = true;
                }
            }
            else
            {
                // Auto-increment for numeric
                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        if (enumStmt.IsConst)
        {
            // Const enums use a simpler wrapper without reverse mapping support
            _environment.Define(enumStmt.Name.Lexeme, new ConstEnumValues(enumStmt.Name.Lexeme, members));
        }
        else
        {
            EnumKind kind = (hasNumeric, hasString) switch
            {
                (true, false) => EnumKind.Numeric,
                (false, true) => EnumKind.String,
                (true, true) => EnumKind.Heterogeneous,
                _ => EnumKind.Numeric
            };

            _environment.Define(enumStmt.Name.Lexeme, new SharpTSEnum(enumStmt.Name.Lexeme, members, kind));
        }
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members via the shared
    /// <see cref="ConstEnumExpressionEvaluator"/>, surfacing failures as InterpreterExceptions.
    /// </summary>
    private static object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return ConstEnumExpressionEvaluator.Evaluate(expr, resolvedMembers, enumName,
            static e => new InterpreterException(e.Message));
    }

    /// <summary>
    /// Executes a block of statements within a given environment scope.
    /// Handles 'using' declarations with automatic disposal at scope exit.
    /// </summary>
    /// <param name="statements">The list of statements to execute.</param>
    /// <param name="environment">The runtime environment for this block's scope.</param>
    /// <remarks>
    /// Temporarily switches to the provided environment, executes all statements,
    /// then restores the previous environment. Uses try/finally to ensure disposal
    /// of 'using' resources even on abrupt completion. SuppressedError is used when
    /// both the block and disposal throw errors.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html#block-scoping">TypeScript Block Scoping</seealso>
    public ExecutionResult ExecuteBlock(List<Stmt> statements, RuntimeEnvironment environment)
    {
        // Create a tracker for using declarations in this scope
        var tracker = new UsingTracker(this);
        _usingTrackerStack.Push(tracker);

        object? pendingError = null;
        ExecutionResult blockResult = ExecutionResult.Success();

        try
        {
            using (PushScope(environment))
            {
                // JS hoists function declarations to the top of their
                // enclosing function/module scope. This lets lodash-style
                // IIFEs use mutually-recursive helpers declared in any order.
                HoistFunctionDeclarations(statements);

                foreach (Stmt statement in statements)
                {
                    // Check vm timeout token before each statement in a block
                    if (_vmTimeoutToken.IsCancellationRequested)
                        throw new Runtime.Exceptions.ThrowException(
                            new Runtime.Types.SharpTSError("Script execution timed out."));

                    var result = Execute(statement);
                    if (result.IsAbrupt)
                    {
                        // Capture the result but continue to finally for disposal
                        if (result.Type == ExecutionResult.ResultType.Throw)
                        {
                            pendingError = result.Value.ToObject();
                        }
                        blockResult = result;
                        break;
                    }
                }
            }
        }
        catch (GeneratorReturnException grex)
        {
            // A generator.return(v) abrupt completion injected at a yield point reached this
            // block with no enclosing try. Settle it as a Return so resources are still
            // disposed and the value flows out to the generator body (ECMA-262 §27.5.3.4).
            // It is not a guest throw, so leave pendingError null.
            blockResult = ExecutionResult.Return(RuntimeValue.FromBoxed(grex.Value));
        }
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() abort — not a guest throw and not catchable; re-throw ahead of
            // the generic handler so it unwinds the worker thread to its host loop.
            throw;
        }
        catch (Exception ex)
        {
            // Capture host exceptions as pending errors. A re-caught ThrowException is a guest
            // throw crossing back through this frame — preserve its guest origin so a downstream
            // catch still binds it verbatim rather than re-typing it (cross-boundary #694).
            bool isGuestThrow = ex is ThrowException;
            pendingError = TranslateException(ex);
            blockResult = ExecutionResult.Throw(pendingError, fromGuestThrow: isGuestThrow);
        }
        finally
        {
            // Always dispose resources and pop the tracker
            _usingTrackerStack.Pop();

            if (tracker.HasResources)
            {
                var finalError = tracker.DisposeAll(pendingError);

                // If disposal added errors (SuppressedError), update the result
                if (finalError != null && finalError != pendingError)
                {
                    blockResult = ExecutionResult.Throw(finalError);
                }
            }
        }

        return blockResult;
    }

    /// <summary>
    /// Executes a labeled statement, resolving break/continue that target this label.
    /// </summary>
    /// <param name="labeledStmt">The labeled statement AST node.</param>
    /// <remarks>
    /// When the label directly wraps an iteration statement, the label is handed to the loop so a
    /// <c>continue &lt;label&gt;</c> runs the loop's own step (increment / re-test / advance) rather
    /// than restarting it — restarting a <c>for</c> would re-run its initializer forever (#558).
    /// Chained labels (<c>a: b: for …</c>) all attach to the same loop. For a non-loop labeled
    /// statement (a block, etc.) only <c>break &lt;label&gt;</c> is meaningful.
    /// </remarks>
    private ExecutionResult ExecuteLabeledStatement(Stmt.LabeledStatement labeledStmt)
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
            // continue/break itself. Restore on the way out so an undrained label can't leak
            // into a sibling loop (defensive — the loop normally consumes them all).
            int baseCount = _pendingLoopLabels.Count;
            _pendingLoopLabels.AddRange(labels);
            ExecutionResult result;
            try
            {
                result = Execute(inner);
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
        var r = Execute(inner);
        if (r.Type == ExecutionResult.ResultType.Break &&
            r.TargetLabel != null && labels.Contains(r.TargetLabel))
            return ExecutionResult.Success();
        return r;
    }

    /// <summary>
    /// Core implementation for executing switch statements, shared between sync and async paths.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating case values and executing statements.</param>
    /// <param name="switchStmt">The switch statement AST node.</param>
    /// <returns>A ValueTask containing the execution result.</returns>
    private async ValueTask<ExecutionResult> ExecuteSwitchCore(IEvaluationContext ctx, Stmt.Switch switchStmt)
    {
        object? subject = (await ctx.EvaluateExprAsync(switchStmt.Subject)).ToObject();
        bool fallen = false;
        bool matched = false;

        foreach (var caseItem in switchStmt.Cases)
        {
            if (!fallen && !matched)
            {
                object? caseValue = (await ctx.EvaluateExprAsync(caseItem.Value)).ToObject();
                if (IsEqual(subject, caseValue))
                {
                    matched = true;
                }
            }

            if (fallen || matched)
            {
                fallen = true;
                foreach (var stmt in caseItem.Body)
                {
                    var result = await ctx.ExecuteStmtAsync(stmt);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                    if (result.IsAbrupt) return result;
                }
            }
        }

        if (switchStmt.DefaultBody != null && (fallen || !matched))
        {
            foreach (var stmt in switchStmt.DefaultBody)
            {
                var result = await ctx.ExecuteStmtAsync(stmt);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                if (result.IsAbrupt) return result;
            }
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes a switch statement with case matching and fall-through semantics.
    /// Pure sync implementation that avoids async overhead.
    /// </summary>
    /// <param name="switchStmt">The switch statement AST node.</param>
    /// <remarks>
    /// Implements JavaScript/TypeScript switch semantics including fall-through behavior
    /// and default case handling. Break statements surface as <see cref="ExecutionResult"/> signals.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements/switch">MDN switch Statement</seealso>
    private ExecutionResult ExecuteSwitch(Stmt.Switch switchStmt)
    {
        object? subject = Evaluate(switchStmt.Subject);
        bool fallen = false;
        bool matched = false;

        foreach (var caseItem in switchStmt.Cases)
        {
            if (!fallen && !matched)
            {
                object? caseValue = Evaluate(caseItem.Value);
                if (IsEqual(subject, caseValue))
                {
                    matched = true;
                }
            }

            if (fallen || matched)
            {
                fallen = true;
                foreach (var stmt in caseItem.Body)
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                    if (result.IsAbrupt) return result;
                }
            }
        }

        if (switchStmt.DefaultBody != null && (fallen || !matched))
        {
            foreach (var stmt in switchStmt.DefaultBody)
            {
                var result = Execute(stmt);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                if (result.IsAbrupt) return result;
            }
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Core implementation for executing try/catch/finally, shared between sync and async paths.
    /// </summary>
    /// <param name="ctx">The evaluation context for executing statements.</param>
    /// <param name="tryCatch">The try/catch statement AST node.</param>
    /// <returns>A ValueTask containing the execution result.</returns>
    private async ValueTask<ExecutionResult> ExecuteTryCatchCore(IEvaluationContext ctx, Stmt.TryCatch tryCatch)
    {
        ExecutionResult pendingResult = ExecutionResult.Success();
        bool exceptionHandled = false;

        try
        {
            foreach (var stmt in tryCatch.TryBlock)
            {
                var result = await ctx.ExecuteStmtAsync(stmt);
                if (result.Type == ExecutionResult.ResultType.Throw)
                {
                    pendingResult = result;
                    (exceptionHandled, pendingResult) = await HandleCatchBlockCore(ctx, tryCatch, result.Value.ToObject(), fromHostException: false);
                    break;
                }
                else if (result.IsAbrupt)
                {
                    pendingResult = result;
                    break;
                }
            }
        }
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() abort — not catchable by guest code; re-throw ahead of the
            // generic handler so it unwinds the worker thread.
            throw;
        }
        catch (Exception ex)
        {
            // A re-caught ThrowException is a genuine guest throw crossing back through a host
            // frame (callback / interop / Promise executor) — bind it verbatim and keep its guest
            // origin. Only a true host C# exception is host-translated and re-typed at the catch
            // binding (#694), so derive fromHostException from the exception kind.
            bool isGuestThrow = ex is ThrowException;
            object? errorValue = TranslateException(ex);
            pendingResult = ExecutionResult.Throw(errorValue, fromGuestThrow: isGuestThrow);
            (exceptionHandled, pendingResult) = await HandleCatchBlockCore(ctx, tryCatch, errorValue, fromHostException: !isGuestThrow);
        }

        // Always execute finally
        if (tryCatch.FinallyBlock != null)
        {
            var finallyResult = await ExecuteFinallyCore(ctx, tryCatch.FinallyBlock);
            if (finallyResult.IsAbrupt)
            {
                // Finally block overrides previous jump/throw
                return finallyResult;
            }
        }

        if (pendingResult.Type == ExecutionResult.ResultType.Throw && !exceptionHandled)
        {
            return pendingResult;
        }

        return pendingResult;
    }

    /// <summary>
    /// Core implementation for handling catch blocks, shared between sync and async paths.
    /// </summary>
    private async ValueTask<(bool Handled, ExecutionResult Result)> HandleCatchBlockCore(
        IEvaluationContext ctx,
        Stmt.TryCatch tryCatch,
        object? errorValue,
        bool fromHostException)
    {
        if (tryCatch.CatchBlock != null)
        {
            RuntimeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam != null)
            {
                // Only host-exception messages carry a stringified JS error type to recover
                // (#694); a genuine guest `throw value` is already the final caught value and
                // must never be re-typed — see CoerceCaughtValueForBinding.
                catchEnv.Define(tryCatch.CatchParam.Lexeme,
                    fromHostException ? CoerceCaughtValueForBinding(errorValue) : errorValue);
            }

            using (PushScope(catchEnv))
            {
                try
                {
                    foreach (var catchStmt in tryCatch.CatchBlock)
                    {
                        var catchResult = await ctx.ExecuteStmtAsync(catchStmt);
                        if (catchResult.IsAbrupt)
                        {
                            return (true, catchResult);
                        }
                    }
                    return (true, ExecutionResult.Success());
                }
                catch (Runtime.Exceptions.WorkerTerminatedException)
                {
                    // worker.terminate() abort raised while running a guest catch block —
                    // re-throw so it unwinds the worker thread instead of being re-caught.
                    throw;
                }
                catch (Exception ex)
                {
                    object? catchError = ex is ThrowException tex ? tex.Value : ex.Message;
                    return (true, ExecutionResult.Throw(catchError, fromGuestThrow: ex is ThrowException));
                }
            }
        }
        return (false, ExecutionResult.Throw(errorValue, fromGuestThrow: !fromHostException));
    }

    /// <summary>
    /// Core implementation for executing finally blocks, shared between sync and async paths.
    /// </summary>
    private async ValueTask<ExecutionResult> ExecuteFinallyCore(IEvaluationContext ctx, List<Stmt> finallyBlock)
    {
        foreach (var stmt in finallyBlock)
        {
            var result = await ctx.ExecuteStmtAsync(stmt);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes a try/catch/finally statement with proper exception handling.
    /// Pure sync implementation that avoids async overhead.
    /// </summary>
    /// <param name="tryCatch">The try/catch statement AST node.</param>
    /// <remarks>
    /// Handles <see cref="ThrowException"/> from user throw statements. Ensures finally block
    /// executes for all exit paths including return, break, and continue. The catch parameter
    /// is bound in a new scope.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements/try...catch">MDN try...catch</seealso>
    private ExecutionResult ExecuteTryCatch(Stmt.TryCatch tryCatch)
    {
        ExecutionResult pendingResult = ExecutionResult.Success();
        bool exceptionHandled = false;

        try
        {
            foreach (var stmt in tryCatch.TryBlock)
            {
                var result = Execute(stmt);
                if (result.Type == ExecutionResult.ResultType.Throw)
                {
                    pendingResult = result;
                    (exceptionHandled, pendingResult) = HandleCatchBlock(tryCatch, result.Value.ToObject(), fromHostException: false);
                    break;
                }
                else if (result.IsAbrupt)
                {
                    pendingResult = result;
                    break;
                }
            }
        }
        catch (GeneratorReturnException grex)
        {
            // A generator.return(v) abrupt completion injected at a yield point inside this
            // try. A return is not catchable, so bypass the catch clause and record a Return;
            // the finally block below still runs (ECMA-262 §27.5.3.4).
            pendingResult = ExecutionResult.Return(RuntimeValue.FromBoxed(grex.Value));
        }
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() abort — a worker cannot catch its own termination; re-throw
            // ahead of the guest catch so it unwinds the worker thread (skips this catch/finally).
            throw;
        }
        catch (Exception ex)
        {
            // A re-caught ThrowException is a genuine guest throw crossing back through a host
            // frame (callback / interop / Promise executor) — bind it verbatim and keep its guest
            // origin. Only a true host C# exception is host-translated and re-typed at the catch
            // binding (#694), so derive fromHostException from the exception kind.
            bool isGuestThrow = ex is ThrowException;
            object? errorValue = TranslateException(ex);
            pendingResult = ExecutionResult.Throw(errorValue, fromGuestThrow: isGuestThrow);
            (exceptionHandled, pendingResult) = HandleCatchBlock(tryCatch, errorValue, fromHostException: !isGuestThrow);
        }

        // Always execute finally
        if (tryCatch.FinallyBlock != null)
        {
            var finallyResult = ExecuteFinallyBlock(tryCatch.FinallyBlock);
            if (finallyResult.IsAbrupt)
            {
                // Finally block overrides previous jump/throw
                return finallyResult;
            }
        }

        if (pendingResult.Type == ExecutionResult.ResultType.Throw && !exceptionHandled)
        {
            return pendingResult;
        }

        return pendingResult;
    }

    /// <summary>
    /// Pure sync implementation for handling catch blocks.
    /// </summary>
    private (bool Handled, ExecutionResult Result) HandleCatchBlock(
        Stmt.TryCatch tryCatch,
        object? errorValue,
        bool fromHostException)
    {
        if (tryCatch.CatchBlock != null)
        {
            RuntimeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam != null)
            {
                // Only host-exception messages carry a stringified JS error type to recover
                // (#694); a genuine guest `throw value` is already the final caught value and
                // must never be re-typed — see CoerceCaughtValueForBinding.
                catchEnv.Define(tryCatch.CatchParam.Lexeme,
                    fromHostException ? CoerceCaughtValueForBinding(errorValue) : errorValue);
            }

            using (PushScope(catchEnv))
            {
                try
                {
                    foreach (var catchStmt in tryCatch.CatchBlock)
                    {
                        var catchResult = Execute(catchStmt);
                        if (catchResult.IsAbrupt)
                        {
                            return (true, catchResult);
                        }
                    }
                    return (true, ExecutionResult.Success());
                }
                catch (GeneratorReturnException grex)
                {
                    // generator.return(v) injected at a yield inside this catch block:
                    // propagate as a Return (which runs the enclosing finally) rather than
                    // re-throwing it as a guest error (ECMA-262 §27.5.3.4).
                    return (true, ExecutionResult.Return(RuntimeValue.FromBoxed(grex.Value)));
                }
                catch (Runtime.Exceptions.WorkerTerminatedException)
                {
                    // worker.terminate() abort raised while running a guest catch block —
                    // re-throw so it unwinds the worker thread instead of being re-caught.
                    throw;
                }
                catch (Exception ex)
                {
                    object? catchError = ex is ThrowException tex ? tex.Value : ex.Message;
                    return (true, ExecutionResult.Throw(catchError, fromGuestThrow: ex is ThrowException));
                }
            }
        }
        return (false, ExecutionResult.Throw(errorValue, fromGuestThrow: !fromHostException));
    }

    /// <summary>
    /// Pure sync implementation for executing finally blocks.
    /// </summary>
    private ExecutionResult ExecuteFinallyBlock(List<Stmt> finallyBlock)
    {
        foreach (var stmt in finallyBlock)
        {
            var result = Execute(stmt);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes a for...of loop, iterating over array elements.
    /// </summary>
    /// <param name="forOf">The for...of statement AST node.</param>
    /// <remarks>
    /// Creates a new scope for each iteration with the loop variable bound to the current element.
    /// Supports break and continue via <see cref="ExecutionResult"/> signals.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/iterators-and-generators.html#forof-statements">TypeScript for...of</seealso>
    private ExecutionResult ExecuteForOf(Stmt.ForOf forOf)
    {
        // Drain before evaluating the iterable so a labeled loop produced by the iterable
        // expression (e.g. an IIFE) can't steal this loop's label.
        var labels = TakePendingLoopLabels();
        object? iterable = Evaluate(forOf.Iterable);

        // First, check for Symbol.iterator protocol on objects/instances
        IEnumerable<object?>? customIterator = TryGetSymbolIterator(iterable);
        if (customIterator != null)
        {
            return IterateWithBreakContinue(customIterator, forOf.Variable.Lexeme, forOf.Body, labels);
        }

        // Get elements based on iterable type.
        // NOTE: three near-copies of this switch exist — here, GetIterableElements (spread /
        // yield*), and the for-await-of one in Interpreter.Async.cs. They have drifted before:
        // typed arrays were iterable in only one of them, so `for (const b of u8)` threw while
        // `[...u8]` worked. Add new iterable kinds to all three. (#1282)
        IEnumerable<object?> elements = iterable switch
        {
            SharpTSArray array => array,
            SharpTSBuffer buffer => buffer.Data.Select(b => (object?)(double)b),  // yields byte values as numbers
            SharpTSTypedArray typed => typed.ToArray(),    // %TypedArray%.prototype[@@iterator]
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            List<object?> list => list,                    // plain List<object?>
            IEnumerable<object?> enumerable => enumerable, // IEnumerable<object?> (e.g., SharpTSIntlSegments)
            _ => throw new InterpreterException("for...of requires an iterable (array, Map, Set, or iterator).")
        };

        return IterateWithBreakContinue(elements, forOf.Variable.Lexeme, forOf.Body, labels);
    }

    /// <summary>
    /// Executes a for...in loop, iterating over object property names.
    /// </summary>
    /// <param name="forIn">The for...in statement AST node.</param>
    /// <remarks>
    /// Iterates over enumerable property names (keys) of objects, instances, or array indices.
    /// Creates a new scope for each iteration. Supports break and continue.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements/for...in">MDN for...in</seealso>
    private ExecutionResult ExecuteForIn(Stmt.ForIn forIn)
    {
        var labels = TakePendingLoopLabels();
        object? obj = Evaluate(forIn.Object);

        IEnumerable<string> keys = obj switch
        {
            SharpTSProxy proxy => proxy.TrapOwnEnumerableKeys(this),
            // Own enumerable keys only, hiding boxed-primitive internal slots and
            // honoring enumerability — consistent with Object.keys (#475).
            SharpTSObject o => o.OwnEnumerableKeys(),
            SharpTSInstance i => i.GetFieldNames(),
            // for...in skips holes per ECMA-262 (only own enumerable index properties
            // that actually exist — holes don't).
            SharpTSArray a => a.OwnEnumerableKeys(),
            // JS functions are objects; enumerate user-assigned properties. Lodash iterates
            // its own utility namespace (which is a function returning the wrapper) with
            // `for (var key in _) { ... }` to copy members onto the mixin target.
            SharpTSFunction f => f.PropertyKeys,
            SharpTSArrowFunction af => af.PropertyKeys,
            SharpTSMath math => math.OwnEnumerableKeys(),
            SharpTSJSON json => json.OwnEnumerableKeys(),
            SharpTSDate date => date.OwnEnumerableKeys(),
            SharpTSRegExp regex => regex.OwnEnumerableKeys(),
            SharpTSObjectNamespace objectNamespace => objectNamespace.OwnEnumerableKeys(),
            SharpTSGlobalThis globalThis => globalThis.OwnEnumerableKeys(),
            // Every built-in prototype singleton at once — Object/Array/String/Number/
            // Boolean/Function.prototype. Naming them individually is how Object.prototype
            // and Number.prototype came to be missing here ("for...in requires an object").
            ISharpTSMutableBuiltIn builtInPrototype => builtInPrototype.OwnEnumerableKeys(),
            // Plain Dictionary<string, object?> from runtime helpers (e.g.,
            // Web Streams iterator results) — see SharpTSReadableStream.MakeReadResult.
            IDictionary<string, object?> d => d.Keys,
            // ECMA-262 §17 — built-in functions have only `name` and `length`
            // as own properties, both non-enumerable. for-in yields nothing,
            // but throwing here would crash test262's verifyProperty (which
            // for-in iterates its target object before checking enumerability).
            ISharpTSCallable => Enumerable.Empty<string>(),
            _ => throw new InterpreterException("for...in requires an object.")
        };

        return IterateWithBreakContinue(keys.Cast<object?>(), forIn.Variable.Lexeme, forIn.Body, labels);
    }

    /// <summary>
    /// Extracts the raw iterator object and its bound <c>next</c> callable from a custom iterable
    /// (one that carries <c>[Symbol.iterator]</c>). Used by <c>yield*</c> delegation to drive the
    /// iterator manually so the outer generator's resume value can be forwarded as the argument to
    /// <c>next(v)</c> (ECMA-262 §14.4.14, #503).
    /// </summary>
    /// <returns>
    /// <c>true</c> when a custom iterator was found; <c>false</c> for built-in iterables (arrays,
    /// strings, Maps, Sets) that have no <c>[Symbol.iterator]</c> in the runtime.
    /// </returns>
    internal bool TryGetCustomIteratorProtocol(
        object? iterable,
        out object? iteratorObj,
        out ISharpTSCallable? nextFn)
    {
        iteratorObj = null;
        nextFn = null;

        object? iteratorFn = null;
        object? thisForBind = null;

        if (iterable is SharpTSObject obj)
        {
            iteratorFn = obj.GetBySymbol(SharpTSSymbol.Iterator);
            thisForBind = obj;
        }
        else if (iterable is SharpTSInstance inst)
        {
            iteratorFn = inst.GetBySymbol(SharpTSSymbol.Iterator);
            if (iteratorFn == null && inst.GetClass().FindSymbolMethod(SharpTSSymbol.Iterator) is { } sym)
                iteratorFn = SharpTSClass.BindMethod(sym, inst);
            thisForBind = inst;
        }

        if (iteratorFn == null) return false;

        // Bind 'this' and call to get the iterator object.
        if (iteratorFn is SharpTSArrowFunction arrowFn && thisForBind != null)
            iteratorFn = arrowFn.Bind(thisForBind);

        object? iterator = iteratorFn is ISharpTSCallable callableIter
            ? callableIter.Call(this, [])
            : iteratorFn is SharpTSFunction fn
                ? fn.Call(this, [])
                : null;

        if (iterator == null) return false;

        // A generator returned by [Symbol.iterator]() is driven directly (it doesn't
        // expose a data-property next()); let the caller fall through to GetIterableElements.
        if (iterator is IEnumerable<object?> and not SharpTSObject and not SharpTSInstance)
            return false;

        // Extract and bind the 'next' method from the iterator object.
        object? nextMethod = null;
        if (iterator is SharpTSObject iterObj)
        {
            nextMethod = iterObj.GetProperty("next");
        }
        else if (iterator is SharpTSInstance iterInst)
        {
            nextMethod = iterInst.GetRawField("next");
            if (nextMethod == null)
            {
                var tok = new Token(TokenType.IDENTIFIER, "next", null, 0);
                try { nextMethod = iterInst.Get(tok); } catch { }
            }
        }

        if (nextMethod is SharpTSArrowFunction arrow)
            nextMethod = arrow.Bind(iterator);
        else if (nextMethod is SharpTSFunction nfn && iterator is SharpTSInstance nInst)
            nextMethod = nfn.Bind(nInst);

        if (nextMethod is not ISharpTSCallable callable) return false;

        iteratorObj = iterator;
        nextFn = callable;
        return true;
    }

    /// <summary>
    /// Attempts to get an iterator from an object using the Symbol.iterator protocol.
    /// </summary>
    /// <returns>An enumerable of values if the object has a Symbol.iterator, null otherwise.</returns>
    private IEnumerable<object?>? TryGetSymbolIterator(object? iterable)
    {
        // Arrays can replace their inherited @@iterator with an own method.
        // Consult that override before falling back to indexed enumeration.
        if (iterable is SharpTSArray array && array.HasSymbolProperty(SharpTSSymbol.Iterator))
        {
            var iteratorFn = array.GetBySymbol(SharpTSSymbol.Iterator);
            if (iteratorFn is not ISharpTSCallable)
                throw new ThrowException(new SharpTSTypeError(
                    "[Symbol.iterator] must be a function"));
            if (TryBindReceiverForMethodAccess(iteratorFn, array) is { } boundIteratorFn)
                iteratorFn = boundIteratorFn;
            return EnumerateWithIteratorProtocol(iteratorFn);
        }

        // Check for Symbol.iterator on SharpTSObject
        if (iterable is SharpTSObject obj)
        {
            object? iteratorFn;
            if (obj.TryGetSymbolAccessor(SharpTSSymbol.Iterator, out var iteratorGetter, out _))
            {
                iteratorFn = iteratorGetter is null
                    ? SharpTSUndefined.Instance
                    : FunctionBuiltIns.CallWithThis(this, iteratorGetter, obj, []);
            }
            else
            {
                iteratorFn = obj.GetBySymbol(SharpTSSymbol.Iterator);
            }
            if (iteratorFn != null || obj.HasSymbolProperty(SharpTSSymbol.Iterator))
            {
                if (iteratorFn is not ISharpTSCallable)
                    throw new ThrowException(new SharpTSTypeError(
                        "[Symbol.iterator] must be a function"));
                // Bind 'this' to the object for a function expression / object method shorthand,
                // including generator forms (`[Symbol.iterator]: function*(){ this... }`, #775).
                if (TryBindReceiverForMethodAccess(iteratorFn, obj) is { } boundIteratorFn)
                {
                    iteratorFn = boundIteratorFn;
                }
                return EnumerateWithIteratorProtocol(iteratorFn);
            }
        }

        // Check for Symbol.iterator on SharpTSInstance
        if (iterable is SharpTSInstance inst)
        {
            var iteratorFn = inst.GetBySymbol(SharpTSSymbol.Iterator);
            // Fall back to a declared symbol-keyed method on the class chain
            // (`class C { [Symbol.iterator]() {...} }`, including generator forms).
            if (iteratorFn == null && inst.GetClass().FindSymbolMethod(SharpTSSymbol.Iterator) is { } symMethod)
            {
                iteratorFn = SharpTSClass.BindMethod(symMethod, inst);
            }
            if (iteratorFn != null)
            {
                if (iteratorFn is not ISharpTSCallable)
                    throw new ThrowException(new SharpTSTypeError(
                        "[Symbol.iterator] must be a function"));
                // Bind 'this' to the instance for a function expression / object method shorthand,
                // including generator forms (#775).
                if (TryBindReceiverForMethodAccess(iteratorFn, inst) is { } boundIteratorFn)
                {
                    iteratorFn = boundIteratorFn;
                }
                return EnumerateWithIteratorProtocol(iteratorFn);
            }
        }

        return null;
    }

    /// <summary>
    /// Iterates using the JavaScript iterator protocol: calls next() until done is true.
    /// </summary>
    private IEnumerable<object?> EnumerateWithIteratorProtocol(object iteratorFn)
    {
        // Call the iterator function to get the iterator object
        object? iterator;
        if (iteratorFn is ISharpTSCallable callable)
        {
            iterator = callable.Call(this, []);
        }
        else if (iteratorFn is SharpTSFunction fn)
        {
            iterator = fn.Call(this, []);
        }
        else
        {
            throw new ThrowException(new SharpTSTypeError(
                "[Symbol.iterator] must be a function"));
        }

        // A generator-valued [Symbol.iterator]() (`*[Symbol.iterator]() { yield ... }`) returns a
        // generator object, which exposes iteration directly via IEnumerable rather than a queryable
        // next() data property — delegate to it (the explicit next()-protocol objects are handled below).
        if (iterator is IEnumerable<object?> directEnumerable and not SharpTSObject and not SharpTSInstance)
        {
            foreach (var item in directEnumerable)
                yield return item;
            yield break;
        }

        if (iterator is not (SharpTSObject or SharpTSInstance))
            throw new ThrowException(new SharpTSTypeError(
                "[Symbol.iterator]() must return an object"));

        // Iterate using the iterator protocol. The surrounding try/finally
        // implements ECMA-262 7.4.6 IteratorClose: when iteration is abandoned
        // before the iterator reports done — a for-of break/throw, or a spread/
        // Array.from element callback throwing (the C# enumerator is disposed on
        // any early foreach/.ToList() exit) — invoke the iterator's return() so
        // it can clean up. `iteratorDone` suppresses the close on normal
        // exhaustion (a completed iterator must not be re-closed). yield is
        // legal here because the try has only a finally, no catch.
        bool iteratorDone = false;
        try
        {
            while (true)
            {
                // Honor the VM timeout token. A custom iterator whose next() never
                // reports done — or whose done/value getters loop — would otherwise
                // spin this thread forever, past the timeout. Under the Test262
                // harness the timed-out test's worker thread is a background thread
                // that keeps running after the runner returns Timeout, leaking a
                // CPU-pegged orphan thread for the rest of the process's life. The
                // accumulating orphans starve later tests and turn the interpreted
                // baseline non-deterministic under load. Checking here unwinds the
                // enumerator (it is consumed via .ToList()/foreach by spread,
                // Array.from, yield*, etc.) so the thread actually exits.
                if (_vmTimeoutToken.IsCancellationRequested)
                    throw new ThrowException(new SharpTSError("Script execution timed out."));

                // Get the next() method
                object? nextMethod = null;
                if (iterator is SharpTSObject iterObj)
                {
                    nextMethod = iterObj.GetProperty("next");
                }
                else if (iterator is SharpTSInstance iterInst)
                {
                    nextMethod = iterInst.GetRawField("next");
                    if (nextMethod == null)
                    {
                        // Try getting a method from the class
                        var tok = new Token(TokenType.IDENTIFIER, "next", null, 0);
                        try { nextMethod = iterInst.Get(tok); } catch { }
                    }
                }

                if (nextMethod == null)
                {
                    throw new InterpreterException("Iterator must have a next() method.");
                }

                // Bind next() to the iterator object so 'this' works correctly
                if (nextMethod is SharpTSArrowFunction arrowFn)
                {
                    nextMethod = arrowFn.Bind(iterator!);
                }
                else if (nextMethod is SharpTSFunction fn && iterator is SharpTSInstance inst)
                {
                    nextMethod = fn.Bind(inst);
                }

                // Call next()
                object? result;
                if (nextMethod is ISharpTSCallable nextCallable)
                {
                    result = nextCallable.Call(this, []);
                }
                else if (nextMethod is SharpTSFunction nextFn)
                {
                    result = nextFn.Call(this, []);
                }
                else
                {
                    throw new InterpreterException("Iterator.next must be a function.");
                }

                // Get done and value from result
                bool done = false;
                object? value = null;

                if (result is SharpTSObject resultObj)
                {
                    // ECMA-262 7.4.5 IteratorComplete / 7.4.4 IteratorValue read
                    // `done` then `value` via Get(), which invokes accessor getters
                    // and walks the prototype chain. `resultObj.GetProperty` reads
                    // only own data fields, so a result with a `get done()`/
                    // `get value()` accessor (e.g. Test262's poisoned-iterator
                    // tests: `Object.defineProperty(r, 'value', { get() { throw } })`)
                    // never fired the getter — `done` stayed undefined/falsy and the
                    // loop spun forever instead of surfacing the throw. Read `value`
                    // only when not done, matching IteratorStep (don't invoke the
                    // value getter once the iterator has completed).
                    done = IsTruthy(EvaluateGetOnRecord(resultObj, "done"));
                    value = done ? null : EvaluateGetOnRecord(resultObj, "value");
                }
                else if (result is SharpTSInstance resultInst)
                {
                    var doneTok = new Token(TokenType.IDENTIFIER, "done", null, 0);
                    var valueTok = new Token(TokenType.IDENTIFIER, "value", null, 0);
                    try
                    {
                        done = IsTruthy(resultInst.Get(doneTok));
                        value = resultInst.Get(valueTok);
                    }
                    catch
                    {
                        // Fall back to field access
                        done = IsTruthy(resultInst.GetRawField("done"));
                        value = resultInst.GetRawField("value");
                    }
                }

                if (done)
                {
                    iteratorDone = true;
                    yield break;
                }

                yield return value;
            }
        }
        finally
        {
            // IteratorClose: only when the consumer abandoned us early (see above).
            if (!iteratorDone)
                TryCallIteratorReturn(iterator);
        }
    }

    /// <summary>
    /// ECMA-262 7.4.6 IteratorClose: invoke an iterator's <c>return()</c> method
    /// when iteration is abandoned before the iterator reports done (a for-of
    /// break/throw, or a spread / <c>Array.from(items, mapFn)</c> element callback
    /// throwing). Best-effort: a missing/undefined <c>return</c> is a no-op, and any
    /// abrupt completion from <c>return()</c> itself is swallowed — this runs inside
    /// a <c>finally</c> during exception unwind, so letting <c>return()</c> throw
    /// would mask the original completion (which is what the spec discards when the
    /// triggering completion is itself a throw).
    /// </summary>
    private void TryCallIteratorReturn(object? iterator)
    {
        object? returnMethod = iterator switch
        {
            // Get(iterator, "return") — getter-aware, walks the prototype chain.
            SharpTSObject iterObj => EvaluateGetOnRecord(iterObj, "return"),
            SharpTSInstance iterInst => TryGetInstanceMember(iterInst, "return"),
            _ => null,
        };
        if (returnMethod is null or SharpTSUndefined) return;

        try
        {
            if (returnMethod is SharpTSArrowFunction arrowFn)
                returnMethod = arrowFn.Bind(iterator!);
            else if (returnMethod is SharpTSFunction fn && iterator is SharpTSInstance inst)
                returnMethod = fn.Bind(inst);

            // SharpTSFunction also implements ISharpTSCallable, so this covers both.
            if (returnMethod is ISharpTSCallable callable)
                callable.Call(this, []);
        }
        catch
        {
            // Swallow — see remarks. IteratorClose must not let return()'s failure
            // replace the completion that triggered it.
        }
    }

    /// <summary>
    /// Reads a member from a class instance the way the iterator protocol does:
    /// raw field first, then a declared method via the class chain (Get throws
    /// when absent, so it is guarded). Returns null when the member is absent.
    /// </summary>
    private object? TryGetInstanceMember(SharpTSInstance instance, string name)
    {
        var field = instance.GetRawField(name);
        if (field != null) return field;
        var tok = new Token(TokenType.IDENTIFIER, name, null, 0);
        try { return instance.Get(tok); } catch { return null; }
    }

    /// <summary>
    /// Gets iterable elements from any iterable value, including custom iterables with Symbol.iterator.
    /// This method is used by spread operators and yield* to uniformly handle all iterable types.
    /// </summary>
    /// <param name="value">The value to iterate.</param>
    /// <returns>An enumerable of the value's elements.</returns>
    /// <exception cref="Exception">Thrown if the value is not iterable.</exception>
    internal IEnumerable<object?> GetIterableElements(object? value)
    {
        // First, check for Symbol.iterator protocol on objects/instances
        IEnumerable<object?>? customIterator = TryGetSymbolIterator(value);
        if (customIterator != null)
        {
            return customIterator;
        }

        // Fall back to known iterable types
        return value switch
        {
            SharpTSArray array => array,
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            // Typed arrays and Buffers are iterable in JS (%TypedArray%.prototype[@@iterator]),
            // and the compiled path expands them, but they carry no Symbol.iterator in this
            // runtime so TryGetSymbolIterator above misses them. Read their elements directly
            // instead of throwing "not iterable". (#1282)
            SharpTSTypedArray typed => typed.ToArray(),
            SharpTSBuffer buf => buf.Data.Select(b => (object?)(double)b),
            string s => s.Select(c => (object?)c.ToString()),
            List<object?> list => list,                    // plain List<object?>
            IEnumerable<object?> enumerable => enumerable, // IEnumerable<object?> (e.g., SharpTSIntlSegments)
            null => throw new InterpreterException("Cannot spread null or undefined."),
            _ => throw new InterpreterException($"Value of type '{value.GetType().Name}' is not iterable. Expected an array, string, Map, Set, generator, or object with [Symbol.iterator].")
        };
    }

    /// <summary>
    /// True when <paramref name="value"/> is iterable via <see cref="GetIterableElements"/>
    /// (a known iterable type or carrying a Symbol.iterator). Lets Array.from choose the
    /// iterator protocol vs the array-like (length + indices) path without catching a throw.
    /// </summary>
    internal bool IsIterableSource(object? value) =>
        value is SharpTSArray or SharpTSMap or SharpTSSet or SharpTSIterator or SharpTSGenerator
            or SharpTSTypedArray or SharpTSBuffer
            or string or List<object?> or IEnumerable<object?>
        || TryGetSymbolIterator(value) != null;

    /// <summary>
    /// ECMA-262 array-like read (Array.from on a non-iterable): ToLength(Get(src,"length")),
    /// then Get(src, 0..len-1). Missing indices yield undefined — so Array.from({length:3})
    /// produces [undefined, undefined, undefined].
    /// </summary>
    internal List<object?> ReadArrayLikeElements(object? value)
    {
        long len = ToLength(GetArrayLikeProperty(value, "length"));
        var result = new List<object?>(len > int.MaxValue ? int.MaxValue : (int)len);
        for (long i = 0; i < len; i++)
            result.Add(GetArrayLikeProperty(value, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    private static object? GetArrayLikeProperty(object? value, string key) => value switch
    {
        SharpTSObject obj => obj.GetProperty(key),
        SharpTSInstance inst => inst.GetProperty(key),
        _ => SharpTSUndefined.Instance,
    };

    private static long ToLength(object? value)
    {
        double n = Compilation.RuntimeTypes.ToNumber(value);
        if (double.IsNaN(n) || n <= 0) return 0;
        return (long)Math.Min(Math.Floor(n), 9007199254740991.0); // 2^53 - 1
    }

    /// <summary>
    /// Normalizes an array binding-pattern source through the iterator protocol (#685). Array
    /// destructuring (<c>const [a, b] = src</c>) desugars to positional index access, which is only
    /// correct for index-addressable sources. Plain arrays pass through unchanged (fast path). Typed
    /// arrays and buffers are index-addressable but NOT iterable via <see cref="GetIterableElements"/>,
    /// so they are materialized element-by-element into a fresh <see cref="SharpTSArray"/>; any other
    /// source — including <b>strings</b> — is materialized via <see cref="GetIterableElements"/> (the
    /// same routine spread uses) so the index access reads the iterated elements. Strings (#753) and
    /// typed arrays/buffers (#781) are intentionally materialized (not on the fast path) so a rest
    /// element binds a fresh <c>Array</c> rather than the trailing substring / typed-array slice
    /// (<c>const [a, ...rest] = "hi"</c> → <c>rest = ["i"]</c>; <c>const [a, ...rest] = u8</c> →
    /// <c>rest</c> is a real Array), matching ECMA-262; non-rest element values are identical either
    /// way. A genuinely non-iterable source throws "is not iterable", matching JS; the type checker
    /// already rejects those statically except behind <c>any</c>.
    /// </summary>
    internal object? NormalizeArrayDestructureSource(object? value)
    {
        switch (value)
        {
            case SharpTSArray:
                return value;
            // Typed arrays / buffers expose index access but no [Symbol.iterator] in this runtime, so
            // GetIterableElements would throw; read their elements directly into a fresh Array (#781).
            case SharpTSTypedArray typed:
                return typed.ToArray();
            case SharpTSBuffer buffer:
                return new SharpTSArray(buffer.Data.Select(b => (object?)(double)b).ToList());
        }

        return new SharpTSArray(GetIterableElements(value).ToList());
    }

    /// <summary>
    /// Iterates over elements with proper break/continue handling.
    /// </summary>
    private ExecutionResult IterateWithBreakContinue(IEnumerable<object?> elements, string variableName, Stmt body, IReadOnlyList<string>? labels = null)
    {
        foreach (var element in elements)
        {
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(variableName, element);

            using (PushScope(loopEnv))
            {
                var result = Execute(body);
                var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
                if (shouldBreak) return ExecutionResult.Success();
                if (shouldContinue) continue;
                if (abruptResult.HasValue) return abruptResult.Value;

                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
        }
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Core while-loop execution logic shared by the sync and async evaluators. The evaluation
    /// context supplies the condition/body evaluation strategy so a single body serves both paths,
    /// replacing the former lambda-based sync core and the hand-copied async twin.
    /// Uses HandleLoopResult for consistent break/continue handling.
    /// </summary>
    private async ValueTask<ExecutionResult> ExecuteWhileCore(
        IEvaluationContext ctx,
        Stmt.While whileStmt,
        IReadOnlyList<string>? labels = null)
    {
        while ((await ctx.EvaluateExprAsync(whileStmt.Condition)).IsTruthy())
        {
            // Check vm timeout token on each loop iteration
            if (_vmTimeoutToken.IsCancellationRequested)
                throw new Runtime.Exceptions.ThrowException(
                    new Runtime.Types.SharpTSError("Script execution timed out."));

            var result = await ctx.ExecuteStmtAsync(whileStmt.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;

            // Process any pending timer callbacks. Timer threads enqueue callbacks
            // and we execute them here, avoiding thread scheduling issues on macOS.
            ProcessPendingCallbacks();
        }
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Core do-while-loop execution logic shared by the sync and async evaluators; the body runs
    /// at least once before the condition is tested.
    /// </summary>
    private async ValueTask<ExecutionResult> ExecuteDoWhileCore(
        IEvaluationContext ctx,
        Stmt.DoWhile doWhileStmt,
        IReadOnlyList<string>? labels = null)
    {
        do
        {
            var result = await ctx.ExecuteStmtAsync(doWhileStmt.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;
            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        } while ((await ctx.EvaluateExprAsync(doWhileStmt.Condition)).IsTruthy());
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Core loop result handling logic, shared between sync and async loop execution.
    /// Processes ExecutionResult to determine break, continue, or propagation behavior.
    /// </summary>
    /// <param name="result">The execution result from the loop body.</param>
    /// <param name="labels">
    /// The labels that directly wrap this loop (empty/null for an unlabeled loop). A labeled
    /// break/continue is handled here only when its target is one of these.
    /// </param>
    /// <returns>A tuple indicating: (shouldBreak, shouldContinue, abruptResultToPropagate).</returns>
    private (bool shouldBreak, bool shouldContinue, ExecutionResult? abruptResult)
        HandleLoopResult(ExecutionResult result, IReadOnlyList<string>? labels)
    {
        if (result.Type == ExecutionResult.ResultType.Break &&
            TargetsThisLoop(result.TargetLabel, labels))
            return (true, false, null);
        if (result.Type == ExecutionResult.ResultType.Continue &&
            TargetsThisLoop(result.TargetLabel, labels))
            return (false, true, null);
        if (result.IsAbrupt)
            return (false, false, result);
        return (false, false, null);
    }

    /// <summary>
    /// True when an unlabeled break/continue (targets the innermost loop) or a labeled one whose
    /// target is among the labels wrapping this loop. A non-matching labeled target propagates.
    /// </summary>
    private static bool TargetsThisLoop(string? targetLabel, IReadOnlyList<string>? labels)
    {
        if (targetLabel == null) return true;
        if (labels == null) return false;
        for (int i = 0; i < labels.Count; i++)
            if (labels[i] == targetLabel) return true;
        return false;
    }

    /// <summary>
    /// Labels parked by <see cref="ExecuteLabeledStatement"/> for the loop it directly wraps.
    /// The loop drains these at entry via <see cref="TakePendingLoopLabels"/> and treats a
    /// <c>continue</c>/<c>break</c> to any of them as targeting itself, running the loop's own
    /// step (a for's increment, a while's re-test) instead of restarting it from scratch — which
    /// for a <c>for</c> would re-run the initializer forever (#558).
    /// </summary>
    private readonly List<string> _pendingLoopLabels = new();

    private static readonly string[] _noLoopLabels = [];

    /// <summary>Returns the labels parked for the loop now being entered, and clears them.</summary>
    private string[] TakePendingLoopLabels()
    {
        if (_pendingLoopLabels.Count == 0) return _noLoopLabels;
        var labels = _pendingLoopLabels.ToArray();
        _pendingLoopLabels.Clear();
        return labels;
    }

    /// <summary>
    /// Translates a host exception to a guest error value.
    /// Shared between sync and async try/catch handling.
    /// </summary>
    /// <param name="ex">The host exception to translate.</param>
    /// <returns>The guest error value (ThrowException value, NodeError object, or message string).</returns>
    internal object? TranslateException(Exception ex)
    {
        if (ex is ThrowException tex)
            return tex.Value;

        if (ex is SharpTSPromiseRejectedException rex)
            return rex.Reason;

        if (ex is AggregateException agg && agg.InnerException is SharpTSPromiseRejectedException innerRex)
            return innerRex.Reason;

        if (ex is NodeError nodeError)
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["name"] = "Error",
                ["message"] = nodeError.Message,
                ["code"] = nodeError.Code,
                ["syscall"] = nodeError.Syscall,
                ["path"] = nodeError.Path,
                ["errno"] = nodeError.Errno.HasValue ? (double)nodeError.Errno.Value : null
            });

        // Host exceptions surface as their raw message string. Built-ins signal JS errors
        // via `throw new Exception("RangeError: ...")`, but the typed-error synthesis is
        // deferred to CoerceCaughtValueForBinding — applied only when a value is bound to a
        // guest `catch` parameter (#694). Doing it here instead would also convert errors
        // on the *propagation* path, where ThrowException.FromResult relies on host errors
        // staying strings so an uncaught strict-mode/internal error keeps propagating to the
        // host as a plain Exception rather than becoming a top-level-swallowed guest throw.
        return ex.Message;
    }

    /// <summary>
    /// Known JS error-name prefixes that built-ins prepend to host <see cref="Exception"/>
    /// messages (e.g. "RangeError: ..."). Used by <see cref="CoerceCaughtValueForBinding"/>
    /// to reconstruct a typed guest error when one is caught (#694). AggregateError is
    /// intentionally excluded — it requires an errors array, not a bare message.
    /// </summary>
    private static readonly (string Prefix, string Name)[] JsErrorMessagePrefixes =
    {
        ("TypeError: ", "TypeError"),
        ("RangeError: ", "RangeError"),
        ("ReferenceError: ", "ReferenceError"),
        ("SyntaxError: ", "SyntaxError"),
        ("EvalError: ", "EvalError"),
        ("URIError: ", "URIError"),
    };

    /// <summary>
    /// Coerces a HOST-exception message about to be bound to a guest <c>catch</c> parameter
    /// (#694). Built-ins signal JS errors as host exceptions whose message carries a
    /// "&lt;Name&gt;Error: " prefix (optionally inside a "Runtime Error: " wrapper);
    /// <see cref="TranslateException"/> surfaces these as the raw message string. When guest
    /// code catches one, present it as the matching typed Error so <c>instanceof</c>,
    /// <c>.name</c>, and <c>.message</c> hold — parity with compiled mode, which throws a
    /// real <c>$RangeError</c>/<c>$TypeError</c>/etc. Non-prefixed strings and non-string
    /// values pass through unchanged.
    /// </summary>
    /// <remarks>
    /// Invoked by <see cref="HandleCatchBlock"/>/<c>Core</c> ONLY when the caught value came
    /// from a translated host <see cref="Exception"/> (<c>fromHostException</c>); a genuine
    /// guest <c>throw value</c> is bound verbatim and never re-typed, so a guest
    /// <c>throw "TypeError: x"</c> stays the exact string (matching JS). Coercion is confined
    /// to the catch binding, never the propagation path: an UNcaught host error must stay a
    /// string so <see cref="ThrowException.FromResult"/> surfaces it to the host as a plain
    /// <see cref="Exception"/> (e.g. an uncaught strict-mode violation).
    /// Cross-boundary identity is preserved too: a guest string thrown ACROSS a host frame
    /// (callback/interop/Promise executor) is carried by <see cref="ThrowException.FromResult"/>
    /// as a <see cref="ThrowException"/> (not a flattened plain <see cref="Exception"/>) whenever
    /// the originating <see cref="Execution.ExecutionResult"/> was a guest throw
    /// (<see cref="Execution.ExecutionResult.FromGuestThrow"/>), so the re-catch derives
    /// <c>fromHostException:false</c> from the exception kind and binds it verbatim — never
    /// re-coerced.
    /// </remarks>
    internal object? CoerceCaughtValueForBinding(object? value)
        => value is string s && TryCreateGuestErrorFromMessage(s) is { } typedError
            ? typedError
            : value;

    /// <summary>
    /// If <paramref name="message"/> begins with a known JS error-name prefix
    /// (e.g. "RangeError: "), returns the matching guest <see cref="SharpTSError"/>
    /// carrying the remainder as its message; otherwise returns <c>null</c> (#694).
    /// </summary>
    private static SharpTSError? TryCreateGuestErrorFromMessage(string? message)
    {
        if (message is null) return null;

        // Internal runtime errors carry a "Runtime Error: " wrapper (undefined-variable
        // access, BigInt range violations, ...) — sometimes around a real JS error name
        // (e.g. "Runtime Error: TypeError: ..."). Strip the wrapper so the inner name is
        // recognised; a wrapper with no inner JS name still becomes a generic Error so guest
        // `catch` observes an `instanceof Error` with `.message`, matching JS (where the
        // runtime never throws a bare string). Non-wrapped, non-prefixed strings pass through.
        const string runtimePrefix = "Runtime Error: ";
        bool hadRuntimePrefix = message.StartsWith(runtimePrefix, StringComparison.Ordinal);
        var body = hadRuntimePrefix ? message.Substring(runtimePrefix.Length) : message;

        foreach (var (prefix, name) in JsErrorMessagePrefixes)
        {
            if (body.StartsWith(prefix, StringComparison.Ordinal))
            {
                var detail = body.Substring(prefix.Length);
                return ErrorBuiltIns.CreateError(name, new List<object?> { detail });
            }
        }

        // An unprefixed message is left as a bare string on purpose: generator and
        // async-generator rejection paths ferry a guest-thrown *value* across a host frame
        // as an Exception whose Message is that value, so blanket-wrapping here would
        // re-type `gen.throw("boom")` into `Error: boom`. Built-ins that mean to raise a
        // JS error prefix the name (or throw ThrowException directly) instead.
        return hadRuntimePrefix
            ? ErrorBuiltIns.CreateError("Error", new List<object?> { body })
            : null;
    }

    /// <summary>
    /// Executes a 'using' or 'await using' declaration.
    /// Evaluates the initializer, defines the variable, and registers the resource for disposal.
    /// </summary>
    private ExecutionResult ExecuteUsingDeclaration(Stmt.Using usingStmt)
    {
        // Get or create the tracker for the current scope
        UsingTracker tracker;
        if (_usingTrackerStack.Count > 0)
        {
            tracker = _usingTrackerStack.Peek();
        }
        else
        {
            // If no tracker exists, create one for the current scope
            // This handles using declarations at module/script level
            tracker = new UsingTracker(this);
            _usingTrackerStack.Push(tracker);
        }

        foreach (var binding in usingStmt.Bindings)
        {
            object? resource = Evaluate(binding.Initializer);

            // Define variable in the current scope
            if (binding.Name != null)
            {
                _environment.Define(binding.Name.Lexeme, resource);
            }

            // Register for disposal at scope exit
            tracker.Add(resource, usingStmt.IsAsync);
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Disposes a single resource using Symbol.dispose or Symbol.asyncDispose.
    /// </summary>
    /// <param name="resource">The resource to dispose.</param>
    /// <param name="isAsync">True for Symbol.asyncDispose, false for Symbol.dispose.</param>
    private void DisposeResource(object? resource, bool isAsync)
    {
        // Null/undefined resources are skipped
        if (resource == null || resource is SharpTSUndefined)
            return;

        var symbol = isAsync ? SharpTSSymbol.AsyncDispose : SharpTSSymbol.Dispose;
        object? disposeMethod = GetSymbolProperty(resource, symbol);

        if (disposeMethod == null)
        {
            // No dispose method found - check for .NET IDisposable as fallback
            if (resource is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }
            if (isAsync && resource is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }
            // No disposal method - silently skip (TypeScript allows this)
            return;
        }

        // Call the dispose method with the resource as 'this' context
        object? result = null;

        if (disposeMethod is SharpTSFunction func)
        {
            // Bind the function to the resource and call it
            // For SharpTSInstance resources, use the instance bind
            if (resource is SharpTSInstance instance)
            {
                var boundFunc = func.Bind(instance);
                result = boundFunc.Call(this, []);
            }
            else
            {
                // For other objects (SharpTSObject), create a temporary scope with 'this'
                var prevEnv = _environment;
                _environment = new RuntimeEnvironment(_environment);
                _environment.Define("this", resource);
                try
                {
                    result = func.Call(this, []);
                }
                finally
                {
                    _environment = prevEnv;
                }
            }
        }
        else if (disposeMethod is SharpTSArrowFunction arrowFunc)
        {
            // Arrow functions with HasOwnThis need 'this' bound
            if (arrowFunc.HasOwnThis)
            {
                var boundFunc = arrowFunc.Bind(resource!);
                result = boundFunc.Call(this, []);
            }
            else
            {
                // Arrow functions without own 'this' use lexical scope
                result = arrowFunc.Call(this, []);
            }
        }
        else if (disposeMethod is ISharpTSCallable callable)
        {
            result = callable.Call(this, []);
        }

        // Wait for async disposal to complete
        if (isAsync)
        {
            if (result is SharpTSPromise promise)
            {
                promise.Task.GetAwaiter().GetResult();
            }
            else if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// Gets a property from an object using a symbol key.
    /// </summary>
    private object? GetSymbolProperty(object? obj, SharpTSSymbol symbol)
    {
        if (obj is SharpTSObject tsObject)
        {
            return tsObject.GetBySymbol(symbol);
        }
        if (obj is SharpTSInstance instance)
        {
            return instance.GetBySymbol(symbol);
        }
        // For other types, return null (no symbol property access)
        return null;
    }

    /// <summary>
    /// Internal wrapper for Execute that allows evaluation contexts to dispatch statements.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>The execution result.</returns>
    internal ExecutionResult ExecuteStatement(Stmt stmt) => Execute(stmt);

    /// <summary>
    /// Internal async wrapper for statement execution.
    /// Uses DispatchStmtAsync which falls back to sync handlers when no async handler exists.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>A task containing the execution result.</returns>
    internal async Task<ExecutionResult> ExecuteStatementAsync(Stmt stmt)
    {
        return await DispatchStmtAsync(stmt);
    }

    /// <summary>
    /// Dispatches a statement to the appropriate execution handler.
    /// </summary>
    /// <param name="stmt">The statement AST node to execute.</param>
    /// <remarks>
    /// Handles all statement types including control flow (if, while, for, switch),
    /// declarations (var, function, class, enum), and control transfer (return, break, continue, throw).
    /// Control flow uses <see cref="ExecutionResult"/> for non-local jumps.
    /// </remarks>
    private ExecutionResult Execute(Stmt stmt)
    {
        return DispatchStmt(stmt);
    }

    // Statement handlers - called by the dispatch switch

    internal ExecutionResult VisitBlock(Stmt.Block block) =>
        ExecuteBlock(block.Statements, new RuntimeEnvironment(_environment));

    internal ExecutionResult VisitLabeledStatement(Stmt.LabeledStatement labeledStmt) =>
        ExecuteLabeledStatement(labeledStmt);

    internal ExecutionResult VisitSequence(Stmt.Sequence seq)
    {
        // Execute in current scope (no new environment)
        foreach (var s in seq.Statements)
        {
            var result = Execute(s);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitExpression(Stmt.Expression exprStmt)
    {
        Evaluate(exprStmt.Expr);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitIf(Stmt.If ifStmt)
    {
        if (IsTruthy(Evaluate(ifStmt.Condition)))
        {
            return Execute(ifStmt.ThenBranch);
        }
        else if (ifStmt.ElseBranch != null)
        {
            return Execute(ifStmt.ElseBranch);
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitWhile(Stmt.While whileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteWhileCore(_syncContext, whileStmt, labels).GetAwaiter().GetResult();
    }

    internal ExecutionResult VisitDoWhile(Stmt.DoWhile doWhileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteDoWhileCore(_syncContext, doWhileStmt, labels).GetAwaiter().GetResult();
    }

    internal ExecutionResult VisitFor(Stmt.For forStmt)
    {
        // Drain labels parked by an enclosing labeled statement before running the initializer,
        // so `continue <label>`/`break <label>` resolve to this loop (#558).
        var labels = TakePendingLoopLabels();
        return ExecuteForCore(_syncContext, forStmt, labels).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Core C-style for-loop execution logic shared by the sync and async evaluators. The evaluation
    /// context supplies the per-clause evaluation strategy and the between-iteration scheduler yield
    /// (<see cref="IEvaluationContext.YieldToSchedulerAsync"/>), so a single body serves both paths.
    /// </summary>
    private async ValueTask<ExecutionResult> ExecuteForCore(IEvaluationContext ctx, Stmt.For forStmt, IReadOnlyList<string>? labels)
    {
        // Create scope for loop variables (ES6 let/const block scoping)
        // Variables declared with let/const in the initializer are scoped to the loop
        RuntimeEnvironment loopEnv = new(_environment);
        using (PushScope(loopEnv))
        {
            // Execute initializer once (defines loop variable in loopEnv)
            if (forStmt.Initializer != null)
                await ctx.ExecuteStmtAsync(forStmt.Initializer);
            // ECMA-262 13.7.4: a `for (let/const …)` loop gives each iteration its
            // own bindings for the loop variables, so closures created in different
            // iterations capture distinct values (#633). `var`/expression
            // initializers share a single binding and keep the no-copy fast path.
            var perIterationNames = CollectPerIterationBindings(forStmt.Initializer);
            if (perIterationNames != null)
                CreatePerIterationEnvironment(loopEnv, perIterationNames);
            // Loop with proper continue handling - increment always runs
            while (forStmt.Condition == null || (await ctx.EvaluateExprAsync(forStmt.Condition)).IsTruthy())
            {
                var result = await ctx.ExecuteStmtAsync(forStmt.Body);
                var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
                if (shouldBreak) break;
                // On continue (unlabeled or to this loop), execute increment then re-test
                if (shouldContinue)
                {
                    if (perIterationNames != null)
                        CreatePerIterationEnvironment(loopEnv, perIterationNames);
                    if (forStmt.Increment != null)
                        await ctx.EvaluateExprAsync(forStmt.Increment);
                    // Yield to allow timer callbacks and other threads to execute
                    await ctx.YieldToSchedulerAsync();
                    continue;
                }
                if (abruptResult.HasValue) return abruptResult.Value;
                // Normal completion: fresh per-iteration binding, then increment
                if (perIterationNames != null)
                    CreatePerIterationEnvironment(loopEnv, perIterationNames);
                if (forStmt.Increment != null)
                    await ctx.EvaluateExprAsync(forStmt.Increment);
                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
            return ExecutionResult.Success();
        }
    }

    /// <summary>
    /// Returns the variable names a <c>for</c> initializer binds that require a
    /// fresh binding per iteration (ECMA-262 13.7.4): <c>let</c>/<c>const</c>
    /// declarations. Returns <c>null</c> for <c>var</c> or expression
    /// initializers, which share a single binding across the whole loop.
    /// </summary>
    private static List<string>? CollectPerIterationBindings(Stmt? initializer)
    {
        switch (initializer)
        {
            // `let`/`const` in a for-initializer parse to Stmt.Var with IsVar=false.
            case Stmt.Var v when !v.IsVar:
                return [v.Name.Lexeme];
            case Stmt.Const c:
                return [c.Name.Lexeme];
            // Multi-declarator initializers (`for (let i = 0, j = 10; …)`).
            case Stmt.Sequence seq:
                List<string>? names = null;
                foreach (var s in seq.Statements)
                {
                    var sub = CollectPerIterationBindings(s);
                    if (sub != null) (names ??= []).AddRange(sub);
                }
                return names;
            default:
                return null;
        }
    }

    /// <summary>
    /// ECMA-262 13.7.4.8 CreatePerIterationEnvironment: copies the current value
    /// of each loop variable into a fresh environment that is a sibling of the
    /// loop environment (same enclosing scope, so the resolver's static scope
    /// distances stay valid) and makes it the active scope. Closures created in
    /// the next iteration capture this distinct binding rather than a shared slot.
    /// </summary>
    private void CreatePerIterationEnvironment(RuntimeEnvironment loopEnv, List<string> names)
    {
        var iterationEnv = new RuntimeEnvironment(loopEnv.Enclosing);
        foreach (var name in names)
            iterationEnv.Define(name, _environment.GetAt(0, name));
        _environment = iterationEnv;
    }

    internal ExecutionResult VisitForOf(Stmt.ForOf forOf) => ExecuteForOf(forOf);

    internal ExecutionResult VisitForIn(Stmt.ForIn forIn) => ExecuteForIn(forIn);

    internal ExecutionResult VisitBreak(Stmt.Break breakStmt) =>
        ExecutionResult.Break(breakStmt.Label?.Lexeme);

    internal ExecutionResult VisitContinue(Stmt.Continue continueStmt) =>
        ExecutionResult.Continue(continueStmt.Label?.Lexeme);

    internal ExecutionResult VisitSwitch(Stmt.Switch switchStmt) => ExecuteSwitch(switchStmt);

    internal ExecutionResult VisitTryCatch(Stmt.TryCatch tryCatch) => ExecuteTryCatch(tryCatch);

    internal ExecutionResult VisitThrow(Stmt.Throw throwStmt) =>
        ExecutionResult.Throw(Evaluate(throwStmt.Value));

    internal ExecutionResult VisitVar(Stmt.Var varStmt)
    {
        object? value = SharpTSUndefined.Instance;
        if (varStmt.Initializer != null)
        {
            value = Evaluate(varStmt.Initializer);
        }
        _environment.Define(varStmt.Name.Lexeme, value);
        if (varStmt.IsVar && _environment.Enclosing is null)
            GlobalThis.SetProperty(varStmt.Name.Lexeme, value);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitConst(Stmt.Const constStmt)
    {
        // Const declarations always have an initializer (enforced by parser)
        object? constValue = Evaluate(constStmt.Initializer);
        _environment.Define(constStmt.Name.Lexeme, constValue);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitFunction(Stmt.Function functionStmt)
    {
        // Skip overload signatures (no body) - they're type-checking only
        if (functionStmt.Body == null) return ExecutionResult.Success();
        // Skip if already hoisted
        if (_environment.IsDefinedLocally(functionStmt.Name.Lexeme)) return ExecutionResult.Success();
        if (functionStmt.IsGenerator && functionStmt.IsAsync)
        {
            // Async generator: async function* foo() { yield await ... }
            SharpTSAsyncGeneratorFunction asyncGenFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, asyncGenFunction);
        }
        else if (functionStmt.IsGenerator)
        {
            SharpTSGeneratorFunction generatorFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, generatorFunction);
        }
        else if (functionStmt.IsAsync)
        {
            SharpTSAsyncFunction asyncFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, asyncFunction);
        }
        else
        {
            SharpTSFunction function = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, function);
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitClass(Stmt.Class classStmt)
    {
        // @DotNetType declare class: bind a DotNet wrapper into the environment
        // instead of creating an empty SharpTSClass. Non-DotNet declare classes still
        // fall through and produce an empty SharpTSClass for type-only compatibility.
        if (classStmt.IsDeclare)
        {
            if (TryRegisterDotNetType(classStmt)) return ExecutionResult.Success();
        }

        object? superclass = null;
        if (classStmt.SuperclassExpr != null)
        {
            superclass = Evaluate(classStmt.SuperclassExpr);

            // `extends Array` (#233): the Array global is a constructor
            // singleton, not a SharpTSClass — substitute the SharpTSArrayClass
            // bridge so the class machinery (super(), method lookup,
            // instanceof) sees a real superclass.
            if (superclass is SharpTSArrayGlobal)
            {
                superclass = SharpTSArrayClass.ArrayBase;
            }

            // `extends Promise` (#242): same substitution for the Promise
            // constructor sentinel.
            if (superclass is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise })
            {
                superclass = SharpTSPromiseClass.PromiseBase;
            }

            if (superclass is not SharpTSClass)
            {
                // Built-in constructors that don't have a class bridge yet
                // get a precise error instead of the generic
                // "Superclass must be a class".
                if (superclass is SharpTSBuiltInConstructor builtInCtor)
                {
                    throw new InterpreterException(
                        $"Class '{classStmt.Name.Lexeme}' cannot extend built-in '{builtInCtor.Name}': subclassing this built-in is not supported yet.");
                }
                throw new InterpreterException("Superclass must be a class.");
            }
        }

        _environment.Define(classStmt.Name.Lexeme, null);

        if (classStmt.SuperclassExpr != null)
        {
            _environment = new RuntimeEnvironment(_environment);
            _environment.Define("super", superclass);
        }

        Dictionary<string, ISharpTSCallable> methods = [];
        Dictionary<string, ISharpTSCallable> staticMethods = [];
        Dictionary<string, object?> staticProperties = [];
        List<Stmt.Field> instanceFields = [];
        // ES2022 private class elements
        List<Stmt.Field> instancePrivateFields = [];
        Dictionary<string, ISharpTSCallable> privateMethods = [];
        Dictionary<string, object?> staticPrivateFields = [];
        Dictionary<string, ISharpTSCallable> staticPrivateMethods = [];

        // Process fields: collect instance fields, defer static field initialization if using StaticInitializers
        // Note: Declare fields are processed normally - they can't have initializers (enforced by parser),
        // so they'll be added with null/undefined values and can be set externally later.
        bool hasStaticInitializers = classStmt.StaticInitializers != null && classStmt.StaticInitializers.Count > 0;

        foreach (Stmt.Field field in classStmt.Fields)
        {
            if (field.IsPrivate)
            {
                // ES2022 private fields
                if (field.IsStatic)
                {
                    if (!hasStaticInitializers)
                    {
                        // Old behavior: evaluate immediately
                        object? fieldValue = field.Initializer != null
                            ? Evaluate(field.Initializer)
                            : null;
                        staticPrivateFields[field.Name.Lexeme] = fieldValue;
                    }
                    // else: will be evaluated via StaticInitializers with proper 'this' binding
                }
                else
                {
                    // Collect instance private fields - they'll be initialized when instances are created
                    instancePrivateFields.Add(field);
                }
            }
            else if (field.IsStatic)
            {
                if (!hasStaticInitializers)
                {
                    // Old behavior: evaluate immediately
                    object? fieldValue = field.Initializer != null
                        ? Evaluate(field.Initializer)
                        : null;
                    staticProperties[field.Name.Lexeme] = fieldValue;
                }
                // else: will be evaluated via StaticInitializers with proper 'this' binding
            }
            else
            {
                // Collect instance fields - they'll be initialized when instances are created
                instanceFields.Add(field);
            }
        }

        // Symbol-keyed computed methods (`[Symbol.iterator]() {...}`) can't go into the string
        // dictionaries; collected here and attached to the class after construction.
        List<(SharpTSSymbol Symbol, ISharpTSCallable Func, bool IsStatic)>? symbolMethods = null;

        // Separate static and instance methods (skip overload signatures with no body)
        foreach (Stmt.Function method in classStmt.Methods.Where(m => m.Body != null))
        {
            // Create the appropriate function type based on async/generator flags
            ISharpTSCallable func;
            if (method.IsGenerator && method.IsAsync)
                func = new SharpTSAsyncGeneratorFunction(method, _environment);
            else if (method.IsAsync)
                func = new SharpTSAsyncFunction(method, _environment);
            else if (method.IsGenerator)
                func = new SharpTSGeneratorFunction(method, _environment);
            else
                func = new SharpTSFunction(method, _environment);

            // Computed method keys (`[Symbol.iterator]()`, `[expr]()`) are evaluated at
            // class-definition time, like computed field keys and accessors. Symbol keys land
            // in the symbol-method table; other keys fold to a string-named method.
            if (method.ComputedKey != null)
            {
                object? key = Evaluate(method.ComputedKey);
                if (key is SharpTSSymbol symbolKey)
                {
                    (symbolMethods ??= []).Add((symbolKey, func, method.IsStatic));
                    continue;
                }
                string keyStr = PropertyKeyConverter.ToPropertyKeyString(key);
                (method.IsStatic ? staticMethods : methods)[keyStr] = func;
                continue;
            }

            if (method.IsPrivate)
            {
                // ES2022 private methods
                if (method.IsStatic)
                {
                    staticPrivateMethods[method.Name.Lexeme] = func;
                }
                else
                {
                    privateMethods[method.Name.Lexeme] = func;
                }
            }
            else if (method.IsStatic)
            {
                staticMethods[method.Name.Lexeme] = func;
            }
            else
            {
                methods[method.Name.Lexeme] = func;
            }
        }

        // Create accessor functions
        Dictionary<string, SharpTSFunction> getters = [];
        Dictionary<string, SharpTSFunction> setters = [];
        Dictionary<string, SharpTSFunction> staticGetters = [];
        Dictionary<string, SharpTSFunction> staticSetters = [];

        // Symbol-keyed accessors can't go into the string dictionaries; collected
        // here and attached to the class after construction.
        List<(SharpTSSymbol Symbol, SharpTSFunction Func, bool IsStatic, bool IsGetter)>? symbolAccessors = null;

        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                // Create a synthetic function for the accessor
                var funcStmt = new Stmt.Function(
                    accessor.Name,
                    null,  // No type parameters for accessor
                    null,  // No this type annotation
                    accessor.SetterParam != null ? [accessor.SetterParam] : [],
                    accessor.Body,
                    accessor.ReturnType);

                SharpTSFunction func = new(funcStmt, _environment);
                bool isGetter = accessor.Kind.Type == TokenType.GET;

                // Computed accessor names (`get [Symbol.toStringTag]()`,
                // `static get [Symbol.species]()`) are evaluated at
                // class-definition time, like computed field keys.
                if (accessor.ComputedKey != null)
                {
                    object? key = Evaluate(accessor.ComputedKey);
                    if (key is SharpTSSymbol symbolKey)
                    {
                        (symbolAccessors ??= []).Add((symbolKey, func, accessor.IsStatic, isGetter));
                        continue;
                    }
                    string keyStr = PropertyKeyConverter.ToPropertyKeyString(key);
                    if (isGetter) (accessor.IsStatic ? staticGetters : getters)[keyStr] = func;
                    else (accessor.IsStatic ? staticSetters : setters)[keyStr] = func;
                    continue;
                }

                var targetGet = accessor.IsStatic ? staticGetters : getters;
                var targetSet = accessor.IsStatic ? staticSetters : setters;

                if (isGetter)
                {
                    targetGet[accessor.Name.Lexeme] = func;
                }
                else
                {
                    targetSet[accessor.Name.Lexeme] = func;
                }
            }
        }

        // Process auto-accessors (TypeScript 4.9+)
        List<Stmt.AutoAccessor> instanceAutoAccessors = [];
        Dictionary<string, object?> staticAutoAccessors = [];

        if (classStmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in classStmt.AutoAccessors)
            {
                if (autoAccessor.IsStatic)
                {
                    // Evaluate static auto-accessor initializer now
                    object? initValue = autoAccessor.Initializer != null
                        ? Evaluate(autoAccessor.Initializer)
                        : null;
                    staticAutoAccessors[autoAccessor.Name.Lexeme] = initValue;
                }
                else
                {
                    // Collect instance auto-accessors for later initialization
                    instanceAutoAccessors.Add(autoAccessor);
                }
            }
        }

        // If the superclass is an Error type, create a SharpTSErrorClass so that
        // instances carry error fields (name, message, stack) and instanceof works.
        // Likewise an Array superclass produces a SharpTSArrayClass whose
        // instances are real arrays (#233), and a Promise superclass produces a
        // SharpTSPromiseClass whose instances are real promises (#242).
        SharpTSClass klass = superclass is SharpTSErrorClass errorSuper
            ? new SharpTSErrorClass(
                classStmt.Name.Lexeme,
                errorSuper,
                methods,
                staticMethods,
                staticProperties,
                getters,
                setters,
                classStmt.IsAbstract,
                instanceFields,
                instancePrivateFields,
                privateMethods,
                staticPrivateFields,
                staticPrivateMethods,
                instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null)
            : superclass is SharpTSArrayClass arraySuper
            ? new SharpTSArrayClass(
                classStmt.Name.Lexeme,
                arraySuper,
                methods,
                staticMethods,
                staticProperties,
                getters,
                setters,
                classStmt.IsAbstract,
                instanceFields,
                instancePrivateFields,
                privateMethods,
                staticPrivateFields,
                staticPrivateMethods,
                instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null)
            : superclass is SharpTSPromiseClass promiseSuper
            ? new SharpTSPromiseClass(
                classStmt.Name.Lexeme,
                promiseSuper,
                methods,
                staticMethods,
                staticProperties,
                getters,
                setters,
                classStmt.IsAbstract,
                instanceFields,
                instancePrivateFields,
                privateMethods,
                staticPrivateFields,
                staticPrivateMethods,
                instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null)
            : new SharpTSClass(
                classStmt.Name.Lexeme,
                (SharpTSClass?)superclass,
                methods,
                staticMethods,
                staticProperties,
                getters,
                setters,
                classStmt.IsAbstract,
                instanceFields,
                instancePrivateFields,
                privateMethods,
                staticPrivateFields,
                staticPrivateMethods,
                instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null);

        if (symbolAccessors != null)
        {
            foreach (var (symbol, func, isStatic, isGetter) in symbolAccessors)
            {
                klass.AddSymbolAccessor(symbol, func, isStatic, isGetter);
            }
        }

        if (symbolMethods != null)
        {
            foreach (var (symbol, func, isStatic) in symbolMethods)
            {
                klass.AddSymbolMethod(symbol, func, isStatic);
            }
        }

        // Execute static initializers in declaration order (if present)
        if (hasStaticInitializers)
        {
            // Create temporary environment with 'this' bound to the class
            // Also make the class name available so code like Foo.x works
            var staticEnv = new RuntimeEnvironment(_environment);
            staticEnv.Define("this", klass);
            staticEnv.Define(classStmt.Name.Lexeme, klass);

            var prevEnv = _environment;
            _environment = staticEnv;

            try
            {
                foreach (var initializer in classStmt.StaticInitializers!)
                {
                    switch (initializer)
                    {
                        case Stmt.Field field when field.IsStatic:
                            object? fieldValue = field.Initializer != null
                                ? Evaluate(field.Initializer)
                                : null;
                            if (field.IsPrivate)
                                klass.SetStaticPrivateField(field.Name.Lexeme, fieldValue);
                            else
                                klass.SetStaticProperty(field.Name.Lexeme, fieldValue);
                            break;

                        case Stmt.StaticBlock block:
                            foreach (var blockStmt in block.Body)
                            {
                                var result = Execute(blockStmt);
                                if (result.IsAbrupt)
                                {
                                    // Handle throw from static block
                                    if (result.Type == ExecutionResult.ResultType.Throw)
                                    {
                                        throw new InterpreterException($"Error in static block: {Stringify(result.Value.ToObject())}");
                                    }
                                    // Return, break, continue are not allowed (validated by type checker)
                                }
                            }
                            break;
                    }
                }
            }
            finally
            {
                _environment = prevEnv;
            }
        }

        // Apply decorators in the correct order
        klass = ApplyAllDecorators(classStmt, klass, methods, staticMethods, getters, setters);

        if (classStmt.SuperclassExpr != null)
        {
            _environment = _environment.Enclosing!;
        }

        _environment.Assign(classStmt.Name, klass);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitTypeAlias(Stmt.TypeAlias typeAlias) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitInterface(Stmt.Interface iface) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitFileDirective(Stmt.FileDirective fileDirective) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitField(Stmt.Field field) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitAccessor(Stmt.Accessor accessor) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitAutoAccessor(Stmt.AutoAccessor autoAccessor) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitStaticBlock(Stmt.StaticBlock staticBlock) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitEnum(Stmt.Enum enumStmt)
    {
        ExecuteEnumDeclaration(enumStmt);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitNamespace(Stmt.Namespace ns) => ExecuteNamespace(ns);

    internal ExecutionResult VisitImportAlias(Stmt.ImportAlias importAlias) => ExecuteImportAlias(importAlias);

    internal ExecutionResult VisitReturn(Stmt.Return returnStmt)
    {
        // A bare `return;` completes with `undefined` — distinct from `return null;`. Emitting the
        // undefined sentinel here (rather than C# null, which represents JS null) is what makes a
        // generator's completion value and a plain function's return value `undefined` instead of
        // conflating them with null (#480). `return <expr>` preserves whatever the expression
        // evaluates to, so an explicit `return null;` still yields null.
        if (returnStmt.Value == null)
            return ExecutionResult.Return(RuntimeValue.Undefined);
        return ExecutionResult.Return(Evaluate(returnStmt.Value));
    }

    internal ExecutionResult VisitImport(Stmt.Import import) =>
        // Imports are handled in BindModuleImports before execution
        // In single-file mode, imports are a no-op (type checker would have errored)
        ExecutionResult.Success();

    internal ExecutionResult VisitImportRequire(Stmt.ImportRequire importReq) => ExecuteImportRequire(importReq);

    internal ExecutionResult VisitExport(Stmt.Export exportStmt) => ExecuteExport(exportStmt);

    internal ExecutionResult VisitDirective(Stmt.Directive directive) =>
        // Directives are processed at the start of interpretation for their side effects (strict mode)
        // When encountered during execution, they are a no-op
        ExecutionResult.Success();

    internal ExecutionResult VisitDeclareModule(Stmt.DeclareModule declareModule) =>
        // Module/global augmentations and ambient declarations are type-only
        // No runtime effect - types were merged during type checking
        ExecutionResult.Success();

    internal ExecutionResult VisitDeclareGlobal(Stmt.DeclareGlobal declareGlobal) =>
        // Module/global augmentations and ambient declarations are type-only
        // No runtime effect - types were merged during type checking
        ExecutionResult.Success();

    internal ExecutionResult VisitUsing(Stmt.Using usingStmt) => ExecuteUsingDeclaration(usingStmt);
}
