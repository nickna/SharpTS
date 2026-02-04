using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

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
    /// Evaluates a constant expression for const enum members.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new InterpreterException($"Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new InterpreterException($"Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new InterpreterException($"Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
        };
    }

    private object EvaluateConstEnumUnary(Expr.Unary unary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var operand = EvaluateConstEnumExpression(unary.Right, resolvedMembers, enumName);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw new InterpreterException($"Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
        };
    }

    private object EvaluateConstEnumBinary(Expr.Binary binary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var left = EvaluateConstEnumExpression(binary.Left, resolvedMembers, enumName);
        var right = EvaluateConstEnumExpression(binary.Right, resolvedMembers, enumName);

        if (left is double l && right is double r)
        {
            return binary.Operator.Type switch
            {
                TokenType.PLUS => l + r,
                TokenType.MINUS => l - r,
                TokenType.STAR => l * r,
                TokenType.SLASH => l / r,
                TokenType.PERCENT => l % r,
                TokenType.STAR_STAR => Math.Pow(l, r),
                TokenType.AMPERSAND => (double)((int)l & (int)r),
                TokenType.PIPE => (double)((int)l | (int)r),
                TokenType.CARET => (double)((int)l ^ (int)r),
                TokenType.LESS_LESS => (double)((int)l << (int)r),
                TokenType.GREATER_GREATER => (double)((int)l >> (int)r),
                _ => throw new InterpreterException($"Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new InterpreterException($"Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
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
                foreach (Stmt statement in statements)
                {
                    var result = Execute(statement);
                    if (result.IsAbrupt)
                    {
                        // Capture the result but continue to finally for disposal
                        if (result.Type == ExecutionResult.ResultType.Throw)
                        {
                            pendingError = result.Value;
                        }
                        blockResult = result;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Capture host exceptions as pending errors
            pendingError = TranslateException(ex);
            blockResult = ExecutionResult.Throw(pendingError);
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
    /// Executes a labeled statement, catching break/continue exceptions that target this label.
    /// </summary>
    /// <param name="labeledStmt">The labeled statement AST node.</param>
    /// <remarks>
    /// Labeled statements allow break and continue to target specific enclosing statements.
    /// For loops, both break and continue are handled. For non-loop statements (blocks),
    /// only break is valid. Labeled exceptions targeting this label are caught; others propagate.
    /// </remarks>
    private ExecutionResult ExecuteLabeledStatement(Stmt.LabeledStatement labeledStmt)
    {
        string labelName = labeledStmt.Label.Lexeme;
        bool isLoop = labeledStmt.Statement is Stmt.While
                   or Stmt.DoWhile
                   or Stmt.ForOf
                   or Stmt.ForIn
                   or Stmt.LabeledStatement; // Chained labels

        if (isLoop)
        {
            // For loops, labeled continue means restart the loop
            while (true)
            {
                var result = Execute(labeledStmt.Statement);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == labelName)
                {
                    return ExecutionResult.Success();
                }
                if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == labelName)
                {
                    continue;
                }
                if (result.IsAbrupt) return result;
                return ExecutionResult.Success();
            }
        }
        else
        {
            // For non-loop statements, only handle break
            var result = Execute(labeledStmt.Statement);
            if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == labelName)
            {
                return ExecutionResult.Success();
            }
            return result;
        }
    }

    /// <summary>
    /// Core implementation for executing switch statements, shared between sync and async paths.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating case values and executing statements.</param>
    /// <param name="switchStmt">The switch statement AST node.</param>
    /// <returns>A ValueTask containing the execution result.</returns>
    private async ValueTask<ExecutionResult> ExecuteSwitchCore(IEvaluationContext ctx, Stmt.Switch switchStmt)
    {
        object? subject = await ctx.EvaluateExprAsync(switchStmt.Subject);
        bool fallen = false;
        bool matched = false;

        foreach (var caseItem in switchStmt.Cases)
        {
            if (!fallen && !matched)
            {
                object? caseValue = await ctx.EvaluateExprAsync(caseItem.Value);
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
    /// and default case handling. Uses <see cref="BreakException"/> for break statements.
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
                    (exceptionHandled, pendingResult) = await HandleCatchBlockCore(ctx, tryCatch, result.Value);
                    break;
                }
                else if (result.IsAbrupt)
                {
                    pendingResult = result;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // Treat host exceptions as guest throws
            object? errorValue = TranslateException(ex);
            pendingResult = ExecutionResult.Throw(errorValue);
            (exceptionHandled, pendingResult) = await HandleCatchBlockCore(ctx, tryCatch, errorValue);
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
        object? errorValue)
    {
        if (tryCatch.CatchBlock != null)
        {
            RuntimeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam != null)
            {
                catchEnv.Define(tryCatch.CatchParam.Lexeme, errorValue);
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
                catch (Exception ex)
                {
                    object? catchError = ex is ThrowException tex ? tex.Value : ex.Message;
                    return (true, ExecutionResult.Throw(catchError));
                }
            }
        }
        return (false, ExecutionResult.Throw(errorValue));
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
                    (exceptionHandled, pendingResult) = HandleCatchBlock(tryCatch, result.Value);
                    break;
                }
                else if (result.IsAbrupt)
                {
                    pendingResult = result;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // Treat host exceptions as guest throws
            object? errorValue = TranslateException(ex);
            pendingResult = ExecutionResult.Throw(errorValue);
            (exceptionHandled, pendingResult) = HandleCatchBlock(tryCatch, errorValue);
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
        object? errorValue)
    {
        if (tryCatch.CatchBlock != null)
        {
            RuntimeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam != null)
            {
                catchEnv.Define(tryCatch.CatchParam.Lexeme, errorValue);
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
                catch (Exception ex)
                {
                    object? catchError = ex is ThrowException tex ? tex.Value : ex.Message;
                    return (true, ExecutionResult.Throw(catchError));
                }
            }
        }
        return (false, ExecutionResult.Throw(errorValue));
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
    /// Supports break and continue via <see cref="BreakException"/> and <see cref="ContinueException"/>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/iterators-and-generators.html#forof-statements">TypeScript for...of</seealso>
    private ExecutionResult ExecuteForOf(Stmt.ForOf forOf)
    {
        object? iterable = Evaluate(forOf.Iterable);

        // First, check for Symbol.iterator protocol on objects/instances
        IEnumerable<object?>? customIterator = TryGetSymbolIterator(iterable);
        if (customIterator != null)
        {
            return IterateWithBreakContinue(customIterator, forOf.Variable.Lexeme, forOf.Body);
        }

        // Get elements based on iterable type
        IEnumerable<object?> elements = iterable switch
        {
            SharpTSArray array => array.Elements,
            SharpTSBuffer buffer => buffer.Data.Select(b => (object?)(double)b),  // yields byte values as numbers
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new InterpreterException("for...of requires an iterable (array, Map, Set, or iterator).")
        };

        return IterateWithBreakContinue(elements, forOf.Variable.Lexeme, forOf.Body);
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
        object? obj = Evaluate(forIn.Object);

        IEnumerable<string> keys = obj switch
        {
            SharpTSObject o => o.Fields.Keys,
            SharpTSInstance i => i.GetFieldNames(),
            SharpTSArray a => Enumerable.Range(0, a.Elements.Count).Select(i => i.ToString()),
            _ => throw new InterpreterException("for...in requires an object.")
        };

        return IterateWithBreakContinue(keys.Cast<object?>(), forIn.Variable.Lexeme, forIn.Body);
    }

    /// <summary>
    /// Attempts to get an iterator from an object using the Symbol.iterator protocol.
    /// </summary>
    /// <returns>An enumerable of values if the object has a Symbol.iterator, null otherwise.</returns>
    private IEnumerable<object?>? TryGetSymbolIterator(object? iterable)
    {
        // Check for Symbol.iterator on SharpTSObject
        if (iterable is SharpTSObject obj)
        {
            var iteratorFn = obj.GetBySymbol(SharpTSSymbol.Iterator);
            if (iteratorFn != null)
            {
                // Bind 'this' to the object if it's an arrow function
                if (iteratorFn is SharpTSArrowFunction arrowFunc)
                {
                    iteratorFn = arrowFunc.Bind(obj);
                }
                return EnumerateWithIteratorProtocol(iteratorFn);
            }
        }

        // Check for Symbol.iterator on SharpTSInstance
        if (iterable is SharpTSInstance inst)
        {
            var iteratorFn = inst.GetBySymbol(SharpTSSymbol.Iterator);
            if (iteratorFn != null)
            {
                // Bind 'this' to the instance if it's an arrow function
                if (iteratorFn is SharpTSArrowFunction arrowFunc)
                {
                    iteratorFn = arrowFunc.Bind(inst);
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
            throw new InterpreterException("[Symbol.iterator] must be a function.");
        }

        // Iterate using the iterator protocol
        while (true)
        {
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
                var doneVal = resultObj.GetProperty("done");
                done = IsTruthy(doneVal);
                value = resultObj.GetProperty("value");
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
                yield break;
            }

            yield return value;
        }
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
            SharpTSArray array => array.Elements,
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            null => throw new InterpreterException("Cannot spread null or undefined."),
            _ => throw new InterpreterException($"Value of type '{value.GetType().Name}' is not iterable. Expected an array, string, Map, Set, generator, or object with [Symbol.iterator].")
        };
    }

    /// <summary>
    /// Iterates over elements with proper break/continue handling.
    /// </summary>
    private ExecutionResult IterateWithBreakContinue(IEnumerable<object?> elements, string variableName, Stmt body)
    {
        foreach (var element in elements)
        {
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(variableName, element);

            using (PushScope(loopEnv))
            {
                var result = Execute(body);
                var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
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
    /// Core while loop execution logic for synchronous execution.
    /// Uses HandleLoopResult for consistent break/continue handling.
    /// </summary>
    /// <param name="evaluateCondition">Function to evaluate the loop condition.</param>
    /// <param name="executeBody">Function to execute the loop body.</param>
    /// <param name="label">Optional label for labeled break/continue support.</param>
    /// <returns>The execution result (Success or propagated abrupt completion).</returns>
    private ExecutionResult ExecuteWhileCore(
        Func<bool> evaluateCondition,
        Func<ExecutionResult> executeBody,
        string? label = null)
    {
        while (evaluateCondition())
        {
            var result = executeBody();
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, label);
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
    /// Core loop result handling logic, shared between sync and async loop execution.
    /// Processes ExecutionResult to determine break, continue, or propagation behavior.
    /// </summary>
    /// <param name="result">The execution result from the loop body.</param>
    /// <param name="label">The label of the current loop (null for unlabeled loops).</param>
    /// <returns>A tuple indicating: (shouldBreak, shouldContinue, abruptResultToPropagate).</returns>
    private (bool shouldBreak, bool shouldContinue, ExecutionResult? abruptResult)
        HandleLoopResult(ExecutionResult result, string? label)
    {
        if (result.Type == ExecutionResult.ResultType.Break &&
            (result.TargetLabel == null || result.TargetLabel == label))
            return (true, false, null);
        if (result.Type == ExecutionResult.ResultType.Continue &&
            (result.TargetLabel == null || result.TargetLabel == label))
            return (false, true, null);
        if (result.IsAbrupt)
            return (false, false, result);
        return (false, false, null);
    }

    /// <summary>
    /// Translates a host exception to a guest error value.
    /// Shared between sync and async try/catch handling.
    /// </summary>
    /// <param name="ex">The host exception to translate.</param>
    /// <returns>The guest error value (ThrowException value, NodeError object, or message string).</returns>
    private object? TranslateException(Exception ex)
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

        return ex.Message;
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
}
