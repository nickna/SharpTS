using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Constructor and instantiation type checking.
/// </summary>
/// <remarks>
/// Contains handler for new expressions:
/// CheckNew - handles built-in types (Date, RegExp, Map, Set, WeakMap, WeakSet) and user-defined classes,
/// as well as interfaces with constructor signatures.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Finds a constructor by walking up the inheritance chain.
    /// Returns the constructor type and the class that owns it, or (null, null) if no constructor found.
    /// </summary>
    private (TypeInfo? Constructor, TypeInfo? OwningClass) FindInheritedConstructor(TypeInfo classType)
    {
        TypeInfo? current = classType;
        while (current != null)
        {
            // Handle MutableClass placeholders (e.g. Any-typed superclasses like Error)
            if (current is TypeInfo.MutableClass mc && mc.Methods.TryGetValue("constructor", out var mcCtor))
                return (mcCtor, current);

            var methods = GetMethods(current);
            if (methods?.TryGetValue("constructor", out var ctor) == true)
                return (ctor, current);
            current = GetSuperclass(current);
        }
        return (null, null);
    }

    /// <summary>
    /// Computes the substitution map for an inherited constructor's parameters. When the constructor
    /// is declared on a generic-class instantiation in the inheritance chain (e.g. <c>Mixed&lt;X&gt; extends
    /// Triple&lt;string, X, number&gt;</c>), maps that class's parameters to its type arguments and resolves
    /// those through <paramref name="currentSubs"/> (the subclass's own bindings), composing the two
    /// (so <c>B := X</c> then <c>X := boolean</c> ⇒ <c>B := boolean</c>). When the constructor is the class's
    /// own (owner is not an instantiation), returns <paramref name="currentSubs"/> unchanged.
    /// </summary>
    private Dictionary<string, TypeInfo> ComposeConstructorSubs(TypeInfo? owningClass, Dictionary<string, TypeInfo> currentSubs)
    {
        if (owningClass is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig)
        {
            Dictionary<string, TypeInfo> composed = [];
            for (int i = 0; i < gc.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                composed[gc.TypeParams[i].Name] = Substitute(ig.TypeArguments[i], currentSubs);
            return composed;
        }
        return currentSubs;
    }

    /// <summary>
    /// Extracts the simple class name from a new expression callee for error messages.
    /// </summary>
    private static string GetCalleeClassName(Expr callee)
    {
        return callee switch
        {
            Expr.Variable v => v.Name.Lexeme,
            Expr.Get g => GetCalleeClassName(g.Object) + "." + g.Name.Lexeme,
            Expr.Grouping gr => GetCalleeClassName(gr.Expression),
            _ => "<expression>"
        };
    }

    /// <summary>
    /// Checks if the callee is a simple identifier (not a member access or complex expression).
    /// </summary>
    private static bool IsSimpleIdentifier(Expr callee) => callee is Expr.Variable;

    /// <summary>
    /// Gets the simple class name from a Variable callee, or null if not a simple identifier.
    /// </summary>
    private static string? GetSimpleClassName(Expr callee)
    {
        return callee is Expr.Variable v ? v.Name.Lexeme : null;
    }

    private TypeInfo CheckNew(Expr.New newExpr)
    {
        // Built-in types only apply when callee is a simple identifier
        bool isSimpleName = IsSimpleIdentifier(newExpr.Callee);
        string? simpleClassName = GetSimpleClassName(newExpr.Callee);

        // Handle new Date() constructor
        if (isSimpleName && simpleClassName == "Date")
        {
            // Date() accepts 0-7 arguments
            if (newExpr.Arguments.Count > 7)
            {
                throw new TypeCheckException("Date constructor accepts at most 7 arguments.", tsCode: "TS2554");
            }

            // Validate argument types
            foreach (var arg in newExpr.Arguments)
            {
                var argType = CheckExpr(arg);
                // First argument can be number (milliseconds) or string (ISO string)
                // Remaining arguments must be numbers (year, month, day, hours, minutes, seconds, ms)
                if (newExpr.Arguments.Count == 1)
                {
                    if (!IsNumber(argType) && !IsString(argType) && argType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" Date constructor single argument must be a number or string, got '{argType}'.", tsCode: "TS2345");
                    }
                }
                else if (!IsNumber(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Date constructor arguments must be numbers, got '{argType}'.", tsCode: "TS2345");
                }
            }

            return new TypeInfo.Date();
        }

        // Handle new RegExp() constructor
        if (isSimpleName && simpleClassName == "RegExp")
        {
            // RegExp() accepts 0-2 arguments (pattern, flags)
            if (newExpr.Arguments.Count > 2)
            {
                throw new TypeCheckException("RegExp constructor accepts at most 2 arguments.", tsCode: "TS2554");
            }

            // Validate argument types. ECMA-262 21.2.3.1: undefined pattern/flags
            // is treated as the empty string (`new RegExp(undefined) === /(?:)/`).
            if (newExpr.Arguments.Count >= 1)
            {
                var patternType = CheckExpr(newExpr.Arguments[0]);
                if (!IsString(patternType) && patternType is not TypeInfo.Any
                    && patternType is not TypeInfo.Undefined && patternType is not TypeInfo.Null)
                {
                    throw new TypeCheckException($" RegExp pattern must be a string, got '{patternType}'.", tsCode: "TS2345");
                }
            }

            if (newExpr.Arguments.Count == 2)
            {
                var flagsType = CheckExpr(newExpr.Arguments[1]);
                if (!IsString(flagsType) && flagsType is not TypeInfo.Any
                    && flagsType is not TypeInfo.Undefined && flagsType is not TypeInfo.Null)
                {
                    throw new TypeCheckException($" RegExp flags must be a string, got '{flagsType}'.", tsCode: "TS2345");
                }
            }

            return new TypeInfo.RegExp();
        }

        // Handle new Map() and new Map<K, V>() constructor
        if (isSimpleName && simpleClassName == "Map")
        {
            // Map() accepts 0-1 arguments (optional iterable of entries)
            if (newExpr.Arguments.Count > 1)
            {
                throw new TypeCheckException("Map constructor accepts at most 1 argument.", tsCode: "TS2554");
            }

            // Validate argument if provided
            foreach (var arg in newExpr.Arguments)
            {
                CheckExpr(arg);
            }

            // Determine key and value types from type arguments or default to any
            TypeInfo keyType = new TypeInfo.Any();
            TypeInfo valueType = new TypeInfo.Any();

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 2)
            {
                keyType = ToTypeInfo(newExpr.TypeArgs[0]);
                valueType = ToTypeInfo(newExpr.TypeArgs[1]);
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException("Map requires exactly 2 type arguments: Map<K, V>", tsCode: "TS2314");
            }

            return new TypeInfo.Map(keyType, valueType);
        }

        // Handle new Set() and new Set<T>() constructor
        if (isSimpleName && simpleClassName == "Set")
        {
            // Set() accepts 0-1 arguments (optional iterable of values)
            if (newExpr.Arguments.Count > 1)
            {
                throw new TypeCheckException("Set constructor accepts at most 1 argument.", tsCode: "TS2554");
            }

            // Validate argument if provided
            foreach (var arg in newExpr.Arguments)
            {
                CheckExpr(arg);
            }

            // Determine element type from type argument or default to any
            TypeInfo elementType = new TypeInfo.Any();

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                elementType = ToTypeInfo(newExpr.TypeArgs[0]);
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException("Set requires exactly 1 type argument: Set<T>", tsCode: "TS2314");
            }

            return new TypeInfo.Set(elementType);
        }

        // Handle new WeakMap() and new WeakMap<K, V>() constructor
        if (isSimpleName && simpleClassName == "WeakMap")
        {
            // WeakMap() accepts 0 arguments only (no iterable initialization)
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException("WeakMap constructor does not accept arguments.", tsCode: "TS2554");
            }

            // Determine key and value types from type arguments or default to any
            TypeInfo keyType = new TypeInfo.Any();
            TypeInfo valueType = new TypeInfo.Any();

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 2)
            {
                keyType = ToTypeInfo(newExpr.TypeArgs[0]);
                valueType = ToTypeInfo(newExpr.TypeArgs[1]);

                // Validate that key type is not a primitive
                if (IsPrimitiveType(keyType))
                {
                    throw new TypeCheckException($" WeakMap keys must be objects, not '{keyType}'.", tsCode: "TS2345");
                }
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException("WeakMap requires exactly 2 type arguments: WeakMap<K, V>", tsCode: "TS2314");
            }

            return new TypeInfo.WeakMap(keyType, valueType);
        }

        // Handle new WeakSet() and new WeakSet<T>() constructor
        if (isSimpleName && simpleClassName == "WeakSet")
        {
            // WeakSet() accepts 0 arguments only (no iterable initialization)
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException("WeakSet constructor does not accept arguments.", tsCode: "TS2554");
            }

            // Determine element type from type argument or default to any
            TypeInfo elementType = new TypeInfo.Any();

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                elementType = ToTypeInfo(newExpr.TypeArgs[0]);

                // Validate that element type is not a primitive
                if (IsPrimitiveType(elementType))
                {
                    throw new TypeCheckException($" WeakSet values must be objects, not '{elementType}'.", tsCode: "TS2345");
                }
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException("WeakSet requires exactly 1 type argument: WeakSet<T>", tsCode: "TS2314");
            }

            return new TypeInfo.WeakSet(elementType);
        }

        // Handle new WeakRef() and new WeakRef<T>() constructor
        if (isSimpleName && simpleClassName == "WeakRef")
        {
            // WeakRef() accepts exactly 1 argument (the target)
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException("WeakRef constructor requires exactly 1 argument.", tsCode: "TS2554");
            }

            // Determine target type from type argument or infer from argument
            TypeInfo targetType;

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                targetType = ToTypeInfo(newExpr.TypeArgs[0]);

                // Validate that target type is not a primitive
                if (IsPrimitiveType(targetType))
                {
                    throw new TypeCheckException($" WeakRef target must be an object, not '{targetType}'.", tsCode: "TS2345");
                }
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException("WeakRef requires exactly 1 type argument: WeakRef<T>", tsCode: "TS2314");
            }
            else
            {
                // Infer from argument type
                targetType = CheckExpr(newExpr.Arguments[0]);
            }

            return new TypeInfo.WeakRef(targetType);
        }

        // Handle new FinalizationRegistry(callback) constructor
        if (isSimpleName && simpleClassName == "FinalizationRegistry")
        {
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException("FinalizationRegistry constructor requires exactly 1 argument (cleanup callback).", tsCode: "TS2554");
            }

            CheckExpr(newExpr.Arguments[0]);
            var targetType = new TypeInfo.Any();
            return new TypeInfo.FinalizationRegistry(targetType);
        }

        // Handle new Proxy(target, handler) constructor
        if (isSimpleName && simpleClassName == "Proxy")
        {
            // Proxy() requires exactly 2 arguments (target, handler)
            if (newExpr.Arguments.Count != 2)
            {
                throw new TypeCheckException("Proxy constructor requires exactly 2 arguments (target, handler).", tsCode: "TS2554");
            }

            // Check argument types - both must be checked
            TypeInfo targetType = CheckExpr(newExpr.Arguments[0]);
            CheckExpr(newExpr.Arguments[1]);

            // Return any type since proxy is transparent to the type system
            return new TypeInfo.Any();
        }

        // Handle new EventEmitter() constructor
        if (isSimpleName && simpleClassName == "EventEmitter")
        {
            // Node's EventEmitter accepts an optional options object
            // ({ captureRejections?: boolean }). Reject anything beyond that.
            if (newExpr.Arguments.Count > 1)
            {
                throw new TypeCheckException("EventEmitter constructor accepts at most one (options) argument.", tsCode: "TS2554");
            }

            // Type-check the options argument when present (shape is permissive).
            if (newExpr.Arguments.Count == 1)
            {
                CheckExpr(newExpr.Arguments[0]);
            }

            return new TypeInfo.EventEmitter();
        }

        // Handle new AbortController() constructor
        if (isSimpleName && simpleClassName == "AbortController")
        {
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException("AbortController constructor does not accept arguments.", tsCode: "TS2554");
            }

            return new TypeInfo.AbortController();
        }

        // Handle constructors that accept any arguments and return Any.
        // URL / URLSearchParams are not here — migrated to stdlib/node/url.ts,
        // so they're resolved through normal import lookup, not special-cased.
        if (isSimpleName && simpleClassName is "Headers" or "Request" or "Response"
            or "ReadableStream" or "WritableStream" or "TransformStream"
            or "ByteLengthQueuingStrategy" or "CountQueuingStrategy"
            or "Blob" or "File")
        {
            foreach (var arg in newExpr.Arguments)
                CheckExpr(arg);
            return new TypeInfo.Any();
        }

        // Handle new BroadcastChannel(name) constructor
        if (isSimpleName && simpleClassName == "BroadcastChannel")
        {
            if (newExpr.Arguments.Count != 1)
                throw new TypeCheckException("BroadcastChannel constructor requires exactly 1 argument (name).", tsCode: "TS2554");
            var nameType = CheckExpr(newExpr.Arguments[0]);
            if (!IsString(nameType) && nameType is not TypeInfo.Any)
                throw new TypeCheckException($"BroadcastChannel name must be a string, got '{nameType}'.", tsCode: "TS2345");
            return new TypeInfo.Any();
        }

        // Handle new SharedArrayBuffer(byteLength) / new ArrayBuffer(byteLength)
        if (isSimpleName && simpleClassName is "SharedArrayBuffer" or "ArrayBuffer")
        {
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException($"{simpleClassName} constructor requires exactly 1 argument (byteLength).", tsCode: "TS2554");
            }

            var byteLengthType = CheckExpr(newExpr.Arguments[0]);
            if (!IsNumber(byteLengthType) && byteLengthType is not TypeInfo.Any)
            {
                throw new TypeCheckException($"{simpleClassName} byteLength must be a number, got '{byteLengthType}'.", tsCode: "TS2345");
            }

            return simpleClassName == "SharedArrayBuffer"
                ? new TypeInfo.SharedArrayBuffer()
                : new TypeInfo.ArrayBuffer();
        }

        // Handle new DataView(buffer, byteOffset?, byteLength?) constructor
        if (isSimpleName && simpleClassName == "DataView")
        {
            // DataView requires at least 1 argument (buffer) and up to 3
            if (newExpr.Arguments.Count < 1 || newExpr.Arguments.Count > 3)
            {
                throw new TypeCheckException("DataView constructor requires 1-3 arguments (buffer, byteOffset?, byteLength?).", tsCode: "TS2554");
            }

            // First argument must be ArrayBuffer or SharedArrayBuffer
            var bufferType = CheckExpr(newExpr.Arguments[0]);
            if (bufferType is not TypeInfo.ArrayBuffer
                && bufferType is not TypeInfo.SharedArrayBuffer
                && bufferType is not TypeInfo.Any)
            {
                throw new TypeCheckException($"DataView buffer must be an ArrayBuffer or SharedArrayBuffer, got '{bufferType}'.", tsCode: "TS2345");
            }

            // Optional byteOffset and byteLength arguments must be numbers
            for (int i = 1; i < newExpr.Arguments.Count; i++)
            {
                var argType = CheckExpr(newExpr.Arguments[i]);
                if (!IsNumber(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($"DataView constructor argument {i + 1} must be a number, got '{argType}'.", tsCode: "TS2345");
                }
            }

            return new TypeInfo.DataView();
        }

        // Handle TypedArray constructors (Int8Array, Uint8Array, etc.)
        if (isSimpleName && simpleClassName != null && IsTypedArrayName(simpleClassName))
        {
            // TypedArray constructors accept:
            // - new TypedArray(length)
            // - new TypedArray(typedArray)
            // - new TypedArray(buffer, byteOffset?, length?)
            // - new TypedArray(iterable)
            if (newExpr.Arguments.Count > 3)
            {
                throw new TypeCheckException($" {simpleClassName} constructor accepts at most 3 arguments.", tsCode: "TS2554");
            }

            // Validate first argument if present
            if (newExpr.Arguments.Count >= 1)
            {
                CheckExpr(newExpr.Arguments[0]);
            }

            // Validate optional byteOffset and length
            for (int i = 1; i < newExpr.Arguments.Count; i++)
            {
                var argType = CheckExpr(newExpr.Arguments[i]);
                if (!IsNumber(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" {simpleClassName} constructor argument {i + 1} must be a number, got '{argType}'.", tsCode: "TS2345");
                }
            }

            // Extract element type prefix (e.g., "Int32" from "Int32Array")
            var elementType = simpleClassName.EndsWith("Array")
                ? simpleClassName[..^5]  // Remove "Array" suffix
                : simpleClassName;
            return new TypeInfo.TypedArray(elementType);
        }

        // Handle new Worker(filename, options?) constructor
        if (isSimpleName && simpleClassName == "Worker")
        {
            // Worker accepts 1-2 arguments (filename, options?)
            if (newExpr.Arguments.Count < 1)
            {
                throw new TypeCheckException("Worker constructor requires at least 1 argument (filename).", tsCode: "TS2554");
            }
            if (newExpr.Arguments.Count > 2)
            {
                throw new TypeCheckException("Worker constructor accepts at most 2 arguments.", tsCode: "TS2554");
            }

            var filenameType = CheckExpr(newExpr.Arguments[0]);
            if (!IsString(filenameType) && filenameType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" Worker filename must be a string, got '{filenameType}'.", tsCode: "TS2345");
            }

            // Validate options if provided
            if (newExpr.Arguments.Count == 2)
            {
                CheckExpr(newExpr.Arguments[1]);
            }

            return new TypeInfo.Worker();
        }

        // Handle new vm.Script(code, options?) constructor
        if (isSimpleName && simpleClassName == "Script")
        {
            if (newExpr.Arguments.Count < 1)
            {
                throw new TypeCheckException("Script constructor requires at least 1 argument (code).", tsCode: "TS2554");
            }
            foreach (var arg in newExpr.Arguments)
                CheckExpr(arg);
            return new TypeInfo.Any();
        }

        // Handle new MessageChannel() constructor
        if (isSimpleName && simpleClassName == "MessageChannel")
        {
            // MessageChannel accepts 0 arguments
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException("MessageChannel constructor does not accept arguments.", tsCode: "TS2554");
            }

            return new TypeInfo.MessageChannel();
        }

        // Handle new Promise<T>((resolve, reject) => { ... }) constructor
        if (isSimpleName && simpleClassName == "Promise")
        {
            // Promise constructor requires exactly 1 argument (the executor function)
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException($" Promise constructor requires exactly 1 argument (executor function), got {newExpr.Arguments.Count}.", tsCode: "TS2554");
            }

            // Determine the Promise value type from type arguments or default to any
            TypeInfo valueType = new TypeInfo.Any();
            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                valueType = ToTypeInfo(newExpr.TypeArgs[0]);
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count > 1)
            {
                throw new TypeCheckException("Promise requires exactly 1 type argument: Promise<T>", tsCode: "TS2314");
            }

            // Check the executor argument type
            var executorType = CheckExpr(newExpr.Arguments[0]);

            // The executor should be a function: (resolve: (value?: T) => void, reject: (reason?: any) => void) => void
            // We're lenient here - just check it's callable (function type)
            if (executorType is not TypeInfo.Function && executorType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" Promise executor must be a function, got '{executorType}'.", tsCode: "TS2345");
            }

            return new TypeInfo.Promise(valueType);
        }

        // Handle new Error(...) and error subtype constructors
        if (isSimpleName && simpleClassName != null && BuiltInNames.IsErrorTypeName(simpleClassName))
        {
            // Error constructors accept 0-2 arguments (message, options)
            // AggregateError accepts 0-3 arguments (errors array, message, options)
            int maxArgs = simpleClassName == "AggregateError" ? 3 : 2;
            if (newExpr.Arguments.Count > maxArgs)
            {
                throw new TypeCheckException($" {simpleClassName} constructor accepts at most {maxArgs} argument(s).", tsCode: "TS2554");
            }

            // Validate argument types
            if (newExpr.Arguments.Count >= 1)
            {
                var firstArgType = CheckExpr(newExpr.Arguments[0]);
                if (simpleClassName == "AggregateError")
                {
                    // First argument should be an array of errors
                    if (firstArgType is not TypeInfo.Array && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" AggregateError first argument must be an array, got '{firstArgType}'.", tsCode: "TS2345");
                    }
                }
                else
                {
                    // For other error types, first argument should be a string message
                    if (!IsString(firstArgType) && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" {simpleClassName} message must be a string, got '{firstArgType}'.", tsCode: "TS2345");
                    }
                }
            }

            // Validate remaining arguments
            for (int i = 1; i < newExpr.Arguments.Count; i++)
            {
                var argType = CheckExpr(newExpr.Arguments[i]);
                if (simpleClassName == "AggregateError" && i == 1)
                {
                    // AggregateError second arg is message (string)
                    if (!IsString(argType) && argType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" AggregateError message must be a string, got '{argType}'.", tsCode: "TS2345");
                    }
                }
                // Options argument (last arg) is an object - accept any type
            }

            return new TypeInfo.Error(simpleClassName!);
        }

        // Handle new http.Agent() / new https.Agent() constructor (namespace-qualified)
        if (newExpr.Callee is Expr.Get { Name.Lexeme: "Agent" } agentGet &&
            agentGet.Object is Expr.Variable { Name.Lexeme: "http" or "https" })
        {
            foreach (var arg in newExpr.Arguments)
                CheckExpr(arg);
            return new TypeInfo.Any();
        }

        // Handle new Intl.*() constructors — all accept any arguments and return Any
        if (newExpr.Callee is Expr.Get { Object: Expr.Variable { Name.Lexeme: "Intl" }, Name.Lexeme: var intlName }
            && intlName is "NumberFormat" or "DateTimeFormat" or "Collator" or "PluralRules"
                        or "RelativeTimeFormat" or "ListFormat" or "Segmenter" or "DisplayNames")
        {
            foreach (var arg in newExpr.Arguments)
                CheckExpr(arg);
            return new TypeInfo.Any();
        }

        // Evaluate the callee expression type
        string qualifiedName = GetCalleeClassName(newExpr.Callee);
        TypeInfo calleeType = CheckExpr(newExpr.Callee);

        // Handle interfaces with constructor signatures
        if (calleeType is TypeInfo.Interface itf && itf.IsConstructable)
        {
            return CheckInterfaceConstructorCall(itf, newExpr.TypeArgs, newExpr.Arguments, qualifiedName);
        }
        if (calleeType is TypeInfo.GenericInterface gi && gi.IsConstructable)
        {
            return CheckGenericInterfaceConstructorCall(gi, newExpr.TypeArgs, newExpr.Arguments, qualifiedName);
        }

        // For class types, continue with existing logic
        TypeInfo type = calleeType;

        // Check for abstract class instantiation
        if (type is TypeInfo.GenericClass gc && gc.IsAbstract)
        {
            throw new TypeCheckException($" Cannot create an instance of abstract class '{qualifiedName}'.", tsCode: "TS2511");
        }
        if (type is TypeInfo.Class c && c.IsAbstract)
        {
            throw new TypeCheckException($" Cannot create an instance of abstract class '{qualifiedName}'.", tsCode: "TS2511");
        }

        // Handle generic class instantiation
        if (type is TypeInfo.GenericClass genericClass)
        {
            List<TypeInfo> typeArgs;

            if (newExpr.TypeArgs == null || newExpr.TypeArgs.Count == 0)
            {
                // Try to infer type arguments from constructor parameters
                List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
                var inferredArgs = InferConstructorTypeArguments(genericClass, argTypes);

                if (inferredArgs == null)
                {
                    throw new TypeCheckException($" Generic class '{qualifiedName}' requires type arguments and they could not be inferred.", tsCode: "TS2314");
                }

                typeArgs = inferredArgs;
            }
            else
            {
                typeArgs = newExpr.TypeArgs.Select(ToTypeInfo).ToList();
            }
            var instantiated = InstantiateGenericClass(genericClass, typeArgs);

            // Build substitution map for constructor parameter types
            Dictionary<string, TypeInfo> subs = [];
            for (int i = 0; i < genericClass.TypeParams.Count; i++)
                subs[genericClass.TypeParams[i].Name] = typeArgs[i];

            // Check constructor with substituted parameter types (walk inheritance chain). When the
            // constructor is inherited from a generic-class instantiation, compose the substitutions.
            var (ctorTypeInfo, owningClass) = FindInheritedConstructor(genericClass);
            ValidateConstructorCall(ctorTypeInfo, newExpr, qualifiedName, ComposeConstructorSubs(owningClass, subs));

            return new TypeInfo.Instance(instantiated);
        }

        if (type is TypeInfo.Class classType)
        {
            // Walk inheritance chain to find constructor. When it's inherited from a generic-class
            // instantiation (e.g. `class StringBox extends Box<string>`), substitute that parent's
            // type arguments so the inherited constructor's parameters resolve (T → string).
            var (ctorTypeInfo, owningClass) = FindInheritedConstructor(classType);
            ValidateConstructorCall(ctorTypeInfo, newExpr, qualifiedName, ComposeConstructorSubs(owningClass, []));

            return new TypeInfo.Instance(classType);
        }
        // Handle function-typed constructors and any other non-class callee.
        // Per ECMA-262 §13.3, `new MemberExpression` is syntactically valid for any
        // expression — if the value isn't a constructor, runtime throws TypeError.
        // Don't block at the type-check phase; let runtime decide. This covers literal
        // callees (`new true`, `new 1`), function expressions (`new function() {}(...)`),
        // and any value whose static type isn't a known class.
        foreach (var arg in newExpr.Arguments)
            CheckExpr(arg);
        return new TypeInfo.Any();
    }

    /// <summary>
    /// Validates a constructor call (overloaded or simple) against the provided arguments.
    /// Applies optional generic substitutions to parameter types when provided.
    /// </summary>
    private void ValidateConstructorCall(
        TypeInfo? ctorTypeInfo,
        Expr.New newExpr,
        string qualifiedName,
        Dictionary<string, TypeInfo>? subs = null)
    {
        if (ctorTypeInfo == null)
        {
            if (newExpr.Arguments.Count > 0)
                throw new TypeCheckException($" Constructor for '{qualifiedName}' expected 0 arguments but got {newExpr.Arguments.Count}.", tsCode: "TS2554");
            return;
        }

        if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
        {
            List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
            bool matched = false;
            foreach (var sig in overloadedCtor.Signatures)
            {
                var paramTypes = subs != null ? sig.ParamTypes.Select(p => Substitute(p, subs)).ToList() : sig.ParamTypes;
                if (TryMatchConstructorArgs(argTypes, paramTypes, sig.MinArity, sig.HasRestParam))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                throw new TypeCheckException($" No constructor overload matches the call for '{qualifiedName}'.", tsCode: "TS2769");
        }
        else if (ctorTypeInfo is TypeInfo.Function ctorType)
        {
            var paramTypes = subs != null ? ctorType.ParamTypes.Select(p => Substitute(p, subs)).ToList() : ctorType.ParamTypes;

            if (newExpr.Arguments.Count < ctorType.MinArity)
                throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.", tsCode: "TS2554");
            if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.", tsCode: "TS2554");

            for (int i = 0; i < newExpr.Arguments.Count; i++)
            {
                TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                // Optional/default constructor params accept an explicit `undefined` (#668);
                // a rest parameter's elements are not optional in that sense.
                bool optional = i >= ctorType.MinArity &&
                                !(ctorType.HasRestParam && i >= paramTypes.Count - 1);
                if (!IsArgumentCompatible(paramTypes[i], argType, optional))
                    throw new TypeCheckException($" Constructor argument {i + 1} expected type '{paramTypes[i]}' but got '{argType}'.", tsCode: "TS2345");
            }
        }
    }

    /// <summary>
    /// Infers type arguments for a generic class constructor from the provided argument types.
    /// Returns null if inference fails (no constructor or unable to infer all type parameters).
    /// </summary>
    private List<TypeInfo>? InferConstructorTypeArguments(TypeInfo.GenericClass genericClass, List<TypeInfo> argTypes)
    {
        // Get the constructor - if no constructor, inference isn't possible
        if (!genericClass.Methods.TryGetValue("constructor", out var ctorTypeInfo))
        {
            // No constructor - can't infer type arguments without parameters
            // If the class has zero type parameters that need inference from constructor, this could succeed,
            // but that's an edge case. For safety, return null.
            return null;
        }

        // Get the constructor parameter types (may be overloaded)
        List<TypeInfo> constructorParamTypes;
        if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
        {
            // Try each overload to find one that matches
            foreach (var sig in overloadedCtor.Signatures)
            {
                var result = TryInferFromConstructorSignature(genericClass, sig.ParamTypes, argTypes);
                if (result != null)
                    return result;
            }
            return null;
        }
        else if (ctorTypeInfo is TypeInfo.Function ctorFunc)
        {
            constructorParamTypes = ctorFunc.ParamTypes;
        }
        else
        {
            return null;
        }

        return TryInferFromConstructorSignature(genericClass, constructorParamTypes, argTypes);
    }

    /// <summary>
    /// Tries to infer type arguments from a specific constructor signature.
    /// </summary>
    private List<TypeInfo>? TryInferFromConstructorSignature(
        TypeInfo.GenericClass genericClass,
        List<TypeInfo> constructorParamTypes,
        List<TypeInfo> argTypes)
    {
        Dictionary<string, TypeInfo> inferred = [];

        // Try to infer each type parameter from the corresponding argument
        for (int i = 0; i < constructorParamTypes.Count && i < argTypes.Count; i++)
        {
            InferFromTypeForConstructor(constructorParamTypes[i], argTypes[i], inferred);
        }

        // Build result list in order of type parameters
        List<TypeInfo> result = [];
        foreach (var tp in genericClass.TypeParams)
        {
            if (inferred.TryGetValue(tp.Name, out var inferredType))
            {
                // Validate constraint if present
                if (tp.Constraint != null && tp.Constraint is not TypeInfo.Any)
                {
                    // For Record constraints, check that actual type has all required fields
                    if (tp.Constraint is TypeInfo.Record constraintRecord && inferredType is TypeInfo.Record actualRecord)
                    {
                        foreach (var (fieldName, _) in constraintRecord.Fields)
                        {
                            if (!actualRecord.Fields.ContainsKey(fieldName))
                            {
                                // Constraint violation - inference failed
                                return null;
                            }
                        }
                    }
                    else if (!IsCompatible(tp.Constraint, inferredType))
                    {
                        // Constraint violation - inference failed
                        return null;
                    }
                }
                result.Add(inferredType);
            }
            else
            {
                // Type parameter could not be inferred
                // If there's a default, we could use it, but for now return null
                if (tp.Constraint != null)
                {
                    // Use constraint as fallback (similar to how we do for functions)
                    result.Add(tp.Constraint);
                }
                else
                {
                    // Cannot infer this type parameter - return null
                    return null;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively infers type parameter bindings from a parameter type and an argument type.
    /// Similar to InferFromType in TypeChecker.Generics.cs but for constructor inference.
    /// </summary>
    private void InferFromTypeForConstructor(TypeInfo paramType, TypeInfo argType, Dictionary<string, TypeInfo> inferred)
    {
        if (paramType is TypeInfo.TypeParameter tp)
        {
            // Direct type parameter - infer from argument
            if (!inferred.ContainsKey(tp.Name))
            {
                inferred[tp.Name] = argType;
            }
            else
            {
                // Already inferred - check if we should unify (use wider type)
                var existing = inferred[tp.Name];
                if (!TypeInfoEquals(existing, argType))
                {
                    // If both are compatible with a common supertype, use union
                    // For simplicity, keep the existing inference
                    // More sophisticated inference could find a common supertype
                }
            }
        }
        else if (paramType is TypeInfo.Array paramArr && argType is TypeInfo.Array argArr)
        {
            // Recurse into array element types
            InferFromTypeForConstructor(paramArr.ElementType, argArr.ElementType, inferred);
        }
        else if (paramType is TypeInfo.Function paramFunc && argType is TypeInfo.Function argFunc)
        {
            // Recurse into function types
            for (int i = 0; i < paramFunc.ParamTypes.Count && i < argFunc.ParamTypes.Count; i++)
            {
                InferFromTypeForConstructor(paramFunc.ParamTypes[i], argFunc.ParamTypes[i], inferred);
            }
            InferFromTypeForConstructor(paramFunc.ReturnType, argFunc.ReturnType, inferred);
        }
        else if (paramType is TypeInfo.InstantiatedGeneric paramGen && argType is TypeInfo.InstantiatedGeneric argGen)
        {
            // Same generic base - infer from type arguments
            for (int i = 0; i < paramGen.TypeArguments.Count && i < argGen.TypeArguments.Count; i++)
            {
                InferFromTypeForConstructor(paramGen.TypeArguments[i], argGen.TypeArguments[i], inferred);
            }
        }
        else if (paramType is TypeInfo.Union paramUnion)
        {
            // For union parameter types, try to find a matching branch
            foreach (var unionMember in paramUnion.FlattenedTypes)
            {
                if (IsCompatible(unionMember, argType))
                {
                    InferFromTypeForConstructor(unionMember, argType, inferred);
                    break;
                }
            }
        }
        else if (paramType is TypeInfo.Tuple paramTuple && argType is TypeInfo.Tuple argTuple)
        {
            // Recurse into tuple element types
            for (int i = 0; i < paramTuple.ElementTypes.Count && i < argTuple.ElementTypes.Count; i++)
            {
                InferFromTypeForConstructor(paramTuple.ElementTypes[i], argTuple.ElementTypes[i], inferred);
            }
        }
        else if (paramType is TypeInfo.Promise paramPromise && argType is TypeInfo.Promise argPromise)
        {
            // Recurse into Promise value types
            InferFromTypeForConstructor(paramPromise.ValueType, argPromise.ValueType, inferred);
        }
        else if (paramType is TypeInfo.Record paramRec && argType is TypeInfo.Record argRec)
        {
            // Recurse into Record field types
            foreach (var (fieldName, fieldType) in paramRec.Fields)
            {
                if (argRec.Fields.TryGetValue(fieldName, out var argFieldType))
                {
                    InferFromTypeForConstructor(fieldType, argFieldType, inferred);
                }
            }
        }
    }

    /// <summary>
    /// Helper to check if two TypeInfo instances are structurally equal.
    /// </summary>
    private static bool TypeInfoEquals(TypeInfo a, TypeInfo b)
    {
        return a.ToString() == b.ToString();
    }

    /// <summary>
    /// Checks a constructor call on an interface with constructor signatures.
    /// Returns the type produced by calling the constructor.
    /// </summary>
    private TypeInfo CheckInterfaceConstructorCall(
        TypeInfo.Interface itf,
        List<string>? typeArgs,
        List<Expr> arguments,
        string qualifiedName)
    {
        if (itf.ConstructorSignatures == null || itf.ConstructorSignatures.Count == 0)
        {
            throw new TypeCheckException($" Interface '{qualifiedName}' is not constructable.", tsCode: "TS2351");
        }

        List<TypeInfo> argTypes = arguments.Select(CheckExpr).ToList();

        // Try each constructor signature
        foreach (var ctorSig in itf.ConstructorSignatures)
        {
            if (ctorSig.IsGeneric)
            {
                // Generic constructor signature - try to instantiate
                var result = TryMatchGenericConstructorSignature(ctorSig, typeArgs, argTypes, qualifiedName);
                if (result != null)
                    return result;
            }
            else
            {
                // Non-generic - direct matching
                if (TryMatchConstructorArgs(argTypes, ctorSig.ParamTypes, ctorSig.MinArity, ctorSig.HasRestParam))
                {
                    // Validate each argument
                    for (int i = 0; i < arguments.Count && i < ctorSig.ParamTypes.Count; i++)
                    {
                        if (!IsCompatible(ctorSig.ParamTypes[i], argTypes[i]))
                        {
                            // Continue to next signature
                            goto NextSignature;
                        }
                    }
                    return ctorSig.ReturnType;
                }
            }
            NextSignature:;
        }

        throw new TypeCheckException($" No constructor signature matches the call for interface '{qualifiedName}'.", tsCode: "TS2769");
    }

    /// <summary>
    /// Checks a constructor call on a generic interface with constructor signatures.
    /// </summary>
    private TypeInfo CheckGenericInterfaceConstructorCall(
        TypeInfo.GenericInterface gi,
        List<string>? typeArgs,
        List<Expr> arguments,
        string qualifiedName)
    {
        if (gi.ConstructorSignatures == null || gi.ConstructorSignatures.Count == 0)
        {
            throw new TypeCheckException($" Generic interface '{qualifiedName}' is not constructable.", tsCode: "TS2351");
        }

        // If type args provided, instantiate the interface first
        if (typeArgs != null && typeArgs.Count > 0)
        {
            var instantiatedTypeArgs = typeArgs.Select(ToTypeInfo).ToList();
            // Build substitution map
            Dictionary<string, TypeInfo> subs = [];
            for (int i = 0; i < gi.TypeParams.Count && i < instantiatedTypeArgs.Count; i++)
            {
                subs[gi.TypeParams[i].Name] = instantiatedTypeArgs[i];
            }

            // Substitute in constructor signatures and check
            List<TypeInfo> argTypes = arguments.Select(CheckExpr).ToList();
            foreach (var ctorSig in gi.ConstructorSignatures)
            {
                var substitutedParamTypes = ctorSig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                if (TryMatchConstructorArgs(argTypes, substitutedParamTypes, ctorSig.MinArity, ctorSig.HasRestParam))
                {
                    for (int i = 0; i < arguments.Count && i < substitutedParamTypes.Count; i++)
                    {
                        if (!IsCompatible(substitutedParamTypes[i], argTypes[i]))
                            goto NextSignature;
                    }
                    return Substitute(ctorSig.ReturnType, subs);
                }
                NextSignature:;
            }
        }

        throw new TypeCheckException($" No constructor signature matches the call for generic interface '{qualifiedName}'.", tsCode: "TS2769");
    }

    /// <summary>
    /// Tries to match a generic constructor signature by inferring type arguments.
    /// </summary>
    private TypeInfo? TryMatchGenericConstructorSignature(
        TypeInfo.ConstructorSignature ctorSig,
        List<string>? explicitTypeArgs,
        List<TypeInfo> argTypes,
        string qualifiedName)
    {
        if (ctorSig.TypeParams == null || ctorSig.TypeParams.Count == 0)
            return null;

        Dictionary<string, TypeInfo> inferred = [];

        if (explicitTypeArgs != null && explicitTypeArgs.Count > 0)
        {
            // Use explicit type arguments
            for (int i = 0; i < ctorSig.TypeParams.Count && i < explicitTypeArgs.Count; i++)
            {
                inferred[ctorSig.TypeParams[i].Name] = ToTypeInfo(explicitTypeArgs[i]);
            }
        }
        else
        {
            // Try to infer from argument types
            for (int i = 0; i < ctorSig.ParamTypes.Count && i < argTypes.Count; i++)
            {
                InferFromTypeForConstructor(ctorSig.ParamTypes[i], argTypes[i], inferred);
            }
        }

        // Check if all type parameters were inferred
        foreach (var tp in ctorSig.TypeParams)
        {
            if (!inferred.ContainsKey(tp.Name))
            {
                if (tp.Default != null)
                    inferred[tp.Name] = tp.Default;
                else
                    return null; // Cannot infer
            }
        }

        // Substitute and check argument compatibility
        var substitutedParamTypes = ctorSig.ParamTypes.Select(p => Substitute(p, inferred)).ToList();
        if (!TryMatchConstructorArgs(argTypes, substitutedParamTypes, ctorSig.MinArity, ctorSig.HasRestParam))
            return null;

        for (int i = 0; i < argTypes.Count && i < substitutedParamTypes.Count; i++)
        {
            if (!IsCompatible(substitutedParamTypes[i], argTypes[i]))
                return null;
        }

        return Substitute(ctorSig.ReturnType, inferred);
    }

    /// <summary>
    /// Checks if a name is a TypedArray constructor name.
    /// </summary>
    private static bool IsTypedArrayName(string name) => Runtime.BuiltIns.BuiltInNames.IsTypedArrayName(name);
}
