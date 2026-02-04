using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

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
            var methods = GetMethods(current);
            if (methods?.TryGetValue("constructor", out var ctor) == true)
                return (ctor, current);
            current = GetSuperclass(current);
        }
        return (null, null);
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
                throw new TypeCheckException(" Date constructor accepts at most 7 arguments.");
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
                        throw new TypeCheckException($" Date constructor single argument must be a number or string, got '{argType}'.");
                    }
                }
                else if (!IsNumber(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Date constructor arguments must be numbers, got '{argType}'.");
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
                throw new TypeCheckException(" RegExp constructor accepts at most 2 arguments.");
            }

            // Validate argument types
            if (newExpr.Arguments.Count >= 1)
            {
                var patternType = CheckExpr(newExpr.Arguments[0]);
                if (!IsString(patternType) && patternType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" RegExp pattern must be a string, got '{patternType}'.");
                }
            }

            if (newExpr.Arguments.Count == 2)
            {
                var flagsType = CheckExpr(newExpr.Arguments[1]);
                if (!IsString(flagsType) && flagsType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" RegExp flags must be a string, got '{flagsType}'.");
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
                throw new TypeCheckException(" Map constructor accepts at most 1 argument.");
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
                throw new TypeCheckException(" Map requires exactly 2 type arguments: Map<K, V>");
            }

            return new TypeInfo.Map(keyType, valueType);
        }

        // Handle new Set() and new Set<T>() constructor
        if (isSimpleName && simpleClassName == "Set")
        {
            // Set() accepts 0-1 arguments (optional iterable of values)
            if (newExpr.Arguments.Count > 1)
            {
                throw new TypeCheckException(" Set constructor accepts at most 1 argument.");
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
                throw new TypeCheckException(" Set requires exactly 1 type argument: Set<T>");
            }

            return new TypeInfo.Set(elementType);
        }

        // Handle new WeakMap() and new WeakMap<K, V>() constructor
        if (isSimpleName && simpleClassName == "WeakMap")
        {
            // WeakMap() accepts 0 arguments only (no iterable initialization)
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException(" WeakMap constructor does not accept arguments.");
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
                    throw new TypeCheckException($" WeakMap keys must be objects, not '{keyType}'.");
                }
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException(" WeakMap requires exactly 2 type arguments: WeakMap<K, V>");
            }

            return new TypeInfo.WeakMap(keyType, valueType);
        }

        // Handle new WeakSet() and new WeakSet<T>() constructor
        if (isSimpleName && simpleClassName == "WeakSet")
        {
            // WeakSet() accepts 0 arguments only (no iterable initialization)
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException(" WeakSet constructor does not accept arguments.");
            }

            // Determine element type from type argument or default to any
            TypeInfo elementType = new TypeInfo.Any();

            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                elementType = ToTypeInfo(newExpr.TypeArgs[0]);

                // Validate that element type is not a primitive
                if (IsPrimitiveType(elementType))
                {
                    throw new TypeCheckException($" WeakSet values must be objects, not '{elementType}'.");
                }
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count != 0)
            {
                throw new TypeCheckException(" WeakSet requires exactly 1 type argument: WeakSet<T>");
            }

            return new TypeInfo.WeakSet(elementType);
        }

        // Handle new EventEmitter() constructor
        if (isSimpleName && simpleClassName == "EventEmitter")
        {
            // EventEmitter() accepts 0 arguments
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException(" EventEmitter constructor does not accept arguments.");
            }

            return new TypeInfo.EventEmitter();
        }

        // Handle new SharedArrayBuffer(byteLength) constructor
        if (isSimpleName && simpleClassName == "SharedArrayBuffer")
        {
            // SharedArrayBuffer accepts 1 argument (byteLength)
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException(" SharedArrayBuffer constructor requires exactly 1 argument (byteLength).");
            }

            var byteLengthType = CheckExpr(newExpr.Arguments[0]);
            if (!IsNumber(byteLengthType) && byteLengthType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" SharedArrayBuffer byteLength must be a number, got '{byteLengthType}'.");
            }

            return new TypeInfo.SharedArrayBuffer();
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
                throw new TypeCheckException($" {simpleClassName} constructor accepts at most 3 arguments.");
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
                    throw new TypeCheckException($" {simpleClassName} constructor argument {i + 1} must be a number, got '{argType}'.");
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
                throw new TypeCheckException(" Worker constructor requires at least 1 argument (filename).");
            }
            if (newExpr.Arguments.Count > 2)
            {
                throw new TypeCheckException(" Worker constructor accepts at most 2 arguments.");
            }

            var filenameType = CheckExpr(newExpr.Arguments[0]);
            if (!IsString(filenameType) && filenameType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" Worker filename must be a string, got '{filenameType}'.");
            }

            // Validate options if provided
            if (newExpr.Arguments.Count == 2)
            {
                CheckExpr(newExpr.Arguments[1]);
            }

            return new TypeInfo.Worker();
        }

        // Handle new MessageChannel() constructor
        if (isSimpleName && simpleClassName == "MessageChannel")
        {
            // MessageChannel accepts 0 arguments
            if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException(" MessageChannel constructor does not accept arguments.");
            }

            return new TypeInfo.MessageChannel();
        }

        // Handle new Promise<T>((resolve, reject) => { ... }) constructor
        if (isSimpleName && simpleClassName == "Promise")
        {
            // Promise constructor requires exactly 1 argument (the executor function)
            if (newExpr.Arguments.Count != 1)
            {
                throw new TypeCheckException($" Promise constructor requires exactly 1 argument (executor function), got {newExpr.Arguments.Count}.");
            }

            // Determine the Promise value type from type arguments or default to any
            TypeInfo valueType = new TypeInfo.Any();
            if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count == 1)
            {
                valueType = ToTypeInfo(newExpr.TypeArgs[0]);
            }
            else if (newExpr.TypeArgs != null && newExpr.TypeArgs.Count > 1)
            {
                throw new TypeCheckException(" Promise requires exactly 1 type argument: Promise<T>");
            }

            // Check the executor argument type
            var executorType = CheckExpr(newExpr.Arguments[0]);

            // The executor should be a function: (resolve: (value?: T) => void, reject: (reason?: any) => void) => void
            // We're lenient here - just check it's callable (function type)
            if (executorType is not TypeInfo.Function && executorType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" Promise executor must be a function, got '{executorType}'.");
            }

            return new TypeInfo.Promise(valueType);
        }

        // Handle new Error(...) and error subtype constructors
        if (isSimpleName && simpleClassName != null && IsErrorTypeName(simpleClassName))
        {
            // Error constructors accept 0-1 argument (optional message)
            // AggregateError accepts 0-2 arguments (errors array, optional message)
            int maxArgs = simpleClassName == "AggregateError" ? 2 : 1;
            if (newExpr.Arguments.Count > maxArgs)
            {
                throw new TypeCheckException($" {simpleClassName} constructor accepts at most {maxArgs} argument(s).");
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
                        throw new TypeCheckException($" AggregateError first argument must be an array, got '{firstArgType}'.");
                    }
                }
                else
                {
                    // For other error types, first argument should be a string message
                    if (!IsString(firstArgType) && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" {simpleClassName} message must be a string, got '{firstArgType}'.");
                    }
                }
            }

            if (newExpr.Arguments.Count == 2)
            {
                var secondArgType = CheckExpr(newExpr.Arguments[1]);
                if (!IsString(secondArgType) && secondArgType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" AggregateError message must be a string, got '{secondArgType}'.");
                }
            }

            return new TypeInfo.Error(simpleClassName!);
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
            throw new TypeCheckException($" Cannot create an instance of abstract class '{qualifiedName}'.");
        }
        if (type is TypeInfo.Class c && c.IsAbstract)
        {
            throw new TypeCheckException($" Cannot create an instance of abstract class '{qualifiedName}'.");
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
                    throw new TypeCheckException($" Generic class '{qualifiedName}' requires type arguments and they could not be inferred.");
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

            // Check constructor with substituted parameter types (walk inheritance chain)
            var (ctorTypeInfo, _) = FindInheritedConstructor(genericClass);
            if (ctorTypeInfo != null)
            {
                // Handle both Function and OverloadedFunction for constructor
                if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
                {
                    // Resolve overloaded constructor call
                    List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
                    bool matched = false;
                    foreach (var sig in overloadedCtor.Signatures)
                    {
                        var substitutedParamTypes = sig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                        if (TryMatchConstructorArgs(argTypes, substitutedParamTypes, sig.MinArity, sig.HasRestParam))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        throw new TypeCheckException($" No constructor overload matches the call for '{qualifiedName}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    var substitutedParamTypes = ctorType.ParamTypes.Select(p => Substitute(p, subs)).ToList();

                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(substitutedParamTypes[i], argType))
                        {
                            throw new TypeCheckException($" Constructor argument {i + 1} expected type '{substitutedParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException($" Constructor for '{qualifiedName}' expected 0 arguments but got {newExpr.Arguments.Count}.");
            }

            return new TypeInfo.Instance(instantiated);
        }

        if (type is TypeInfo.Class classType)
        {
            // Walk inheritance chain to find constructor
            var (ctorTypeInfo, _) = FindInheritedConstructor(classType);
            if (ctorTypeInfo != null)
            {
                // Handle both Function and OverloadedFunction for constructor
                if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
                {
                    // Resolve overloaded constructor call
                    List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
                    bool matched = false;
                    foreach (var sig in overloadedCtor.Signatures)
                    {
                        if (TryMatchConstructorArgs(argTypes, sig.ParamTypes, sig.MinArity, sig.HasRestParam))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        throw new TypeCheckException($" No constructor overload matches the call for '{qualifiedName}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    // Use MinArity to allow optional parameters
                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new TypeCheckException($" Constructor for '{qualifiedName}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(ctorType.ParamTypes[i], argType))
                        {
                            throw new TypeCheckException($" Constructor argument {i + 1} expected type '{ctorType.ParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new TypeCheckException($" Constructor for '{qualifiedName}' expected 0 arguments but got {newExpr.Arguments.Count}.");
            }

            return new TypeInfo.Instance(classType);
        }
        throw new TypeCheckException($" '{qualifiedName}' is not a class.");
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
            throw new TypeCheckException($" Interface '{qualifiedName}' is not constructable.");
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

        throw new TypeCheckException($" No constructor signature matches the call for interface '{qualifiedName}'.");
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
            throw new TypeCheckException($" Generic interface '{qualifiedName}' is not constructable.");
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

        throw new TypeCheckException($" No constructor signature matches the call for generic interface '{qualifiedName}'.");
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
