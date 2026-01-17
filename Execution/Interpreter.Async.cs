using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

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
                        continue;
                    }
                    if (result.IsAbrupt) return result;
                    // Normal completion: execute increment
                    if (forStmt.Increment != null)
                        await EvaluateAsync(forStmt.Increment);
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
            _ => throw new Exception("Runtime Error: for...of requires an iterable (array, Map, Set, or iterator).")
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
            _ => throw new Exception("Runtime Error: for...in requires an object.")
        };

        foreach (var key in keys)
        {
            var result = await ExecuteLoopBodyAsync(forIn.Variable.Lexeme, key, forIn.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;
        }

        return ExecutionResult.Success();
    }

    private async Task<ExecutionResult> ExecuteSwitchAsync(Stmt.Switch switchStmt)
    {
        object? subject = await EvaluateAsync(switchStmt.Subject);
        bool matched = false;
        bool fallThrough = false;

        foreach (var caseItem in switchStmt.Cases)
        {
            if (!matched && !fallThrough)
            {
                object? caseValue = await EvaluateAsync(caseItem.Value);
                if (IsEqual(subject, caseValue))
                {
                    matched = true;
                }
            }

            if (matched || fallThrough)
            {
                foreach (var caseStmt in caseItem.Body)
                {
                    var result = await ExecuteAsync(caseStmt);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                    if (result.IsAbrupt) return result;
                }
                fallThrough = true;
            }
        }

        if (!matched && switchStmt.DefaultBody != null)
        {
            foreach (var defaultStmt in switchStmt.DefaultBody)
            {
                var result = await ExecuteAsync(defaultStmt);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                if (result.IsAbrupt) return result;
            }
        }
        
        return ExecutionResult.Success();
    }

    private async Task<ExecutionResult> ExecuteTryCatchAsync(Stmt.TryCatch tryCatch)
    {
        ExecutionResult pendingResult = ExecutionResult.Success();
        bool exceptionHandled = false;

        try
        {
            foreach (var stmt in tryCatch.TryBlock)
            {
                var result = await ExecuteAsync(stmt);
                if (result.Type == ExecutionResult.ResultType.Throw)
                {
                    pendingResult = result;
                    var catchOutcome = await HandleCatchBlockAsync(tryCatch, result.Value);
                    exceptionHandled = catchOutcome.Handled;
                    if (exceptionHandled) pendingResult = catchOutcome.Result;
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
            object? errorValue = TranslateException(ex);
            var catchOutcome = await HandleCatchBlockAsync(tryCatch, errorValue);
            exceptionHandled = catchOutcome.Handled;
            pendingResult = exceptionHandled ? catchOutcome.Result : ExecutionResult.Throw(errorValue);
        }

        if (tryCatch.FinallyBlock != null)
        {
            var finallyResult = await ExecuteFinallyAsync(tryCatch.FinallyBlock);
            if (finallyResult.IsAbrupt) return finallyResult;
        }

        if (pendingResult.Type == ExecutionResult.ResultType.Throw && !exceptionHandled)
        {
            return pendingResult;
        }

        return pendingResult;
    }

    private async Task<ExecutionResult> ExecuteFinallyAsync(List<Stmt> finallyBlock)
    {
        foreach (var stmt in finallyBlock)
        {
            var result = await ExecuteAsync(stmt);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    private async Task<(bool Handled, ExecutionResult Result)> HandleCatchBlockAsync(Stmt.TryCatch tryCatch, object? value)
    {
        if (tryCatch.CatchBlock != null)
        {
            RuntimeEnvironment catchEnv = new(_environment);
            if (tryCatch.CatchParam != null)
            {
                catchEnv.Define(tryCatch.CatchParam.Lexeme, value);
            }

            RuntimeEnvironment prev = _environment;
            _environment = catchEnv;
            try
            {
                foreach (var stmt in tryCatch.CatchBlock)
                {
                    var result = await ExecuteAsync(stmt);
                    if (result.IsAbrupt) return (true, result);
                }
                return (true, ExecutionResult.Success());
            }
            catch (Exception ex)
            {
                return (true, ExecutionResult.Throw(TranslateException(ex)));
            }
            finally
            {
                _environment = prev;
            }
        }
        return (false, ExecutionResult.Success());
    }

    // ===================== Async Expression Helpers =====================

    private async Task<object?> EvaluateBinaryAsync(Expr.Binary binary)
    {
        object? left = await EvaluateAsync(binary.Left);
        object? right = await EvaluateAsync(binary.Right);
        return EvaluateBinaryOperation(binary.Operator, left, right);
    }

    private async Task<object?> EvaluateLogicalAsync(Expr.Logical logical)
    {
        var left = await EvaluateAsync(logical.Left);
        if (logical.Operator.Type == TokenType.OR_OR)
            return IsTruthy(left) ? left : await EvaluateAsync(logical.Right);
        return !IsTruthy(left) ? left : await EvaluateAsync(logical.Right);
    }

    private async Task<object?> EvaluateNullishCoalescingAsync(Expr.NullishCoalescing nc)
    {
        var left = await EvaluateAsync(nc.Left);
        return left ?? await EvaluateAsync(nc.Right);
    }

    private async Task<object?> EvaluateTernaryAsync(Expr.Ternary ternary)
    {
        var condition = await EvaluateAsync(ternary.Condition);
        return IsTruthy(condition)
            ? await EvaluateAsync(ternary.ThenBranch)
            : await EvaluateAsync(ternary.ElseBranch);
    }

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
        // Handle console.log special case
        if (call.Callee is Expr.Variable v && v.Name.Lexeme == "console.log")
        {
            List<object?> consoleArgs = [];
            foreach (Expr argument in call.Arguments)
            {
                consoleArgs.Add(await EvaluateAsync(argument));
            }
            Console.WriteLine(string.Join(" ", consoleArgs.Select(Stringify)));
            return null;
        }

        // Handle built-in static methods: Object.keys(), Array.isArray(), JSON.parse(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable nsVar)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (method != null)
            {
                List<object?> args = [];
                foreach (var arg in call.Arguments)
                {
                    args.Add(await EvaluateAsync(arg));
                }
                return method.Call(this, args);
            }
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            if (call.Arguments.Count >= 2)
            {
                var source = await EvaluateAsync(call.Arguments[0]);
                var excludeKeys = await EvaluateAsync(call.Arguments[1]) as SharpTSArray;
                return ObjectBuiltIns.ObjectRest(source, excludeKeys?.Elements ?? []);
            }
            throw new Exception("__objectRest requires 2 arguments");
        }

        // Handle Symbol() constructor
        if (call.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            string? description = null;
            if (call.Arguments.Count > 0)
            {
                description = (await EvaluateAsync(call.Arguments[0]))?.ToString();
            }
            return new SharpTSSymbol(description);
        }

        // Handle BigInt() constructor - converts number/string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
                throw new Exception("Runtime Error: BigInt() requires exactly one argument.");

            var arg = await EvaluateAsync(call.Arguments[0]);
            return arg switch
            {
                SharpTSBigInt bi => bi,
                System.Numerics.BigInteger biVal => new SharpTSBigInt(biVal),
                double d => new SharpTSBigInt(d),
                string s => new SharpTSBigInt(s),
                _ => throw new Exception($"Runtime Error: Cannot convert {arg?.GetType().Name ?? "null"} to bigint.")
            };
        }

        // Handle Date() function call - returns current date as string (without 'new')
        if (call.Callee is Expr.Variable dateVar && dateVar.Name.Lexeme == "Date")
        {
            // Date() called without 'new' ignores all arguments and returns current date as string
            return new SharpTSDate().ToString();
        }

        object? callee = await EvaluateAsync(call.Callee);

        List<object?> arguments = [];
        foreach (var arg in call.Arguments)
        {
            if (arg is Expr.Spread spread)
            {
                object? spreadValue = await EvaluateAsync(spread.Expression);
                if (spreadValue is SharpTSArray arr)
                {
                    arguments.AddRange(arr.Elements);
                }
                else
                {
                    throw new Exception("Runtime Error: Spread argument must be an array.");
                }
            }
            else
            {
                arguments.Add(await EvaluateAsync(arg));
            }
        }

        // Always use Call() to get the Promise object.
        // The await expression will handle unwrapping via CallAsync.
        if (callee is ISharpTSCallable callable)
        {
            return callable.Call(this, arguments);
        }

        throw new Exception("Can only call functions and classes.");
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
        object? klass = ResolveQualifiedClass(newExpr.NamespacePath, newExpr.ClassName);
        if (klass is not SharpTSClass sharpClass)
        {
            throw new Exception("Can only instantiate classes.");
        }

        List<object?> arguments = [];
        foreach (var arg in newExpr.Arguments)
        {
            arguments.Add(await EvaluateAsync(arg));
        }

        return sharpClass.Call(this, arguments);
    }

    private async Task<object?> EvaluateArrayAsync(Expr.ArrayLiteral array)
    {
        List<object?> elements = [];
        foreach (Expr element in array.Elements)
        {
            if (element is Expr.Spread spread)
            {
                object? spreadValue = await EvaluateAsync(spread.Expression);
                if (spreadValue is SharpTSArray arr)
                {
                    elements.AddRange(arr.Elements);
                }
                else
                {
                    throw new Exception("Runtime Error: Spread expression must be an array.");
                }
            }
            else
            {
                elements.Add(await EvaluateAsync(element));
            }
        }
        return new SharpTSArray(elements);
    }

    private async Task<object?> EvaluateObjectAsync(Expr.ObjectLiteral obj)
    {
        Dictionary<string, object?> stringFields = [];
        Dictionary<SharpTSSymbol, object?> symbolFields = [];

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = await EvaluateAsync(prop.Value);
                if (spreadValue is SharpTSObject spreadObj)
                {
                    foreach (var kv in spreadObj.Fields)
                    {
                        stringFields[kv.Key] = kv.Value;
                    }
                }
                else if (spreadValue is SharpTSInstance inst)
                {
                    foreach (var key in inst.GetFieldNames())
                    {
                        stringFields[key] = inst.GetRawField(key);
                    }
                }
                else
                {
                    throw new Exception("Runtime Error: Spread in object literal requires an object.");
                }
            }
            else
            {
                object? value = await EvaluateAsync(prop.Value);

                switch (prop.Key)
                {
                    case Expr.IdentifierKey ik:
                        stringFields[ik.Name.Lexeme] = value;
                        break;
                    case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                        stringFields[(string)lk.Literal.Literal!] = value;
                        break;
                    case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                        // Number keys are converted to strings in JS/TS
                        stringFields[lk.Literal.Literal!.ToString()!] = value;
                        break;
                    case Expr.ComputedKey ck:
                        object? keyValue = await EvaluateAsync(ck.Expression);
                        if (keyValue is SharpTSSymbol sym)
                            symbolFields[sym] = value;
                        else if (keyValue is double numKey)
                            stringFields[numKey.ToString()] = value;
                        else
                            stringFields[keyValue?.ToString() ?? "undefined"] = value;
                        break;
                }
            }
        }

        var result = new SharpTSObject(stringFields);
        // Apply symbol fields
        foreach (var (sym, val) in symbolFields)
        {
            result.SetBySymbol(sym, val);
        }
        return result;
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

    private async Task<object?> EvaluatePrefixIncrementAsync(Expr.PrefixIncrement prefix)
    {
        // Delegate to sync version since prefix increment evaluates operand once
        return EvaluatePrefixIncrement(prefix);
    }

    private async Task<object?> EvaluatePostfixIncrementAsync(Expr.PostfixIncrement postfix)
    {
        // Delegate to sync version since postfix increment evaluates operand once
        return EvaluatePostfixIncrement(postfix);
    }

    private async Task<object?> EvaluateTemplateLiteralAsync(Expr.TemplateLiteral template)
    {
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < template.Strings.Count; i++)
        {
            result.Append(template.Strings[i]);
            if (i < template.Expressions.Count)
            {
                result.Append(Stringify(await EvaluateAsync(template.Expressions[i])));
            }
        }

        return result.ToString();
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
        if (obj is SharpTSArray array && index is double idx)
        {
            array.Set((int)idx, value);
            return value;
        }
        if (obj is SharpTSObject sharpObj && index is string strKey)
        {
            sharpObj.SetProperty(strKey, value);
            return value;
        }
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            numObj.SetProperty(numKey.ToString(), value);
            return value;
        }
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            instance.SetRawField(instanceKey, value);
            return value;
        }
        throw new Exception("Index assignment not supported on this type.");
    }
}
