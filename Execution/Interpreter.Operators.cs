using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    /// <summary>
    /// Evaluates a compound assignment expression on a variable (e.g., <c>x += 1</c>).
    /// </summary>
    /// <param name="compound">The compound assignment expression AST node.</param>
    /// <returns>The new value after the operation.</returns>
    /// <remarks>
    /// Retrieves the current value, applies the compound operator via
    /// <see cref="ApplyCompoundOperator"/>, and stores the result.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Addition_assignment">MDN Compound Assignment</seealso>
    private object? EvaluateCompoundAssign(Expr.CompoundAssign compound)
    {
        object? currentValue = _environment.Get(compound.Name);
        object? addValue = Evaluate(compound.Value);
        object? newValue = ApplyCompoundOperator(compound.Operator.Type, currentValue, addValue);
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
    private object? EvaluateCompoundSet(Expr.CompoundSet compound)
    {
        object? obj = Evaluate(compound.Object);
        object? addValue = Evaluate(compound.Value);

        if (obj is SharpTSInstance instance)
        {
            object? currentValue = instance.Get(compound.Name);
            object? newValue = ApplyCompoundOperator(compound.Operator.Type, currentValue, addValue);
            instance.Set(compound.Name, newValue);
            return newValue;
        }
        if (obj is SharpTSObject simpleObj)
        {
            object? currentValue = simpleObj.Get(compound.Name.Lexeme);
            object? newValue = ApplyCompoundOperator(compound.Operator.Type, currentValue, addValue);
            simpleObj.Set(compound.Name.Lexeme, newValue);
            return newValue;
        }

        throw new Exception("Only instances and objects have fields.");
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
    private object? EvaluateCompoundSetIndex(Expr.CompoundSetIndex compound)
    {
        object? obj = Evaluate(compound.Object);
        object? index = Evaluate(compound.Index);
        object? addValue = Evaluate(compound.Value);

        if (obj is SharpTSArray array && index is double idx)
        {
            object? currentValue = array.Get((int)idx);
            object? newValue = ApplyCompoundOperator(compound.Operator.Type, currentValue, addValue);
            array.Set((int)idx, newValue);
            return newValue;
        }

        throw new Exception("Compound index assignment not supported on this type.");
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
    private object? EvaluatePrefixIncrement(Expr.PrefixIncrement prefix)
    {
        double delta = prefix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;

        if (prefix.Operand is Expr.Variable variable)
        {
            double current = (double)_environment.Get(variable.Name)!;
            double newValue = current + delta;
            _environment.Assign(variable.Name, newValue);
            return newValue; // Prefix returns NEW value
        }
        if (prefix.Operand is Expr.Get get)
        {
            object? obj = Evaluate(get.Object);
            if (obj is SharpTSInstance instance)
            {
                double current = (double)instance.Get(get.Name)!;
                double newValue = current + delta;
                instance.Set(get.Name, newValue);
                return newValue;
            }
            if (obj is SharpTSObject simpleObj)
            {
                double current = (double)simpleObj.Get(get.Name.Lexeme)!;
                double newValue = current + delta;
                simpleObj.Set(get.Name.Lexeme, newValue);
                return newValue;
            }
        }
        if (prefix.Operand is Expr.GetIndex getIndex)
        {
            object? obj = Evaluate(getIndex.Object);
            object? index = Evaluate(getIndex.Index);
            if (obj is SharpTSArray array && index is double idx)
            {
                double current = (double)array.Get((int)idx)!;
                double newValue = current + delta;
                array.Set((int)idx, newValue);
                return newValue;
            }
        }

        throw new Exception("Invalid increment operand.");
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
    private object? EvaluatePostfixIncrement(Expr.PostfixIncrement postfix)
    {
        double delta = postfix.Operator.Type == TokenType.PLUS_PLUS ? 1 : -1;

        if (postfix.Operand is Expr.Variable variable)
        {
            double current = (double)_environment.Get(variable.Name)!;
            double newValue = current + delta;
            _environment.Assign(variable.Name, newValue);
            return current; // Postfix returns OLD value
        }
        if (postfix.Operand is Expr.Get get)
        {
            object? obj = Evaluate(get.Object);
            if (obj is SharpTSInstance instance)
            {
                double current = (double)instance.Get(get.Name)!;
                double newValue = current + delta;
                instance.Set(get.Name, newValue);
                return current;
            }
            if (obj is SharpTSObject simpleObj)
            {
                double current = (double)simpleObj.Get(get.Name.Lexeme)!;
                double newValue = current + delta;
                simpleObj.Set(get.Name.Lexeme, newValue);
                return current;
            }
        }
        if (postfix.Operand is Expr.GetIndex getIndex)
        {
            object? obj = Evaluate(getIndex.Object);
            object? index = Evaluate(getIndex.Index);
            if (obj is SharpTSArray array && index is double idx)
            {
                double current = (double)array.Get((int)idx)!;
                double newValue = current + delta;
                array.Set((int)idx, newValue);
                return current;
            }
        }

        throw new Exception("Invalid increment operand.");
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
        if (left is double l && right is double r) return l + r;
        if (left is string ls || right is string rs) return Stringify(left) + Stringify(right);
        throw new Exception("Operands must be two numbers or two strings.");
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
    private object? EvaluateUnary(Expr.Unary unary)
    {
        object? right = Evaluate(unary.Right);
        return EvaluateUnaryOperation(unary.Operator, right);
    }

    /// <summary>
    /// Core unary operation logic, shared between sync and async evaluation.
    /// </summary>
    private object? EvaluateUnaryOperation(Token op, object? right)
    {
        return op.Type switch
        {
            TokenType.BANG => !IsTruthy(right),
            TokenType.MINUS when right is SharpTSBigInt bi => new SharpTSBigInt(-bi.Value),
            TokenType.MINUS when right is System.Numerics.BigInteger biVal => new SharpTSBigInt(-biVal),
            TokenType.MINUS => -(double)right!,
            TokenType.TYPEOF => GetTypeofString(right),
            TokenType.TILDE when right is SharpTSBigInt bi => new SharpTSBigInt(~bi.Value),
            TokenType.TILDE when right is System.Numerics.BigInteger biVal => new SharpTSBigInt(~biVal),
            TokenType.TILDE => (double)(~ToInt32(right)),
            _ => null
        };
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
        bool => "boolean",
        double => "number",
        string => "string",
        SharpTSSymbol => "symbol",
        SharpTSBigInt or System.Numerics.BigInteger => "bigint",
        SharpTSFunction or SharpTSArrowFunction or SharpTSClass => "function",
        _ => "object"
    };

    /// <summary>
    /// Determines if a value is truthy in JavaScript/TypeScript semantics.
    /// </summary>
    /// <param name="obj">The value to check.</param>
    /// <returns><c>true</c> if the value is truthy; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Null is falsy, booleans use their value, all other values are truthy.
    /// Note: This simplified implementation doesn't handle 0, "", NaN as falsy.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Glossary/Truthy">MDN Truthy</seealso>
    private bool IsTruthy(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        return true;
    }

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
        if (a == null && b == null) return true;
        if (a == null) return false;
        return a.Equals(b);
    }

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
        // Since we're already strongly typed, this is essentially the same as ==
        // But we check that types match exactly
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;
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
            // For arrays, check if index exists
            if (double.TryParse(key, out double index))
            {
                int i = (int)index;
                return i >= 0 && i < arr.Elements.Count;
            }
            return false;
        }

        throw new Exception("Runtime Error: 'in' operator requires an object on the right side.");
    }

    /// <summary>
    /// Converts a runtime value to its string representation.
    /// </summary>
    /// <param name="obj">The value to stringify.</param>
    /// <returns>The string representation of the value.</returns>
    /// <remarks>
    /// Null becomes "null", booleans are lowercase, other values use <c>ToString()</c>.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String#string_coercion">MDN String Coercion</seealso>
    private string Stringify(object? obj)
    {
        if (obj == null) return "null";
        if (obj is bool b) return b.ToString().ToLower();
        return obj.ToString()!;
    }
}
