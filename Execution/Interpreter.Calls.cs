using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    /// <summary>
    /// Evaluates a function or method call expression.
    /// </summary>
    /// <param name="call">The call expression AST node.</param>
    /// <returns>The return value of the called function.</returns>
    /// <remarks>
    /// Handles special cases for <c>console.log</c>, <c>Object.*</c> static methods,
    /// and the internal <c>__objectRest</c> helper. Supports spread arguments.
    /// Validates arity before invoking the callable.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/functions.html">TypeScript Functions</seealso>
    private object? EvaluateCall(Expr.Call call)
    {
        if (call.Callee is Expr.Variable v && v.Name.Lexeme == "console.log")
        {
            List<object?> arguments = [];
            foreach (Expr argument in call.Arguments)
            {
                arguments.Add(Evaluate(argument));
            }
            Console.WriteLine(string.Join(" ", arguments.Select(Stringify)));
            return null;
        }

        // Handle Object.keys(), Object.values(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable objVar &&
            objVar.Name.Lexeme == "Object")
        {
            var method = ObjectBuiltIns.GetStaticMethod(get.Name.Lexeme);
            if (method is BuiltInMethod builtIn)
            {
                List<object?> args = call.Arguments.Select(Evaluate).ToList();
                return builtIn.Call(this, args);
            }
        }

        // Handle Array.isArray()
        if (call.Callee is Expr.Get arrGet &&
            arrGet.Object is Expr.Variable arrVar &&
            arrVar.Name.Lexeme == "Array")
        {
            var method = ArrayStaticBuiltIns.GetStaticMethod(arrGet.Name.Lexeme);
            if (method is BuiltInMethod builtIn)
            {
                List<object?> args = call.Arguments.Select(Evaluate).ToList();
                return builtIn.Call(this, args);
            }
        }

        // Handle JSON.parse(), JSON.stringify()
        if (call.Callee is Expr.Get jsonGet &&
            jsonGet.Object is Expr.Variable jsonVar &&
            jsonVar.Name.Lexeme == "JSON")
        {
            var method = JSONBuiltIns.GetStaticMethod(jsonGet.Name.Lexeme);
            if (method is BuiltInMethod builtIn)
            {
                List<object?> args = call.Arguments.Select(Evaluate).ToList();
                return builtIn.Call(this, args);
            }
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            if (call.Arguments.Count >= 2)
            {
                var source = Evaluate(call.Arguments[0]);
                var excludeKeys = Evaluate(call.Arguments[1]) as SharpTSArray;
                return ObjectBuiltIns.ObjectRest(source, excludeKeys?.Elements ?? []);
            }
            throw new Exception("__objectRest requires 2 arguments");
        }

        object? callee = Evaluate(call.Callee);

        List<object?> argumentsList = [];
        foreach (Expr argument in call.Arguments)
        {
            if (argument is Expr.Spread spread)
            {
                object? spreadValue = Evaluate(spread.Expression);
                if (spreadValue is SharpTSArray arr)
                {
                    argumentsList.AddRange(arr.Elements);
                }
                else
                {
                    throw new Exception("Runtime Error: Spread expression must be an array.");
                }
            }
            else
            {
                argumentsList.Add(Evaluate(argument));
            }
        }

        if (callee is not ISharpTSCallable function)
        {
            throw new Exception("Can only call functions and classes.");
        }

        if (argumentsList.Count < function.Arity())
        {
            throw new Exception($"Expected at least {function.Arity()} arguments but got {argumentsList.Count}.");
        }

        return function.Call(this, argumentsList);
    }

    /// <summary>
    /// Evaluates a binary operator expression.
    /// </summary>
    /// <param name="binary">The binary expression AST node.</param>
    /// <returns>The result of applying the operator to both operands.</returns>
    /// <remarks>
    /// Supports arithmetic (+, -, *, /, %, **), comparison (&gt;, &lt;, &gt;=, &lt;=, ==, ===, !=, !==),
    /// bitwise (&amp;, |, ^, &lt;&lt;, &gt;&gt;, &gt;&gt;&gt;), and special operators (in, instanceof).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Guide/Expressions_and_operators">MDN Expressions and Operators</seealso>
    private object? EvaluateBinary(Expr.Binary binary)
    {
        object? left = Evaluate(binary.Left);
        object? right = Evaluate(binary.Right);

        return binary.Operator.Type switch
        {
            TokenType.GREATER => (double)left! > (double)right!,
            TokenType.GREATER_EQUAL => (double)left! >= (double)right!,
            TokenType.LESS => (double)left! < (double)right!,
            TokenType.LESS_EQUAL => (double)left! <= (double)right!,
            TokenType.BANG_EQUAL => !IsEqual(left, right),
            TokenType.BANG_EQUAL_EQUAL => !IsStrictEqual(left, right),
            TokenType.EQUAL_EQUAL => IsEqual(left, right),
            TokenType.EQUAL_EQUAL_EQUAL => IsStrictEqual(left, right),
            TokenType.MINUS => (double)left! - (double)right!,
            TokenType.PLUS => EvaluatePlus(left, right),
            TokenType.SLASH => (double)left! / (double)right!,
            TokenType.STAR => (double)left! * (double)right!,
            TokenType.STAR_STAR => Math.Pow((double)left!, (double)right!),
            TokenType.PERCENT => (double)left! % (double)right!,
            TokenType.IN => EvaluateIn(left, right),
            TokenType.INSTANCEOF => EvaluateInstanceof(left, right),
            // Bitwise operators
            TokenType.AMPERSAND => (double)(ToInt32(left) & ToInt32(right)),
            TokenType.PIPE => (double)(ToInt32(left) | ToInt32(right)),
            TokenType.CARET => (double)(ToInt32(left) ^ ToInt32(right)),
            TokenType.LESS_LESS => (double)(ToInt32(left) << (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER => (double)(ToInt32(left) >> (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER_GREATER => (double)((uint)ToInt32(left) >> (ToInt32(right) & 0x1F)),
            _ => null
        };
    }

    /// <summary>
    /// Converts a runtime value to a 32-bit signed integer.
    /// </summary>
    /// <param name="value">The value to convert (expected to be a double).</param>
    /// <returns>The 32-bit integer representation.</returns>
    /// <remarks>
    /// Used for bitwise operations which operate on 32-bit integers per ECMAScript spec.
    /// </remarks>
    /// <seealso href="https://tc39.es/ecma262/#sec-toint32">ECMAScript ToInt32</seealso>
    private int ToInt32(object? value) => (int)(double)value!;

    /// <summary>
    /// Evaluates the <c>instanceof</c> operator.
    /// </summary>
    /// <param name="left">The instance to check.</param>
    /// <param name="right">The class to check against.</param>
    /// <returns><c>true</c> if the instance is of the specified class or a subclass; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Walks up the inheritance chain to check if the instance's class or any superclass
    /// matches the target class name.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/narrowing.html#instanceof-narrowing">TypeScript instanceof Narrowing</seealso>
    private object EvaluateInstanceof(object? left, object? right)
    {
        if (left is not SharpTSInstance instance || right is not SharpTSClass targetClass)
            return false;
        SharpTSClass? current = instance.GetClass();
        while (current != null)
        {
            if (current.Name == targetClass.Name) return true;
            current = current.Superclass;
        }
        return false;
    }

    /// <summary>
    /// Evaluates a logical operator expression (AND/OR) with short-circuit evaluation.
    /// </summary>
    /// <param name="logical">The logical expression AST node.</param>
    /// <returns>The value that determined the result (not necessarily a boolean).</returns>
    /// <remarks>
    /// Implements JavaScript short-circuit semantics: OR (<c>||</c>) returns the first truthy value
    /// or the last value; AND (<c>&amp;&amp;</c>) returns the first falsy value or the last value.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Logical_AND">MDN Logical AND</seealso>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Logical_OR">MDN Logical OR</seealso>
    private object? EvaluateLogical(Expr.Logical logical)
    {
        object? left = Evaluate(logical.Left);

        // Short-circuit evaluation
        if (logical.Operator.Type == TokenType.OR_OR)
        {
            if (IsTruthy(left)) return left;
        }
        else // AND_AND
        {
            if (!IsTruthy(left)) return left;
        }

        return Evaluate(logical.Right);
    }

    /// <summary>
    /// Evaluates the nullish coalescing operator (<c>??</c>).
    /// </summary>
    /// <param name="nc">The nullish coalescing expression AST node.</param>
    /// <returns>The left value if not null; otherwise the right value.</returns>
    /// <remarks>
    /// Unlike OR (<c>||</c>), nullish coalescing only falls back for <c>null</c>,
    /// not for other falsy values like <c>0</c>, <c>""</c>, or <c>false</c>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-7.html#nullish-coalescing">TypeScript Nullish Coalescing</seealso>
    private object? EvaluateNullishCoalescing(Expr.NullishCoalescing nc)
    {
        object? left = Evaluate(nc.Left);
        // Only return right if left is null (not for other falsy values)
        if (left == null)
        {
            return Evaluate(nc.Right);
        }
        return left;
    }

    /// <summary>
    /// Evaluates a ternary conditional expression (<c>?:</c>).
    /// </summary>
    /// <param name="ternary">The ternary expression AST node.</param>
    /// <returns>The then-branch value if condition is truthy; otherwise the else-branch value.</returns>
    /// <remarks>
    /// Only evaluates one branch based on the truthiness of the condition.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Conditional_operator">MDN Conditional Operator</seealso>
    private object? EvaluateTernary(Expr.Ternary ternary)
    {
        object? condition = Evaluate(ternary.Condition);
        return IsTruthy(condition)
            ? Evaluate(ternary.ThenBranch)
            : Evaluate(ternary.ElseBranch);
    }

    /// <summary>
    /// Applies a compound assignment operator to two values.
    /// </summary>
    /// <param name="op">The compound operator token type (e.g., PLUS_EQUAL, MINUS_EQUAL).</param>
    /// <param name="left">The current value of the target.</param>
    /// <param name="right">The value to combine with.</param>
    /// <returns>The result of the operation.</returns>
    /// <remarks>
    /// Supports arithmetic (+=, -=, *=, /=, %=) and bitwise (&amp;=, |=, ^=, &lt;&lt;=, &gt;&gt;=, &gt;&gt;&gt;=)
    /// compound operators. String concatenation is handled for +=.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition_assignment">MDN Compound Assignment</seealso>
    private object? ApplyCompoundOperator(TokenType op, object? left, object? right)
    {
        return op switch
        {
            TokenType.PLUS_EQUAL => left is string ? Stringify(left) + Stringify(right) : (double)left! + (double)right!,
            TokenType.MINUS_EQUAL => (double)left! - (double)right!,
            TokenType.STAR_EQUAL => (double)left! * (double)right!,
            TokenType.SLASH_EQUAL => (double)left! / (double)right!,
            TokenType.PERCENT_EQUAL => (double)left! % (double)right!,
            // Bitwise compound operators
            TokenType.AMPERSAND_EQUAL => (double)(ToInt32(left) & ToInt32(right)),
            TokenType.PIPE_EQUAL => (double)(ToInt32(left) | ToInt32(right)),
            TokenType.CARET_EQUAL => (double)(ToInt32(left) ^ ToInt32(right)),
            TokenType.LESS_LESS_EQUAL => (double)(ToInt32(left) << (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER_EQUAL => (double)(ToInt32(left) >> (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER_GREATER_EQUAL => (double)((uint)ToInt32(left) >> (ToInt32(right) & 0x1F)),
            _ => throw new Exception($"Unknown compound operator: {op}")
        };
    }
}
