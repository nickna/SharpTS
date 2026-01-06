using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Property and member access type checking.
/// </summary>
/// <remarks>
/// Contains handlers for property access, setting, indexing:
/// CheckGet, CheckSet, CheckThis, CheckSuper, CheckNew, CheckGetIndex, CheckSetIndex.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckSuper(Expr.Super expr)
    {
        if (_currentClass == null)
        {
            throw new Exception("Type Error: Cannot use 'super' outside of a class.");
        }
        if (_currentClass.Superclass == null)
        {
            throw new Exception($"Type Error: Class '{_currentClass.Name}' does not have a superclass.");
        }

        // super() constructor call - Method is null
        if (expr.Method == null)
        {
            if (_currentClass.Superclass.Methods.TryGetValue("constructor", out var ctorType))
            {
                return ctorType;
            }
            // Default constructor with no parameters
            return new TypeInfo.Function([], new TypeInfo.Void());
        }

        if (_currentClass.Superclass.Methods.TryGetValue(expr.Method.Lexeme, out var methodType))
        {
            return methodType;
        }

        throw new Exception($"Type Error: Property '{expr.Method.Lexeme}' does not exist on superclass '{_currentClass.Superclass.Name}'.");
    }

    private TypeInfo CheckGetIndex(Expr.GetIndex getIndex)
    {
        TypeInfo objType = CheckExpr(getIndex.Object);
        TypeInfo indexType = CheckExpr(getIndex.Index);

        // Allow indexing on 'any' type (returns 'any')
        if (objType is TypeInfo.Any)
        {
            return new TypeInfo.Any();
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // String literal index - look up specific property
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return fieldType;
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return memberType;
            }

            // Dynamic string index - use index signature if available
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return rec2.StringIndexType;
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return itf2.StringIndexType;

            // Allow bracket access on any object/interface (returns any for unknown keys)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple indexing with position-based types
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> exact element type
                if (getIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return tupleType.ElementTypes[i];
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return tupleType.RestElementType;
                    if (i < 0 || (tupleType.MaxLength != null && i >= tupleType.MaxLength))
                        throw new Exception($"Type Error: Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> union of all possible types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
            }

            if (objType is TypeInfo.Array arrayType)
            {
                return arrayType.ElementType;
            }

            // Enum reverse mapping: Direction[0] returns "Up" (only for numeric enums)
            if (objType is TypeInfo.Enum enumType)
            {
                // Const enums cannot use reverse mapping
                if (enumType.IsConst)
                {
                    throw new Exception($"Type Error: A const enum member can only be accessed using its name, not by index. Cannot use reverse mapping on const enum '{enumType.Name}'.");
                }
                if (enumType.Kind == EnumKind.String)
                {
                    throw new Exception($"Type Error: Reverse mapping is not supported for string enum '{enumType.Name}'.");
                }
                return new TypeInfo.Primitive(TokenType.TYPE_STRING);
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return itf3.NumberIndexType;
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return rec3.NumberIndexType;
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf4 && itf4.SymbolIndexType != null)
                return itf4.SymbolIndexType;
            if (objType is TypeInfo.Record rec4 && rec4.SymbolIndexType != null)
                return rec4.SymbolIndexType;

            // Allow symbol bracket access on any object (returns any)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        throw new Exception($"Type Error: Index type '{indexType}' is not valid for indexing '{objType}'.");
    }

    private TypeInfo CheckSetIndex(Expr.SetIndex setIndex)
    {
        TypeInfo objType = CheckExpr(setIndex.Object);
        TypeInfo indexType = CheckExpr(setIndex.Index);
        TypeInfo valueType = CheckExpr(setIndex.Value);

        // Allow setting on 'any' type
        if (objType is TypeInfo.Any)
        {
            return valueType;
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // Check if value is compatible with string index signature
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (!IsCompatible(itf.StringIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to index signature type '{itf.StringIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (!IsCompatible(rec.StringIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to index signature type '{rec.StringIndexType}'.");
                return valueType;
            }

            // Allow bracket assignment on any object/interface
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple index assignment
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> check against specific element type
                if (setIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.ElementTypes[i], valueType))
                            throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple element of type '{tupleType.ElementTypes[i]}'.");
                        return valueType;
                    }
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.RestElementType, valueType))
                            throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple rest element of type '{tupleType.RestElementType}'.");
                        return valueType;
                    }
                    throw new Exception($"Type Error: Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> value must be compatible with all possible element types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                if (!allTypes.All(t => IsCompatible(t, valueType)))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple with mixed element types.");
                return valueType;
            }

            if (objType is TypeInfo.Array arrayType)
            {
                if (!IsCompatible(arrayType.ElementType, valueType))
                {
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to array of '{arrayType.ElementType}'.");
                }
                return valueType;
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (!IsCompatible(itf2.NumberIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to number index signature type '{itf2.NumberIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (!IsCompatible(rec2.NumberIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to number index signature type '{rec2.NumberIndexType}'.");
                return valueType;
            }
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf3 && itf3.SymbolIndexType != null)
            {
                if (!IsCompatible(itf3.SymbolIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to symbol index signature type '{itf3.SymbolIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec3 && rec3.SymbolIndexType != null)
            {
                if (!IsCompatible(rec3.SymbolIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to symbol index signature type '{rec3.SymbolIndexType}'.");
                return valueType;
            }

            // Allow symbol bracket assignment on any object
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        throw new Exception($"Type Error: Index type '{indexType}' is not valid for assigning to '{objType}'.");
    }

    private TypeInfo CheckNew(Expr.New newExpr)
    {
        // Handle new Date() constructor
        if (newExpr.ClassName.Lexeme == "Date")
        {
            // Date() accepts 0-7 arguments
            if (newExpr.Arguments.Count > 7)
            {
                throw new Exception("Type Error: Date constructor accepts at most 7 arguments.");
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
                        throw new Exception($"Type Error: Date constructor single argument must be a number or string, got '{argType}'.");
                    }
                }
                else if (!IsNumber(argType) && argType is not TypeInfo.Any)
                {
                    throw new Exception($"Type Error: Date constructor arguments must be numbers, got '{argType}'.");
                }
            }

            return new TypeInfo.Date();
        }

        // Handle new RegExp() constructor
        if (newExpr.ClassName.Lexeme == "RegExp")
        {
            // RegExp() accepts 0-2 arguments (pattern, flags)
            if (newExpr.Arguments.Count > 2)
            {
                throw new Exception("Type Error: RegExp constructor accepts at most 2 arguments.");
            }

            // Validate argument types
            if (newExpr.Arguments.Count >= 1)
            {
                var patternType = CheckExpr(newExpr.Arguments[0]);
                if (!IsString(patternType) && patternType is not TypeInfo.Any)
                {
                    throw new Exception($"Type Error: RegExp pattern must be a string, got '{patternType}'.");
                }
            }

            if (newExpr.Arguments.Count == 2)
            {
                var flagsType = CheckExpr(newExpr.Arguments[1]);
                if (!IsString(flagsType) && flagsType is not TypeInfo.Any)
                {
                    throw new Exception($"Type Error: RegExp flags must be a string, got '{flagsType}'.");
                }
            }

            return new TypeInfo.RegExp();
        }

        // Handle new Map() and new Map<K, V>() constructor
        if (newExpr.ClassName.Lexeme == "Map")
        {
            // Map() accepts 0-1 arguments (optional iterable of entries)
            if (newExpr.Arguments.Count > 1)
            {
                throw new Exception("Type Error: Map constructor accepts at most 1 argument.");
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
                throw new Exception("Type Error: Map requires exactly 2 type arguments: Map<K, V>");
            }

            return new TypeInfo.Map(keyType, valueType);
        }

        // Handle new Set() and new Set<T>() constructor
        if (newExpr.ClassName.Lexeme == "Set")
        {
            // Set() accepts 0-1 arguments (optional iterable of values)
            if (newExpr.Arguments.Count > 1)
            {
                throw new Exception("Type Error: Set constructor accepts at most 1 argument.");
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
                throw new Exception("Type Error: Set requires exactly 1 type argument: Set<T>");
            }

            return new TypeInfo.Set(elementType);
        }

        TypeInfo type = LookupVariable(newExpr.ClassName);

        // Check for abstract class instantiation
        if (type is TypeInfo.GenericClass gc && gc.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{newExpr.ClassName.Lexeme}'.");
        }
        if (type is TypeInfo.Class c && c.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{newExpr.ClassName.Lexeme}'.");
        }

        // Handle generic class instantiation
        if (type is TypeInfo.GenericClass genericClass)
        {
            if (newExpr.TypeArgs == null || newExpr.TypeArgs.Count == 0)
            {
                throw new Exception($"Type Error: Generic class '{newExpr.ClassName.Lexeme}' requires type arguments.");
            }

            var typeArgs = newExpr.TypeArgs.Select(ToTypeInfo).ToList();
            var instantiated = InstantiateGenericClass(genericClass, typeArgs);

            // Build substitution map for constructor parameter types
            var subs = new Dictionary<string, TypeInfo>();
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
                        throw new Exception($"Type Error: No constructor overload matches the call for '{newExpr.ClassName.Lexeme}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    var substitutedParamTypes = ctorType.ParamTypes.Select(p => Substitute(p, subs)).ToList();

                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(substitutedParamTypes[i], argType))
                        {
                            throw new Exception($"Type Error: Constructor argument {i + 1} expected type '{substitutedParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected 0 arguments but got {newExpr.Arguments.Count}.");
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
                        throw new Exception($"Type Error: No constructor overload matches the call for '{newExpr.ClassName.Lexeme}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    // Use MinArity to allow optional parameters
                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(ctorType.ParamTypes[i], argType))
                        {
                            throw new Exception($"Type Error: Constructor argument {i + 1} expected type '{ctorType.ParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected 0 arguments but got {newExpr.Arguments.Count}.");
            }

            return new TypeInfo.Instance(classType);
        }
        throw new Exception($"Type Error: '{newExpr.ClassName.Lexeme}' is not a class.");
    }

    private TypeInfo CheckThis(Expr.This expr)
    {
        // If there's an explicit 'this' type from a this parameter, use it
        if (_currentFunctionThisType != null)
        {
            return _currentFunctionThisType;
        }

        if (_currentClass == null)
        {
            throw new Exception("Type Error: Cannot use 'this' outside of a class.");
        }
        if (_inStaticMethod)
        {
            throw new Exception("Type Error: Cannot use 'this' in a static method.");
        }
        return new TypeInfo.Instance(_currentClass);
    }

    private TypeInfo CheckGet(Expr.Get get)
    {
        TypeInfo objType = CheckExpr(get.Object);

        // Handle static member access on class type
        if (objType is TypeInfo.Class classType)
        {
            // Check static methods
            TypeInfo.Class? current = classType;
            while (current != null)
            {
                if (current.StaticMethods.TryGetValue(get.Name.Lexeme, out var staticMethodType))
                {
                    return staticMethodType;
                }
                if (current.StaticProperties.TryGetValue(get.Name.Lexeme, out var staticPropType))
                {
                    return staticPropType;
                }
                current = current.Superclass;
            }
            return new TypeInfo.Any();
        }

        // Handle namespace member access (e.g., Foo.Bar or Foo.someFunction)
        if (objType is TypeInfo.Namespace nsType)
        {
            var memberType = nsType.GetMember(get.Name.Lexeme);
            if (memberType != null)
            {
                return memberType;
            }
            throw new Exception($"Type Error: '{get.Name.Lexeme}' does not exist on namespace '{nsType.Name}'.");
        }

        // Handle enum member access (e.g., Direction.Up or Status.Success)
        if (objType is TypeInfo.Enum enumTypeInfo)
        {
            if (enumTypeInfo.Members.TryGetValue(get.Name.Lexeme, out var memberValue))
            {
                // Return type based on the actual member value type
                return memberValue switch
                {
                    double => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                    string => new TypeInfo.Primitive(TokenType.TYPE_STRING),
                    _ => throw new Exception($"Type Error: Unexpected enum member type for '{get.Name.Lexeme}'.")
                };
            }
            throw new Exception($"Type Error: '{get.Name.Lexeme}' does not exist on enum '{enumTypeInfo.Name}'.");
        }

        if (objType is TypeInfo.Instance instance)
        {
            string memberName = get.Name.Lexeme;

            // Handle instantiated generic class (e.g., Box<number>)
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                // Build substitution map from type parameters to type arguments
                var subs = new Dictionary<string, TypeInfo>();
                for (int i = 0; i < gc.TypeParams.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                // Check for getter first
                if (gc.Getters?.TryGetValue(memberName, out var getterType) == true)
                {
                    return Substitute(getterType, subs);
                }

                // Check for field
                if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                {
                    return Substitute(fieldType, subs);
                }

                // Check for method
                if (gc.Methods.TryGetValue(memberName, out var methodType))
                {
                    // Substitute type parameters in method signature
                    if (methodType is TypeInfo.Function funcType)
                    {
                        var substitutedParams = funcType.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                        var substitutedReturn = Substitute(funcType.ReturnType, subs);
                        return new TypeInfo.Function(substitutedParams, substitutedReturn, funcType.RequiredParams, funcType.HasRestParam);
                    }
                    else if (methodType is TypeInfo.OverloadedFunction overloadedFunc)
                    {
                        // Substitute type parameters in all overload signatures
                        var substitutedSignatures = overloadedFunc.Signatures.Select(sig =>
                        {
                            var substitutedParams = sig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                            var substitutedReturn = Substitute(sig.ReturnType, subs);
                            return new TypeInfo.Function(substitutedParams, substitutedReturn, sig.RequiredParams, sig.HasRestParam);
                        }).ToList();
                        var substitutedImpl = new TypeInfo.Function(
                            overloadedFunc.Implementation.ParamTypes.Select(p => Substitute(p, subs)).ToList(),
                            Substitute(overloadedFunc.Implementation.ReturnType, subs),
                            overloadedFunc.Implementation.RequiredParams,
                            overloadedFunc.Implementation.HasRestParam);
                        return new TypeInfo.OverloadedFunction(substitutedSignatures, substitutedImpl);
                    }
                    return methodType; // Fallback - shouldn't happen
                }

                // Check superclass if any
                if (gc.Superclass != null)
                {
                    TypeInfo.Class? current = gc.Superclass;
                    while (current != null)
                    {
                        if (current.Methods.TryGetValue(memberName, out var superMethod))
                            return superMethod;
                        if (current.FieldTypes?.TryGetValue(memberName, out var superField) == true)
                            return superField;
                        current = current.Superclass;
                    }
                }

                return new TypeInfo.Any();
            }

            // Handle regular class instance
            if (instance.ClassType is TypeInfo.Class instanceClassType)
            {
                TypeInfo.Class? current = instanceClassType;
                while (current != null)
                {
                    // Check for getter first
                    if (current.GetterTypes.TryGetValue(memberName, out var getterType))
                    {
                        return getterType;
                    }

                    // Check access modifier
                    AccessModifier access = AccessModifier.Public;
                    if (current.MethodAccessModifiers.TryGetValue(memberName, out var ma))
                        access = ma;
                    else if (current.FieldAccessModifiers.TryGetValue(memberName, out var fa))
                        access = fa;

                    if (access == AccessModifier.Private && _currentClass?.Name != current.Name)
                    {
                        throw new Exception($"Type Error: Property '{memberName}' is private and only accessible within class '{current.Name}'.");
                    }
                    if (access == AccessModifier.Protected && !IsSubclassOf(_currentClass, current))
                    {
                        throw new Exception($"Type Error: Property '{memberName}' is protected and only accessible within class '{current.Name}' and its subclasses.");
                    }

                    if (current.Methods.TryGetValue(memberName, out var methodType))
                    {
                        return methodType;
                    }

                    // Check for field
                    if (current.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                    {
                        return fieldType;
                    }

                    current = current.Superclass;
                }
                return new TypeInfo.Any();
            }

            return new TypeInfo.Any();
        }
        // Handle interface member access
        if (objType is TypeInfo.Interface itf)
        {
            if (itf.Members.TryGetValue(get.Name.Lexeme, out var memberType))
            {
                return memberType;
            }
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on interface '{itf.Name}'.");
        }
        if (objType is TypeInfo.Record record)
        {
            if (record.Fields.TryGetValue(get.Name.Lexeme, out var fieldType))
            {
                return fieldType;
            }
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type '{record}'.");
        }
        // Handle string methods
        if (objType is TypeInfo.Primitive p && p.Type == TokenType.TYPE_STRING)
        {
            var memberType = BuiltInTypes.GetStringMemberType(get.Name.Lexeme);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'string'.");
        }
        // Handle array methods
        if (objType is TypeInfo.Array arrayType)
        {
            var memberType = BuiltInTypes.GetArrayMemberType(get.Name.Lexeme, arrayType.ElementType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'array'.");
        }
        // Handle tuple methods (tuples support array methods)
        if (objType is TypeInfo.Tuple tupleType)
        {
            // Create union of all element types for method type resolution
            var allTypes = tupleType.ElementTypes.ToList();
            if (tupleType.RestElementType != null)
                allTypes.Add(tupleType.RestElementType);
            var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            TypeInfo unionElem = unique.Count == 0
                ? new TypeInfo.Any()
                : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
            var memberType = BuiltInTypes.GetArrayMemberType(get.Name.Lexeme, unionElem);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on tuple type.");
        }
        // Handle Date instance methods
        if (objType is TypeInfo.Date)
        {
            var memberType = BuiltInTypes.GetDateInstanceMemberType(get.Name.Lexeme);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'Date'.");
        }
        // Handle RegExp instance members
        if (objType is TypeInfo.RegExp)
        {
            var memberType = BuiltInTypes.GetRegExpMemberType(get.Name.Lexeme);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'RegExp'.");
        }
        // Handle Map instance methods
        if (objType is TypeInfo.Map mapType)
        {
            var memberType = BuiltInTypes.GetMapMemberType(get.Name.Lexeme, mapType.KeyType, mapType.ValueType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'Map'.");
        }
        // Handle Set instance methods
        if (objType is TypeInfo.Set setType)
        {
            var memberType = BuiltInTypes.GetSetMemberType(get.Name.Lexeme, setType.ElementType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'Set'.");
        }
        return new TypeInfo.Any();
    }

    private TypeInfo CheckSet(Expr.Set set)
    {
        TypeInfo objType = CheckExpr(set.Object);

        // Handle static property assignment
        if (objType is TypeInfo.Class classType)
        {
            TypeInfo.Class? current = classType;
            while (current != null)
            {
                if (current.StaticProperties.TryGetValue(set.Name.Lexeme, out var staticPropType))
                {
                    TypeInfo valueType = CheckExpr(set.Value);
                    if (!IsCompatible(staticPropType, valueType))
                    {
                        throw new Exception($"Type Error: Cannot assign '{valueType}' to static property '{set.Name.Lexeme}' of type '{staticPropType}'.");
                    }
                    return valueType;
                }
                current = current.Superclass;
            }
            return CheckExpr(set.Value);
        }

        if (objType is TypeInfo.Instance instance)
        {
             string memberName = set.Name.Lexeme;

             // Handle InstantiatedGeneric
             if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                 ig.GenericDefinition is TypeInfo.GenericClass gc)
             {
                 // Build substitution map
                 var subs = new Dictionary<string, TypeInfo>();
                 for (int i = 0; i < gc.TypeParams.Count; i++)
                     subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                 // Check for setter
                 if (gc.Setters?.TryGetValue(memberName, out var setterType) == true)
                 {
                     var substitutedType = Substitute(setterType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{memberName}' expecting '{substitutedType}'.");
                     }
                     return valueType;
                 }

                 // Check for field
                 if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                 {
                     var substitutedType = Substitute(fieldType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to field '{memberName}' of type '{substitutedType}'.");
                     }
                     return valueType;
                 }

                 return CheckExpr(set.Value);
             }

             // Handle regular Class
             if (instance.ClassType is not TypeInfo.Class startClass)
                 return CheckExpr(set.Value);

             TypeInfo.Class? current = startClass;

             // Check for setter first
             while (current != null)
             {
                 if (current.SetterTypes.TryGetValue(memberName, out var setterType))
                 {
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(setterType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{memberName}' expecting '{setterType}'.");
                     }
                     return valueType;
                 }

                 // Check if there's a getter but no setter (read-only property)
                 if (current.GetterTypes.ContainsKey(memberName) && !current.SetterTypes.ContainsKey(memberName))
                 {
                     throw new Exception($"Type Error: Cannot assign to '{memberName}' because it is a read-only property (has getter but no setter).");
                 }

                 current = current.Superclass;
             }

             // Reset to check access and readonly
             current = startClass;

             // Check access and readonly
             while (current != null)
             {
                 // Check access modifier
                 AccessModifier access = AccessModifier.Public;
                 if (current.FieldAccessModifiers.TryGetValue(memberName, out var fa))
                     access = fa;

                 if (access == AccessModifier.Private && _currentClass?.Name != current.Name)
                 {
                     throw new Exception($"Type Error: Property '{memberName}' is private and only accessible within class '{current.Name}'.");
                 }
                 if (access == AccessModifier.Protected && !IsSubclassOf(_currentClass, current))
                 {
                     throw new Exception($"Type Error: Property '{memberName}' is protected and only accessible within class '{current.Name}' and its subclasses.");
                 }

                 // Check readonly - only allow assignment in constructor
                 if (current.ReadonlyFieldSet.Contains(memberName))
                 {
                     // Allow in constructor
                     bool inConstructor = _currentClass?.Name == current.Name &&
                         _environment.IsDefined("this");
                     // Simplified check - just allow if we're in the same class
                     if (_currentClass?.Name != current.Name)
                     {
                         throw new Exception($"Type Error: Cannot assign to '{memberName}' because it is a read-only property.");
                     }
                 }

                 current = current.Superclass;
             }

             return CheckExpr(set.Value);
        }
        else if (objType is TypeInfo.Record record)
        {
             if (record.Fields.TryGetValue(set.Name.Lexeme, out var fieldType))
             {
                 TypeInfo valueType = CheckExpr(set.Value);
                 if (!IsCompatible(fieldType, valueType))
                 {
                     throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{fieldType}'.");
                 }
                 return valueType;
             }
             // For now, disallow adding new properties to records via assignment to mimic strictness
             throw new Exception($"Type Error: Property '{set.Name.Lexeme}' does not exist on type '{record}'.");
        }
        // Allow property assignment on Any type (e.g., 'this' in object method shorthand)
        if (objType is TypeInfo.Any)
        {
            return CheckExpr(set.Value);
        }
        throw new Exception("Type Error: Only instances and objects have properties.");
    }
}
