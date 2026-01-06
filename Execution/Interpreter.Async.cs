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
    internal async Task ExecuteBlockAsync(List<Stmt> statements, RuntimeEnvironment environment)
    {
        RuntimeEnvironment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                await ExecuteAsync(statement);
            }
        }
        finally
        {
            _environment = previous;
        }
    }

    /// <summary>
    /// Asynchronously dispatches a statement to the appropriate execution handler.
    /// </summary>
    private async Task ExecuteAsync(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                await ExecuteBlockAsync(block.Statements, new RuntimeEnvironment(_environment));
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    await ExecuteAsync(s);
                break;
            case Stmt.Expression exprStmt:
                await EvaluateAsync(exprStmt.Expr);
                break;
            case Stmt.If ifStmt:
                if (IsTruthy(await EvaluateAsync(ifStmt.Condition)))
                {
                    await ExecuteAsync(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    await ExecuteAsync(ifStmt.ElseBranch);
                }
                break;
            case Stmt.While whileStmt:
                while (IsTruthy(await EvaluateAsync(whileStmt.Condition)))
                {
                    try
                    {
                        await ExecuteAsync(whileStmt.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        continue;
                    }
                }
                break;
            case Stmt.DoWhile doWhileStmt:
                do
                {
                    try
                    {
                        await ExecuteAsync(doWhileStmt.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        continue;
                    }
                } while (IsTruthy(await EvaluateAsync(doWhileStmt.Condition)));
                break;
            case Stmt.ForOf forOf:
                await ExecuteForOfAsync(forOf);
                break;
            case Stmt.ForIn forIn:
                await ExecuteForInAsync(forIn);
                break;
            case Stmt.Break breakStmt:
                throw new BreakException(breakStmt.Label?.Lexeme);
            case Stmt.Continue continueStmt:
                throw new ContinueException(continueStmt.Label?.Lexeme);
            case Stmt.Switch switchStmt:
                await ExecuteSwitchAsync(switchStmt);
                break;
            case Stmt.TryCatch tryCatch:
                await ExecuteTryCatchAsync(tryCatch);
                break;
            case Stmt.Throw throwStmt:
                throw new ThrowException(await EvaluateAsync(throwStmt.Value));
            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    value = await EvaluateAsync(varStmt.Initializer);
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                break;
            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null) returnValue = await EvaluateAsync(returnStmt.Value);
                throw new ReturnException(returnValue);
            case Stmt.Print printStmt:
                Console.WriteLine(Stringify(await EvaluateAsync(printStmt.Expr)));
                break;
            default:
                // Fall back to sync execution for other statements
                Execute(stmt);
                break;
        }
    }

    private async Task ExecuteForOfAsync(Stmt.ForOf forOf)
    {
        object? iterable = await EvaluateAsync(forOf.Iterable);

        IEnumerable<object?> items = iterable switch
        {
            SharpTSArray arr => arr.Elements,
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new Exception("Runtime Error: for...of requires an iterable.")
        };

        foreach (var item in items)
        {
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(forOf.Variable.Lexeme, item);

            try
            {
                RuntimeEnvironment prev = _environment;
                _environment = loopEnv;
                try
                {
                    await ExecuteAsync(forOf.Body);
                }
                finally
                {
                    _environment = prev;
                }
            }
            catch (BreakException ex) when (ex.TargetLabel == null)
            {
                break;
            }
            catch (ContinueException ex) when (ex.TargetLabel == null)
            {
                continue;
            }
        }
    }

    private async Task ExecuteForInAsync(Stmt.ForIn forIn)
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
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(forIn.Variable.Lexeme, key);

            try
            {
                RuntimeEnvironment prev = _environment;
                _environment = loopEnv;
                try
                {
                    await ExecuteAsync(forIn.Body);
                }
                finally
                {
                    _environment = prev;
                }
            }
            catch (BreakException ex) when (ex.TargetLabel == null)
            {
                break;
            }
            catch (ContinueException ex) when (ex.TargetLabel == null)
            {
                continue;
            }
        }
    }

    private async Task ExecuteSwitchAsync(Stmt.Switch switchStmt)
    {
        object? subject = await EvaluateAsync(switchStmt.Subject);
        bool matched = false;
        bool fallThrough = false;

        try
        {
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
                        await ExecuteAsync(caseStmt);
                    }
                    fallThrough = true;
                }
            }

            if (!matched && switchStmt.DefaultBody != null)
            {
                foreach (var defaultStmt in switchStmt.DefaultBody)
                {
                    await ExecuteAsync(defaultStmt);
                }
            }
        }
        catch (BreakException ex) when (ex.TargetLabel == null)
        {
            // Break from switch
        }
    }

    private async Task ExecuteTryCatchAsync(Stmt.TryCatch tryCatch)
    {
        try
        {
            foreach (var stmt in tryCatch.TryBlock)
            {
                await ExecuteAsync(stmt);
            }
        }
        catch (ThrowException ex)
        {
            await HandleCatchBlockAsync(tryCatch, ex.Value);
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            // Handle rejected promise exceptions the same as ThrowException
            await HandleCatchBlockAsync(tryCatch, ex.Reason);
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            // Handle wrapped rejection exception
            await HandleCatchBlockAsync(tryCatch, rejEx.Reason);
        }
        finally
        {
            if (tryCatch.FinallyBlock != null)
            {
                foreach (var stmt in tryCatch.FinallyBlock)
                {
                    await ExecuteAsync(stmt);
                }
            }
        }
    }

    private async Task HandleCatchBlockAsync(Stmt.TryCatch tryCatch, object? value)
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
                    await ExecuteAsync(stmt);
                }
            }
            finally
            {
                _environment = prev;
            }
        }
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
        object? left = await EvaluateAsync(logical.Left);
        if (logical.Operator.Type == TokenType.OR_OR)
        {
            if (IsTruthy(left)) return left;
        }
        else // AND_AND
        {
            if (!IsTruthy(left)) return left;
        }
        return await EvaluateAsync(logical.Right);
    }

    private async Task<object?> EvaluateNullishCoalescingAsync(Expr.NullishCoalescing nc)
    {
        object? left = await EvaluateAsync(nc.Left);
        if (left != null) return left;
        return await EvaluateAsync(nc.Right);
    }

    private async Task<object?> EvaluateTernaryAsync(Expr.Ternary ternary)
    {
        if (IsTruthy(await EvaluateAsync(ternary.Condition)))
        {
            return await EvaluateAsync(ternary.ThenBranch);
        }
        return await EvaluateAsync(ternary.ElseBranch);
    }

    private async Task<object?> EvaluateUnaryAsync(Expr.Unary unary)
    {
        object? right = await EvaluateAsync(unary.Right);
        return EvaluateUnaryOperation(unary.Operator, right);
    }

    private async Task<object?> EvaluateAssignAsync(Expr.Assign assign)
    {
        object? value = await EvaluateAsync(assign.Value);
        _environment.Assign(assign.Name, value);
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
        object? klass = _environment.Get(newExpr.ClassName);
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
        Dictionary<string, object?> fields = [];
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = await EvaluateAsync(prop.Value);
                if (spreadValue is SharpTSObject spreadObj)
                {
                    foreach (var kv in spreadObj.Fields)
                    {
                        fields[kv.Key] = kv.Value;
                    }
                }
                else if (spreadValue is SharpTSInstance inst)
                {
                    foreach (var key in inst.GetFieldNames())
                    {
                        fields[key] = inst.GetFieldValue(key);
                    }
                }
                else
                {
                    throw new Exception("Runtime Error: Spread in object literal requires an object.");
                }
            }
            else
            {
                fields[prop.Name!.Lexeme] = await EvaluateAsync(prop.Value);
            }
        }
        return new SharpTSObject(fields);
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
            return sharpObj.Get(strKey);
        }
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            return numObj.Get(numKey.ToString());
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
            sharpObj.Set(strKey, value);
            return value;
        }
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            numObj.Set(numKey.ToString(), value);
            return value;
        }
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            instance.SetFieldValue(instanceKey, value);
            return value;
        }
        throw new Exception("Index assignment not supported on this type.");
    }
}
