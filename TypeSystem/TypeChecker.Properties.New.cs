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
            if (newExpr.TypeArgs == null || newExpr.TypeArgs.Count == 0)
            {
                throw new TypeCheckException($" Generic class '{qualifiedName}' requires type arguments.");
            }

            var typeArgs = newExpr.TypeArgs.Select(ToTypeInfo).ToList();
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
}
