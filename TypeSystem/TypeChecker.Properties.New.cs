using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Constructor and instantiation type checking.
/// </summary>
/// <remarks>
/// Contains handler for new expressions:
/// CheckNew - handles built-in types (Date, RegExp, Map, Set, WeakMap, WeakSet) and user-defined classes.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Gets the fully qualified name for error messages.
    /// </summary>
    private static string GetQualifiedClassName(Expr.New newExpr)
    {
        if (newExpr.NamespacePath == null || newExpr.NamespacePath.Count == 0)
            return newExpr.ClassName.Lexeme;
        return string.Join(".", newExpr.NamespacePath.Select(t => t.Lexeme)) + "." + newExpr.ClassName.Lexeme;
    }

    /// <summary>
    /// Resolves a qualified name (Namespace.SubNs.ClassName) to a TypeInfo.
    /// </summary>
    private TypeInfo ResolveQualifiedType(List<Token>? namespacePath, Token className)
    {
        if (namespacePath == null || namespacePath.Count == 0)
        {
            // Simple class name - use existing lookup
            return LookupVariable(className);
        }

        // Start from first namespace
        TypeInfo current = LookupVariable(namespacePath[0]);

        // Traverse namespace chain
        for (int i = 1; i < namespacePath.Count; i++)
        {
            if (current is not TypeInfo.Namespace ns)
            {
                throw new TypeCheckException($" '{namespacePath[i - 1].Lexeme}' is not a namespace.");
            }
            var member = ns.GetMember(namespacePath[i].Lexeme);
            if (member == null)
            {
                throw new TypeCheckException($" '{namespacePath[i].Lexeme}' does not exist in namespace '{ns.Name}'.");
            }
            current = member;
        }

        // Now get the class from the final namespace
        if (current is not TypeInfo.Namespace finalNs)
        {
            throw new TypeCheckException($" '{namespacePath[^1].Lexeme}' is not a namespace.");
        }

        var classType = finalNs.GetMember(className.Lexeme);
        if (classType == null)
        {
            throw new TypeCheckException($" Class '{className.Lexeme}' does not exist in namespace '{finalNs.Name}'.");
        }

        return classType;
    }

    private TypeInfo CheckNew(Expr.New newExpr)
    {
        // Built-in types only apply when there's no namespace path
        bool isSimpleName = newExpr.NamespacePath == null || newExpr.NamespacePath.Count == 0;

        // Handle new Date() constructor
        if (isSimpleName && newExpr.ClassName.Lexeme == "Date")
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
        if (isSimpleName && newExpr.ClassName.Lexeme == "RegExp")
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
        if (isSimpleName && newExpr.ClassName.Lexeme == "Map")
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
        if (isSimpleName && newExpr.ClassName.Lexeme == "Set")
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
        if (isSimpleName && newExpr.ClassName.Lexeme == "WeakMap")
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
        if (isSimpleName && newExpr.ClassName.Lexeme == "WeakSet")
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

        string qualifiedName = GetQualifiedClassName(newExpr);
        TypeInfo type = ResolveQualifiedType(newExpr.NamespacePath, newExpr.ClassName);

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

            // Check constructor with substituted parameter types
            if (genericClass.Methods.TryGetValue("constructor", out var ctorTypeInfo))
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
            if (classType.Methods.TryGetValue("constructor", out var ctorTypeInfo))
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
}
