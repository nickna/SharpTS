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

        // Handle built-in static methods: Object.keys(), Array.isArray(), JSON.parse(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable nsVar)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (method != null)
            {
                List<object?> args = call.Arguments.Select(Evaluate).ToList();
                return method.Call(this, args);
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

        // Handle Symbol() constructor - creates unique symbols
        if (call.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            string? description = null;
            if (call.Arguments.Count > 0)
            {
                var arg = Evaluate(call.Arguments[0]);
                description = arg?.ToString();
            }
            return new SharpTSSymbol(description);
        }

        // Handle BigInt() constructor - converts number/string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
                throw new Exception("Runtime Error: BigInt() requires exactly one argument.");

            var arg = Evaluate(call.Arguments[0]);
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

        // Handle global parseInt()
        if (call.Callee is Expr.Variable parseIntVar && parseIntVar.Name.Lexeme == "parseInt")
        {
            if (call.Arguments.Count < 1)
                throw new Exception("Runtime Error: parseInt() requires at least one argument.");
            var str = Evaluate(call.Arguments[0])?.ToString() ?? "";
            var radix = call.Arguments.Count > 1 && Evaluate(call.Arguments[1]) != null
                ? (int)(double)Evaluate(call.Arguments[1])!
                : 10;
            return NumberBuiltIns.ParseInt(str, radix);
        }

        // Handle global parseFloat()
        if (call.Callee is Expr.Variable parseFloatVar && parseFloatVar.Name.Lexeme == "parseFloat")
        {
            if (call.Arguments.Count < 1)
                throw new Exception("Runtime Error: parseFloat() requires at least one argument.");
            var str = Evaluate(call.Arguments[0])?.ToString() ?? "";
            return NumberBuiltIns.ParseFloat(str);
        }

        // Handle global isNaN()
        if (call.Callee is Expr.Variable isNaNVar && isNaNVar.Name.Lexeme == "isNaN")
        {
            if (call.Arguments.Count < 1) return true; // isNaN() with no args returns true
            var arg = Evaluate(call.Arguments[0]);
            // Global isNaN coerces to number first (different from Number.isNaN)
            if (arg is double d) return double.IsNaN(d);
            if (arg is string s) return !double.TryParse(s, out _);
            if (arg is null) return true;
            if (arg is bool) return false;
            return true;
        }

        // Handle global isFinite()
        if (call.Callee is Expr.Variable isFiniteVar && isFiniteVar.Name.Lexeme == "isFinite")
        {
            if (call.Arguments.Count < 1) return false; // isFinite() with no args returns false
            var arg = Evaluate(call.Arguments[0]);
            // Global isFinite coerces to number first (different from Number.isFinite)
            if (arg is double d) return double.IsFinite(d);
            if (arg is string s && double.TryParse(s, out double parsed)) return double.IsFinite(parsed);
            if (arg is null) return true; // null coerces to 0 which is finite
            if (arg is bool) return true; // true=1, false=0, both finite
            return false;
        }

        object? callee = Evaluate(call.Callee);

        List<object?> argumentsList = [];
        foreach (Expr argument in call.Arguments)
        {
            if (argument is Expr.Spread spread)
            {
                object? spreadValue = Evaluate(spread.Expression);
                // Use GetIterableElements to support custom iterables with Symbol.iterator
                argumentsList.AddRange(GetIterableElements(spreadValue));
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
        return EvaluateBinaryOperation(binary.Operator, left, right);
    }

    /// <summary>
    /// Core binary operation logic, shared between sync and async evaluation.
    /// </summary>
    private object? EvaluateBinaryOperation(Token op, object? left, object? right)
    {
        // Check for bigint operations
        var leftBigInt = GetBigIntValue(left);
        var rightBigInt = GetBigIntValue(right);

        if (leftBigInt.HasValue || rightBigInt.HasValue)
        {
            return EvaluateBigIntBinary(op.Type, left, right, leftBigInt, rightBigInt);
        }

        return op.Type switch
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

    private System.Numerics.BigInteger? GetBigIntValue(object? value) => value switch
    {
        SharpTSBigInt bi => bi.Value,
        System.Numerics.BigInteger biVal => biVal,
        _ => null
    };

    private object EvaluateBigIntBinary(TokenType op, object? left, object? right,
        System.Numerics.BigInteger? leftBi, System.Numerics.BigInteger? rightBi)
    {
        // Equality operators allow mixed types (bigint with anything)
        if (op == TokenType.EQUAL_EQUAL || op == TokenType.EQUAL_EQUAL_EQUAL)
        {
            if (!leftBi.HasValue || !rightBi.HasValue) return false;
            return leftBi.Value == rightBi.Value;
        }
        if (op == TokenType.BANG_EQUAL || op == TokenType.BANG_EQUAL_EQUAL)
        {
            if (!leftBi.HasValue || !rightBi.HasValue) return true;
            return leftBi.Value != rightBi.Value;
        }

        // All other operators require both to be bigint
        if (!leftBi.HasValue || !rightBi.HasValue)
            throw new Exception("Runtime Error: Cannot mix bigint and other types in operations.");

        var l = leftBi.Value;
        var r = rightBi.Value;

        return op switch
        {
            // Arithmetic
            TokenType.PLUS => new SharpTSBigInt(l + r),
            TokenType.MINUS => new SharpTSBigInt(l - r),
            TokenType.STAR => new SharpTSBigInt(l * r),
            TokenType.SLASH => new SharpTSBigInt(System.Numerics.BigInteger.Divide(l, r)),
            TokenType.PERCENT => new SharpTSBigInt(System.Numerics.BigInteger.Remainder(l, r)),
            TokenType.STAR_STAR => SharpTSBigInt.Pow(new SharpTSBigInt(l), new SharpTSBigInt(r)),

            // Comparison
            TokenType.GREATER => l > r,
            TokenType.GREATER_EQUAL => l >= r,
            TokenType.LESS => l < r,
            TokenType.LESS_EQUAL => l <= r,

            // Bitwise
            TokenType.AMPERSAND => new SharpTSBigInt(l & r),
            TokenType.PIPE => new SharpTSBigInt(l | r),
            TokenType.CARET => new SharpTSBigInt(l ^ r),
            TokenType.LESS_LESS => new SharpTSBigInt(l << (int)r),
            TokenType.GREATER_GREATER => new SharpTSBigInt(l >> (int)r),
            TokenType.GREATER_GREATER_GREATER => throw new Exception("Runtime Error: Unsigned right shift (>>>) is not supported for bigint."),

            _ => throw new Exception($"Runtime Error: Operator {op} not supported for bigint.")
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
    /// Core logical operation logic, shared between sync and async evaluation.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    /// <param name="op">The operator type (OR_OR or AND_AND).</param>
    /// <param name="left">The already-evaluated left operand.</param>
    /// <param name="evaluateRight">A function to evaluate the right operand (only called if needed).</param>
    /// <returns>The value that determined the result.</returns>
    private object? EvaluateLogicalCore(TokenType op, object? left, Func<object?> evaluateRight)
    {
        if (op == TokenType.OR_OR)
            return IsTruthy(left) ? left : evaluateRight();
        return !IsTruthy(left) ? left : evaluateRight();
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
    private object? EvaluateLogical(Expr.Logical logical) =>
        EvaluateLogicalCore(logical.Operator.Type, Evaluate(logical.Left), () => Evaluate(logical.Right));

    /// <summary>
    /// Core nullish coalescing logic, shared between sync and async evaluation.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    /// <param name="left">The already-evaluated left operand.</param>
    /// <param name="evaluateRight">A function to evaluate the right operand (only called if left is nullish).</param>
    /// <returns>The left value if not nullish; otherwise the right value.</returns>
    private object? EvaluateNullishCoalescingCore(object? left, Func<object?> evaluateRight) =>
        (left == null || left is Runtime.Types.SharpTSUndefined) ? evaluateRight() : left;

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
    private object? EvaluateNullishCoalescing(Expr.NullishCoalescing nc) =>
        EvaluateNullishCoalescingCore(Evaluate(nc.Left), () => Evaluate(nc.Right));

    /// <summary>
    /// Core ternary operation logic, shared between sync and async evaluation.
    /// Uses lazy evaluation via Func delegates to ensure only one branch is evaluated.
    /// </summary>
    /// <param name="condition">The already-evaluated condition.</param>
    /// <param name="evalThen">A function to evaluate the then branch (only called if condition is truthy).</param>
    /// <param name="evalElse">A function to evaluate the else branch (only called if condition is falsy).</param>
    /// <returns>The result of evaluating the appropriate branch.</returns>
    private object? EvaluateTernaryCore(object? condition, Func<object?> evalThen, Func<object?> evalElse) =>
        IsTruthy(condition) ? evalThen() : evalElse();

    /// <summary>
    /// Evaluates a ternary conditional expression (<c>?:</c>).
    /// </summary>
    /// <param name="ternary">The ternary expression AST node.</param>
    /// <returns>The then-branch value if condition is truthy; otherwise the else-branch value.</returns>
    /// <remarks>
    /// Only evaluates one branch based on the truthiness of the condition.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Conditional_operator">MDN Conditional Operator</seealso>
    private object? EvaluateTernary(Expr.Ternary ternary) =>
        EvaluateTernaryCore(Evaluate(ternary.Condition), () => Evaluate(ternary.ThenBranch), () => Evaluate(ternary.ElseBranch));

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
