using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

// Type category helper methods are defined in TypeChecker.Properties.Helpers.cs

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
            throw new TypeCheckException(" Cannot use 'super' outside of a class.");
        }
        if (_currentClass.Superclass == null)
        {
            throw new TypeCheckException($" Class '{_currentClass.Name}' does not have a superclass.");
        }

        // Get methods from superclass, handling both Class and InstantiatedGeneric
        var superMethods = GetMethods(_currentClass.Superclass);
        var superName = GetClassName(_currentClass.Superclass) ?? "unknown";

        // super() constructor call - Method is null
        if (expr.Method == null)
        {
            if (superMethods != null && superMethods.TryGetValue("constructor", out var ctorType))
            {
                return ctorType;
            }
            // Default constructor with no parameters
            return new TypeInfo.Function([], new TypeInfo.Void());
        }

        if (superMethods != null && superMethods.TryGetValue(expr.Method.Lexeme, out var methodType))
        {
            return methodType;
        }

        throw new TypeCheckException($" Property '{expr.Method.Lexeme}' does not exist on superclass '{superName}'.");
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
            throw new TypeCheckException(" Cannot use 'this' outside of a class.");
        }
        // In static blocks, 'this' refers to the class constructor (the class type itself)
        if (_inStaticBlock)
        {
            return _currentClass;
        }
        if (_inStaticMethod)
        {
            throw new TypeCheckException(" Cannot use 'this' in a static method.");
        }
        return new TypeInfo.Instance(_currentClass);
    }

    private TypeInfo CheckGet(Expr.Get get)
    {
        // Special case: Symbol.iterator, Symbol.asyncIterator, etc. return unique symbol types
        if (get.Object is Expr.Variable v && v.Name.Lexeme == "Symbol")
        {
            var wellKnownType = WellKnownSymbolTypes.TryGet(get.Name.Lexeme);
            if (wellKnownType != null)
                return wellKnownType;
        }

        TypeInfo objType = CheckExpr(get.Object);

        // Expand recursive type aliases lazily before property access
        if (objType is TypeInfo.RecursiveTypeAlias rta)
        {
            objType = ExpandRecursiveTypeAlias(rta);
        }

        var category = TypeCategoryResolver.Classify(objType);
        string memberName = get.Name.Lexeme;

        // Fast path for built-in types with explicit member validation
        if (TypeCategoryResolver.HasBuiltInMemberValidation(category))
        {
            var memberType = ResolveBuiltInMemberType(category, objType, memberName);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName}' does not exist on type '{GetTypeDisplayName(category, objType)}'.");
        }

        // Category-based dispatch for user-defined and special types
        return category switch
        {
            TypeCategory.TypeParameter when objType is TypeInfo.TypeParameter tp =>
                CheckGetOnTypeParameter(tp, get.Name),
            TypeCategory.Class when objType is TypeInfo.Class classType =>
                CheckGetOnClass(classType, get.Name),
            TypeCategory.Instance when objType is TypeInfo.Instance instance =>
                CheckGetOnInstance(instance, get.Name),
            TypeCategory.Interface when objType is TypeInfo.Interface itf =>
                CheckGetOnInterface(itf, get.Name),
            TypeCategory.Record when objType is TypeInfo.Record record =>
                CheckGetOnRecord(record, get.Name),
            TypeCategory.Enum when objType is TypeInfo.Enum enumType =>
                CheckGetOnEnum(enumType, get.Name),
            TypeCategory.Namespace when objType is TypeInfo.Namespace nsType =>
                CheckGetOnNamespace(nsType, get.Name),
            _ => new TypeInfo.Any()
        };
    }

    /// <summary>
    /// Gets a display name for error messages based on the type category.
    /// </summary>
    private static string GetTypeDisplayName(TypeCategory category, TypeInfo objType) => category switch
    {
        TypeCategory.String => "string",
        TypeCategory.Number => "number",
        TypeCategory.Boolean => "boolean",
        TypeCategory.Array => "array",
        TypeCategory.Tuple => "tuple",
        TypeCategory.Map => "Map",
        TypeCategory.Set => "Set",
        TypeCategory.WeakMap => "WeakMap",
        TypeCategory.WeakSet => "WeakSet",
        TypeCategory.Date => "Date",
        TypeCategory.RegExp => "RegExp",
        TypeCategory.Error when objType is TypeInfo.Error err => err.Name,
        TypeCategory.Timeout => "Timeout",
        _ => objType.ToString() ?? "unknown"
    };

    private TypeInfo CheckSet(Expr.Set set)
    {
        TypeInfo objType = CheckExpr(set.Object);

        // Handle TypeParameter - delegate to constraint type for property assignment
        if (objType is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
            {
                // Check that the property exists on the constraint
                var propType = CheckGetOnType(tp.Constraint, set.Name);
                TypeInfo valueType = CheckExpr(set.Value);
                if (!IsCompatible(propType, valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{propType}'.");
                }
                return valueType;
            }
            throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.");
        }

        // Handle static property assignment
        if (objType is TypeInfo.Class classType)
        {
            TypeInfo? current = classType;
            while (current != null)
            {
                var staticProps = GetStaticProperties(current);
                if (staticProps != null && staticProps.TryGetValue(set.Name.Lexeme, out var staticPropType))
                {
                    TypeInfo valueType = CheckExpr(set.Value);
                    if (!IsCompatible(staticPropType, valueType))
                    {
                        throw new TypeCheckException($" Cannot assign '{valueType}' to static property '{set.Name.Lexeme}' of type '{staticPropType}'.");
                    }
                    return valueType;
                }
                current = GetSuperclass(current);
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
                 Dictionary<string, TypeInfo> subs = [];
                 for (int i = 0; i < gc.TypeParams.Count; i++)
                     subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                 // Check for setter
                 if (gc.Setters?.TryGetValue(memberName, out var setterType) == true)
                 {
                     var substitutedType = Substitute(setterType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new TypeCheckException($" Cannot assign '{valueType}' to property '{memberName}' expecting '{substitutedType}'.");
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
                         throw new TypeCheckException($" Cannot assign '{valueType}' to field '{memberName}' of type '{substitutedType}'.");
                     }
                     return valueType;
                 }

                 return CheckExpr(set.Value);
             }

             // Handle regular Class
             if (instance.ClassType is not TypeInfo.Class startClass)
                 return CheckExpr(set.Value);

             TypeInfo? current = startClass;

             // Check for setter first
             while (current != null)
             {
                 var setters = GetSetters(current);
                 var getters = GetGetters(current);
                 if (setters != null && setters.TryGetValue(memberName, out var setterType))
                 {
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(setterType, valueType))
                     {
                         throw new TypeCheckException($" Cannot assign '{valueType}' to property '{memberName}' expecting '{setterType}'.");
                     }
                     return valueType;
                 }

                 // Check if there's a getter but no setter (read-only property)
                 if (getters != null && getters.ContainsKey(memberName) && (setters == null || !setters.ContainsKey(memberName)))
                 {
                     throw new TypeCheckException($" Cannot assign to '{memberName}' because it is a read-only property (has getter but no setter).");
                 }

                 current = GetSuperclass(current);
             }

             // Reset to check access and readonly
             current = startClass;

             // Check access and readonly
             while (current != null)
             {
                 // Check access modifier
                 AccessModifier access = AccessModifier.Public;
                 var fieldAccess = GetFieldAccess(current);
                 if (fieldAccess != null && fieldAccess.TryGetValue(memberName, out var fa))
                     access = fa;

                 var currentName = GetClassName(current);
                 if (access == AccessModifier.Private && _currentClass?.Name != currentName)
                 {
                     throw new TypeCheckException($" Property '{memberName}' is private and only accessible within class '{currentName}'.");
                 }
                 var currentClass2 = AsClass(current);
                 if (access == AccessModifier.Protected && currentClass2 != null && !IsSubclassOf(_currentClass, currentClass2))
                 {
                     throw new TypeCheckException($" Property '{memberName}' is protected and only accessible within class '{currentName}' and its subclasses.");
                 }

                 // Check readonly - only allow assignment in constructor
                 var readonlyFields = GetReadonlyFields(current);
                 if (readonlyFields != null && readonlyFields.Contains(memberName))
                 {
                     // Allow in constructor
                     bool inConstructor = _currentClass?.Name == currentName &&
                         _environment.IsDefined("this");
                     // Simplified check - just allow if we're in the same class
                     if (_currentClass?.Name != currentName)
                     {
                         throw new TypeCheckException($" Cannot assign to '{memberName}' because it is a read-only property.");
                     }
                 }

                 current = GetSuperclass(current);
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
                     throw new TypeCheckException($" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{fieldType}'.");
                 }
                 return valueType;
             }
             // For now, disallow adding new properties to records via assignment to mimic strictness
             throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on type '{record}'.");
        }
        // Handle Error property assignment (name, message, stack are all mutable strings)
        if (objType is TypeInfo.Error)
        {
            string propName = set.Name.Lexeme;
            if (ErrorBuiltIns.CanSetProperty(propName))
            {
                TypeInfo valueType = CheckExpr(set.Value);
                if (!IsCompatible(new TypeInfo.String(), valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to property '{propName}' of type 'string'.");
                }
                return valueType;
            }
            throw new TypeCheckException($" Property '{propName}' does not exist on type 'Error'.");
        }
        // Allow property assignment on Any type (e.g., 'this' in object method shorthand)
        if (objType is TypeInfo.Any)
        {
            return CheckExpr(set.Value);
        }
        throw new TypeCheckException(" Only instances and objects have properties.");
    }

    /// <summary>
    /// Resolves member access on a given type without needing an actual expression.
    /// Used for TypeParameter constraint delegation and other scenarios where we need
    /// to check member access on a type directly.
    /// </summary>
    private TypeInfo CheckGetOnType(TypeInfo objType, Token memberName)
    {
        // Expand recursive type aliases lazily before property access
        if (objType is TypeInfo.RecursiveTypeAlias rta)
        {
            objType = ExpandRecursiveTypeAlias(rta);
        }

        // Handle TypeParameter recursively - delegate to constraint
        if (objType is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
            {
                return CheckGetOnType(tp.Constraint, memberName);
            }
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.");
        }

        // Handle Interface - check own and inherited members
        if (objType is TypeInfo.Interface itf)
        {
            foreach (var member in itf.GetAllMembers())
            {
                if (member.Key == memberName.Lexeme)
                {
                    return member.Value;
                }
            }
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on interface '{itf.Name}'.");
        }

        // Handle Record type - check fields and index signatures
        if (objType is TypeInfo.Record record)
        {
            if (record.Fields.TryGetValue(memberName.Lexeme, out var fieldType))
            {
                return fieldType;
            }
            if (record.StringIndexType != null)
            {
                return record.StringIndexType;
            }
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{record}'.");
        }

        // Handle String type - check string methods
        if (objType is TypeInfo.String or TypeInfo.StringLiteral)
        {
            var memberType = BuiltInTypes.GetStringMemberType(memberName.Lexeme);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'string'.");
        }

        // Handle primitive number type - no methods
        if (objType is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER)
        {
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'number'.");
        }

        // Handle Array type - check array methods
        if (objType is TypeInfo.Array arrayType)
        {
            var memberType = BuiltInTypes.GetArrayMemberType(memberName.Lexeme, arrayType.ElementType);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'array'.");
        }

        // Handle Tuple type - treat as array with union element type
        if (objType is TypeInfo.Tuple tupleType)
        {
            var allTypes = tupleType.ElementTypes.ToList();
            if (tupleType.RestElementType != null)
                allTypes.Add(tupleType.RestElementType);
            var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            TypeInfo unionElem = unique.Count == 0
                ? new TypeInfo.Any()
                : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
            var memberType = BuiltInTypes.GetArrayMemberType(memberName.Lexeme, unionElem);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on tuple type.");
        }

        // Handle Class type - check static members
        if (objType is TypeInfo.Class classType)
        {
            TypeInfo? current = classType;
            while (current != null)
            {
                var staticMethods = GetStaticMethods(current);
                var staticProps = GetStaticProperties(current);
                if (staticMethods != null && staticMethods.TryGetValue(memberName.Lexeme, out var staticMethodType))
                {
                    return staticMethodType;
                }
                if (staticProps != null && staticProps.TryGetValue(memberName.Lexeme, out var staticPropType))
                {
                    return staticPropType;
                }
                current = GetSuperclass(current);
            }
            return new TypeInfo.Any();
        }

        // Handle Instance type - check instance members
        if (objType is TypeInfo.Instance instance)
        {
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                Dictionary<string, TypeInfo> subs = [];
                for (int i = 0; i < gc.TypeParams.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                if (gc.Getters?.TryGetValue(memberName.Lexeme, out var getterType) == true)
                    return Substitute(getterType, subs);
                if (gc.FieldTypes?.TryGetValue(memberName.Lexeme, out var fieldType) == true)
                    return Substitute(fieldType, subs);
                if (gc.Methods.TryGetValue(memberName.Lexeme, out var methodType))
                {
                    if (methodType is TypeInfo.Function funcType)
                    {
                        var substitutedParams = funcType.ParamTypes.Select(pt => Substitute(pt, subs)).ToList();
                        var substitutedReturn = Substitute(funcType.ReturnType, subs);
                        return new TypeInfo.Function(substitutedParams, substitutedReturn, funcType.RequiredParams, funcType.HasRestParam);
                    }
                    return methodType;
                }
            }

            if (instance.ClassType is TypeInfo.Class instanceClassType)
            {
                TypeInfo? current = instanceClassType;
                while (current != null)
                {
                    var getters = GetGetters(current);
                    if (getters != null && getters.TryGetValue(memberName.Lexeme, out var getterType))
                        return getterType;
                    var methods = GetMethods(current);
                    if (methods != null && methods.TryGetValue(memberName.Lexeme, out var methodType))
                        return methodType;
                    var fieldTypes = GetFieldTypes(current);
                    if (fieldTypes != null && fieldTypes.TryGetValue(memberName.Lexeme, out var fieldType))
                        return fieldType;
                    current = GetSuperclass(current);
                }
            }
            return new TypeInfo.Any();
        }

        // Handle Union type - check if all members have the property
        if (objType is TypeInfo.Union union)
        {
            List<TypeInfo> memberTypes = [];
            foreach (var member in union.FlattenedTypes)
            {
                try
                {
                    var memberType = CheckGetOnType(member, memberName);
                    memberTypes.Add(memberType);
                }
                catch (TypeCheckException)
                {
                    // If any member doesn't have the property, it's an error
                    throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on all members of union type '{union}'.");
                }
            }
            // Return union of all member types
            var unique = memberTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
        }

        // Handle Intersection type - merge members from all types
        if (objType is TypeInfo.Intersection intersection)
        {
            // Try each type in the intersection - first match wins
            foreach (var member in intersection.FlattenedTypes)
            {
                try
                {
                    return CheckGetOnType(member, memberName);
                }
                catch (TypeCheckException)
                {
                    // Continue to next type in intersection
                }
            }
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{intersection}'.");
        }

        // Handle Date, RegExp, Map, Set, WeakMap, WeakSet
        if (objType is TypeInfo.Date)
        {
            var memberType = BuiltInTypes.GetDateInstanceMemberType(memberName.Lexeme);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'Date'.");
        }
        if (objType is TypeInfo.RegExp)
        {
            var memberType = BuiltInTypes.GetRegExpMemberType(memberName.Lexeme);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'RegExp'.");
        }
        if (objType is TypeInfo.Error errorType)
        {
            var memberType = BuiltInTypes.GetErrorMemberType(memberName.Lexeme, errorType.Name);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{errorType.Name}'.");
        }
        if (objType is TypeInfo.Map mapType)
        {
            var memberType = BuiltInTypes.GetMapMemberType(memberName.Lexeme, mapType.KeyType, mapType.ValueType);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'Map'.");
        }
        if (objType is TypeInfo.Set setType)
        {
            var memberType = BuiltInTypes.GetSetMemberType(memberName.Lexeme, setType.ElementType);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'Set'.");
        }
        if (objType is TypeInfo.WeakMap weakMapType)
        {
            var memberType = BuiltInTypes.GetWeakMapMemberType(memberName.Lexeme, weakMapType.KeyType, weakMapType.ValueType);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'WeakMap'.");
        }
        if (objType is TypeInfo.WeakSet weakSetType)
        {
            var memberType = BuiltInTypes.GetWeakSetMemberType(memberName.Lexeme, weakSetType.ElementType);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'WeakSet'.");
        }
        // Handle Timeout instance methods (ref, unref) and properties (hasRef)
        if (objType is TypeInfo.Timeout)
        {
            var memberType = BuiltInTypes.GetTimeoutMemberType(memberName.Lexeme);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'Timeout'.");
        }

        // Handle Any type
        if (objType is TypeInfo.Any)
        {
            return new TypeInfo.Any();
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// Type checks ES2022 private field access: obj.#field
    /// Private fields are only accessible within the declaring class body.
    /// </summary>
    private TypeInfo CheckGetPrivate(Expr.GetPrivate get)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{get.Name.Lexeme}' outside of a class body.");
        }

        // Check the object expression
        TypeInfo objType = CheckExpr(get.Object);

        // For 'this' access, verify we have the private field
        string fieldName = get.Name.Lexeme;

        // Look up in current class's private fields (NOT inherited - private fields are not inherited)
        if (_currentClass.PrivateFieldTypes.TryGetValue(fieldName, out var fieldType))
        {
            return fieldType;
        }

        // Check static private fields if accessing on the class itself
        if (objType is TypeInfo.Class && _currentClass.StaticPrivateFieldTypes.TryGetValue(fieldName, out var staticFieldType))
        {
            return staticFieldType;
        }

        throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.");
    }

    /// <summary>
    /// Type checks ES2022 private field assignment: obj.#field = value
    /// </summary>
    private TypeInfo CheckSetPrivate(Expr.SetPrivate set)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{set.Name.Lexeme}' outside of a class body.");
        }

        TypeInfo objType = CheckExpr(set.Object);
        TypeInfo valueType = CheckExpr(set.Value);
        string fieldName = set.Name.Lexeme;

        // Look up in current class's private fields (NOT inherited)
        TypeInfo? fieldType = null;
        if (_currentClass.PrivateFieldTypes.TryGetValue(fieldName, out var pf))
        {
            fieldType = pf;
        }
        else if (objType is TypeInfo.Class && _currentClass.StaticPrivateFieldTypes.TryGetValue(fieldName, out var spf))
        {
            fieldType = spf;
        }

        if (fieldType == null)
        {
            throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.");
        }

        if (!IsCompatible(fieldType, valueType))
        {
            throw new TypeCheckException($" Cannot assign type '{valueType}' to private field '{fieldName}' of type '{fieldType}'.");
        }

        return valueType;
    }

    /// <summary>
    /// Type checks ES2022 private method call: obj.#method(args)
    /// </summary>
    private TypeInfo CheckCallPrivate(Expr.CallPrivate call)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot call private method '{call.Name.Lexeme}' outside of a class body.");
        }

        TypeInfo objType = CheckExpr(call.Object);
        string methodName = call.Name.Lexeme;

        // Look up in current class's private methods (NOT inherited)
        TypeInfo? methodType = null;
        if (_currentClass.PrivateMethodTypes.TryGetValue(methodName, out var pm))
        {
            methodType = pm;
        }
        else if (objType is TypeInfo.Class && _currentClass.StaticPrivateMethodTypes.TryGetValue(methodName, out var spm))
        {
            methodType = spm;
        }

        if (methodType == null)
        {
            throw new TypeCheckException($" Private method '{methodName}' does not exist on class '{_currentClass.Name}'.");
        }

        // Check argument types against method signature
        if (methodType is not TypeInfo.Function funcType)
        {
            throw new TypeCheckException($" Private member '{methodName}' is not a method.");
        }

        // Check argument count
        if (call.Arguments.Count < funcType.RequiredParams)
        {
            throw new TypeCheckException($" Private method '{methodName}' requires at least {funcType.RequiredParams} arguments, got {call.Arguments.Count}.");
        }

        if (!funcType.HasRestParam && call.Arguments.Count > funcType.ParamTypes.Count)
        {
            throw new TypeCheckException($" Private method '{methodName}' accepts at most {funcType.ParamTypes.Count} arguments, got {call.Arguments.Count}.");
        }

        // Check each argument type
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            TypeInfo argType = CheckExpr(call.Arguments[i]);
            TypeInfo paramType = i < funcType.ParamTypes.Count
                ? funcType.ParamTypes[i]
                : funcType.ParamTypes[^1]; // Rest parameter type

            if (!IsCompatible(paramType, argType))
            {
                throw new TypeCheckException($" Argument {i + 1} to private method '{methodName}' has type '{argType}' but expected '{paramType}'.");
            }
        }

        return funcType.ReturnType;
    }
}
