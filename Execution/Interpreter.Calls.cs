using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    /// <summary>
    /// Core implementation for evaluating function/method calls, shared between sync and async paths.
    /// Handles all special cases: console.log, built-in methods, Symbol, BigInt, Date, Error, timers, etc.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating arguments.</param>
    /// <param name="call">The call expression AST node.</param>
    /// <returns>A ValueTask containing the return value of the called function.</returns>
    private async ValueTask<object?> EvaluateCallCore(IEvaluationContext ctx, Expr.Call call)
    {
        // Handle console.* methods
        if (call.Callee is Expr.Variable v && v.Name.Lexeme.StartsWith("console."))
        {
            var methodName = v.Name.Lexeme["console.".Length..];
            var method = BuiltInRegistry.Instance.GetStaticMethod("console", methodName);
            if (method != null)
            {
                List<object?> arguments = await ctx.EvaluateAllAsync(call.Arguments);
                return method.Call(this, arguments);
            }
        }

        // Handle globalThis.console.* calls
        if (call.Callee is Expr.Get chainedGet &&
            chainedGet.Object is Expr.Get innerGet &&
            innerGet.Object is Expr.Variable globalThisVar &&
            globalThisVar.Name.Lexeme == "globalThis" &&
            innerGet.Name.Lexeme == "console")
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod("console", chainedGet.Name.Lexeme);
            if (method != null)
            {
                List<object?> args = await ctx.EvaluateAllAsync(call.Arguments);
                return method.Call(this, args);
            }
        }

        // Handle globalThis.<namespace>.<method>() calls (e.g., globalThis.Math.floor())
        if (call.Callee is Expr.Get gtChainedGet &&
            gtChainedGet.Object is Expr.Get gtInnerGet &&
            gtInnerGet.Object is Expr.Variable gtVar &&
            gtVar.Name.Lexeme == "globalThis")
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(gtInnerGet.Name.Lexeme, gtChainedGet.Name.Lexeme);
            if (method != null)
            {
                List<object?> args = await ctx.EvaluateAllAsync(call.Arguments);
                return method.Call(this, args);
            }
        }

        // Handle built-in static methods: Object.keys(), Array.isArray(), JSON.parse(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable nsVar)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (method != null)
            {
                List<object?> args = await ctx.EvaluateAllAsync(call.Arguments);
                return method.Call(this, args);
            }
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            if (call.Arguments.Count >= 2)
            {
                var source = await ctx.EvaluateExprAsync(call.Arguments[0]);
                var excludeKeys = await ctx.EvaluateExprAsync(call.Arguments[1]) as SharpTSArray;
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
                var arg = await ctx.EvaluateExprAsync(call.Arguments[0]);
                description = arg?.ToString();
            }
            return new SharpTSSymbol(description);
        }

        // Handle BigInt() constructor - converts number/string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException(" BigInt() requires exactly one argument.");

            var arg = await ctx.EvaluateExprAsync(call.Arguments[0]);
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

        // Handle Error() and error subtypes called without 'new' - still creates an error object
        if (call.Callee is Expr.Variable errorVar && IsErrorType(errorVar.Name.Lexeme))
        {
            List<object?> args = await ctx.EvaluateAllAsync(call.Arguments);
            return ErrorBuiltIns.CreateError(errorVar.Name.Lexeme, args);
        }

        // Handle global parseInt()
        if (call.Callee is Expr.Variable parseIntVar && parseIntVar.Name.Lexeme == "parseInt")
        {
            if (call.Arguments.Count < 1)
                throw new InterpreterException(" parseInt() requires at least one argument.");
            var str = (await ctx.EvaluateExprAsync(call.Arguments[0]))?.ToString() ?? "";
            int radix = 10;
            if (call.Arguments.Count > 1)
            {
                var radixValue = await ctx.EvaluateExprAsync(call.Arguments[1]);
                if (radixValue != null)
                    radix = (int)(double)radixValue;
            }
            return NumberBuiltIns.ParseInt(str, radix);
        }

        // Handle global parseFloat()
        if (call.Callee is Expr.Variable parseFloatVar && parseFloatVar.Name.Lexeme == "parseFloat")
        {
            if (call.Arguments.Count < 1)
                throw new InterpreterException(" parseFloat() requires at least one argument.");
            var str = (await ctx.EvaluateExprAsync(call.Arguments[0]))?.ToString() ?? "";
            return NumberBuiltIns.ParseFloat(str);
        }

        // Handle global isNaN()
        if (call.Callee is Expr.Variable isNaNVar && isNaNVar.Name.Lexeme == "isNaN")
        {
            if (call.Arguments.Count < 1) return true; // isNaN() with no args returns true
            var arg = await ctx.EvaluateExprAsync(call.Arguments[0]);
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
            var arg = await ctx.EvaluateExprAsync(call.Arguments[0]);
            // Global isFinite coerces to number first (different from Number.isFinite)
            if (arg is double d) return double.IsFinite(d);
            if (arg is string s && double.TryParse(s, out double parsed)) return double.IsFinite(parsed);
            if (arg is null) return true; // null coerces to 0 which is finite
            if (arg is bool) return true; // true=1, false=0, both finite
            return false;
        }

        // Handle setTimeout(callback, delay?, ...args)
        if (call.Callee is Expr.Variable setTimeoutVar && setTimeoutVar.Name.Lexeme == "setTimeout")
        {
            if (call.Arguments.Count < 1)
                throw new InterpreterException(" setTimeout() requires at least one argument (callback).");

            var callbackValue = await ctx.EvaluateExprAsync(call.Arguments[0]);
            if (callbackValue is not ISharpTSCallable callback)
                throw new InterpreterException(" setTimeout() callback must be a function.");

            // Get delay (defaults to 0)
            double delayMs = 0;
            if (call.Arguments.Count >= 2)
            {
                var delayValue = await ctx.EvaluateExprAsync(call.Arguments[1]);
                if (delayValue is double dv)
                    delayMs = dv;
                else if (delayValue != null && delayValue is not SharpTSUndefined)
                    throw new Exception($"Runtime Error: setTimeout() delay must be a number, got {delayValue.GetType().Name}.");
            }

            // Get additional args for the callback
            List<object?> callbackArgs = [];
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                callbackArgs.Add(await ctx.EvaluateExprAsync(call.Arguments[i]));
            }

            return TimerBuiltIns.SetTimeout(this, callback, delayMs, callbackArgs);
        }

        // Handle clearTimeout(handle?)
        if (call.Callee is Expr.Variable clearTimeoutVar && clearTimeoutVar.Name.Lexeme == "clearTimeout")
        {
            object? handle = null;
            if (call.Arguments.Count > 0)
            {
                handle = await ctx.EvaluateExprAsync(call.Arguments[0]);
            }
            TimerBuiltIns.ClearTimeout(handle);
            return null;
        }

        // Handle setInterval(callback, delay?, ...args)
        if (call.Callee is Expr.Variable setIntervalVar && setIntervalVar.Name.Lexeme == "setInterval")
        {
            if (call.Arguments.Count < 1)
                throw new InterpreterException(" setInterval() requires at least one argument (callback).");

            var callbackValue = await ctx.EvaluateExprAsync(call.Arguments[0]);
            if (callbackValue is not ISharpTSCallable callback)
                throw new InterpreterException(" setInterval() callback must be a function.");

            // Get delay (defaults to 0)
            double delayMs = 0;
            if (call.Arguments.Count >= 2)
            {
                var delayValue = await ctx.EvaluateExprAsync(call.Arguments[1]);
                if (delayValue is double dv)
                    delayMs = dv;
                else if (delayValue != null && delayValue is not SharpTSUndefined)
                    throw new Exception($"Runtime Error: setInterval() delay must be a number, got {delayValue.GetType().Name}.");
            }

            // Get additional args for the callback
            List<object?> callbackArgs = [];
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                callbackArgs.Add(await ctx.EvaluateExprAsync(call.Arguments[i]));
            }

            return TimerBuiltIns.SetInterval(this, callback, delayMs, callbackArgs);
        }

        // Handle clearInterval(handle?)
        if (call.Callee is Expr.Variable clearIntervalVar && clearIntervalVar.Name.Lexeme == "clearInterval")
        {
            object? handle = null;
            if (call.Arguments.Count > 0)
            {
                handle = await ctx.EvaluateExprAsync(call.Arguments[0]);
            }
            TimerBuiltIns.ClearInterval(handle);
            return null;
        }

        object? callee = await ctx.EvaluateExprAsync(call.Callee);

        List<object?> argumentsList = [];
        foreach (Expr argument in call.Arguments)
        {
            if (argument is Expr.Spread spread)
            {
                object? spreadValue = await ctx.EvaluateExprAsync(spread.Expression);
                // Use GetIterableElements to support custom iterables with Symbol.iterator
                argumentsList.AddRange(GetIterableElements(spreadValue));
            }
            else
            {
                argumentsList.Add(await ctx.EvaluateExprAsync(argument));
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
        // Use sync context - ValueTask with sync context completes synchronously
        return EvaluateCallCore(_syncContext, call).GetAwaiter().GetResult();
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
    /// Uses SemanticOperatorResolver for centralized operator dispatch.
    /// </summary>
    private object? EvaluateBinaryOperation(Token op, object? left, object? right)
    {
        // Check for bigint operations first
        var leftBigInt = GetBigIntValue(left);
        var rightBigInt = GetBigIntValue(right);

        if (leftBigInt.HasValue || rightBigInt.HasValue)
        {
            return EvaluateBigIntBinary(op.Type, left, right, leftBigInt, rightBigInt);
        }

        var desc = SemanticOperatorResolver.Resolve(op.Type);

        return desc switch
        {
            OperatorDescriptor.Plus => EvaluatePlus(left, right),
            OperatorDescriptor.Arithmetic => EvaluateArithmetic(op.Type, (double)left!, (double)right!),
            OperatorDescriptor.Power => Math.Pow((double)left!, (double)right!),
            OperatorDescriptor.Comparison => EvaluateComparison(op.Type, (double)left!, (double)right!),
            OperatorDescriptor.Equality eq => EvaluateEquality(left, right, eq.IsStrict, eq.IsNegated),
            OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift =>
                EvaluateBitwise(op.Type, ToInt32(left), ToInt32(right)),
            OperatorDescriptor.UnsignedRightShift => (double)((uint)ToInt32(left) >> (ToInt32(right) & 0x1F)),
            OperatorDescriptor.In => EvaluateIn(left, right),
            OperatorDescriptor.InstanceOf => EvaluateInstanceof(left, right),
            _ => null
        };
    }

    /// <summary>
    /// Evaluates arithmetic operators (-, *, /, %).
    /// </summary>
    private static double EvaluateArithmetic(TokenType op, double left, double right) => op switch
    {
        TokenType.MINUS => left - right,
        TokenType.STAR => left * right,
        TokenType.SLASH => left / right,
        TokenType.PERCENT => left % right,
        _ => throw new Exception($"Unknown arithmetic operator: {op}")
    };

    /// <summary>
    /// Evaluates comparison operators (&lt;, &gt;, &lt;=, &gt;=).
    /// </summary>
    private static bool EvaluateComparison(TokenType op, double left, double right) => op switch
    {
        TokenType.LESS => left < right,
        TokenType.GREATER => left > right,
        TokenType.LESS_EQUAL => left <= right,
        TokenType.GREATER_EQUAL => left >= right,
        _ => throw new Exception($"Unknown comparison operator: {op}")
    };

    /// <summary>
    /// Evaluates equality operators (==, ===, !=, !==).
    /// </summary>
    private bool EvaluateEquality(object? left, object? right, bool isStrict, bool isNegated)
    {
        bool result = isStrict ? IsStrictEqual(left, right) : IsEqual(left, right);
        return isNegated ? !result : result;
    }

    /// <summary>
    /// Evaluates bitwise operators (&amp;, |, ^, &lt;&lt;, &gt;&gt;).
    /// </summary>
    private static double EvaluateBitwise(TokenType op, int left, int right) => op switch
    {
        TokenType.AMPERSAND => left & right,
        TokenType.PIPE => left | right,
        TokenType.CARET => left ^ right,
        TokenType.LESS_LESS => left << (right & 0x1F),
        TokenType.GREATER_GREATER => left >> (right & 0x1F),
        _ => throw new Exception($"Unknown bitwise operator: {op}")
    };

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
        if (BigIntOperatorHelper.IsEqualityOperator(op))
        {
            if (!leftBi.HasValue || !rightBi.HasValue)
            {
                // Mixed types: equality is false, inequality is true
                return op is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;
            }
            return BigIntOperatorHelper.EvaluateBinary(op, leftBi.Value, rightBi.Value);
        }

        // All other operators require both to be bigint
        if (!leftBi.HasValue || !rightBi.HasValue)
            throw new InterpreterException(" Cannot mix bigint and other types in operations.");

        // Use centralized helper for all BigInt operations
        return BigIntOperatorHelper.EvaluateBinary(op, leftBi.Value, rightBi.Value);
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

    // ===================== Async Core Methods =====================

    /// <summary>
    /// Async version of logical operation core logic.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    private async Task<object?> EvaluateLogicalCoreAsync(
        TokenType op,
        Task<object?> leftTask,
        Func<Task<object?>> evaluateRightAsync)
    {
        var left = await leftTask;
        if (op == TokenType.OR_OR)
            return IsTruthy(left) ? left : await evaluateRightAsync();
        return !IsTruthy(left) ? left : await evaluateRightAsync();
    }

    /// <summary>
    /// Async version of nullish coalescing core logic.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    private async Task<object?> EvaluateNullishCoalescingCoreAsync(
        Task<object?> leftTask,
        Func<Task<object?>> evaluateRightAsync)
    {
        var left = await leftTask;
        return (left == null || left is Runtime.Types.SharpTSUndefined)
            ? await evaluateRightAsync()
            : left;
    }

    /// <summary>
    /// Async version of ternary operation core logic.
    /// Uses lazy evaluation via Func delegates to ensure only one branch is evaluated.
    /// </summary>
    private async Task<object?> EvaluateTernaryCoreAsync(
        Task<object?> conditionTask,
        Func<Task<object?>> evalThenAsync,
        Func<Task<object?>> evalElseAsync)
    {
        var condition = await conditionTask;
        return IsTruthy(condition)
            ? await evalThenAsync()
            : await evalElseAsync();
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
            TokenType.PLUS_EQUAL => left is string s ? string.Concat(s, Stringify(right)) : (double)left! + (double)right!,
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

    // ===================== Shared Builder Helpers =====================

    /// <summary>
    /// Builds a SharpTSArray from evaluated elements, handling spread elements.
    /// Shared between sync and async array evaluation paths.
    /// </summary>
    /// <param name="evaluatedElements">Tuples of (isSpread, evaluatedValue).</param>
    /// <returns>A new SharpTSArray containing all elements.</returns>
    private SharpTSArray BuildArrayFromElements(IEnumerable<(bool isSpread, object? value)> evaluatedElements)
    {
        List<object?> elements = [];
        foreach (var (isSpread, value) in evaluatedElements)
        {
            if (isSpread)
            {
                // Use GetIterableElements to support custom iterables with Symbol.iterator
                elements.AddRange(GetIterableElements(value));
            }
            else
            {
                elements.Add(value);
            }
        }
        return new SharpTSArray(elements);
    }

    /// <summary>
    /// Builds a SharpTSObject from evaluated properties.
    /// Shared between sync and async object evaluation paths.
    /// </summary>
    /// <param name="stringFields">String-keyed properties.</param>
    /// <param name="symbolFields">Symbol-keyed properties.</param>
    /// <returns>A new SharpTSObject with all properties set.</returns>
    private static SharpTSObject BuildObjectFromFields(
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?> symbolFields)
    {
        var result = new SharpTSObject(stringFields);
        foreach (var (sym, val) in symbolFields)
        {
            result.SetBySymbol(sym, val);
        }
        return result;
    }

    /// <summary>
    /// Builds a template literal string from strings and evaluated expressions.
    /// Shared between sync and async template literal evaluation paths.
    /// </summary>
    /// <param name="strings">The static string parts of the template.</param>
    /// <param name="evaluatedExprs">The evaluated expression values.</param>
    /// <returns>The interpolated string result.</returns>
    private string BuildTemplateLiteralString(IReadOnlyList<string> strings, IReadOnlyList<object?> evaluatedExprs)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < strings.Count; i++)
        {
            result.Append(strings[i]);
            if (i < evaluatedExprs.Count)
            {
                result.Append(Stringify(evaluatedExprs[i]));
            }
        }
        return result.ToString();
    }
}
