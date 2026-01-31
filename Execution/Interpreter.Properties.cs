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
    /// Extracts the simple class name from a new expression callee for runtime use.
    /// </summary>
    private static string? GetSimpleClassName(Expr callee)
    {
        return callee is Expr.Variable v ? v.Name.Lexeme : null;
    }

    /// <summary>
    /// Checks if the callee is a simple identifier (not a member access or complex expression).
    /// </summary>
    private static bool IsSimpleIdentifier(Expr callee) => callee is Expr.Variable;

    /// <summary>
    /// Core implementation for evaluating 'new' expressions, shared between sync and async paths.
    /// Handles all built-in types (Date, RegExp, Map, Set, WeakMap, WeakSet, Error) and user classes.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating arguments.</param>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A ValueTask containing the instantiated object.</returns>
    private async ValueTask<object?> EvaluateNewCore(IEvaluationContext ctx, Expr.New newExpr)
    {
        // Built-in types only apply when callee is a simple identifier
        bool isSimpleName = IsSimpleIdentifier(newExpr.Callee);
        string? simpleClassName = GetSimpleClassName(newExpr.Callee);

        // Handle new Date(...) constructor
        if (isSimpleName && simpleClassName == "Date")
        {
            List<object?> args = await ctx.EvaluateAllAsync(newExpr.Arguments);
            return CreateDate(args);
        }

        // Handle new RegExp(...) constructor
        if (isSimpleName && simpleClassName == "RegExp")
        {
            List<object?> args = await ctx.EvaluateAllAsync(newExpr.Arguments);
            var pattern = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
            return new SharpTSRegExp(pattern, flags);
        }

        // Handle new Map(...) constructor
        if (isSimpleName && simpleClassName == "Map")
        {
            if (newExpr.Arguments.Count == 0)
            {
                return new SharpTSMap();
            }
            // Handle new Map([[k1, v1], [k2, v2], ...])
            var arg = await ctx.EvaluateExprAsync(newExpr.Arguments[0]);
            if (arg is SharpTSArray entriesArray)
            {
                return SharpTSMap.FromEntries(entriesArray);
            }
            return new SharpTSMap();
        }

        // Handle new Set(...) constructor
        if (isSimpleName && simpleClassName == "Set")
        {
            if (newExpr.Arguments.Count == 0)
            {
                return new SharpTSSet();
            }
            // Handle new Set([v1, v2, v3, ...])
            var arg = await ctx.EvaluateExprAsync(newExpr.Arguments[0]);
            if (arg is SharpTSArray valuesArray)
            {
                return SharpTSSet.FromArray(valuesArray);
            }
            return new SharpTSSet();
        }

        // Handle new WeakMap() constructor (empty only)
        if (isSimpleName && simpleClassName == "WeakMap")
        {
            return new SharpTSWeakMap();
        }

        // Handle new WeakSet() constructor (empty only)
        if (isSimpleName && simpleClassName == "WeakSet")
        {
            return new SharpTSWeakSet();
        }

        // Handle new SharedArrayBuffer(byteLength) constructor
        if (isSimpleName && simpleClassName == "SharedArrayBuffer")
        {
            List<object?> args = await ctx.EvaluateAllAsync(newExpr.Arguments);
            return WorkerBuiltIns.SharedArrayBufferConstructor.Call(this, args);
        }

        // Handle new MessageChannel() constructor
        if (isSimpleName && simpleClassName == "MessageChannel")
        {
            return WorkerBuiltIns.MessageChannelConstructor.Call(this, []);
        }

        // Handle TypedArray constructors (Int8Array, Uint8Array, etc.)
        if (isSimpleName && simpleClassName != null && IsTypedArrayName(simpleClassName))
        {
            List<object?> args = await ctx.EvaluateAllAsync(newExpr.Arguments);
            return WorkerBuiltIns.GetTypedArrayConstructor(simpleClassName).Call(this, args);
        }

        // Handle new Error(...) and error subtype constructors
        if (isSimpleName && simpleClassName != null && IsErrorType(simpleClassName))
        {
            List<object?> args = await ctx.EvaluateAllAsync(newExpr.Arguments);
            return ErrorBuiltIns.CreateError(simpleClassName, args);
        }

        // Handle new EventEmitter() constructor for simple identifier
        if (isSimpleName && simpleClassName == "EventEmitter")
        {
            return new SharpTSEventEmitter();
        }

        // Handle new Promise((resolve, reject) => { ... }) constructor
        if (isSimpleName && simpleClassName == "Promise")
        {
            if (newExpr.Arguments.Count != 1)
            {
                throw new InterpreterException($"Promise constructor requires exactly 1 argument (executor function), got {newExpr.Arguments.Count}.");
            }

            object? executor = await ctx.EvaluateExprAsync(newExpr.Arguments[0]);
            return CreatePromiseFromExecutor(executor);
        }

        // Evaluate the callee expression to get the class/constructor
        object? klass = await ctx.EvaluateExprAsync(newExpr.Callee);

        // Handle callable constructors (like SharpTSEventEmitterConstructor)
        // These implement ISharpTSCallable and are used for module-imported types
        if (klass is ISharpTSCallable callable && klass is not SharpTSClass && klass is not BoundFunction)
        {
            List<object?> ctorArgs = await ctx.EvaluateAllAsync(newExpr.Arguments);
            return callable.Call(this, ctorArgs);
        }

        // Bound functions cannot be used as constructors (JS spec compliance)
        if (klass is BoundFunction)
        {
            throw new InterpreterException("Bound functions cannot be used as constructors.");
        }

        if (klass is not SharpTSClass sharpClass)
        {
             throw new Exception("Type Error: Can only instantiate classes.");
        }

        // Runtime check for abstract class instantiation (backup to type checker)
        if (sharpClass.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{sharpClass.Name}'.");
        }

        List<object?> arguments = await ctx.EvaluateAllAsync(newExpr.Arguments);
        return sharpClass.Call(this, arguments);
    }

    /// <summary>
    /// Evaluates a <c>new</c> expression, instantiating a class.
    /// </summary>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A new <see cref="SharpTSInstance"/> of the class.</returns>
    /// <remarks>
    /// Looks up the class by evaluating the callee expression,
    /// and invokes the class's <see cref="SharpTSClass.Call"/> method.
    /// Supports new on expressions: new ctor(), new Namespace.Class(), new (expr)()
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#constructors">TypeScript Constructors</seealso>
    private object? EvaluateNew(Expr.New newExpr)
    {
        // Use sync context - ValueTask with sync context completes synchronously
        return EvaluateNewCore(_syncContext, newExpr).GetAwaiter().GetResult();
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
    /// Uses TypeCategoryResolver for unified type dispatch.
    /// </summary>
    private object? EvaluateGetOnObject(Expr.Get get, object? obj)
    {
        // Handle optional chaining - return undefined if object is null or undefined
        if (get.Optional && (obj == null || obj is Runtime.Types.SharpTSUndefined))
        {
            return Runtime.Types.SharpTSUndefined.Instance;
        }

        var category = TypeCategoryResolver.ClassifyRuntime(obj);
        string memberName = get.Name.Lexeme;

        // Category-based dispatch for user-defined types
        return category switch
        {
            TypeCategory.Class when obj is SharpTSClass klass =>
                EvaluateGetOnClass(klass, memberName),
            TypeCategory.Namespace when obj is SharpTSNamespace nsObj =>
                EvaluateGetOnNamespace(nsObj, memberName),
            TypeCategory.Enum when obj is SharpTSEnum enumObj =>
                enumObj.GetMember(memberName),
            TypeCategory.Enum when obj is ConstEnumValues constEnumObj =>
                constEnumObj.GetMember(memberName),
            TypeCategory.Instance when obj is SharpTSInstance instance =>
                EvaluateGetOnInstance(instance, get.Name),
            TypeCategory.Record when obj is SharpTSObject simpleObj =>
                EvaluateGetOnRecord(simpleObj, memberName),
            _ => EvaluateGetOnFallback(obj, memberName)
        };
    }

    /// <summary>
    /// Evaluates property access on a class (static members).
    /// </summary>
    private static object? EvaluateGetOnClass(SharpTSClass klass, string memberName)
    {
        // Try static auto-accessor first (TypeScript 4.9+)
        if (klass.HasStaticAutoAccessor(memberName))
        {
            return klass.GetStaticAutoAccessorValue(memberName);
        }

        // Try static method
        SharpTSFunction? staticMethod = klass.FindStaticMethod(memberName);
        if (staticMethod != null) return staticMethod;

        // Try static property
        if (klass.HasStaticProperty(memberName))
        {
            return klass.GetStaticProperty(memberName);
        }

        throw new Exception($"Runtime Error: Static member '{memberName}' does not exist on class '{klass.Name}'.");
    }

    /// <summary>
    /// Evaluates property access on a namespace.
    /// </summary>
    private static object? EvaluateGetOnNamespace(SharpTSNamespace nsObj, string memberName)
    {
        if (nsObj.HasMember(memberName))
        {
            return nsObj.Get(memberName);
        }
        throw new Exception($"Runtime Error: '{memberName}' does not exist on namespace '{nsObj.Name}'.");
    }

    /// <summary>
    /// Evaluates property access on a class instance.
    /// </summary>
    private object? EvaluateGetOnInstance(SharpTSInstance instance, Token memberName)
    {
        instance.SetInterpreter(this);
        return instance.Get(memberName);
    }

    /// <summary>
    /// Evaluates property access on a record/object literal.
    /// </summary>
    private object? EvaluateGetOnRecord(SharpTSObject simpleObj, string memberName)
    {
        // Check for getter first
        var getter = simpleObj.GetGetter(memberName);
        if (getter != null)
        {
            // Invoke the getter with 'this' bound to the object
            var boundGetter = BindAccessorToObject(getter, simpleObj);
            return boundGetter.Call(this, []);
        }

        var value = simpleObj.GetProperty(memberName);
        // Bind 'this' for function expressions and object method shorthand (HasOwnThis=true)
        if (value is SharpTSArrowFunction arrowFunc && arrowFunc.HasOwnThis)
        {
            return arrowFunc.Bind(simpleObj);
        }
        return value;
    }

    /// <summary>
    /// Binds an accessor function to an object for 'this' binding.
    /// </summary>
    private static ISharpTSCallable BindAccessorToObject(ISharpTSCallable accessor, SharpTSObject obj)
    {
        if (accessor is SharpTSArrowFunction arrow && arrow.HasOwnThis)
        {
            return arrow.Bind(obj);
        }
        // For callables that don't support binding, return as-is
        return accessor;
    }

    /// <summary>
    /// Fallback for property access on built-in types and ISharpTSPropertyAccessor.
    /// </summary>
    private object? EvaluateGetOnFallback(object? obj, string memberName)
    {
        // Handle objects that implement ISharpTSPropertyAccessor (e.g., SharpTSTemplateStringsArray)
        // Only return if the accessor has this property, otherwise fall through to built-ins
        if (obj is ISharpTSPropertyAccessor accessor && accessor.HasProperty(memberName))
        {
            return accessor.GetProperty(memberName);
        }

        // Handle built-in instance members: strings, arrays, Math, Promise
        if (obj != null)
        {
            // Single registry lookup - TryGetInstanceMember returns both member and whether type is known
            var (member, isBuiltInType) = BuiltInRegistry.Instance.TryGetInstanceMember(obj, memberName);
            if (member != null)
            {
                // Bind methods to their receiver, return properties directly
                if (member is BuiltInMethod m) return m.Bind(obj);
                if (member is BuiltInAsyncMethod am) return am.Bind(obj);
                return member;
            }

            // If we have a built-in type but didn't find the member, throw a specific error
            if (isBuiltInType)
            {
                string typeName = GetRuntimeTypeName(obj);
                throw new Exception($"Runtime Error: Property '{memberName}' does not exist on {typeName}.");
            }
        }

        throw new Exception("Only instances and objects have properties.");
    }

    /// <summary>
    /// Gets a display name for a runtime object type.
    /// </summary>
    private static string GetRuntimeTypeName(object obj) => obj switch
    {
        string => "string",
        SharpTSArray => "array",
        SharpTSMath => "Math",
        SharpTSMap => "Map",
        SharpTSSet => "Set",
        SharpTSDate => "Date",
        SharpTSRegExp => "RegExp",
        SharpTSError => "Error",
        SharpTSPromise => "Promise",
        _ => obj.GetType().Name
    };

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
            // Check for static auto-accessor (TypeScript 4.9+)
            if (klass.HasStaticAutoAccessor(set.Name.Lexeme))
            {
                klass.SetStaticAutoAccessorValue(set.Name.Lexeme, value);
                return value;
            }
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
            // Check for setter first
            var setter = simpleObj.GetSetter(set.Name.Lexeme);
            if (setter != null)
            {
                // Invoke the setter with 'this' bound to the object
                var boundSetter = BindAccessorToObject(setter, simpleObj);
                boundSetter.Call(this, [value]);
                return value;
            }

            // If there's a getter but no setter, the property is read-only
            if (simpleObj.HasGetter(set.Name.Lexeme))
            {
                if (strictMode)
                {
                    throw new Exception($"TypeError: Cannot set property '{set.Name.Lexeme}' which has only a getter.");
                }
                // Non-strict mode silently fails
                return value;
            }

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

    private static bool IsTypedArrayName(string name)
    {
        return name is "Int8Array" or "Uint8Array" or "Uint8ClampedArray"
            or "Int16Array" or "Uint16Array"
            or "Int32Array" or "Uint32Array"
            or "Float32Array" or "Float64Array"
            or "BigInt64Array" or "BigUint64Array";
    }

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
