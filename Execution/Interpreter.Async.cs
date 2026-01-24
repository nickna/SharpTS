using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

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
    /// </summary>
    internal async Task<ExecutionResult> ExecuteBlockAsync(List<Stmt> statements, RuntimeEnvironment environment)
    {
        RuntimeEnvironment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                var result = await ExecuteAsync(statement);
                if (result.IsAbrupt) return result;
            }
            return ExecutionResult.Success();
        }
        finally
        {
            _environment = previous;
        }
    }

    /// <summary>
    /// Asynchronously dispatches a statement to the appropriate execution handler.
    /// </summary>
    private async Task<ExecutionResult> ExecuteAsync(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                return await ExecuteBlockAsync(block.Statements, new RuntimeEnvironment(_environment));
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                {
                    var result = await ExecuteAsync(s);
                    if (result.IsAbrupt) return result;
                }
                return ExecutionResult.Success();
            case Stmt.Expression exprStmt:
                await EvaluateAsync(exprStmt.Expr);
                return ExecutionResult.Success();
            case Stmt.If ifStmt:
                if (IsTruthy(await EvaluateAsync(ifStmt.Condition)))
                {
                    return await ExecuteAsync(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    return await ExecuteAsync(ifStmt.ElseBranch);
                }
                return ExecutionResult.Success();
            case Stmt.While whileStmt:
                while (IsTruthy(await EvaluateAsync(whileStmt.Condition)))
                {
                    var result = await ExecuteAsync(whileStmt.Body);
                    var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
                    if (shouldBreak) return ExecutionResult.Success();
                    if (shouldContinue) continue;
                    if (abruptResult.HasValue) return abruptResult.Value;
                    // Process any pending timer callbacks
                    ProcessPendingCallbacks();
                }
                return ExecutionResult.Success();
            case Stmt.DoWhile doWhileStmt:
                do
                {
                    var result = await ExecuteAsync(doWhileStmt.Body);
                    var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
                    if (shouldBreak) return ExecutionResult.Success();
                    if (shouldContinue) continue;
                    if (abruptResult.HasValue) return abruptResult.Value;
                    // Process any pending timer callbacks
                    ProcessPendingCallbacks();
                } while (IsTruthy(await EvaluateAsync(doWhileStmt.Condition)));
                return ExecutionResult.Success();
            case Stmt.For forStmt:
                // Execute initializer once
                if (forStmt.Initializer != null)
                    await ExecuteAsync(forStmt.Initializer);
                // Loop with proper continue handling - increment always runs
                while (forStmt.Condition == null || IsTruthy(await EvaluateAsync(forStmt.Condition)))
                {
                    var result = await ExecuteAsync(forStmt.Body);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) break;
                    // On continue, execute increment then continue the loop
                    if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null)
                    {
                        if (forStmt.Increment != null)
                            await EvaluateAsync(forStmt.Increment);
                        // Yield to allow timer callbacks and other threads to execute
                        await Task.Yield();
                        continue;
                    }
                    if (result.IsAbrupt) return result;
                    // Normal completion: execute increment
                    if (forStmt.Increment != null)
                        await EvaluateAsync(forStmt.Increment);
                    // Process any pending timer callbacks
                    ProcessPendingCallbacks();
                }
                return ExecutionResult.Success();
            case Stmt.ForOf forOf:
                return await ExecuteForOfAsync(forOf);
            case Stmt.ForIn forIn:
                return await ExecuteForInAsync(forIn);
            case Stmt.Break breakStmt:
                return ExecutionResult.Break(breakStmt.Label?.Lexeme);
            case Stmt.Continue continueStmt:
                return ExecutionResult.Continue(continueStmt.Label?.Lexeme);
            case Stmt.Switch switchStmt:
                return await ExecuteSwitchAsync(switchStmt);
            case Stmt.TryCatch tryCatch:
                return await ExecuteTryCatchAsync(tryCatch);
            case Stmt.Throw throwStmt:
                return ExecutionResult.Throw(await EvaluateAsync(throwStmt.Value));
            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    value = await EvaluateAsync(varStmt.Initializer);
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                return ExecutionResult.Success();
            case Stmt.Const constStmt:
                object? constValue = await EvaluateAsync(constStmt.Initializer);
                _environment.Define(constStmt.Name.Lexeme, constValue);
                return ExecutionResult.Success();
            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null) returnValue = await EvaluateAsync(returnStmt.Value);
                return ExecutionResult.Return(returnValue);
            case Stmt.Print printStmt:
                Console.WriteLine(Stringify(await EvaluateAsync(printStmt.Expr)));
                return ExecutionResult.Success();
            default:
                // Fall back to sync execution for other statements
                return Execute(stmt);
        }
    }

    private async Task<ExecutionResult> ExecuteForOfAsync(Stmt.ForOf forOf)
    {
        object? iterable = await EvaluateAsync(forOf.Iterable);

        // For 'for await...of', check for async iterator protocol first
        if (forOf.IsAsync)
        {
            var asyncIterator = TryGetAsyncIterator(iterable);
            if (asyncIterator != null)
            {
                return await IterateAsyncIterator(asyncIterator, forOf);
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
                object? value = forOf.IsAsync && item is Task<object?> t ? await t : item;

                var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null) continue;
                if (result.IsAbrupt) return result;

                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
            return ExecutionResult.Success();
        }

        // Get elements based on iterable type
        IEnumerable<object?> items = iterable switch
        {
            SharpTSArray arr => arr.Elements,
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new InterpreterException(" for...of requires an iterable (array, Map, Set, or iterator).")
        };

        foreach (var item in items)
        {
            // For 'for await...of' with sync iterables, unwrap promises
            object? value = forOf.IsAsync && item is Task<object?> t ? await t : item;

            var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
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
            return await ExecuteAsync(body);
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
            if (asyncIteratorFn != null)
            {
                if (asyncIteratorFn is ISharpTSCallable callable)
                    return callable.Call(this, []);
            }
        }
        return null;
    }

    /// <summary>
    /// Iterates an async iterator by repeatedly calling .next() and awaiting results.
    /// </summary>
    private async Task<ExecutionResult> IterateAsyncIterator(object asyncIterator, Stmt.ForOf forOf)
    {
        while (true)
        {
            // Call iterator.next()
            var nextResult = CallMethodOnObject(asyncIterator, "next", []);

            // Await the result if it's a promise/task
            if (nextResult is SharpTSPromise promise)
                nextResult = await promise.Task;
            else if (nextResult is Task<object?> task)
                nextResult = await task;

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

            if (done) break;

            var result = await ExecuteLoopBodyAsync(forOf.Variable.Lexeme, value, forOf.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;

            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        }

        return ExecutionResult.Success();
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
                var bound = method.Bind(inst);
                return bound.Call(this, args);
            }
        }
        else if (target is SharpTSGenerator gen)
        {
            // Handle generator methods
            return methodName switch
            {
                "next" => gen.Next(),
                "return" => gen.Return(args.Count > 0 ? args[0] : null),
                "throw" => gen.Throw(args.Count > 0 ? args[0] : null),
                _ => throw new Exception($"Runtime Error: Generator does not have method '{methodName}'.")
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
                _ => throw new Exception($"Runtime Error: AsyncGenerator does not have method '{methodName}'.")
            };
        }

        throw new Exception($"Runtime Error: Cannot call method '{methodName}' on {target?.GetType().Name ?? "null"}.");
    }

    private async Task<ExecutionResult> ExecuteForInAsync(Stmt.ForIn forIn)
    {
        object? obj = await EvaluateAsync(forIn.Object);

        IEnumerable<string> keys = obj switch
        {
            SharpTSObject o => o.Fields.Keys,
            SharpTSInstance inst => inst.GetFieldNames(),
            SharpTSArray arr => Enumerable.Range(0, arr.Elements.Count).Select(i => i.ToString()),
            _ => throw new InterpreterException(" for...in requires an object.")
        };

        foreach (var key in keys)
        {
            var result = await ExecuteLoopBodyAsync(forIn.Variable.Lexeme, key, forIn.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
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

    // ===================== Async Expression Helpers =====================

    private async Task<object?> EvaluateBinaryAsync(Expr.Binary binary)
    {
        object? left = await EvaluateAsync(binary.Left);
        object? right = await EvaluateAsync(binary.Right);
        return EvaluateBinaryOperation(binary.Operator, left, right);
    }

    private Task<object?> EvaluateLogicalAsync(Expr.Logical logical) =>
        EvaluateLogicalCoreAsync(
            logical.Operator.Type,
            EvaluateAsync(logical.Left),
            () => EvaluateAsync(logical.Right));

    private Task<object?> EvaluateNullishCoalescingAsync(Expr.NullishCoalescing nc) =>
        EvaluateNullishCoalescingCoreAsync(
            EvaluateAsync(nc.Left),
            () => EvaluateAsync(nc.Right));

    private Task<object?> EvaluateTernaryAsync(Expr.Ternary ternary) =>
        EvaluateTernaryCoreAsync(
            EvaluateAsync(ternary.Condition),
            () => EvaluateAsync(ternary.ThenBranch),
            () => EvaluateAsync(ternary.ElseBranch));

    private async Task<object?> EvaluateUnaryAsync(Expr.Unary unary)
    {
        object? right = await EvaluateAsync(unary.Right);
        return EvaluateUnaryOperation(unary.Operator, right);
    }

    private async ValueTask<object?> EvaluateAssignAsync(Expr.Assign assign)
    {
        object? value = await EvaluateAsync(assign.Value);
        
        if (_locals.TryGetValue(assign, out int distance))
        {
            _environment.AssignAt(distance, assign.Name, value);
        }
        else
        {
            _environment.Assign(assign.Name, value);
        }
        
        return value;
    }

    private async Task<object?> EvaluateCallAsync(Expr.Call call)
    {
        // Use async context with unified core - handles all special cases
        return await EvaluateCallCore(_asyncContext, call);
    }

    private async Task<object?> EvaluateGetAsync(Expr.Get get)
    {
        // Handle namespace static property access (e.g., Number.MAX_VALUE, Number.NaN)
        // These namespaces don't have runtime values, but have static properties
        if (get.Object is Expr.Variable nsVar)
        {
            var member = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (member != null)
            {
                // If it's a constant (like Number.MAX_VALUE), it's wrapped in a BuiltInMethod
                // that returns the value when invoked with no args
                if (member is BuiltInMethod bm && bm.MinArity == 0 && bm.MaxArity == 0)
                {
                    // It's a constant property, invoke it to get the value
                    return bm.Call(this, []);
                }
                return member;
            }
        }

        object? obj = await EvaluateAsync(get.Object);
        return EvaluateGetOnObject(get, obj);
    }

    private async Task<object?> EvaluateSetAsync(Expr.Set set)
    {
        object? obj = await EvaluateAsync(set.Object);
        object? value = await EvaluateAsync(set.Value);
        return EvaluateSetOnObject(set, obj, value);
    }

    private async Task<object?> EvaluateNewAsync(Expr.New newExpr)
    {
        // Use async context with unified core - handles all built-in types
        return await EvaluateNewCore(_asyncContext, newExpr);
    }

    private async Task<object?> EvaluateArrayAsync(Expr.ArrayLiteral array)
    {
        // Use async context with unified core
        return await EvaluateArrayCore(_asyncContext, array);
    }

    private async Task<object?> EvaluateObjectAsync(Expr.ObjectLiteral obj)
    {
        // Use async context with unified core
        return await EvaluateObjectCore(_asyncContext, obj);
    }

    private async Task<object?> EvaluateGetIndexAsync(Expr.GetIndex getIndex)
    {
        object? obj = await EvaluateAsync(getIndex.Object);
        object? index = await EvaluateAsync(getIndex.Index);
        return EvaluateIndexGet(obj, index);
    }

    private async Task<object?> EvaluateSetIndexAsync(Expr.SetIndex setIndex)
    {
        object? obj = await EvaluateAsync(setIndex.Object);
        object? index = await EvaluateAsync(setIndex.Index);
        object? value = await EvaluateAsync(setIndex.Value);
        return EvaluateIndexSet(obj, index, value);
    }

    private async Task<object?> EvaluateCompoundAssignAsync(Expr.CompoundAssign compound)
    {
        object? currentValue = _environment.Get(compound.Name);
        object? operandValue = await EvaluateAsync(compound.Value);
        object? result = ApplyCompoundOperator(compound.Operator.Type, currentValue, operandValue);
        _environment.Assign(compound.Name, result);
        return result;
    }

    private async Task<object?> EvaluateCompoundSetAsync(Expr.CompoundSet compoundSet)
    {
        object? obj = await EvaluateAsync(compoundSet.Object);
        object? currentValue = EvaluateGetOnObject(new Expr.Get(compoundSet.Object, compoundSet.Name), obj);
        object? operandValue = await EvaluateAsync(compoundSet.Value);
        object? result = ApplyCompoundOperator(compoundSet.Operator.Type, currentValue, operandValue);
        return EvaluateSetOnObject(new Expr.Set(compoundSet.Object, compoundSet.Name, new Expr.Literal(result)), obj, result);
    }

    private async Task<object?> EvaluateCompoundSetIndexAsync(Expr.CompoundSetIndex compoundSetIndex)
    {
        object? obj = await EvaluateAsync(compoundSetIndex.Object);
        object? index = await EvaluateAsync(compoundSetIndex.Index);
        object? currentValue = EvaluateIndexGet(obj, index);
        object? operandValue = await EvaluateAsync(compoundSetIndex.Value);
        object? result = ApplyCompoundOperator(compoundSetIndex.Operator.Type, currentValue, operandValue);
        return EvaluateIndexSet(obj, index, result);
    }

    private async Task<object?> EvaluateLogicalAssignAsync(Expr.LogicalAssign logical)
    {
        object? currentValue = _environment.Get(logical.Name);

        switch (logical.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                if (!IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.OR_OR_EQUAL:
                if (IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                if (currentValue != null) return currentValue;
                break;
        }

        object? newValue = await EvaluateAsync(logical.Value);
        _environment.Assign(logical.Name, newValue);
        return newValue;
    }

    private async Task<object?> EvaluateLogicalSetAsync(Expr.LogicalSet logical)
    {
        object? obj = await EvaluateAsync(logical.Object);

        if (!TryGetProperty(obj, logical.Name, out object? currentValue))
        {
            throw new Exception("Only instances and objects have fields.");
        }

        switch (logical.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                if (!IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.OR_OR_EQUAL:
                if (IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                if (currentValue != null) return currentValue;
                break;
        }

        object? newValue = await EvaluateAsync(logical.Value);
        if (!TrySetProperty(obj, logical.Name, newValue))
        {
            throw new Exception("Only instances and objects have fields.");
        }
        return newValue;
    }

    private async Task<object?> EvaluateLogicalSetIndexAsync(Expr.LogicalSetIndex logical)
    {
        object? obj = await EvaluateAsync(logical.Object);
        object? index = await EvaluateAsync(logical.Index);
        object? currentValue = EvaluateIndexGet(obj, index);

        switch (logical.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                if (!IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.OR_OR_EQUAL:
                if (IsTruthy(currentValue)) return currentValue;
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                if (currentValue != null) return currentValue;
                break;
        }

        object? newValue = await EvaluateAsync(logical.Value);
        return EvaluateIndexSet(obj, index, newValue);
    }

    private async Task<object?> EvaluateTemplateLiteralAsync(Expr.TemplateLiteral template)
    {
        var evaluatedExprs = new List<object?>();
        foreach (var expr in template.Expressions)
        {
            evaluatedExprs.Add(await EvaluateAsync(expr));
        }
        return BuildTemplateLiteralString(template.Strings, evaluatedExprs);
    }

    private async Task<object?> EvaluateTaggedTemplateLiteralAsync(Expr.TaggedTemplateLiteral tagged)
    {
        object? tag = await EvaluateAsync(tagged.Tag);

        if (tag is not Runtime.Types.ISharpTSCallable callable)
            throw new InterpreterException(" Tagged template tag must be a function.");

        var cookedList = tagged.CookedStrings.Cast<object?>().ToList();
        var stringsArray = new Runtime.Types.SharpTSTemplateStringsArray(cookedList, tagged.RawStrings);

        List<object?> args = [stringsArray];
        foreach (var expr in tagged.Expressions)
            args.Add(await EvaluateAsync(expr));

        return callable.Call(this, args);
    }

    // Helper methods for index operations
    private object? EvaluateIndexGet(object? obj, object? index)
    {
        if (obj is SharpTSArray array && index is double idx)
        {
            return array.Get((int)idx);
        }
        if (obj is SharpTSEnum enumObj && index is double enumIdx)
        {
            return enumObj.GetReverse(enumIdx);
        }
        if (obj is SharpTSObject sharpObj && index is string strKey)
        {
            return sharpObj.GetProperty(strKey);
        }
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            return numObj.GetProperty(numKey.ToString());
        }
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            return instance.Get(new Token(TokenType.IDENTIFIER, instanceKey, null, 0));
        }
        throw new Exception("Index access not supported on this type.");
    }

    private object? EvaluateIndexSet(object? obj, object? index, object? value)
    {
        bool strictMode = _environment.IsStrictMode;

        if (obj is SharpTSArray array && index is double idx)
        {
            if (strictMode)
            {
                array.SetStrict((int)idx, value, strictMode);
            }
            else
            {
                array.Set((int)idx, value);
            }
            return value;
        }
        if (obj is SharpTSObject sharpObj && index is string strKey)
        {
            if (strictMode)
            {
                sharpObj.SetPropertyStrict(strKey, value, strictMode);
            }
            else
            {
                sharpObj.SetProperty(strKey, value);
            }
            return value;
        }
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            if (strictMode)
            {
                numObj.SetPropertyStrict(numKey.ToString(), value, strictMode);
            }
            else
            {
                numObj.SetProperty(numKey.ToString(), value);
            }
            return value;
        }
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            if (strictMode)
            {
                instance.SetRawFieldStrict(instanceKey, value, strictMode);
            }
            else
            {
                instance.SetRawField(instanceKey, value);
            }
            return value;
        }
        throw new Exception("Index assignment not supported on this type.");
    }
}
