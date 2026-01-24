using System.Numerics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

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
    /// For async expressions (await), this will block synchronously. Use EvaluateAsync for fully async evaluation.
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
            Expr.Literal literal => EvaluateLiteral(literal),
            Expr.Unary unary => EvaluateUnary(unary),
            Expr.Delete delete => EvaluateDelete(delete),
            Expr.Variable variable => EvaluateVariable(variable),
            Expr.Assign assign => EvaluateAssign(assign),
            Expr.Call call => EvaluateCall(call),
            Expr.Get get => EvaluateGet(get),
            Expr.Set set => EvaluateSet(set),
            Expr.GetPrivate gp => EvaluateGetPrivate(gp),
            Expr.SetPrivate sp => EvaluateSetPrivate(sp),
            Expr.CallPrivate cp => EvaluateCallPrivate(cp),
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
            Expr.LogicalAssign logical => EvaluateLogicalAssign(logical),
            Expr.LogicalSet logicalSet => EvaluateLogicalSet(logicalSet),
            Expr.LogicalSetIndex logicalSetIndex => EvaluateLogicalSetIndex(logicalSetIndex),
            Expr.PrefixIncrement prefix => EvaluatePrefixIncrement(prefix),
            Expr.PostfixIncrement postfix => EvaluatePostfixIncrement(postfix),
            Expr.ArrowFunction arrow => EvaluateArrowFunction(arrow),
            Expr.TemplateLiteral template => EvaluateTemplateLiteral(template),
            Expr.TaggedTemplateLiteral tagged => EvaluateTaggedTemplateLiteral(tagged),
            Expr.Spread spread => Evaluate(spread.Expression), // Spread evaluates to its inner value
            Expr.TypeAssertion ta => Evaluate(ta.Expression), // Type assertions are pass-through at runtime
            Expr.Satisfies sat => Evaluate(sat.Expression), // Satisfies is pass-through at runtime
            Expr.NonNullAssertion nna => Evaluate(nna.Expression), // Non-null assertions are pass-through at runtime
            Expr.Await => throw new InterpreterException(" 'await' can only be used inside async functions."),
            Expr.DynamicImport di => EvaluateDynamicImport(di),
            Expr.ImportMeta im => EvaluateImportMeta(im),
            Expr.Yield yieldExpr => EvaluateYield(yieldExpr),
            Expr.RegexLiteral regex => new SharpTSRegExp(regex.Pattern, regex.Flags),
            Expr.ClassExpr classExpr => EvaluateClassExpression(classExpr),
            _ => throw new Exception("Unknown expression type.")
        };
    }

    /// <summary>
    /// Asynchronously dispatches an expression to the appropriate evaluator.
    /// </summary>
    /// <param name="expr">The expression AST node to evaluate.</param>
    /// <returns>A task that resolves to the runtime value produced by evaluating the expression.</returns>
    /// <remarks>
    /// Async version of Evaluate that properly handles await expressions without blocking.
    /// Used by async functions and arrow functions.
    /// </remarks>
    /// <summary>
    /// Asynchronously dispatches an expression to the appropriate evaluator.
    /// </summary>
    /// <param name="expr">The expression AST node to evaluate.</param>
    /// <returns>A task that resolves to the runtime value produced by evaluating the expression.</returns>
    /// <remarks>
    /// Async version of Evaluate that properly handles await expressions without blocking.
    /// Used by async functions and arrow functions.
    /// </remarks>
    internal async Task<object?> EvaluateAsync(Expr expr)
    {
        switch (expr)
        {
            case Expr.Binary binary: return await EvaluateBinaryAsync(binary);
            case Expr.Logical logical: return await EvaluateLogicalAsync(logical);
            case Expr.NullishCoalescing nc: return await EvaluateNullishCoalescingAsync(nc);
            case Expr.Ternary ternary: return await EvaluateTernaryAsync(ternary);
            case Expr.Grouping grouping: return await EvaluateAsync(grouping.Expression);
            case Expr.Literal literal: return EvaluateLiteral(literal);
            case Expr.Unary unary: return await EvaluateUnaryAsync(unary);
            case Expr.Delete delete: return await EvaluateDeleteAsync(delete);
            case Expr.Variable variable: return EvaluateVariable(variable);
            case Expr.Assign assign: return await EvaluateAssignAsync(assign);
            case Expr.Call call: return await EvaluateCallAsync(call);
            case Expr.Get get: return await EvaluateGetAsync(get);
            case Expr.Set set: return await EvaluateSetAsync(set);
            case Expr.GetPrivate gp: return EvaluateGetPrivate(gp);
            case Expr.SetPrivate sp: return EvaluateSetPrivate(sp);
            case Expr.CallPrivate cp: return EvaluateCallPrivate(cp);
            case Expr.This thisExpr: return EvaluateThis(thisExpr);
            case Expr.New newExpr: return await EvaluateNewAsync(newExpr);
            case Expr.ArrayLiteral array: return await EvaluateArrayAsync(array);
            case Expr.ObjectLiteral obj: return await EvaluateObjectAsync(obj);
            case Expr.GetIndex getIndex: return await EvaluateGetIndexAsync(getIndex);
            case Expr.SetIndex setIndex: return await EvaluateSetIndexAsync(setIndex);
            case Expr.Super super: return EvaluateSuper(super);
            case Expr.CompoundAssign compound: return await EvaluateCompoundAssignAsync(compound);
            case Expr.CompoundSet compoundSet: return await EvaluateCompoundSetAsync(compoundSet);
            case Expr.CompoundSetIndex compoundSetIndex: return await EvaluateCompoundSetIndexAsync(compoundSetIndex);
            case Expr.LogicalAssign logical: return await EvaluateLogicalAssignAsync(logical);
            case Expr.LogicalSet logicalSet: return await EvaluateLogicalSetAsync(logicalSet);
            case Expr.LogicalSetIndex logicalSetIndex: return await EvaluateLogicalSetIndexAsync(logicalSetIndex);
            case Expr.PrefixIncrement prefix: return EvaluatePrefixIncrement(prefix);
            case Expr.PostfixIncrement postfix: return EvaluatePostfixIncrement(postfix);
            case Expr.ArrowFunction arrow: return EvaluateArrowFunction(arrow);
            case Expr.TemplateLiteral template: return await EvaluateTemplateLiteralAsync(template);
            case Expr.TaggedTemplateLiteral tagged: return await EvaluateTaggedTemplateLiteralAsync(tagged);
            case Expr.Spread spread: return await EvaluateAsync(spread.Expression);
            case Expr.TypeAssertion ta: return await EvaluateAsync(ta.Expression);
            case Expr.Satisfies sat: return await EvaluateAsync(sat.Expression);
            case Expr.NonNullAssertion nna: return await EvaluateAsync(nna.Expression);
            case Expr.Await awaitExpr: return await EvaluateAwaitAsync(awaitExpr);
            case Expr.DynamicImport di: return EvaluateDynamicImport(di);
            case Expr.ImportMeta im: return EvaluateImportMeta(im);
            case Expr.Yield yieldExpr: return EvaluateYield(yieldExpr);
            case Expr.RegexLiteral regex: return new SharpTSRegExp(regex.Pattern, regex.Flags);
            case Expr.ClassExpr classExpr: return EvaluateClassExpression(classExpr);
            default: throw new Exception("Unknown expression type.");
        }
    }

    /// <summary>
    /// Evaluates a yield expression, throwing YieldException for control flow.
    /// </summary>
    /// <param name="yieldExpr">The yield expression AST node.</param>
    /// <returns>Never returns normally - always throws YieldException.</returns>
    /// <remarks>
    /// Yield expressions suspend generator execution by throwing YieldException,
    /// which is caught by SharpTSGenerator.Next() to extract the yielded value.
    /// For yield*, the IsDelegating flag indicates delegation to another iterable.
    /// </remarks>
    private object? EvaluateYield(Expr.Yield yieldExpr)
    {
        object? value = yieldExpr.Value != null ? Evaluate(yieldExpr.Value) : null;
        throw new YieldException(value, yieldExpr.IsDelegating);
    }

    /// <summary>
    /// Evaluates an await expression, unwrapping the Promise value.
    /// </summary>
    private async Task<object?> EvaluateAwaitAsync(Expr.Await awaitExpr)
    {
        object? value = await EvaluateAsync(awaitExpr.Expression);

        // Unwrap Promise
        if (value is SharpTSPromise promise)
        {
            return await promise.GetValueAsync();
        }

        // Await on non-Promise returns the value (TypeScript behavior)
        return value;
    }

    /// <summary>
    /// Evaluates a literal expression, wrapping BigInteger values in SharpTSBigInt.
    /// </summary>
    /// <param name="literal">The literal expression AST node.</param>
    /// <returns>The literal value, with BigInteger wrapped in SharpTSBigInt.</returns>
    private object? EvaluateLiteral(Expr.Literal literal)
    {
        // Wrap BigInteger in SharpTSBigInt for proper toString behavior
        if (literal.Value is BigInteger bi)
        {
            return new SharpTSBigInt(bi);
        }
        return literal.Value;
    }

    /// <summary>
    /// Evaluates a variable reference, looking up its value in the current environment.
    /// </summary>
    /// <param name="variable">The variable expression AST node.</param>
    /// <returns>The current value of the variable.</returns>
    /// <remarks>
    /// Uses side-channel resolution information if available, otherwise falls back
    /// to dynamic lookup via <see cref="RuntimeEnvironment"/>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html">TypeScript Variable Declarations</seealso>
    private object? EvaluateVariable(Expr.Variable variable)
    {
        return LookupVariable(variable.Name, variable);
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
        var evaluatedExprs = template.Expressions.Select(Evaluate).ToList();
        return BuildTemplateLiteralString(template.Strings, evaluatedExprs);
    }

    /// <summary>
    /// Evaluates a tagged template literal, invoking the tag function with strings and values.
    /// </summary>
    /// <param name="tagged">The tagged template literal AST node.</param>
    /// <returns>The result of calling the tag function.</returns>
    private object? EvaluateTaggedTemplateLiteral(Expr.TaggedTemplateLiteral tagged)
    {
        object? tag = Evaluate(tagged.Tag);

        if (tag is not ISharpTSCallable callable)
            throw new InterpreterException(" Tagged template tag must be a function.");

        // Create template strings array with raw property
        // Cooked values: null becomes undefined (or just null in our runtime)
        var cookedList = tagged.CookedStrings.Cast<object?>().ToList();
        var stringsArray = new SharpTSTemplateStringsArray(cookedList, tagged.RawStrings);

        // Evaluate all expression arguments
        List<object?> args = [stringsArray];
        foreach (var expr in tagged.Expressions)
            args.Add(Evaluate(expr));

        return callable.Call(this, args);
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
    /// <returns>A <see cref="SharpTSArrowFunction"/> or <see cref="SharpTSAsyncArrowFunction"/> that captures the current environment.</returns>
    /// <remarks>
    /// Arrow functions capture their lexical environment at creation time,
    /// enabling closures over outer variables. Async arrow functions return a Promise.
    /// For named function expressions, the function name is visible inside the function body
    /// for recursion, but not outside.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/functions.html#arrow-functions">TypeScript Arrow Functions</seealso>
    private object? EvaluateArrowFunction(Expr.ArrowFunction arrow)
    {
        RuntimeEnvironment closure = _environment;

        // For named function expressions, create a child environment for self-reference
        // This enables recursion: const f = function myFunc(n) { return myFunc(n-1); }
        if (arrow.Name != null)
        {
            closure = new RuntimeEnvironment(_environment);
            closure.Define(arrow.Name.Lexeme, null);  // Placeholder, will be assigned after function creation
            closure.MarkAsReadOnly(arrow.Name.Lexeme); // Function name is read-only in strict mode
        }

        ISharpTSCallable func;
        if (arrow.IsAsync)
        {
            func = new SharpTSAsyncArrowFunction(arrow, closure, arrow.HasOwnThis);
        }
        else if (arrow.IsGenerator)
        {
            // Generator function expressions - wrap in a generator-creating function
            // Note: This uses a different wrapper than Stmt.Function generators
            func = new SharpTSArrowGeneratorFunction(arrow, closure, arrow.HasOwnThis);
        }
        else
        {
            func = new SharpTSArrowFunction(arrow, closure, arrow.HasOwnThis);
        }

        // Complete the self-reference for named function expressions
        if (arrow.Name != null)
        {
            closure.Assign(arrow.Name, func);
        }

        return func;
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
        Dictionary<string, object?> stringFields = [];
        Dictionary<SharpTSSymbol, object?> symbolFields = [];
        List<(string name, ISharpTSCallable getter)>? getters = null;
        List<(string name, ISharpTSCallable setter)>? setters = null;

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                object? spreadValue = Evaluate(prop.Value);
                ApplySpreadToFields(spreadValue, stringFields);
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter)
            {
                string name = GetPropertyKeyName(prop.Key!, Evaluate);
                var getter = CreateAccessorFunction(prop.Value);
                getters ??= [];
                getters.Add((name, getter));
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Setter)
            {
                string name = GetPropertyKeyName(prop.Key!, Evaluate);
                var setter = CreateSetterFunction(prop.Value, prop.SetterParam!);
                setters ??= [];
                setters.Add((name, setter));
            }
            else
            {
                object? value = Evaluate(prop.Value);
                ApplyPropertyToFields(prop.Key!, value, stringFields, symbolFields, Evaluate);
            }
        }

        var result = BuildObjectFromFields(stringFields, symbolFields);

        // Apply getters and setters
        if (getters != null)
        {
            foreach (var (name, getter) in getters)
            {
                result.DefineGetter(name, getter);
            }
        }
        if (setters != null)
        {
            foreach (var (name, setter) in setters)
            {
                result.DefineSetter(name, setter);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the string name from a property key.
    /// </summary>
    private static string GetPropertyKeyName(Expr.PropertyKey key, Func<Expr, object?> evaluateKey)
    {
        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            Expr.ComputedKey ck => evaluateKey(ck.Expression)?.ToString() ?? "undefined",
            _ => throw new InterpreterException(" Invalid property key for accessor.")
        };
    }

    /// <summary>
    /// Creates an accessor function (getter) from the function expression.
    /// </summary>
    private SharpTSArrowFunction CreateAccessorFunction(Expr body)
    {
        // The body should be a function or arrow function expression
        if (body is Expr.ArrowFunction arrow)
        {
            return EvaluateArrowFunction(arrow) as SharpTSArrowFunction
                   ?? throw new InterpreterException(" Failed to create getter function.");
        }
        throw new InterpreterException(" Getter must be a function expression.");
    }

    /// <summary>
    /// Creates a setter function from the function expression and parameter.
    /// </summary>
    private SharpTSArrowFunction CreateSetterFunction(Expr body, Stmt.Parameter setterParam)
    {
        // The body should be a function expression
        if (body is Expr.ArrowFunction arrow)
        {
            return EvaluateArrowFunction(arrow) as SharpTSArrowFunction
                   ?? throw new InterpreterException(" Failed to create setter function.");
        }
        throw new InterpreterException(" Setter must be a function expression.");
    }

    /// <summary>
    /// Applies a spread value's properties to the target fields dictionary.
    /// Shared between sync and async object evaluation paths.
    /// </summary>
    private static void ApplySpreadToFields(object? spreadValue, Dictionary<string, object?> stringFields)
    {
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
            throw new InterpreterException(" Spread in object literal requires an object.");
        }
    }

    /// <summary>
    /// Applies a property key-value pair to the target fields dictionaries.
    /// Shared between sync and async object evaluation paths.
    /// </summary>
    private static void ApplyPropertyToFields(
        Expr.PropertyKey key,
        object? value,
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?> symbolFields,
        Func<Expr, object?> evaluateKey)
    {
        switch (key)
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
                object? keyValue = evaluateKey(ck.Expression);
                if (keyValue is SharpTSSymbol sym)
                    symbolFields[sym] = value;
                else if (keyValue is double numKey)
                    stringFields[numKey.ToString()] = value;
                else
                    stringFields[keyValue?.ToString() ?? "undefined"] = value;
                break;
        }
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
        var evaluated = array.Elements.Select(e =>
            (e is Expr.Spread, Evaluate(e is Expr.Spread s ? s.Expression : e)));
        return BuildArrayFromElements(evaluated);
    }

    /// <summary>
    /// Resolves an (object, index) pair to a typed IndexTarget for dispatch.
    /// </summary>
    /// <param name="obj">The object being indexed.</param>
    /// <param name="index">The index value.</param>
    /// <returns>An IndexTarget discriminated union representing the resolved target.</returns>
    private static IndexTarget ResolveIndexTarget(object? obj, object? index) => (obj, index) switch
    {
        (SharpTSArray array, double idx) => new IndexTarget.Array(array, (int)idx),
        (SharpTSEnum enumObj, double enumIdx) => new IndexTarget.EnumReverse(enumObj, enumIdx),
        (ConstEnumValues constEnum, _) => new IndexTarget.ConstEnumError(constEnum),
        (SharpTSObject sharpObj, string strKey) => new IndexTarget.ObjectString(sharpObj, strKey),
        (SharpTSObject numObj, double numKey) => new IndexTarget.ObjectString(numObj, numKey.ToString()),
        (SharpTSObject symbolObj, SharpTSSymbol symbol) => new IndexTarget.ObjectSymbol(symbolObj, symbol),
        (SharpTSInstance instance, string instanceKey) => new IndexTarget.InstanceString(instance, instanceKey),
        (SharpTSInstance symInstance, SharpTSSymbol symKey) => new IndexTarget.InstanceSymbol(symInstance, symKey),
        (SharpTSGlobalThis globalThis, string globalKey) => new IndexTarget.GlobalThis(globalThis, globalKey),
        _ => new IndexTarget.Unsupported(obj, index)
    };

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

        return ResolveIndexTarget(obj, index) switch
        {
            IndexTarget.Array t => t.Target.Get(t.Index),
            IndexTarget.EnumReverse t => t.Target.GetReverse(t.Index),
            IndexTarget.ConstEnumError t => throw new Exception(
                $"Runtime Error: Cannot use index access on const enum '{t.Target.Name}'. Const enum members can only be accessed by name."),
            IndexTarget.ObjectString t => t.Target.GetProperty(t.Key),
            IndexTarget.ObjectSymbol t => t.Target.GetBySymbol(t.Key),
            IndexTarget.InstanceString t => t.Target.Get(new Token(TokenType.IDENTIFIER, t.Key, null, 0)),
            IndexTarget.InstanceSymbol t => t.Target.GetBySymbol(t.Key),
            IndexTarget.GlobalThis t => t.Target.GetProperty(t.Key),
            IndexTarget.Unsupported => throw new Exception("Index access not supported on this type."),
            _ => throw new Exception("Index access not supported on this type.")
        };
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
        bool strictMode = _environment.IsStrictMode;

        var target = ResolveIndexTarget(obj, index);

        if (target is IndexTarget.EnumReverse or IndexTarget.ConstEnumError)
            throw new Exception("Index assignment not supported on enum types.");

        switch (target)
        {
            case IndexTarget.Array t:
                if (strictMode) t.Target.SetStrict(t.Index, value, strictMode);
                else t.Target.Set(t.Index, value);
                return value;

            case IndexTarget.ObjectString t:
                if (strictMode) t.Target.SetPropertyStrict(t.Key, value, strictMode);
                else t.Target.SetProperty(t.Key, value);
                return value;

            case IndexTarget.ObjectSymbol t:
                if (strictMode) t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                else t.Target.SetBySymbol(t.Key, value);
                return value;

            case IndexTarget.InstanceString t:
                if (strictMode) t.Target.SetRawFieldStrict(t.Key, value, strictMode);
                else t.Target.SetRawField(t.Key, value);
                return value;

            case IndexTarget.InstanceSymbol t:
                if (strictMode) t.Target.SetBySymbolStrict(t.Key, value, strictMode);
                else t.Target.SetBySymbol(t.Key, value);
                return value;

            case IndexTarget.GlobalThis t:
                t.Target.SetProperty(t.Key, value);
                return value;

            default:
                throw new Exception("Index assignment not supported on this type.");
        }
    }

    /// <summary>
    /// Evaluates a dynamic import expression, returning a Promise of the module namespace.
    /// </summary>
    /// <param name="di">The dynamic import expression AST node.</param>
    /// <returns>A <see cref="SharpTSPromise"/> that resolves to the module namespace object.</returns>
    /// <remarks>
    /// Dynamic imports allow runtime module loading with expression paths.
    /// The returned Promise resolves to an object containing all module exports.
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/import">MDN Dynamic Import</seealso>
    private object? EvaluateDynamicImport(Expr.DynamicImport di)
    {
        return new SharpTSPromise(DynamicImportAsync(di));
    }

    /// <summary>
    /// Evaluates an import.meta expression, returning an object with module metadata.
    /// </summary>
    private object? EvaluateImportMeta(Expr.ImportMeta im)
    {
        // Get current module path
        string url = _currentModule?.Path ?? "";

        // Convert to file:// URL format if it's a file path
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["url"] = url
        });
    }

    /// <summary>
    /// Asynchronously loads a module dynamically.
    /// </summary>
    private async Task<object?> DynamicImportAsync(Expr.DynamicImport di)
    {
        // Evaluate the path expression
        object? pathValue = Evaluate(di.PathExpression);
        string specifier = pathValue?.ToString()
            ?? throw new InterpreterException(" Dynamic import path cannot be null.");

        // Create resolver if needed (single-file mode without module context)
        _moduleResolver ??= new ModuleResolver(
            _currentModule?.Path ?? Directory.GetCurrentDirectory());

        string currentPath = _currentModule?.Path ?? Directory.GetCurrentDirectory();
        string absolutePath = _moduleResolver.ResolveModulePath(specifier, currentPath);

        // Check if module is already loaded
        if (_loadedModules.TryGetValue(absolutePath, out var cached))
        {
            return cached.ExportsAsObject();
        }

        // Load and execute the module
        ParsedModule module = _moduleResolver.LoadModule(absolutePath);

        // Type check the new module (optional - errors become Promise rejections)
        // Note: Skipping type checking for dynamic imports for flexibility

        // Execute the module
        ExecuteModule(module);

        // Return the module namespace
        return _loadedModules[absolutePath].ExportsAsObject();
    }

    // Counter for generating unique anonymous class expression names
    private int _classExprCounter = 0;

    /// <summary>
    /// Evaluates a class expression and returns the SharpTSClass object.
    /// Unlike class declarations, the class is not added to the environment.
    /// </summary>
    private object? EvaluateClassExpression(Expr.ClassExpr classExpr)
    {
        // Generate name for anonymous classes
        string className = classExpr.Name?.Lexeme ?? $"$ClassExpr_{++_classExprCounter}";

        // Resolve superclass if present
        object? superclass = null;
        if (classExpr.Superclass != null)
        {
            superclass = _environment.Get(classExpr.Superclass);
            if (superclass is not SharpTSClass)
            {
                throw new Exception("Superclass must be a class.");
            }
        }

        // Create environment for class body
        RuntimeEnvironment classEnv = _environment;

        // If named, define the name in class body scope for self-reference
        if (classExpr.Name != null)
        {
            classEnv = new RuntimeEnvironment(_environment);
            classEnv.Define(classExpr.Name.Lexeme, null); // Placeholder for self-reference
        }

        if (classExpr.Superclass != null)
        {
            classEnv = new RuntimeEnvironment(classEnv);
            classEnv.Define("super", superclass);
        }

        var savedEnv = _environment;
        _environment = classEnv;

        try
        {
            Dictionary<string, SharpTSFunction> methods = [];
            Dictionary<string, SharpTSFunction> staticMethods = [];
            Dictionary<string, object?> staticProperties = [];
            List<Stmt.Field> instanceFields = [];

            // Check if we have static initializers for proper ordering
            bool hasStaticInitializers = classExpr.StaticInitializers != null && classExpr.StaticInitializers.Count > 0;

            // Process fields
            foreach (Stmt.Field field in classExpr.Fields)
            {
                if (field.IsStatic)
                {
                    if (!hasStaticInitializers)
                    {
                        // Old behavior: evaluate immediately
                        object? fieldValue = field.Initializer != null
                            ? Evaluate(field.Initializer)
                            : null;
                        staticProperties[field.Name.Lexeme] = fieldValue;
                    }
                    // else: will be evaluated via StaticInitializers with proper 'this' binding
                }
                else
                {
                    instanceFields.Add(field);
                }
            }

            // Process methods (skip overload signatures with no body)
            foreach (Stmt.Function method in classExpr.Methods.Where(m => m.Body != null))
            {
                SharpTSFunction func = new(method, _environment);
                if (method.IsStatic)
                {
                    staticMethods[method.Name.Lexeme] = func;
                }
                else
                {
                    methods[method.Name.Lexeme] = func;
                }
            }

            // Create accessor functions
            Dictionary<string, SharpTSFunction> getters = [];
            Dictionary<string, SharpTSFunction> setters = [];

            if (classExpr.Accessors != null)
            {
                foreach (var accessor in classExpr.Accessors)
                {
                    var funcStmt = new Stmt.Function(
                        accessor.Name,
                        null,
                        null,
                        accessor.SetterParam != null ? [accessor.SetterParam] : [],
                        accessor.Body,
                        accessor.ReturnType);

                    SharpTSFunction func = new(funcStmt, _environment);

                    if (accessor.Kind.Type == TokenType.GET)
                    {
                        getters[accessor.Name.Lexeme] = func;
                    }
                    else
                    {
                        setters[accessor.Name.Lexeme] = func;
                    }
                }
            }

            SharpTSClass klass = new(
                className,
                (SharpTSClass?)superclass,
                methods,
                staticMethods,
                staticProperties,
                getters,
                setters,
                classExpr.IsAbstract,
                instanceFields);

            // Execute static initializers in declaration order (if present)
            if (hasStaticInitializers)
            {
                // Create temporary environment with 'this' bound to the class
                // Also make the class name available so code like Foo.x works
                var staticEnv = new RuntimeEnvironment(_environment);
                staticEnv.Define("this", klass);
                if (classExpr.Name != null)
                {
                    staticEnv.Define(classExpr.Name.Lexeme, klass);
                }

                var prevEnv = _environment;
                _environment = staticEnv;

                try
                {
                    foreach (var initializer in classExpr.StaticInitializers!)
                    {
                        switch (initializer)
                        {
                            case Stmt.Field field when field.IsStatic:
                                object? fieldValue = field.Initializer != null
                                    ? Evaluate(field.Initializer)
                                    : null;
                                klass.SetStaticProperty(field.Name.Lexeme, fieldValue);
                                break;

                            case Stmt.StaticBlock block:
                                foreach (var blockStmt in block.Body)
                                {
                                    var result = Execute(blockStmt);
                                    if (result.IsAbrupt)
                                    {
                                        // Handle throw from static block
                                        if (result.Type == ExecutionResult.ResultType.Throw)
                                        {
                                            throw new Exception($"Error in static block: {Stringify(result.Value)}");
                                        }
                                        // Return, break, continue are not allowed (validated by type checker)
                                    }
                                }
                                break;
                        }
                    }
                }
                finally
                {
                    _environment = prevEnv;
                }
            }

            // Update self-reference if named
            if (classExpr.Name != null)
            {
                classEnv.Assign(classExpr.Name, klass);
            }

            return klass;
        }
        finally
        {
            _environment = savedEnv;
        }
    }
}
