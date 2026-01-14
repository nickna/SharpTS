using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

public partial class Interpreter
{
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
            Expr.Literal lit => lit.Value ?? throw new Exception($"Runtime Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Runtime Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Runtime Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
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
            _ => throw new Exception($"Runtime Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
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
                _ => throw new Exception($"Runtime Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Runtime Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }

    /// <summary>
    /// Executes a block of statements within a given environment scope.
    /// </summary>
    /// <param name="statements">The list of statements to execute.</param>
    /// <param name="environment">The runtime environment for this block's scope.</param>
    /// <remarks>
    /// Temporarily switches to the provided environment, executes all statements,
    /// then restores the previous environment. Used for block scoping in control structures.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html#block-scoping">TypeScript Block Scoping</seealso>
    public ExecutionResult ExecuteBlock(List<Stmt> statements, RuntimeEnvironment environment)
    {
        using (PushScope(environment))
        {
            foreach (Stmt statement in statements)
            {
                var result = Execute(statement);
                if (result.IsAbrupt) return result;
            }
            return ExecutionResult.Success();
        }
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
    /// Executes a switch statement with case matching and fall-through semantics.
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
            if (fallen || IsEqual(subject, Evaluate(caseItem.Value)))
            {
                matched = fallen = true;
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
    /// Executes a try/catch/finally statement with proper exception handling.
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
                    exceptionHandled = HandleCatchBlock(tryCatch, result.Value, out pendingResult);
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
            object? errorValue = ex is ThrowException tex ? tex.Value : ex.Message;
            pendingResult = ExecutionResult.Throw(errorValue);
            exceptionHandled = HandleCatchBlock(tryCatch, errorValue, out pendingResult);
        }

        // Always execute finally
        if (tryCatch.FinallyBlock != null)
        {
            var finallyResult = ExecuteFinally(tryCatch.FinallyBlock);
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

    private bool HandleCatchBlock(Stmt.TryCatch tryCatch, object? errorValue, out ExecutionResult result)
    {
        result = ExecutionResult.Success();
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
                            result = catchResult;
                            return true;
                        }
                    }
                    result = ExecutionResult.Success();
                    return true;
                }
                catch (Exception ex)
                {
                    object? catchError = ex is ThrowException tex ? tex.Value : ex.Message;
                    result = ExecutionResult.Throw(catchError);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Executes a finally block if present.
    /// </summary>
    /// <param name="finallyBlock">The list of statements in the finally block, or null if none.</param>
    /// <remarks>
    /// Helper method called by <see cref="ExecuteTryCatch"/> to ensure finally block
    /// runs regardless of how the try block exits.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements/try...catch#the_finally_block">MDN finally Block</seealso>
    private ExecutionResult ExecuteFinally(List<Stmt> finallyBlock)
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
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            SharpTSGenerator gen => gen,                   // generators implement IEnumerable<object?>
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new Exception("Runtime Error: for...of requires an iterable (array, Map, Set, or iterator).")
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
            _ => throw new Exception("Runtime Error: for...in requires an object.")
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
            throw new Exception("Runtime Error: [Symbol.iterator] must be a function.");
        }

        // Iterate using the iterator protocol
        while (true)
        {
            // Get the next() method
            object? nextMethod = null;
            if (iterator is SharpTSObject iterObj)
            {
                nextMethod = iterObj.Get("next");
            }
            else if (iterator is SharpTSInstance iterInst)
            {
                nextMethod = iterInst.GetFieldValue("next");
                if (nextMethod == null)
                {
                    // Try getting a method from the class
                    var tok = new Token(TokenType.IDENTIFIER, "next", null, 0);
                    try { nextMethod = iterInst.Get(tok); } catch { }
                }
            }

            if (nextMethod == null)
            {
                throw new Exception("Runtime Error: Iterator must have a next() method.");
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
                throw new Exception("Runtime Error: Iterator.next must be a function.");
            }

            // Get done and value from result
            bool done = false;
            object? value = null;

            if (result is SharpTSObject resultObj)
            {
                var doneVal = resultObj.Get("done");
                done = IsTruthy(doneVal);
                value = resultObj.Get("value");
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
                    done = IsTruthy(resultInst.GetFieldValue("done"));
                    value = resultInst.GetFieldValue("value");
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
            null => throw new Exception("Runtime Error: Cannot spread null or undefined."),
            _ => throw new Exception($"Runtime Error: Value of type '{value.GetType().Name}' is not iterable. Expected an array, string, Map, Set, generator, or object with [Symbol.iterator].")
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
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) return ExecutionResult.Success();
                if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null) continue;
                if (result.IsAbrupt) return result;
            }
        }
        return ExecutionResult.Success();
    }
}
