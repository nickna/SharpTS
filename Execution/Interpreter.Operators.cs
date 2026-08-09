using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using DotNet = SharpTS.Runtime.DotNet;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    /// <summary>
    /// Evaluates a compound assignment expression on a variable (e.g., <c>x += 1</c>).
    /// </summary>
    /// <param name="compound">The compound assignment expression AST node.</param>
    /// <returns>The new value after the operation.</returns>
    /// <remarks>
    /// Retrieves the current value, applies the compound operator via
    /// <see cref="ApplyCompoundOperatorRV"/>, and stores the result.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition_assignment">MDN Compound Assignment</seealso>
    private RuntimeValue EvaluateCompoundAssign(Expr.CompoundAssign compound)
        => EvaluateCompoundAssignCore(_syncContext, compound).GetAwaiter().GetResult();

    /// <summary>
    /// Core compound-assignment logic shared by the sync and async evaluators; the evaluation
    /// context supplies the operand-evaluation strategy so a single body serves both paths.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateCompoundAssignCore(IEvaluationContext ctx, Expr.CompoundAssign compound)
    {
        RuntimeValue currentValue = _environment.Get(compound.Name);
        RuntimeValue addValue = await ctx.EvaluateExprAsync(compound.Value);
        RuntimeValue newValue = ApplyCompoundOperatorRV(compound.Operator.Type, currentValue, addValue);
        _environment.Assign(compound.Name, newValue);
        return newValue;
    }

    /// <summary>
    /// Evaluates a compound assignment expression on an object property (e.g., <c>obj.x += 1</c>).
    /// </summary>
    /// <param name="compound">The compound property assignment expression AST node.</param>
    /// <returns>The new value after the operation.</returns>
    /// <remarks>
    /// Works with both class instances (<see cref="SharpTSInstance"/>) and
    /// plain objects (<see cref="SharpTSObject"/>).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition_assignment">MDN Compound Assignment</seealso>
    private RuntimeValue EvaluateCompoundSet(Expr.CompoundSet compound)
        => EvaluateCompoundSetCore(_syncContext, compound).GetAwaiter().GetResult();

    /// <summary>
    /// Core property compound-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateCompoundSetCore(IEvaluationContext ctx, Expr.CompoundSet compound)
    {
        object? obj = (await ctx.EvaluateExprAsync(compound.Object)).ToObject();

        // ECMA-262: a compound assignment reads first (GetValue), so a nullish base
        // throws the *read*-worded guest TypeError before any operation (#733).
        if (obj is null or SharpTSUndefined)
        {
            ThrowCannotReadProperty(obj, compound.Name.Lexeme);
        }

        if (TryGetPropertyRV(obj, compound.Name, out RuntimeValue currentRV))
        {
            RuntimeValue addValue = await ctx.EvaluateExprAsync(compound.Value);
            RuntimeValue newValue = ApplyCompoundOperatorRV(compound.Operator.Type, currentRV, addValue);
            if (TrySetProperty(obj, compound.Name, newValue.ToObject()))
            {
                return newValue;
            }
        }

        throw new InterpreterException($"Only instances and objects have fields. Cannot compound-set '{compound.Name.Lexeme}' on {obj?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Evaluates a compound assignment expression on an array element (e.g., <c>arr[i] += 1</c>).
    /// </summary>
    /// <param name="compound">The compound index assignment expression AST node.</param>
    /// <returns>The new value after the operation.</returns>
    /// <remarks>
    /// Currently only supports array element compound assignment.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition_assignment">MDN Compound Assignment</seealso>
    private RuntimeValue EvaluateCompoundSetIndex(Expr.CompoundSetIndex compound)
        => EvaluateCompoundSetIndexCore(_syncContext, compound).GetAwaiter().GetResult();

    /// <summary>
    /// Core indexed compound-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateCompoundSetIndexCore(IEvaluationContext ctx, Expr.CompoundSetIndex compound)
    {
        object? obj = (await ctx.EvaluateExprAsync(compound.Object)).ToObject();
        object? index = (await ctx.EvaluateExprAsync(compound.Index)).ToObject();

        // Nullish base reads first → *read*-worded guest TypeError (#733).
        if (obj is null or SharpTSUndefined)
        {
            ThrowCannotReadProperty(obj, index?.ToString() ?? "");
        }

        if (obj is SharpTSArray array && index is double idx)
        {
            RuntimeValue addValue = await ctx.EvaluateExprAsync(compound.Value);
            RuntimeValue newValue = ApplyCompoundOperatorRV(compound.Operator.Type, array.GetRV((int)idx), addValue);
            array.Set((int)idx, newValue.ToObject());
            return newValue;
        }

        // Typed-array element compound assignment (e.g. `a[i] += 1` on an Int32Array). Read the
        // element (a boxed double, or BigInteger for BigInt arrays), apply the op, narrow back via
        // the typed-array setter. The compiled side has a dedicated unboxed fast path; this keeps the
        // interpreter consistent (previously it threw "not supported on this type").
        if (obj is SharpTSTypedArray typedArray && index is double typedIdx)
        {
            int ti = (int)typedIdx;
            RuntimeValue addValue = await ctx.EvaluateExprAsync(compound.Value);
            RuntimeValue newValue = ApplyCompoundOperatorRV(
                compound.Operator.Type, RuntimeValue.FromBoxed(typedArray[ti]), addValue);
            typedArray[ti] = newValue.ToObject();
            return newValue;
        }

        if (obj is DotNet.DotNetInstance external && external.HasReadableIndexer)
        {
            RuntimeValue current = RuntimeValue.FromBoxed(external.GetIndex(index, this));
            RuntimeValue addValue = await ctx.EvaluateExprAsync(compound.Value);
            RuntimeValue newValue = ApplyCompoundOperatorRV(
                compound.Operator.Type, current, addValue);
            external.SetIndex(index, newValue.ToObject(), this);
            return newValue;
        }

        throw new InterpreterException("Compound index assignment not supported on this type.");
    }

    /// <summary>
    /// Evaluates a logical assignment expression on a variable (e.g., <c>x &&= y</c>, <c>x ||= y</c>, <c>x ??= y</c>).
    /// </summary>
    /// <param name="logical">The logical assignment expression AST node.</param>
    /// <returns>The result of the logical assignment (the final value of x).</returns>
    /// <remarks>
    /// Unlike compound assignment, logical assignment has short-circuit semantics:
    /// - <c>x &&= y</c>: Only assigns y to x if x is truthy
    /// - <c>x ||= y</c>: Only assigns y to x if x is falsy
    /// - <c>x ??= y</c>: Only assigns y to x if x is null/undefined
    /// </remarks>
    private RuntimeValue EvaluateLogicalAssign(Expr.LogicalAssign logical)
        => EvaluateLogicalAssignCore(_syncContext, logical).GetAwaiter().GetResult();

    /// <summary>
    /// Core variable logical-assignment logic shared by the sync and async evaluators.
    /// Short-circuits before evaluating the right-hand side per ECMA-262.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateLogicalAssignCore(IEvaluationContext ctx, Expr.LogicalAssign logical)
    {
        RuntimeValue currentValue = _environment.Get(logical.Name);

        switch (logical.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                if (!currentValue.IsTruthy()) return currentValue;
                break;
            case TokenType.OR_OR_EQUAL:
                if (currentValue.IsTruthy()) return currentValue;
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                if (!currentValue.IsNullish) return currentValue;
                break;
        }

        // Short-circuit condition not met, evaluate and assign
        RuntimeValue newValue = await ctx.EvaluateExprAsync(logical.Value);
        _environment.Assign(logical.Name, newValue);
        return newValue;
    }

    /// <summary>
    /// Evaluates a logical assignment expression on an object property (e.g., <c>obj.x &&= y</c>).
    /// </summary>
    private RuntimeValue EvaluateLogicalSet(Expr.LogicalSet logical)
        => EvaluateLogicalSetCore(_syncContext, logical).GetAwaiter().GetResult();

    /// <summary>
    /// Core property logical-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateLogicalSetCore(IEvaluationContext ctx, Expr.LogicalSet logical)
    {
        object? obj = (await ctx.EvaluateExprAsync(logical.Object)).ToObject();

        // Logical assignment reads first → nullish base throws the *read*-worded
        // guest TypeError before the short-circuit check (#733).
        if (obj is null or SharpTSUndefined)
        {
            ThrowCannotReadProperty(obj, logical.Name.Lexeme);
        }

        if (!TryGetPropertyRV(obj, logical.Name, out RuntimeValue currentRV))
        {
            throw new InterpreterException($"Only instances and objects have fields. Cannot logical-get '{logical.Name.Lexeme}' on {obj?.GetType().Name ?? "null"}.");
        }

        switch (logical.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                if (!currentRV.IsTruthy()) return currentRV;
                break;
            case TokenType.OR_OR_EQUAL:
                if (currentRV.IsTruthy()) return currentRV;
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                if (!currentRV.IsNullish) return currentRV;
                break;
        }

        // Short-circuit condition not met, evaluate and assign
        RuntimeValue newValue = await ctx.EvaluateExprAsync(logical.Value);
        if (!TrySetProperty(obj, logical.Name, newValue.ToObject()))
        {
            throw new InterpreterException($"Only instances and objects have fields. Cannot logical-set '{logical.Name.Lexeme}' on {obj?.GetType().Name ?? "null"}.");
        }
        return newValue;
    }

    /// <summary>
    /// Evaluates a logical assignment expression on an array element (e.g., <c>arr[i] &&= y</c>).
    /// </summary>
    private RuntimeValue EvaluateLogicalSetIndex(Expr.LogicalSetIndex logical)
        => EvaluateLogicalSetIndexCore(_syncContext, logical).GetAwaiter().GetResult();

    /// <summary>
    /// Core indexed logical-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateLogicalSetIndexCore(IEvaluationContext ctx, Expr.LogicalSetIndex logical)
    {
        object? obj = (await ctx.EvaluateExprAsync(logical.Object)).ToObject();
        object? index = (await ctx.EvaluateExprAsync(logical.Index)).ToObject();

        // Nullish base reads first → *read*-worded guest TypeError (#733).
        if (obj is null or SharpTSUndefined)
        {
            ThrowCannotReadProperty(obj, index?.ToString() ?? "");
        }

        if (obj is SharpTSArray array && index is double idx)
        {
            RuntimeValue currentRV = array.GetRV((int)idx);

            switch (logical.Operator.Type)
            {
                case TokenType.AND_AND_EQUAL:
                    if (!currentRV.IsTruthy()) return currentRV;
                    break;
                case TokenType.OR_OR_EQUAL:
                    if (currentRV.IsTruthy()) return currentRV;
                    break;
                case TokenType.QUESTION_QUESTION_EQUAL:
                    if (!currentRV.IsNullish) return currentRV;
                    break;
            }

            // Short-circuit condition not met, evaluate and assign
            RuntimeValue newValue = await ctx.EvaluateExprAsync(logical.Value);
            array.Set((int)idx, newValue.ToObject());
            return newValue;
        }

        throw new InterpreterException("Logical index assignment not supported on this type.");
    }

    /// <summary>
    /// Evaluates a prefix increment or decrement expression (<c>++x</c> or <c>--x</c>).
    /// </summary>
    /// <param name="prefix">The prefix increment expression AST node.</param>
    /// <returns>The new value after incrementing/decrementing.</returns>
    /// <remarks>
    /// Prefix operators modify the value and return the new value.
    /// Supports variables, property access, and index access as operands.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Increment">MDN Increment Operator</seealso>
    private RuntimeValue EvaluatePrefixIncrement(Expr.PrefixIncrement prefix)
    {
        double delta = prefix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;
        return EvaluateIncrement(prefix.Operand, delta, returnOld: false);
    }

    /// <summary>
    /// Evaluates a postfix increment or decrement expression (<c>x++</c> or <c>x--</c>).
    /// </summary>
    /// <param name="postfix">The postfix increment expression AST node.</param>
    /// <returns>The original value before incrementing/decrementing.</returns>
    /// <remarks>
    /// Postfix operators modify the value but return the old value.
    /// Supports variables, property access, and index access as operands.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Increment">MDN Increment Operator</seealso>
    private RuntimeValue EvaluatePostfixIncrement(Expr.PostfixIncrement postfix)
    {
        double delta = postfix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;
        return EvaluateIncrement(postfix.Operand, delta, returnOld: true);
    }

    /// <summary>
    /// Async counterpart of <see cref="EvaluatePrefixIncrement"/>; resolves an
    /// <c>await</c>/thenable in the operand's receiver or index (issue #451).
    /// </summary>
    private Task<RuntimeValue> EvaluatePrefixIncrementAsync(Expr.PrefixIncrement prefix)
    {
        double delta = prefix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;
        return EvaluateIncrementAsync(prefix.Operand, delta, returnOld: false);
    }

    /// <summary>
    /// Async counterpart of <see cref="EvaluatePostfixIncrement"/>; resolves an
    /// <c>await</c>/thenable in the operand's receiver or index (issue #451).
    /// </summary>
    private Task<RuntimeValue> EvaluatePostfixIncrementAsync(Expr.PostfixIncrement postfix)
    {
        double delta = postfix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;
        return EvaluateIncrementAsync(postfix.Operand, delta, returnOld: true);
    }

    /// <summary>
    /// Evaluates the plus operator, handling both addition and string concatenation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum if both are numbers; otherwise the concatenated string.</returns>
    /// <remarks>
    /// If either operand is a string, performs string concatenation.
    /// Otherwise performs numeric addition.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition">MDN Addition Operator</seealso>
    private object EvaluatePlus(object? left, object? right)
    {
        // ECMA-262 §13.15.3 ApplyStringOrNumericBinaryOperator: ToPrimitive both
        // operands (default hint) before deciding string-concat vs numeric add.
        // Reduces boxed Number/String/Boolean wrappers (and objects with an own
        // valueOf/toString) to their primitive so `"x" + new Number(5)` → "x5"
        // and `new Number(5) + 1` → 6 (#708). No-op for other values.
        left = ToPrimitive(left, PrimitiveHint.Default);
        right = ToPrimitive(right, PrimitiveHint.Default);
        if (left is double l && right is double r) return l + r;
        if (left is string || right is string)
        {
            // ECMA-262 §7.1.17: a Symbol operand cannot be coerced to a string.
            ThrowIfSymbolStringCoercion(left);
            ThrowIfSymbolStringCoercion(right);
            // Avoid calling Stringify on values that are already strings
            return string.Concat(
                left as string ?? Stringify(left),
                right as string ?? Stringify(right));
        }
        // ECMA-262 §13.15.3: with an object operand, ToPrimitive (default hint)
        // yields a string for arrays/plain objects, so `+` concatenates.
        if (IsObjectLike(left) || IsObjectLike(right))
        {
            ThrowIfSymbolStringCoercion(left);
            ThrowIfSymbolStringCoercion(right);
            return string.Concat(Stringify(left), Stringify(right));
        }
        // Both primitives, neither a string: numeric addition with ToNumber
        // coercion (undefined→NaN, null→0, booleans→0/1) per #190.
        return CoerceToNumber(left) + CoerceToNumber(right);
    }

    /// <summary>
    /// True for guest object values — anything that is not a JS primitive
    /// (number, string, boolean, null, undefined, bigint, symbol).
    /// </summary>
    private static bool IsObjectLike(object? value) =>
        value is not (null or double or string or bool or SharpTSUndefined or SharpTSBigInt or SharpTSSymbol);

    /// <summary>
    /// ECMA-262 §7.1.17 ToString: Symbol values throw a TypeError when
    /// implicitly coerced to a string (template-literal interpolation,
    /// <c>+</c> concatenation). Only the explicit <c>String()</c> call form and
    /// <c>Symbol.prototype.toString()</c> are exempt — those route through
    /// dedicated paths, not this guard. <c>console.log</c> formatting also
    /// bypasses this (it is not a language-level coercion).
    /// </summary>
    internal static void ThrowIfSymbolStringCoercion(object? value)
    {
        if (value is SharpTSSymbol)
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert a Symbol value to a string"));
    }

    /// <summary>
    /// Evaluates a unary operator expression.
    /// </summary>
    /// <param name="unary">The unary expression AST node.</param>
    /// <returns>The result of applying the unary operator.</returns>
    /// <remarks>
    /// Supports logical NOT (<c>!</c>), numeric negation (<c>-</c>),
    /// typeof operator, and bitwise NOT (<c>~</c>).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/typeof">MDN typeof</seealso>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Logical_NOT">MDN Logical NOT</seealso>
    private RuntimeValue EvaluateUnary(Expr.Unary unary)
    {
        // typeof never throws on undeclared variables - it returns "undefined"
        if (unary.Operator.Type == TokenType.TYPEOF && unary.Right is Expr.Variable)
        {
            RuntimeValue right;
            try { right = EvaluateRV(unary.Right); }
            catch (InterpreterException) { right = RuntimeValue.Undefined; }
            return RuntimeValue.FromString(right.TypeofString());
        }

        // Fast path for common unary operations on RuntimeValue
        var rv = EvaluateRV(unary.Right);
        return EvaluateUnaryOperationRV(unary.Operator, rv);
    }

    /// <summary>
    /// RuntimeValue-native unary operation — avoids boxing for all cases including BigInt.
    /// </summary>
    private RuntimeValue EvaluateUnaryOperationRV(Token op, RuntimeValue rv)
    {
        if (rv.ToObject() is DotNet.DotNetInstance instance)
        {
            var methods = DotNet.DotNetOperatorResolver.GetUnaryCandidates(
                op.Type, instance.Type);
            if (methods.Length > 0)
            {
                var arguments = new List<object?> { instance };
                var candidate = DotNet.DotNetMethodResolver.ResolveMethod(
                    methods, arguments);
                var method = (System.Reflection.MethodInfo)candidate.Method;
                var invokeArgs = DotNet.DotNetMethod.BuildInvokeArgs(
                    method.GetParameters(), arguments, candidate, this);
                object? value = DotNet.DotNetInstance.InvokeWithMapping(
                    () => method.Invoke(null, invokeArgs));
                return RuntimeValue.FromBoxed(
                    DotNet.DotNetMarshaller.WrapReturn(value, method.ReturnType));
            }
        }

        switch (op.Type)
        {
            case TokenType.BANG:
                return RuntimeValue.FromBoolean(!rv.IsTruthy());
            case TokenType.PLUS when rv.IsNumber:
                return rv;
            case TokenType.PLUS when rv.IsBigInt:
                // Unary `+` on BigInt throws TypeError in JS.
                throw new InterpreterException("TypeError: Cannot convert a BigInt value to a number");
            case TokenType.PLUS:
                return RuntimeValue.FromNumber(CoerceToNumber(rv));
            case TokenType.MINUS when rv.IsNumber:
                return RuntimeValue.FromNumber(-rv.AsNumber());
            case TokenType.MINUS when rv.IsBigInt:
                return RuntimeValue.FromBigInt(new SharpTSBigInt(-rv.AsBigInt().Value));
            case TokenType.MINUS:
                return RuntimeValue.FromNumber(-CoerceToNumber(rv));
            case TokenType.TYPEOF:
                return RuntimeValue.FromString(rv.TypeofString());
            case TokenType.VOID:
                return RuntimeValue.Undefined;
            case TokenType.TILDE when rv.IsNumber:
                return RuntimeValue.FromNumber(~(int)rv.AsNumber());
            case TokenType.TILDE when rv.IsBigInt:
                return RuntimeValue.FromBigInt(new SharpTSBigInt(~rv.AsBigInt().Value));
            case TokenType.TILDE:
                return RuntimeValue.FromNumber(~(int)CoerceToNumber(rv));
            default:
                return RuntimeValue.Undefined;
        }
    }

    /// <summary>
    /// JS ToNumber coercion for a RuntimeValue. Mirrors the ECMAScript
    /// abstract operation: null→0, undefined→NaN, true→1, false→0,
    /// strings parsed (empty/whitespace→0, otherwise NaN on parse failure).
    /// Numbers pass through unchanged.
    /// </summary>
    /// <summary>
    /// Boxed-value overload of <see cref="CoerceToNumber(RuntimeValue)"/> for the
    /// legacy object?-based slow paths.
    /// </summary>
    private static double CoerceToNumber(object? value) => CoerceToNumber(RuntimeValue.FromBoxed(value));

    /// <summary>
    /// JS ToNumber for a boxed value, unwrapping a boxed Number/String/Boolean
    /// wrapper to its primitive first (#708). Exposed for numeric built-ins
    /// (e.g. <c>String.fromCharCode</c>) that otherwise hard-crash on a wrapper
    /// argument via <see cref="RuntimeValue.AsNumber"/>.
    /// </summary>
    internal static double ToNumber(object? value) => CoerceToNumber(RuntimeValue.FromBoxed(value));

    /// <summary>
    /// JS ToNumber for an unboxed <see cref="RuntimeValue"/>. Built-ins reading a
    /// guest-supplied numeric argument must go through this rather than
    /// <see cref="RuntimeValue.AsNumber"/>, which is a kind *assertion*: it hard-fails on
    /// the string / boolean / wrapper arguments ECMA-262 requires to be coerced
    /// (<c>Math.abs("-5")</c> is 5, not a crash).
    /// </summary>
    internal static double ToNumber(RuntimeValue value) => CoerceToNumber(value);

    private static double CoerceToNumber(RuntimeValue rv)
    {
        if (rv.IsNumber) return rv.AsNumber();
        if (rv.IsBoolean) return rv.AsBoolean() ? 1.0 : 0.0;
        // ECMA-262 §7.1.4 ToNumber: a Symbol argument is a TypeError, not NaN.
        if (rv.Kind == ValueKind.Symbol)
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert a Symbol value to a number"));
        if (rv.Kind == ValueKind.BigInt)
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert a BigInt value to a number"));
        if (rv.Kind == ValueKind.Null) return 0.0;
        if (rv.Kind == ValueKind.Undefined) return double.NaN;
        if (rv.IsString)
        {
            var s = rv.AsString().Trim();
            if (s.Length == 0) return 0.0;
            if (s.Length >= 2 && s[0] == '0')
            {
                int radix = s[1] switch
                {
                    'x' or 'X' => 16,
                    'b' or 'B' => 2,
                    'o' or 'O' => 8,
                    _ => 0,
                };
                if (radix != 0)
                    return TryParseUnsignedInteger(s.AsSpan(2), radix, out var integer)
                        ? integer
                        : double.NaN;
            }
            if (s is "Infinity" or "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("+Infinity", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
                return double.NaN;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            return double.NaN;
        }
        // ECMA-262 ToNumber on an object goes through ToPrimitive (number hint).
        // For a boxed Number/String/Boolean wrapper that reduces to its
        // [[PrimitiveValue]]; without this `new Number(5) * 2`, comparisons, and
        // bitwise ops on wrappers coerce to NaN (#708). A user-overridden valueOf
        // is honored on the instance ToPrimitive paths (+, ==, templates, JSON).
        if (rv.Kind == ValueKind.Object && TryGetBoxedPrimitiveValue(rv.ToObject(), out var prim))
            return CoerceToNumber(RuntimeValue.FromBoxed(prim));
        return double.NaN;
    }

    private static bool TryParseUnsignedInteger(
        ReadOnlySpan<char> digits, int radix, out double value)
    {
        value = 0;
        if (digits.IsEmpty) return false;

        foreach (char c in digits)
        {
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= radix) return false;
            value = value * radix + digit;
        }
        return true;
    }

    /// <summary>
    /// ToNumber including the object-to-primitive step. Array length
    /// descriptors use this before validating the uint32 result.
    /// </summary>
    internal double ToNumberWithPrimitive(object? value)
    {
        object? primitive = value is SharpTSObject obj
            ? OrdinaryToPrimitiveNumber(obj)
            : ToPrimitive(value, PrimitiveHint.Number);
        return CoerceToNumber(RuntimeValue.FromBoxed(primitive));
    }

    /// <summary>
    /// OrdinaryToPrimitive with a number hint for plain objects. Unlike the
    /// compatibility-oriented general ToPrimitive path below, this performs
    /// full prototype-chain Get for valueOf/toString and throws when neither
    /// callable returns a primitive, as ArraySetLength requires.
    /// </summary>
    private object? OrdinaryToPrimitiveNumber(SharpTSObject obj)
    {
        if (TryCallExoticToPrimitive(obj, PrimitiveHint.Number, out var exoticResult))
            return exoticResult;

        // Boxed primitives retain the dedicated internal-slot behavior used by
        // the wider coercion paths.
        if (TryGetBoxedPrimitiveValue(obj, out _))
            return ToPrimitive(obj, PrimitiveHint.Number);

        if (TryCallConversion(obj, "valueOf", out var valueOfResult))
            return valueOfResult;
        if (TryCallConversion(obj, "toString", out var toStringResult))
            return toStringResult;

        throw new ThrowException(new SharpTSTypeError(
            "Cannot convert object to primitive value"));
    }

    private bool TryCallConversion(SharpTSObject receiver, string name, out object? result)
    {
        result = null;
        if (GetProperty(receiver, name) is not ISharpTSCallable callable)
            return false;

        var converted = FunctionBuiltIns.CallWithThis(this, callable, receiver, []);
        if (IsObjectLike(converted))
            return false;

        result = converted;
        return true;
    }

    private enum PrimitiveHint { Default, Number, String }

    /// <summary>
    /// True when <paramref name="value"/> is a boxed <c>new Number/String/Boolean</c>
    /// wrapper (carries a <c>__primitiveType</c> tag); yields its <c>__primitiveValue</c>.
    /// </summary>
    internal static bool TryGetBoxedPrimitiveValue(object? value, out object? primitive)
    {
        primitive = null;
        if (value is SharpTSObject obj
            && obj.GetProperty("__primitiveType") is string pt
            && pt is "Number" or "String" or "Boolean" or "BigInt"
            && obj.HasProperty("__primitiveValue"))
        {
            primitive = obj.GetProperty("__primitiveValue");
            return true;
        }
        return false;
    }

    /// <summary>
    /// ECMA-262 §7.1.1 ToPrimitive / OrdinaryToPrimitive for the cases the
    /// interpreter must coerce explicitly: boxed primitive wrappers and plain
    /// objects carrying an own <c>valueOf</c>/<c>toString</c>. Calls the own
    /// conversion methods in hint order (honoring a user override — #574), then
    /// falls back to a wrapper's <c>__primitiveValue</c> (#708). Every other
    /// value (already-primitive, array, class instance, plain object without an
    /// own conversion) is returned unchanged so existing behavior is preserved.
    /// </summary>
    private object? ToPrimitive(object? value, PrimitiveHint hint)
    {
        // Number.prototype is itself a Number object with [[NumberData]] +0.
        if (value is SharpTSNumberPrototype) return 0.0;
        // String.prototype is itself a String object with [[StringData]] "".
        if (value is SharpTSStringPrototype) return "";
        // Boolean.prototype is itself a Boolean object with [[BooleanData]] false.
        if (value is SharpTSBooleanPrototype) return false;
        // Arrays have no own valueOf/toString and inherit Array.prototype.toString
        // (= join(',')); OrdinaryToPrimitive resolves to it for every hint (valueOf
        // returns the array, not a primitive, so it is skipped). e.g. `'' + [1,2,3]`
        // -> "1,2,3" and `[1,2,3] == "1,2,3"` -> true. The console/debug ToString
        // ("[1, 2, 3]") is intentionally NOT used here.
        if (value is SharpTSArguments) return "[object Arguments]";
        if (value is SharpTSArray arr)
        {
            // Array.prototype conversion methods are mutable. Keep the direct
            // join fast path for the ordinary realm, but perform the full
            // lookup when either method has been overridden.
            var arrayPrototype = GetArrayPrototype();
            if (arrayPrototype.HasExtra("toString") || arrayPrototype.HasExtra("valueOf"))
                return OrdinaryToPrimitiveObject(arr, hint);
            return ArrayBuiltIns.ToJsString(this, arr);
        }
        if (value is SharpTSGlobalThis)
            return OrdinaryToPrimitiveObject(value, hint);
        if (value is ISharpTSCallable)
            return OrdinaryToPrimitiveObject(value, hint);
        if (value is not SharpTSObject obj) return value;
        if (TryCallExoticToPrimitive(obj, hint, out var exoticResult))
            return exoticResult;
        bool isWrapper = TryGetBoxedPrimitiveValue(obj, out var primitiveValue);

        string first = hint == PrimitiveHint.String ? "toString" : "valueOf";
        string second = hint == PrimitiveHint.String ? "valueOf" : "toString";
        if (!isWrapper)
        {
            if (TryCallConversion(obj, first, out var firstResult))
                return firstResult;
            if (TryCallConversion(obj, second, out var secondResult))
                return secondResult;
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert object to primitive value"));
        }

        // An own override of the hint-preferred conversion always wins.
        if (TryCallOwnConversion(obj, first, out var r1)) return r1;
        // For a boxed wrapper, the *inherited* preferred conversion
        // (Number/String/Boolean.prototype.toString|valueOf) returns the
        // [[PrimitiveValue]] and short-circuits before the second method is
        // tried — so an own override of the *other* method must not leak into a
        // string→valueOf or number→toString coercion (#574). Use the primitive
        // directly. A plain object (no internal slot) keeps the two-step
        // OrdinaryToPrimitive fallback.
        if (isWrapper) return primitiveValue;
        if (TryCallOwnConversion(obj, second, out var r2)) return r2;
        return primitiveValue;
    }

    private object? OrdinaryToPrimitiveObject(object receiver, PrimitiveHint hint)
    {
        string first = hint == PrimitiveHint.String ? "toString" : "valueOf";
        string second = hint == PrimitiveHint.String ? "valueOf" : "toString";
        foreach (string name in new[] { first, second })
        {
            if (GetPropertyValue(receiver, name) is not ISharpTSCallable callable)
                continue;
            var result = FunctionBuiltIns.CallWithThis(this, callable, receiver, []);
            if (!IsObjectLike(result)) return result;
        }
        throw new ThrowException(new SharpTSTypeError(
            "Cannot convert object to primitive value"));
    }

    /// <summary>
    /// Invokes an own <c>valueOf</c>/<c>toString</c> on <paramref name="obj"/> bound
    /// to it; succeeds only when the result is a primitive (per OrdinaryToPrimitive,
    /// an object result is skipped so the next method is tried).
    /// </summary>
    private bool TryCallOwnConversion(SharpTSObject obj, string name, out object? result)
    {
        result = null;
        if (!obj.HasProperty(name)) return false;
        var fn = obj.GetProperty(name);
        if (fn is SharpTSArrowFunction af && af.HasOwnThis) fn = af.Bind(obj);
        if (fn is not ISharpTSCallable callable) return false;
        // Zero-arg span, not a shared List: a callee mutating its arguments must
        // not corrupt other calls (the old shared static list was handed to
        // arbitrary guest callables).
        var r = callable.CallV2(this, ReadOnlySpan<RuntimeValue>.Empty).ToObject();
        if (IsObjectLike(r)) return false;
        result = r;
        return true;
    }

    /// <summary>
    /// String-coerces a class instance by resolving and invoking its
    /// <c>toString</c> through the class chain (e.g. Error.prototype.toString or
    /// a user override). Succeeds only when a callable <c>toString</c> resolves
    /// and returns a primitive — otherwise the caller falls back to the
    /// <c>[object &lt;Class&gt;]</c> brand form. Used by <see cref="Stringify"/> so
    /// templates / <c>+</c> / <c>String()</c> on an instance match Node.
    /// </summary>
    private bool TryStringifyInstanceViaToString(SharpTSInstance instance, out string result)
    {
        result = "";
        var resolved = instance.Get(new Token(TokenType.IDENTIFIER, "toString", null, 0));
        if (resolved is not ISharpTSCallable toString) return false;
        var coerced = toString.CallV2(this, ReadOnlySpan<RuntimeValue>.Empty).ToObject();
        if (IsObjectLike(coerced)) return false;
        result = coerced as string ?? Stringify(coerced);
        return true;
    }

    /// <summary>
    /// ECMA-262 §25.5.2.2 SerializeJSONProperty step 4: coerce a boxed
    /// Number/String/Boolean/BigInt wrapper to the primitive JSON serializes. Number→
    /// ToNumber, String→ToString (both via <see cref="ToPrimitive"/>, so a user
    /// override of valueOf/toString is honored — #574); Boolean→its
    /// [[BooleanData]] (no coercion per spec). Returns false for any non-wrapper.
    /// </summary>
    internal bool TryCoerceBoxedPrimitiveForJson(object? value, out object? primitive)
    {
        primitive = null;
        if (value is not SharpTSObject obj
            || obj.GetProperty("__primitiveType") is not string tag)
            return false;
        switch (tag)
        {
            case "Number":
                primitive = CoerceToNumber(RuntimeValue.FromBoxed(ToPrimitive(obj, PrimitiveHint.Number)));
                return true;
            case "String":
                var sp = ToPrimitive(obj, PrimitiveHint.String);
                primitive = sp as string ?? Stringify(sp);
                return true;
            case "Boolean":
                primitive = obj.GetProperty("__primitiveValue");
                return true;
            case "BigInt":
                primitive = obj.GetProperty("__primitiveValue");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// ECMA-262 ToString abstract operation for an object argument, used by the
    /// <c>String(value)</c> call form: ToPrimitive(value, "string") then the
    /// string form — so an own <c>toString</c> override on a boxed wrapper is
    /// honored and a bare wrapper yields its primitive's natural string
    /// (#574 / boxed-wrapper string coercion). Primitives pass straight to
    /// <see cref="Stringify"/>.
    /// </summary>
    internal string ToStringForStringCall(object? value)
        => Stringify(IsObjectLike(value) ? ToPrimitive(value, PrimitiveHint.String) : value);

    /// <summary>
    /// ECMA-262 ToString for built-in algorithm arguments. Unlike the
    /// compatibility-oriented String() call path, this performs the complete
    /// OrdinaryToPrimitive fallback and throws when both conversion methods
    /// return objects.
    /// </summary>
    internal string ToStringForBuiltInArgument(object? value)
    {
        ThrowIfSymbolStringCoercion(value);
        if (value is SharpTSArguments)
            return "[object Arguments]";
        if (value is SharpTSArray array)
            return ArrayBuiltIns.ToJsString(this, array);
        if (value is ISharpTSCallable or SharpTSGlobalThis or SharpTSRegExp)
            return Stringify(OrdinaryToPrimitiveObject(value, PrimitiveHint.String));
        if (value is not SharpTSObject obj)
            return Stringify(value);

        if (obj.GetProperty("__primitiveType") is "Symbol"
            && obj.GetProperty("__primitiveValue") is SharpTSSymbol boxedSymbol)
        {
            ThrowIfSymbolStringCoercion(boxedSymbol);
        }

        if (TryCallExoticToPrimitive(obj, PrimitiveHint.String, out var exoticResult))
        {
            ThrowIfSymbolStringCoercion(exoticResult);
            return Stringify(exoticResult);
        }

        if (TryGetBoxedPrimitiveValue(obj, out _))
            return Stringify(ToPrimitive(obj, PrimitiveHint.String));

        if (TryCallConversion(obj, "toString", out var stringResult))
        {
            ThrowIfSymbolStringCoercion(stringResult);
            return Stringify(stringResult);
        }
        if (TryCallConversion(obj, "valueOf", out var valueResult))
        {
            ThrowIfSymbolStringCoercion(valueResult);
            return Stringify(valueResult);
        }

        throw new ThrowException(new SharpTSTypeError(
            "Cannot convert object to primitive value"));
    }

    internal object? ToPrimitiveForBuiltIn(object? value)
        => ToPrimitive(value, PrimitiveHint.Number);

    /// <summary>
    /// ECMA-262 §7.1.1: consults an object's @@toPrimitive method before
    /// OrdinaryToPrimitive. An explicitly undefined method is treated as absent;
    /// a non-callable method or an object result throws TypeError.
    /// </summary>
    private bool TryCallExoticToPrimitive(
        SharpTSObject receiver, PrimitiveHint hint, out object? result)
    {
        result = null;
        object? method;
        if (receiver.TryGetSymbolAccessor(
                SharpTSSymbol.ToPrimitive, out var getter, out _))
        {
            method = getter is null
                ? SharpTSUndefined.Instance
                : FunctionBuiltIns.CallWithThis(this, getter, receiver, []);
        }
        else if (receiver.HasSymbolProperty(SharpTSSymbol.ToPrimitive))
        {
            method = receiver.GetBySymbol(SharpTSSymbol.ToPrimitive)
                ?? SharpTSUndefined.Instance;
        }
        else
        {
            return false;
        }

        if (method is null or SharpTSUndefined)
            return false;
        if (method is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.toPrimitive is not callable"));
        }

        string hintName = hint switch
        {
            PrimitiveHint.String => "string",
            PrimitiveHint.Number => "number",
            _ => "default",
        };
        result = FunctionBuiltIns.CallWithThis(
            this, callable, receiver, [hintName]);
        if (IsObjectLike(result))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert object to primitive value"));
        }
        return true;
    }

    /// <summary>
    /// ECMA-262 §7.1.19 ToPropertyKey for non-Symbol values. Property-key
    /// coercion uses the string hint, so arrays use their comma-joined form,
    /// boxed primitives unwrap, and user-defined toString/valueOf methods run
    /// in the required order.
    /// </summary>
    internal string ToPropertyKeyString(object? value)
    {
        if (!IsObjectLike(value))
            return PropertyKeyConverter.ToPropertyKeyString(value);

        // Boxed String/Number/Boolean objects carry their primitive in an
        // interpreter-only internal slot. Their prototype conversion methods
        // expect the unboxed receiver, so use the wrapper-aware coercion path.
        if (TryGetBoxedPrimitiveValue(value, out _))
            return PropertyKeyConverter.ToPropertyKeyString(
                ToPrimitive(value, PrimitiveHint.String));

        foreach (var methodName in new[] { "toString", "valueOf" })
        {
            var method = GetProperty(value, methodName);
            if (method is not ISharpTSCallable callable)
                continue;

            var primitive = FunctionBuiltIns.CallWithThis(this, callable, value, []);
            if (!IsObjectLike(primitive))
                return PropertyKeyConverter.ToPropertyKeyString(primitive);
        }

        throw new ThrowException(new SharpTSTypeError(
            "Cannot convert object to primitive value"));
    }

    /// <summary>
    /// ECMA-262 §25.5.2.1 step 4.b: coerce a replacer-array element to a
    /// PropertyList key. A String stays as-is; a Number coerces via ToString;
    /// an Object carrying a [[StringData]]/[[NumberData]] slot (a boxed
    /// <c>new String</c>/<c>new Number</c> wrapper) coerces via ToString too —
    /// honoring an own <c>toString</c>/<c>valueOf</c> override (#574, string hint
    /// so <c>toString</c> is tried first). Any other element is ignored (returns
    /// false). The interpreter and compiled JSON.stringify build their allowed-key
    /// set through this rule.
    /// </summary>
    internal bool TryCoerceReplacerArrayKey(object? value, out string key)
    {
        key = "";
        switch (value)
        {
            case string s:
                key = s;
                return true;
            case double d:
                key = Stringify(d);
                return true;
            case SharpTSObject obj when obj.GetProperty("__primitiveType") is "String" or "Number":
                var sp = ToPrimitive(obj, PrimitiveHint.String);
                key = sp as string ?? Stringify(sp);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Evaluates the delete operator.
    /// </summary>
    /// <param name="delete">The delete expression AST node.</param>
    /// <returns>true if deletion succeeded, false otherwise.</returns>
    /// <remarks>
    /// - delete obj.prop: removes property from object, returns true
    /// - delete obj[key]: removes computed property from object, returns true
    /// - delete variable: throws SyntaxError in strict mode, returns false in sloppy mode
    /// - delete on non-existent property: returns true
    /// </remarks>
    private RuntimeValue EvaluateDelete(Expr.Delete delete)
        => RuntimeValue.FromBoolean(DeleteCore(_syncContext, delete).GetAwaiter().GetResult());

    /// <summary>
    /// Core delete logic shared by the sync and async evaluators. The evaluation context
    /// supplies the operand-evaluation strategy (synchronous or awaiting), so a single body
    /// serves both paths — there is no separate async twin to keep in lockstep.
    /// </summary>
    private async ValueTask<bool> DeleteCore(IEvaluationContext ctx, Expr.Delete delete)
    {
        bool strictMode = _environment.IsStrictMode;

        return delete.Operand switch
        {
            Expr.Get get => await DeletePropertyCore(ctx, get, strictMode),
            Expr.GetIndex getIndex => await DeleteIndexedPropertyCore(ctx, getIndex, strictMode),
            Expr.Variable v when strictMode =>
                throw StrictModeErrors.SyntaxError($"Delete of unqualified identifier '{v.Name.Lexeme}' in strict mode"),
            Expr.Variable v =>
                SloppyModeWarnings.WarnAndReturn(false, "delete variable", $"delete {v.Name.Lexeme} returns false in sloppy mode"),
            _ => true // Deleting other expressions returns true but does nothing
        };
    }

    /// <summary>
    /// Deletes a property from an object (delete obj.prop).
    /// In strict mode, throws TypeError for frozen/sealed objects.
    /// </summary>
    private async ValueTask<bool> DeletePropertyCore(IEvaluationContext ctx, Expr.Get get, bool strictMode)
    {
        object? obj = (await ctx.EvaluateExprAsync(get.Object)).ToObject();
        string name = get.Name.Lexeme;

        return DeleteProperty(obj, name, strictMode);
    }

    /// <summary>
    /// ECMA-262 DeletePropertyOrThrow dispatch for already-evaluated operands.
    /// Shared by delete expressions and built-in algorithms that must delete a
    /// receiver property without synthesizing an AST node.
    /// </summary>
    internal bool DeleteProperty(object? obj, string name, bool strictMode = true)
    {
        if (obj is SharpTSProxy proxy)
            return proxy.TrapDeleteProperty(name, this);

        return obj switch
        {
            SharpTSArray array => array.DeletePropertyStrict(name, strictMode),
            SharpTSObject tsObj => tsObj.DeletePropertyStrict(name, strictMode),
            SharpTSInstance tsInst => tsInst.DeleteFieldStrict(name, strictMode),
            SharpTSMath math => math.DeleteExtra(name),
            SharpTSJSON json => json.DeleteExtra(name),
            SharpTSDate date => date.DeleteExtra(name),
            SharpTSRegExp regex => regex.DeleteProperty(name),
            SharpTSArrayGlobal arrayGlobal => arrayGlobal.DeleteProperty(name),
            SharpTSObjectNamespace objectNamespace => objectNamespace.DeleteProperty(name),
            SharpTSGlobalThis globalThis => globalThis.DeleteProperty(name),
            SharpTSStringNamespace when name == "prototype" =>
                DeleteNonConfigurableClassPrototype(name, strictMode),
            SharpTSStringNamespace stringNamespace => stringNamespace.DeleteProperty(name),
            SharpTSNumberNamespace when name == "prototype" =>
                DeleteNonConfigurableClassPrototype(name, strictMode),
            SharpTSNumberNamespace numberNamespace => numberNamespace.DeleteProperty(name),
            SharpTSBooleanNamespace when name == "prototype" =>
                DeleteNonConfigurableClassPrototype(name, strictMode),
            SharpTSBooleanNamespace booleanNamespace => booleanNamespace.DeleteProperty(name),
            SharpTSFunctionPrototype functionPrototype => functionPrototype.DeleteProperty(name),
            SharpTSArrayPrototype arrayPrototype => arrayPrototype.DeleteProperty(name),
            SharpTSStringPrototype stringPrototype => stringPrototype.DeleteProperty(name),
            SharpTSNumberPrototype numberPrototype => numberPrototype.DeleteProperty(name),
            SharpTSBooleanPrototype booleanPrototype => booleanPrototype.DeleteProperty(name),
            SharpTSBigIntPrototype bigIntPrototype => bigIntPrototype.DeleteProperty(name),
            SharpTSSymbolPrototype symbolPrototype => symbolPrototype.DeleteProperty(name),
            SharpTSObjectPrototype objectPrototype => objectPrototype.DeleteProperty(name),
            SharpTSClassPrototype classPrototype => classPrototype.DeleteProperty(name),
            SharpTSPromisePrototype promisePrototype => promisePrototype.DeleteProperty(name),
            SharpTSClass when name == "prototype" =>
                DeleteNonConfigurableClassPrototype(name, strictMode),
            SharpTSClass klass => klass.DeleteStaticProperty(name),
            SharpTSBuiltInConstructor
                { Name: BuiltInNames.Promise or BuiltInNames.RegExp }
                when name == "prototype" =>
                    DeleteNonConfigurableClassPrototype(name, strictMode),
            SharpTSBuiltInConstructor constructor =>
                DeleteBuiltInConstructorProperty(constructor, name),
            IBuiltInFunctionMetadata builtInFn => builtInFn.DeleteMetadataProperty(name),
            SharpTSFunction function => function.DeleteProperty(name),
            SharpTSArrowFunction arrow => arrow.DeleteProperty(name),
            Dictionary<string, object?> dict => dict.Remove(name),
            _ => true // Deleting non-existent property on primitive returns true
        };
    }

    /// <summary>
    /// Deletes a computed property from an object (delete obj[key]).
    /// In strict mode, throws TypeError for frozen/sealed objects.
    /// </summary>
    private async ValueTask<bool> DeleteIndexedPropertyCore(IEvaluationContext ctx, Expr.GetIndex getIndex, bool strictMode)
    {
        object? obj = (await ctx.EvaluateExprAsync(getIndex.Object)).ToObject();
        object? key = (await ctx.EvaluateExprAsync(getIndex.Index)).ToObject();

        // Handle proxy
        if (obj is SharpTSProxy proxy)
        {
            string proxyKey = key is SharpTSSymbol ? key.ToString()! : Stringify(key);
            return proxy.TrapDeleteProperty(proxyKey, this);
        }

        // Handle symbol keys
        if (key is SharpTSSymbol symbol)
        {
            return obj switch
            {
                SharpTSObject tsObj => tsObj.DeleteBySymbolStrict(symbol, strictMode),
                SharpTSInstance tsInst => tsInst.DeleteBySymbolStrict(symbol, strictMode),
                SharpTSArray array => array.DeleteBySymbolStrict(symbol, strictMode),
                SharpTSMath math => math.DeleteBySymbolStrict(symbol, strictMode),
                SharpTSStringPrototype stringPrototype
                    => stringPrototype.DeleteBySymbolStrict(symbol, strictMode),
                _ => true
            };
        }

        string keyStr = Stringify(key);
        return DeleteProperty(obj, keyStr, strictMode);
    }

    private static bool DeleteNonConfigurableClassPrototype(
        string propertyName, bool strictMode)
    {
        if (strictMode)
            throw StrictModeErrors.TypeError(
                $"Cannot delete property '{propertyName}' of class constructor");
        return false;
    }

    /// <summary>
    /// Returns the typeof string for a runtime value.
    /// </summary>
    /// <param name="value">The value to get the type of.</param>
    /// <returns>The JavaScript/TypeScript type string ("undefined", "boolean", "number", "string", "function", or "object").</returns>
    /// <remarks>
    /// Maps runtime types to JavaScript typeof results. Null returns "undefined",
    /// functions and classes return "function", everything else returns "object".
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/typeof">MDN typeof</seealso>
    private string GetTypeofString(object? value) => value switch
    {
        null => "object",  // JavaScript quirk: typeof null === "object"
        SharpTSUndefined => "undefined",
        bool => "boolean",
        double => "number",
        string => "string",
        SharpTSSymbol => "symbol",
        SharpTSBigInt or System.Numerics.BigInteger => "bigint",
        SharpTSProxy proxy => proxy.IsCallable ? "function" : "object",
        SharpTSFunction or SharpTSArrowFunction or SharpTSClass or BuiltInMethod or ISharpTSCallable => "function",
        // Node/JS quirk: `typeof Buffer === 'function'` even though our Buffer
        // is a singleton namespace object. Match this explicitly so bare
        // references behave like the other global constructors.
        Runtime.Types.SharpTSBufferConstructor => "function",
        _ => "object"
    };

    /// <summary>
    /// Determines if a value is truthy in JavaScript/TypeScript semantics.
    /// </summary>
    /// <param name="obj">The value to check.</param>
    /// <returns><c>true</c> if the value is truthy; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Falsy values include null, undefined, false, 0, NaN, and "".
    /// All other values are truthy.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Glossary/Truthy">MDN Truthy</seealso>
    private static bool IsTruthy(object? obj) => RuntimeTypes.IsTruthy(obj);
    private static bool IsTruthy(RuntimeValue rv) => rv.IsTruthy();

    /// <summary>
    /// Determines if two values are equal using loose equality (<c>==</c>).
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns><c>true</c> if the values are loosely equal; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Uses object equality. Both null values are equal.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Equality">MDN Equality</seealso>
    private bool IsEqual(object? a, object? b)
    {
        // null == null, undefined == undefined, null == undefined (loose equality)
        bool aIsNullish = a == null || a is SharpTSUndefined;
        bool bIsNullish = b == null || b is SharpTSUndefined;
        if (aIsNullish && bIsNullish) return true;
        if (aIsNullish || bIsNullish) return false;
        // ECMA-262 §7.2.15 steps 10-11: Object vs primitive coerces the object
        // through ToPrimitive, so `new Number(0) == 0` is true (#708). Only when
        // the other operand is a primitive — object == object stays reference-based.
        if (a is SharpTSObject or SharpTSNumberPrototype or SharpTSStringPrototype
                or SharpTSBooleanPrototype
            && IsPrimitiveOperand(b))
            a = ToPrimitive(a, PrimitiveHint.Default);
        else if (b is SharpTSObject or SharpTSNumberPrototype or SharpTSStringPrototype
                or SharpTSBooleanPrototype
            && IsPrimitiveOperand(a))
            b = ToPrimitive(b, PrimitiveHint.Default);
        return a!.Equals(b);
    }

    /// <summary>
    /// True for JS primitive operands that trigger object→primitive coercion on
    /// the other side of loose <c>==</c> (number, string, boolean, bigint).
    /// </summary>
    private static bool IsPrimitiveOperand(object? value) =>
        value is double or string or bool or SharpTSBigInt;

    /// <summary>
    /// Determines if two values are equal using strict equality (<c>===</c>).
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns><c>true</c> if the values are strictly equal (same type and value); otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Checks type equality first, then value equality. No type coercion is performed.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Strict_equality">MDN Strict Equality</seealso>
    private bool IsStrictEqual(object? a, object? b)
    {
        // In TypeScript/JS, === checks both value and type
        // null === null and undefined === undefined, but NOT null === undefined
        if (a == null && b == null) return true;
        if (a is SharpTSUndefined && b is SharpTSUndefined) return true;
        if (a == null || b == null || a is SharpTSUndefined || b is SharpTSUndefined) return false;
        if (a.GetType() != b.GetType()) return false;
        // ECMA-262 7.2.16 IsStrictlyEqual: NaN is never equal to anything,
        // including itself. Object.Equals defers to Double.Equals which
        // treats NaN as equal to itself; check explicitly.
        if (a is double da && b is double db && (double.IsNaN(da) || double.IsNaN(db)))
            return false;
        return a.Equals(b);
    }

    /// <summary>
    /// Evaluates the <c>in</c> operator to check property existence.
    /// </summary>
    /// <param name="left">The property name to check for.</param>
    /// <param name="right">The object to check in.</param>
    /// <returns><c>true</c> if the property exists; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Works with objects, instances, and arrays (checking index existence).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/in">MDN in Operator</seealso>
    private object EvaluateIn(object? left, object? right)
    {
        // 'in' operator checks if a property exists in an object
        // Handle proxy has trap
        if (right is SharpTSProxy proxy)
        {
            string proxyKey = left is SharpTSSymbol ? left.ToString()! : (left?.ToString() ?? "");
            return proxy.TrapHas(proxyKey, this);
        }

        // Handle symbol keys specially
        if (left is SharpTSSymbol symbol)
        {
            if (right is SharpTSObject symObj)
            {
                return symObj.HasSymbolProperty(symbol);
            }
            if (right is SharpTSInstance symInst)
            {
                return symInst.HasSymbolProperty(symbol);
            }
            // Symbols can't be in arrays or other types
            if (right is SharpTSArray)
            {
                return false;
            }
            throw new InterpreterException("'in' operator requires an object on the right side.");
        }

        string key = left?.ToString() ?? "";

        if (right is SharpTSObject obj)
        {
            return obj.HasProperty(key);
        }
        if (right is SharpTSInstance instance)
        {
            return instance.HasProperty(key);
        }
        if (right is SharpTSArray arr)
        {
            // Route through the shared HasProperty operation so holes remain
            // absent while inherited indexed and named properties participate.
            return HasProperty(arr, key);
        }

        throw new InterpreterException("'in' operator requires an object on the right side.");
    }

    /// <summary>
    /// Converts a runtime value to its string representation.
    /// </summary>
    /// <param name="obj">The value to stringify.</param>
    /// <returns>The string representation of the value.</returns>
    internal string Stringify(object? obj)
    {
        if (obj == null) return "null";
        if (obj is SharpTSUndefined) return "undefined";
        if (obj is bool b) return b ? "true" : "false";
        if (obj is double d)
        {
            return Compilation.RuntimeTypes.FormatNumber(d);
        }

        if (obj is SharpTSArray array)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Stringify(array[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        if (obj is SharpTSObject sharpObj)
        {
            // ECMA-262 §7.1.17 ToString of an object goes through ToPrimitive,
            // which (for hint "string") tries the object's own toString
            // method first. The previous shortcut returned "[object Object]"
            // unconditionally, which silently swallowed throwing user
            // toStrings — test262 patterns like
            // `{toString: () => { throw }}` rely on the abrupt completion
            // propagating up. Only invoke the user-installed toString;
            // skip the prototype to preserve the cheap default for plain
            // records (which would otherwise spin up a call frame for
            // every Stringify).
            if (sharpObj.HasProperty("toString")
                && sharpObj.GetProperty("toString") is ISharpTSCallable userToString)
            {
                var coerced = FunctionBuiltIns.CallWithThis(this, userToString, sharpObj, []);
                if (coerced is string strRes) return strRes;
                return Stringify(coerced);
            }
            return "[object Object]";
        }

        if (obj is SharpTSInstance instance)
        {
            // ECMA-262 §7.1.17 ToString of an object goes through ToPrimitive,
            // which (for hint "string") invokes the object's toString. A class
            // instance resolves toString through its class chain — e.g.
            // Error.prototype.toString -> "TypeError: msg", or a user-defined
            // override. Invoke it so string coercion (templates, `+`, String())
            // matches Node/compiled mode instead of yielding "<Class> instance"
            // (#921/#922). Fall back to "[object Object]" only when no callable
            // toString resolves (e.g. a plain class) — Node's Object.prototype.toString
            // brand for an ordinary object, matching compiled mode (#931).
            if (TryStringifyInstanceViaToString(instance, out var instanceStr))
                return instanceStr;
            return "[object Object]";
        }

        // ECMA-262 7.1.17 ToString(bigint) = bare numeric form ("42"). The "42n"
        // debug form belongs to console.log / util.inspect (ConsoleBuiltIns), not
        // language-level string coercion (`+` concat, template literals).
        if (obj is SharpTSBigInt bigint)
        {
            return bigint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return obj.ToString()!;
    }
}
