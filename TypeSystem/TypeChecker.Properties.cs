using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Property and member access type checking.
/// </summary>
/// <remarks>
/// Contains handlers for property access:
/// CheckThis, CheckSuper, CheckGet, CheckSet.
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
        // Handle WeakMap instance methods
        if (objType is TypeInfo.WeakMap weakMapType)
        {
            var memberType = BuiltInTypes.GetWeakMapMemberType(get.Name.Lexeme, weakMapType.KeyType, weakMapType.ValueType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'WeakMap'.");
        }
        // Handle WeakSet instance methods
        if (objType is TypeInfo.WeakSet weakSetType)
        {
            var memberType = BuiltInTypes.GetWeakSetMemberType(get.Name.Lexeme, weakSetType.ElementType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'WeakSet'.");
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
