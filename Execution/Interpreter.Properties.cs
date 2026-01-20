using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    /// <summary>
    /// Resolves a qualified class name (Namespace.SubNs.ClassName) to a runtime class.
    /// </summary>
    private object? ResolveQualifiedClass(List<Token>? namespacePath, Token className)
    {
        if (namespacePath == null || namespacePath.Count == 0)
        {
            // Simple class name - use existing lookup
            return _environment.Get(className);
        }

        // Start from first namespace
        object? current = _environment.Get(namespacePath[0]);

        // Traverse namespace chain
        for (int i = 1; i < namespacePath.Count; i++)
        {
            if (current is not SharpTSNamespace ns)
            {
                throw new Exception($"Runtime Error: '{namespacePath[i - 1].Lexeme}' is not a namespace.");
            }
            current = ns.Get(namespacePath[i].Lexeme);
            if (current == null)
            {
                throw new Exception($"Runtime Error: '{namespacePath[i].Lexeme}' does not exist in namespace '{ns.Name}'.");
            }
        }

        // Now get the class from the final namespace
        if (current is not SharpTSNamespace finalNs)
        {
            throw new Exception($"Runtime Error: '{namespacePath[^1].Lexeme}' is not a namespace.");
        }

        return finalNs.Get(className.Lexeme);
    }

    /// <summary>
    /// Evaluates a <c>new</c> expression, instantiating a class.
    /// </summary>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A new <see cref="SharpTSInstance"/> of the class.</returns>
    /// <remarks>
    /// Looks up the class by name, evaluates constructor arguments,
    /// and invokes the class's <see cref="SharpTSClass.Call"/> method.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#constructors">TypeScript Constructors</seealso>
    private object? EvaluateNew(Expr.New newExpr)
    {
        // Built-in types only apply when there's no namespace path
        bool isSimpleName = newExpr.NamespacePath == null || newExpr.NamespacePath.Count == 0;

        // Handle new Date(...) constructor
        if (isSimpleName && newExpr.ClassName.Lexeme == "Date")
        {
            List<object?> args = newExpr.Arguments.Select(Evaluate).ToList();
            return CreateDate(args);
        }

        // Handle new RegExp(...) constructor
        if (isSimpleName && newExpr.ClassName.Lexeme == "RegExp")
        {
            List<object?> args = newExpr.Arguments.Select(Evaluate).ToList();
            var pattern = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return new SharpTSRegExp(pattern, flags);
        }

        // Handle new Map(...) constructor
        if (isSimpleName && newExpr.ClassName.Lexeme == "Map")
        {
            if (newExpr.Arguments.Count == 0)
            {
                return new SharpTSMap();
            }
            // Handle new Map([[k1, v1], [k2, v2], ...])
            var arg = Evaluate(newExpr.Arguments[0]);
            if (arg is SharpTSArray entriesArray)
            {
                return SharpTSMap.FromEntries(entriesArray);
            }
            return new SharpTSMap();
        }

        // Handle new Set(...) constructor
        if (isSimpleName && newExpr.ClassName.Lexeme == "Set")
        {
            if (newExpr.Arguments.Count == 0)
            {
                return new SharpTSSet();
            }
            // Handle new Set([v1, v2, v3, ...])
            var arg = Evaluate(newExpr.Arguments[0]);
            if (arg is SharpTSArray valuesArray)
            {
                return SharpTSSet.FromArray(valuesArray);
            }
            return new SharpTSSet();
        }

        // Handle new WeakMap() constructor (empty only)
        if (isSimpleName && newExpr.ClassName.Lexeme == "WeakMap")
        {
            return new SharpTSWeakMap();
        }

        // Handle new WeakSet() constructor (empty only)
        if (isSimpleName && newExpr.ClassName.Lexeme == "WeakSet")
        {
            return new SharpTSWeakSet();
        }

        // Handle new Error(...) and error subtype constructors
        if (isSimpleName && IsErrorType(newExpr.ClassName.Lexeme))
        {
            List<object?> args = newExpr.Arguments.Select(Evaluate).ToList();
            return ErrorBuiltIns.CreateError(newExpr.ClassName.Lexeme, args);
        }

        object? klass = ResolveQualifiedClass(newExpr.NamespacePath, newExpr.ClassName);
        if (klass is not SharpTSClass sharpClass)
        {
             throw new Exception("Type Error: Can only instantiate classes.");
        }

        // Runtime check for abstract class instantiation (backup to type checker)
        if (sharpClass.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{sharpClass.Name}'.");
        }

        List<object?> arguments = [];
        foreach (Expr argument in newExpr.Arguments)
        {
            arguments.Add(Evaluate(argument));
        }

        return sharpClass.Call(this, arguments);
    }

    /// <summary>
    /// Creates a Date object from constructor arguments.
    /// </summary>
    private static SharpTSDate CreateDate(List<object?> args)
    {
        return args.Count switch
        {
            0 => new SharpTSDate(),
            1 when args[0] is double ms => new SharpTSDate(ms),
            1 when args[0] is string str => new SharpTSDate(str),
            _ => new SharpTSDate(
                (int)(double)args[0]!,
                (int)(double)args[1]!,
                args.Count > 2 ? (int)(double)args[2]! : 1,
                args.Count > 3 ? (int)(double)args[3]! : 0,
                args.Count > 4 ? (int)(double)args[4]! : 0,
                args.Count > 5 ? (int)(double)args[5]! : 0,
                args.Count > 6 ? (int)(double)args[6]! : 0)
        };
    }

    /// <summary>
    /// Evaluates a <c>this</c> expression, returning the current instance.
    /// </summary>
    /// <param name="expr">The this expression AST node.</param>
    /// <returns>The current class instance bound to <c>this</c>.</returns>
    /// <remarks>
    /// The <c>this</c> keyword is bound in the environment when a method is called
    /// on an instance.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#this-at-runtime-in-classes">TypeScript this in Classes</seealso>
    private object? EvaluateThis(Expr.This expr)
    {
        return _environment.Get(expr.Keyword);
    }

    /// <summary>
    /// Evaluates a property access expression (dot notation).
    /// </summary>
    /// <param name="get">The property access expression AST node.</param>
    /// <returns>The value of the property, or a bound method.</returns>
    /// <remarks>
    /// Handles optional chaining (<c>?.</c>), static member access on classes,
    /// enum member access, instance properties/methods, object properties,
    /// string methods, array methods, and Math object members.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-7.html#optional-chaining">TypeScript Optional Chaining</seealso>
    private object? EvaluateGet(Expr.Get get)
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

        object? obj = Evaluate(get.Object);
        return EvaluateGetOnObject(get, obj);
    }

    /// <summary>
    /// Core property access logic, shared between sync and async evaluation.
    /// </summary>
    private object? EvaluateGetOnObject(Expr.Get get, object? obj)
    {
        // Handle optional chaining - return undefined if object is null or undefined
        if (get.Optional && (obj == null || obj is Runtime.Types.SharpTSUndefined))
        {
            return Runtime.Types.SharpTSUndefined.Instance;
        }

        // Handle static member access on class
        if (obj is SharpTSClass klass)
        {
            // Try static method first
            SharpTSFunction? staticMethod = klass.FindStaticMethod(get.Name.Lexeme);
            if (staticMethod != null) return staticMethod;

            // Try static property
            if (klass.HasStaticProperty(get.Name.Lexeme))
            {
                return klass.GetStaticProperty(get.Name.Lexeme);
            }

            throw new Exception($"Runtime Error: Static member '{get.Name.Lexeme}' does not exist on class '{klass.Name}'.");
        }

        // Handle namespace member access
        if (obj is SharpTSNamespace nsObj)
        {
            if (nsObj.HasMember(get.Name.Lexeme))
            {
                return nsObj.Get(get.Name.Lexeme);
            }
            throw new Exception($"Runtime Error: '{get.Name.Lexeme}' does not exist on namespace '{nsObj.Name}'.");
        }

        // Handle enum member access
        if (obj is SharpTSEnum enumObj)
        {
            return enumObj.GetMember(get.Name.Lexeme);
        }

        // Handle const enum member access
        if (obj is ConstEnumValues constEnumObj)
        {
            return constEnumObj.GetMember(get.Name.Lexeme);
        }

        if (obj is SharpTSInstance instance)
        {
            instance.SetInterpreter(this);
            return instance.Get(get.Name);
        }
        if (obj is SharpTSObject simpleObj)
        {
            var value = simpleObj.GetProperty(get.Name.Lexeme);
            // Bind 'this' for object method shorthand functions
            if (value is SharpTSArrowFunction arrowFunc && arrowFunc.IsObjectMethod)
            {
                return arrowFunc.Bind(simpleObj);
            }
            return value;
        }

        // Handle built-in instance members: strings, arrays, Math, Promise
        if (obj != null)
        {
            var member = BuiltInRegistry.Instance.GetInstanceMember(obj, get.Name.Lexeme);
            if (member != null)
            {
                // Bind methods to their receiver, return properties directly
                if (member is BuiltInMethod m) return m.Bind(obj);
                if (member is BuiltInAsyncMethod am) return am.Bind(obj);
                return member;
            }

            // If we have a built-in type but didn't find the member, throw a specific error
            if (BuiltInRegistry.Instance.HasInstanceMembers(obj))
            {
                string typeName = obj switch
                {
                    string => "string",
                    SharpTSArray => "array",
                    SharpTSMath => "Math",
                    _ => obj.GetType().Name
                };
                throw new Exception($"Runtime Error: Property '{get.Name.Lexeme}' does not exist on {typeName}.");
            }
        }

        throw new Exception("Only instances and objects have properties.");
    }

    /// <summary>
    /// Evaluates a property assignment expression (dot notation with assignment).
    /// </summary>
    /// <param name="set">The property assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Supports static property assignment on classes, instance field assignment,
    /// and simple object property assignment.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    private object? EvaluateSet(Expr.Set set)
    {
        object? obj = Evaluate(set.Object);
        object? value = Evaluate(set.Value);
        return EvaluateSetOnObject(set, obj, value);
    }

    /// <summary>
    /// Core property assignment logic, shared between sync and async evaluation.
    /// </summary>
    private object? EvaluateSetOnObject(Expr.Set set, object? obj, object? value)
    {
        bool strictMode = _environment.IsStrictMode;

        // Handle static property assignment
        if (obj is SharpTSClass klass)
        {
            klass.SetStaticProperty(set.Name.Lexeme, value);
            return value;
        }

        // Handle globalThis property assignment
        if (obj is SharpTSGlobalThis globalThis)
        {
            globalThis.SetProperty(set.Name.Lexeme, value);
            return value;
        }

        if (obj is SharpTSInstance instance)
        {
            instance.SetInterpreter(this);
            if (strictMode)
            {
                instance.SetStrict(set.Name, value, strictMode);
            }
            else
            {
                instance.Set(set.Name, value);
            }
            return value;
        }
        if (obj is SharpTSObject simpleObj)
        {
            if (strictMode)
            {
                simpleObj.SetPropertyStrict(set.Name.Lexeme, value, strictMode);
            }
            else
            {
                simpleObj.SetProperty(set.Name.Lexeme, value);
            }
            return value;
        }

        // Handle RegExp.lastIndex assignment
        if (obj is SharpTSRegExp regex)
        {
            if (set.Name.Lexeme == "lastIndex")
            {
                regex.LastIndex = (int)(double)value!;
                return value;
            }
            throw new Exception($"Runtime Error: Cannot set property '{set.Name.Lexeme}' on RegExp.");
        }

        // Handle Error property assignment (name, message, stack)
        if (obj is SharpTSError error)
        {
            if (ErrorBuiltIns.SetMember(error, set.Name.Lexeme, value))
            {
                return value;
            }
            throw new Exception($"Runtime Error: Cannot set property '{set.Name.Lexeme}' on Error.");
        }

        throw new Exception("Only instances and objects have fields.");
    }

    /// <summary>
    /// Evaluates a variable assignment expression.
    /// </summary>
    /// <param name="assign">The assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Evaluates the right-hand side value and updates the variable
    /// in the current <see cref="RuntimeEnvironment"/>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html">TypeScript Variable Declarations</seealso>
    private object? EvaluateAssign(Expr.Assign assign)
    {
        object? value = Evaluate(assign.Value);
        
        if (_locals.TryGetValue(assign, out int distance))
        {
            _environment.AssignAt(distance, assign.Name, value);
        }
        else
        {
            _environment.Assign(assign.Name, value);
        }

        return value;
    }

    /// <summary>
    /// Checks if a type name is a built-in Error type.
    /// Delegates to ErrorBuiltIns for centralized type name knowledge.
    /// </summary>
    private static bool IsErrorType(string name) => ErrorBuiltIns.IsErrorTypeName(name);

    #region ES2022 Private Class Elements

    /// <summary>
    /// Evaluates a private field access expression (obj.#field).
    /// </summary>
    private object? EvaluateGetPrivate(Expr.GetPrivate expr)
    {
        object? obj = Evaluate(expr.Object);
        string fieldName = expr.Name.Lexeme;

        // Handle static private field access on class
        if (obj is SharpTSClass klass)
        {
            // For static private fields, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            if (klass.HasStaticPrivateField(fieldName))
            {
                return klass.GetStaticPrivateField(fieldName);
            }

            throw new Exception($"Runtime Error: Static private field '{fieldName}' does not exist on class '{klass.Name}'.");
        }

        // Instance private field access
        if (obj is SharpTSInstance instance)
        {
            // For instance private fields, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            return declaringClass.GetPrivateField(instance, fieldName);
        }

        throw new Exception($"Runtime Error: Cannot read private field '{fieldName}' from non-class value.");
    }

    /// <summary>
    /// Evaluates a private field assignment expression (obj.#field = value).
    /// </summary>
    private object? EvaluateSetPrivate(Expr.SetPrivate expr)
    {
        object? obj = Evaluate(expr.Object);
        object? value = Evaluate(expr.Value);
        string fieldName = expr.Name.Lexeme;

        // Handle static private field assignment on class
        if (obj is SharpTSClass klass)
        {
            // For static private fields, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            if (klass.HasStaticPrivateField(fieldName))
            {
                klass.SetStaticPrivateField(fieldName, value);
                return value;
            }

            throw new Exception($"Runtime Error: Static private field '{fieldName}' does not exist on class '{klass.Name}'.");
        }

        // Instance private field assignment
        if (obj is SharpTSInstance instance)
        {
            // For instance private fields, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            declaringClass.SetPrivateField(instance, fieldName, value);
            return value;
        }

        throw new Exception($"Runtime Error: Cannot write private field '{fieldName}' to non-class value.");
    }

    /// <summary>
    /// Evaluates a private method call expression (obj.#method(...)).
    /// </summary>
    private object? EvaluateCallPrivate(Expr.CallPrivate expr)
    {
        object? obj = Evaluate(expr.Object);
        string methodName = expr.Name.Lexeme;

        // Evaluate arguments
        List<object?> arguments = [];
        foreach (var arg in expr.Arguments)
        {
            arguments.Add(Evaluate(arg));
        }

        // Handle static private method call on class
        if (obj is SharpTSClass klass)
        {
            // For static private methods, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            var method = klass.GetStaticPrivateMethod(methodName);
            if (method == null)
            {
                throw new Exception($"Runtime Error: Static private method '{methodName}' does not exist on class '{klass.Name}'.");
            }

            return method.Call(this, arguments);
        }

        // Instance private method call
        if (obj is SharpTSInstance instance)
        {
            // For instance private methods, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            var method = declaringClass.GetPrivateMethod(methodName);
            if (method == null)
            {
                throw new Exception($"Runtime Error: Private method '{methodName}' does not exist on class '{declaringClass.Name}'.");
            }

            // Bind method to instance
            return method.Bind(instance).Call(this, arguments);
        }

        throw new Exception($"Runtime Error: Cannot call private method '{methodName}' on non-class value.");
    }

    #endregion
}
