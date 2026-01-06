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
    public void ExecuteBlock(List<Stmt> statements, RuntimeEnvironment environment)
    {
        RuntimeEnvironment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        finally
        {
            _environment = previous;
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
    private void ExecuteLabeledStatement(Stmt.LabeledStatement labeledStmt)
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
                try
                {
                    Execute(labeledStmt.Statement);
                    return; // Statement completed normally
                }
                catch (BreakException ex) when (ex.TargetLabel == labelName)
                {
                    // Break targeting this label - exit
                    return;
                }
                catch (ContinueException ex) when (ex.TargetLabel == labelName)
                {
                    // Continue targeting this label - restart the loop
                    continue;
                }
                // Unlabeled or differently-labeled exceptions propagate up
            }
        }
        else
        {
            // For non-loop statements, only handle break
            try
            {
                Execute(labeledStmt.Statement);
            }
            catch (BreakException ex) when (ex.TargetLabel == labelName)
            {
                // Break targeting this label - exit
                return;
            }
            // Unlabeled or differently-labeled exceptions propagate up
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
    private void ExecuteSwitch(Stmt.Switch switchStmt)
    {
        object? subject = Evaluate(switchStmt.Subject);
        bool fallen = false;
        bool matched = false;

        try
        {
            foreach (var caseItem in switchStmt.Cases)
            {
                if (fallen || IsEqual(subject, Evaluate(caseItem.Value)))
                {
                    matched = fallen = true;
                    foreach (var stmt in caseItem.Body)
                    {
                        Execute(stmt);
                    }
                }
            }

            if (switchStmt.DefaultBody != null && (fallen || !matched))
            {
                foreach (var stmt in switchStmt.DefaultBody)
                {
                    Execute(stmt);
                }
            }
        }
        catch (BreakException ex) when (ex.TargetLabel == null)
        {
            // Exit switch (only unlabeled breaks)
        }
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
    private void ExecuteTryCatch(Stmt.TryCatch tryCatch)
    {
        Exception? pendingException = null;
        bool exceptionHandled = false;

        try
        {
            foreach (var stmt in tryCatch.TryBlock)
            {
                Execute(stmt);
            }
        }
        catch (ThrowException ex)
        {
            pendingException = ex;

            if (tryCatch.CatchBlock != null && tryCatch.CatchParam != null)
            {
                exceptionHandled = true;
                RuntimeEnvironment catchEnv = new(_environment);
                catchEnv.Define(tryCatch.CatchParam.Lexeme, ex.Value);

                RuntimeEnvironment previous = _environment;
                _environment = catchEnv;
                try
                {
                    foreach (var stmt in tryCatch.CatchBlock)
                    {
                        Execute(stmt);
                    }
                }
                finally
                {
                    _environment = previous;
                }
            }
        }
        catch (ReturnException)
        {
            // Execute finally before propagating return
            ExecuteFinally(tryCatch.FinallyBlock);
            throw;
        }
        catch (BreakException)
        {
            ExecuteFinally(tryCatch.FinallyBlock);
            throw;
        }
        catch (ContinueException)
        {
            ExecuteFinally(tryCatch.FinallyBlock);
            throw;
        }

        // Always execute finally for normal completion
        ExecuteFinally(tryCatch.FinallyBlock);

        // Re-throw if exception wasn't handled
        if (pendingException != null && !exceptionHandled)
        {
            throw pendingException;
        }
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
    private void ExecuteFinally(List<Stmt>? finallyBlock)
    {
        if (finallyBlock != null)
        {
            foreach (var stmt in finallyBlock)
            {
                Execute(stmt);
            }
        }
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
    private void ExecuteForOf(Stmt.ForOf forOf)
    {
        object? iterable = Evaluate(forOf.Iterable);

        // Get elements based on iterable type
        IEnumerable<object?> elements = iterable switch
        {
            SharpTSArray array => array.Elements,
            SharpTSMap map => map.Entries().Elements,      // yields [key, value] arrays
            SharpTSSet set => set.Values().Elements,       // yields values
            SharpTSIterator iter => iter.Elements,
            string s => s.Select(c => (object?)c.ToString()),
            _ => throw new Exception("Runtime Error: for...of requires an iterable (array, Map, Set, or iterator).")
        };

        foreach (var element in elements)
        {
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(forOf.Variable.Lexeme, element);

            RuntimeEnvironment previous = _environment;
            _environment = loopEnv;

            try
            {
                Execute(forOf.Body);
            }
            catch (BreakException ex) when (ex.TargetLabel == null)
            {
                _environment = previous;
                break;
            }
            catch (ContinueException ex) when (ex.TargetLabel == null)
            {
                _environment = previous;
                continue;
            }
            finally
            {
                _environment = previous;
            }
        }
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
    private void ExecuteForIn(Stmt.ForIn forIn)
    {
        object? obj = Evaluate(forIn.Object);

        IEnumerable<string> keys = obj switch
        {
            SharpTSObject o => o.Fields.Keys,
            SharpTSInstance i => i.GetFieldNames(),
            SharpTSArray a => Enumerable.Range(0, a.Elements.Count).Select(i => i.ToString()),
            _ => throw new Exception("Runtime Error: for...in requires an object.")
        };

        foreach (string key in keys)
        {
            RuntimeEnvironment loopEnv = new(_environment);
            loopEnv.Define(forIn.Variable.Lexeme, key);

            RuntimeEnvironment previous = _environment;
            _environment = loopEnv;

            try
            {
                Execute(forIn.Body);
            }
            catch (BreakException ex) when (ex.TargetLabel == null)
            {
                _environment = previous;
                break;
            }
            catch (ContinueException ex) when (ex.TargetLabel == null)
            {
                _environment = previous;
                continue;
            }
            finally
            {
                _environment = previous;
            }
        }
    }
}
