using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    /// <summary>
    /// Dispatches an expression to the appropriate evaluator using pattern matching.
    /// </summary>
    /// <param name="expr">The expression AST node to evaluate.</param>
    /// <returns>The runtime value produced by evaluating the expression.</returns>
    /// <remarks>
    /// Central dispatch point for all expression types. Handles literals, operators,
    /// function calls, property access, and control flow expressions.
    /// </remarks>
    internal object? Evaluate(Expr expr)
    {
        return expr switch
        {
            Expr.Binary binary => EvaluateBinary(binary),
            Expr.Logical logical => EvaluateLogical(logical),
            Expr.NullishCoalescing nc => EvaluateNullishCoalescing(nc),
            Expr.Ternary ternary => EvaluateTernary(ternary),
            Expr.Grouping grouping => Evaluate(grouping.Expression),
            Expr.Literal literal => literal.Value,
            Expr.Unary unary => EvaluateUnary(unary),
            Expr.Variable variable => EvaluateVariable(variable),
            Expr.Assign assign => EvaluateAssign(assign),
            Expr.Call call => EvaluateCall(call),
            Expr.Get get => EvaluateGet(get),
            Expr.Set set => EvaluateSet(set),
            Expr.This thisExpr => EvaluateThis(thisExpr),
            Expr.New newExpr => EvaluateNew(newExpr),
            Expr.ArrayLiteral array => EvaluateArray(array),
            Expr.ObjectLiteral obj => EvaluateObject(obj),
            Expr.GetIndex getIndex => EvaluateGetIndex(getIndex),
            Expr.SetIndex setIndex => EvaluateSetIndex(setIndex),
            Expr.Super super => EvaluateSuper(super),
            Expr.CompoundAssign compound => EvaluateCompoundAssign(compound),
            Expr.CompoundSet compoundSet => EvaluateCompoundSet(compoundSet),
            Expr.CompoundSetIndex compoundSetIndex => EvaluateCompoundSetIndex(compoundSetIndex),
            Expr.PrefixIncrement prefix => EvaluatePrefixIncrement(prefix),
            Expr.PostfixIncrement postfix => EvaluatePostfixIncrement(postfix),
            Expr.ArrowFunction arrow => EvaluateArrowFunction(arrow),
            Expr.TemplateLiteral template => EvaluateTemplateLiteral(template),
            Expr.Spread spread => Evaluate(spread.Expression), // Spread evaluates to its inner value
            Expr.TypeAssertion ta => Evaluate(ta.Expression), // Type assertions are pass-through at runtime
            _ => throw new Exception("Unknown expression type.")
        };
    }

    /// <summary>
    /// Evaluates a variable reference, looking up its value in the current environment.
    /// </summary>
    /// <param name="variable">The variable expression AST node.</param>
    /// <returns>The current value of the variable.</returns>
    /// <remarks>
    /// Special-cases the Math global object. All other variables are resolved
    /// through the <see cref="RuntimeEnvironment"/> scope chain.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html">TypeScript Variable Declarations</seealso>
    private object? EvaluateVariable(Expr.Variable variable)
    {
        // Check for built-in singleton namespaces (e.g., Math)
        var singleton = BuiltInRegistry.Instance.GetSingleton(variable.Name.Lexeme);
        if (singleton != null)
        {
            return singleton;
        }
        return _environment.Get(variable.Name);
    }

    /// <summary>
    /// Evaluates a template literal (backtick string) with embedded expressions.
    /// </summary>
    /// <param name="template">The template literal expression AST node.</param>
    /// <returns>The interpolated string result.</returns>
    /// <remarks>
    /// Alternates between static string parts and evaluated expressions,
    /// stringifying each expression result before concatenation.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Template_literals">MDN Template Literals</seealso>
    private object? EvaluateTemplateLiteral(Expr.TemplateLiteral template)
    {
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < template.Strings.Count; i++)
        {
            result.Append(template.Strings[i]);
            if (i < template.Expressions.Count)
            {
                result.Append(Stringify(Evaluate(template.Expressions[i])));
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Evaluates a super method access, binding the superclass method to the current instance.
    /// </summary>
    /// <param name="expr">The super expression AST node.</param>
    /// <returns>The bound method from the superclass.</returns>
    /// <remarks>
    /// Retrieves the superclass from the environment and looks up the method,
    /// then binds it to the current instance for proper <c>this</c> context.
    /// For constructor calls (super()), if no explicit constructor exists, returns a
    /// no-op callable to match TypeScript's implicit default constructor behavior.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#super-calls">TypeScript super Calls</seealso>
    private object? EvaluateSuper(Expr.Super expr)
    {
        SharpTSClass superclass = (SharpTSClass)_environment.Get(expr.Keyword)!;
        SharpTSInstance instance = (SharpTSInstance)_environment.Get(new Token(TokenType.THIS, "this", null, 0))!;

        // super() with null Method means constructor call
        string methodName = expr.Method?.Lexeme ?? "constructor";
        SharpTSFunction? method = superclass.FindMethod(methodName);

        // If no constructor exists, return a no-op callable for super() calls
        // This matches TypeScript's implicit default constructor behavior
        if (method == null && methodName == "constructor")
        {
            return new NoOpCallable();
        }

        if (method == null)
        {
            throw new Exception($"Undefined property '{methodName}'.");
        }

        return method.Bind(instance);
    }

    /// <summary>
    /// A no-op callable used for super() calls when the parent class has no explicit constructor.
    /// </summary>
    private class NoOpCallable : ISharpTSCallable
    {
        public int Arity() => 0;
        public object? Call(Interpreter interpreter, List<object?> arguments) => null;
    }

    /// <summary>
    /// Evaluates an arrow function expression, creating a callable closure.
    /// </summary>
    /// <param name="arrow">The arrow function expression AST node.</param>
    /// <returns>A <see cref="SharpTSArrowFunction"/> that captures the current environment.</returns>
    /// <remarks>
    /// Arrow functions capture their lexical environment at creation time,
    /// enabling closures over outer variables.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/functions.html#arrow-functions">TypeScript Arrow Functions</seealso>
    private object? EvaluateArrowFunction(Expr.ArrowFunction arrow)
    {
        return new SharpTSArrowFunction(arrow, _environment);
    }

    /// <summary>
    /// Evaluates an object literal expression, creating a runtime object.
    /// </summary>
    /// <param name="obj">The object literal expression AST node.</param>
    /// <returns>A <see cref="SharpTSObject"/> containing the evaluated properties.</returns>
    /// <remarks>
    /// Supports spread properties (<c>...obj</c>) which copy all enumerable properties
    /// from the source object or instance.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    private object? EvaluateObject(Expr.ObjectLiteral obj)
    {
        Dictionary<string, object?> fields = [];
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = Evaluate(prop.Value);
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
                fields[prop.Name!.Lexeme] = Evaluate(prop.Value);
            }
        }
        return new SharpTSObject(fields);
    }

    /// <summary>
    /// Evaluates an array literal expression, creating a runtime array.
    /// </summary>
    /// <param name="array">The array literal expression AST node.</param>
    /// <returns>A <see cref="SharpTSArray"/> containing the evaluated elements.</returns>
    /// <remarks>
    /// Supports spread elements (<c>...arr</c>) which expand array contents inline.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#arrays">TypeScript Arrays</seealso>
    private object? EvaluateArray(Expr.ArrayLiteral array)
    {
        List<object?> elements = [];
        foreach (Expr element in array.Elements)
        {
            if (element is Expr.Spread spread)
            {
                object? spreadValue = Evaluate(spread.Expression);
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
                elements.Add(Evaluate(element));
            }
        }
        return new SharpTSArray(elements);
    }

    /// <summary>
    /// Evaluates an index access expression (bracket notation).
    /// </summary>
    /// <param name="getIndex">The index access expression AST node.</param>
    /// <returns>The value at the specified index.</returns>
    /// <remarks>
    /// Supports array element access and enum reverse mapping (numeric value to name).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Property_accessors#bracket_notation">MDN Bracket Notation</seealso>
    private object? EvaluateGetIndex(Expr.GetIndex getIndex)
    {
        object? obj = Evaluate(getIndex.Object);
        object? index = Evaluate(getIndex.Index);

        // Array with numeric index
        if (obj is SharpTSArray array && index is double idx)
        {
            return array.Get((int)idx);
        }

        // Handle enum reverse mapping: Direction[0] -> "Up"
        if (obj is SharpTSEnum enumObj && index is double enumIdx)
        {
            return enumObj.GetReverse(enumIdx);
        }

        // Const enums do not support reverse mapping
        if (obj is ConstEnumValues constEnum)
        {
            throw new Exception($"Runtime Error: Cannot use index access on const enum '{constEnum.Name}'. Const enum members can only be accessed by name.");
        }

        // Object with string key
        if (obj is SharpTSObject sharpObj && index is string strKey)
        {
            return sharpObj.Get(strKey);
        }

        // Object with number key (convert to string)
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            return numObj.Get(numKey.ToString());
        }

        // Object with symbol key
        if (obj is SharpTSObject symbolObj && index is SharpTSSymbol symbol)
        {
            return symbolObj.GetBySymbol(symbol);
        }

        // Class instance with string key
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            return instance.Get(new Token(TokenType.IDENTIFIER, instanceKey, null, 0));
        }

        // Class instance with symbol key (store in internal dictionary)
        if (obj is SharpTSInstance symInstance && index is SharpTSSymbol symKey)
        {
            return symInstance.GetBySymbol(symKey);
        }

        throw new Exception("Index access not supported on this type.");
    }

    /// <summary>
    /// Evaluates an index assignment expression (bracket notation with assignment).
    /// </summary>
    /// <param name="setIndex">The index assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Currently only supports array element assignment.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Property_accessors#bracket_notation">MDN Bracket Notation</seealso>
    private object? EvaluateSetIndex(Expr.SetIndex setIndex)
    {
        object? obj = Evaluate(setIndex.Object);
        object? index = Evaluate(setIndex.Index);
        object? value = Evaluate(setIndex.Value);

        // Array with numeric index
        if (obj is SharpTSArray array && index is double idx)
        {
            array.Set((int)idx, value);
            return value;
        }

        // Object with string key
        if (obj is SharpTSObject sharpObj && index is string strKey)
        {
            sharpObj.Set(strKey, value);
            return value;
        }

        // Object with number key (convert to string)
        if (obj is SharpTSObject numObj && index is double numKey)
        {
            numObj.Set(numKey.ToString(), value);
            return value;
        }

        // Object with symbol key
        if (obj is SharpTSObject symbolObj && index is SharpTSSymbol symbol)
        {
            symbolObj.SetBySymbol(symbol, value);
            return value;
        }

        // Class instance with string key
        if (obj is SharpTSInstance instance && index is string instanceKey)
        {
            instance.SetFieldValue(instanceKey, value);
            return value;
        }

        // Class instance with symbol key
        if (obj is SharpTSInstance symInstance && index is SharpTSSymbol symKey)
        {
            symInstance.SetBySymbol(symKey, value);
            return value;
        }

        throw new Exception("Index assignment not supported on this type.");
    }
}
