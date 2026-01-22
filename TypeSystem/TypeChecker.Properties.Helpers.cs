using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Helper methods for category-based property type checking.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Resolves member type for built-in types using the TypeCategory dispatch pattern.
    /// Returns null if the category doesn't have built-in member resolution.
    /// </summary>
    private TypeInfo? ResolveBuiltInMemberType(TypeCategory category, TypeInfo objType, string memberName)
    {
        return category switch
        {
            TypeCategory.String => BuiltInTypes.GetStringMemberType(memberName),
            TypeCategory.Array when objType is TypeInfo.Array arr =>
                BuiltInTypes.GetArrayMemberType(memberName, arr.ElementType),
            TypeCategory.Tuple when objType is TypeInfo.Tuple tuple =>
                ResolveArrayMemberForTuple(tuple, memberName),
            TypeCategory.Map when objType is TypeInfo.Map map =>
                BuiltInTypes.GetMapMemberType(memberName, map.KeyType, map.ValueType),
            TypeCategory.Set when objType is TypeInfo.Set set =>
                BuiltInTypes.GetSetMemberType(memberName, set.ElementType),
            TypeCategory.WeakMap when objType is TypeInfo.WeakMap wm =>
                BuiltInTypes.GetWeakMapMemberType(memberName, wm.KeyType, wm.ValueType),
            TypeCategory.WeakSet when objType is TypeInfo.WeakSet ws =>
                BuiltInTypes.GetWeakSetMemberType(memberName, ws.ElementType),
            TypeCategory.Date => BuiltInTypes.GetDateInstanceMemberType(memberName),
            TypeCategory.RegExp => BuiltInTypes.GetRegExpMemberType(memberName),
            TypeCategory.Error when objType is TypeInfo.Error err =>
                BuiltInTypes.GetErrorMemberType(memberName, err.Name),
            TypeCategory.Timeout => BuiltInTypes.GetTimeoutMemberType(memberName),
            TypeCategory.Function when objType is TypeInfo.Function func =>
                BuiltInTypes.GetFunctionMemberType(memberName, func),
            TypeCategory.Function when objType is TypeInfo.GenericFunction gf =>
                BuiltInTypes.GetFunctionMemberType(memberName,
                    new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType, gf.ParamNames)),
            TypeCategory.Function when objType is TypeInfo.OverloadedFunction of =>
                BuiltInTypes.GetFunctionMemberType(memberName, of.Implementation),
            _ => null
        };
    }

    /// <summary>
    /// Resolves array member type for a tuple by computing the union of element types.
    /// </summary>
    private TypeInfo? ResolveArrayMemberForTuple(TypeInfo.Tuple tuple, string memberName)
    {
        var allTypes = tuple.ElementTypes.ToList();
        if (tuple.RestElementType != null)
            allTypes.Add(tuple.RestElementType);
        var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
        TypeInfo unionElem = unique.Count == 0
            ? new TypeInfo.Any()
            : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
        return BuiltInTypes.GetArrayMemberType(memberName, unionElem);
    }

    /// <summary>
    /// Type checks property access on a TypeParameter by delegating to its constraint.
    /// </summary>
    private TypeInfo CheckGetOnTypeParameter(TypeInfo.TypeParameter tp, Token memberName)
    {
        if (tp.Constraint != null)
        {
            return CheckGetOnType(tp.Constraint, memberName);
        }
        throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.");
    }

    /// <summary>
    /// Type checks static member access on a class type (Foo.staticProp).
    /// </summary>
    private TypeInfo CheckGetOnClass(TypeInfo.Class classType, Token memberName)
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

    /// <summary>
    /// Type checks member access on a namespace type.
    /// </summary>
    private TypeInfo CheckGetOnNamespace(TypeInfo.Namespace nsType, Token memberName)
    {
        var memberType = nsType.GetMember(memberName.Lexeme);
        if (memberType != null)
        {
            return memberType;
        }
        throw new TypeCheckException($" '{memberName.Lexeme}' does not exist on namespace '{nsType.Name}'.");
    }

    /// <summary>
    /// Type checks member access on an enum type.
    /// </summary>
    private TypeInfo CheckGetOnEnum(TypeInfo.Enum enumType, Token memberName)
    {
        if (enumType.Members.TryGetValue(memberName.Lexeme, out var memberValue))
        {
            return memberValue switch
            {
                double => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                string => new TypeInfo.String(),
                _ => throw new TypeCheckException($" Unexpected enum member type for '{memberName.Lexeme}'.")
            };
        }
        throw new TypeCheckException($" '{memberName.Lexeme}' does not exist on enum '{enumType.Name}'.");
    }

    /// <summary>
    /// Type checks member access on an interface type.
    /// </summary>
    private TypeInfo CheckGetOnInterface(TypeInfo.Interface itf, Token memberName)
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

    /// <summary>
    /// Type checks member access on a record/object literal type.
    /// </summary>
    private TypeInfo CheckGetOnRecord(TypeInfo.Record record, Token memberName)
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

    /// <summary>
    /// Type checks member access on a class instance (new Foo().prop).
    /// Handles both regular and generic class instances with inheritance chain walking.
    /// </summary>
    private TypeInfo CheckGetOnInstance(TypeInfo.Instance instance, Token memberName)
    {
        string memberNameStr = memberName.Lexeme;

        // Handle instantiated generic class (e.g., Box<number>)
        if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
            ig.GenericDefinition is TypeInfo.GenericClass gc)
        {
            return CheckGetOnGenericInstance(gc, ig.TypeArguments, memberNameStr);
        }

        // Handle regular class instance
        if (instance.ClassType is TypeInfo.Class instanceClassType)
        {
            return CheckGetOnRegularInstance(instanceClassType, memberName);
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// Type checks member access on a generic class instance.
    /// </summary>
    private TypeInfo CheckGetOnGenericInstance(TypeInfo.GenericClass gc, List<TypeInfo> typeArgs, string memberName)
    {
        // Build substitution map from type parameters to type arguments
        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gc.TypeParams.Count; i++)
            subs[gc.TypeParams[i].Name] = typeArgs[i];

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
            return SubstituteMethodType(methodType, subs);
        }

        // Check superclass if any
        if (gc.Superclass != null)
        {
            TypeInfo? currentSuper = gc.Superclass;
            while (currentSuper != null)
            {
                var superMethods = GetMethods(currentSuper);
                var superFields = GetFieldTypes(currentSuper);
                if (superMethods != null && superMethods.TryGetValue(memberName, out var superMethod))
                    return superMethod;
                if (superFields != null && superFields.TryGetValue(memberName, out var superField))
                    return superField;
                currentSuper = GetSuperclass(currentSuper);
            }
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// Substitutes type parameters in a method type.
    /// </summary>
    private TypeInfo SubstituteMethodType(TypeInfo methodType, Dictionary<string, TypeInfo> subs)
    {
        if (methodType is TypeInfo.Function funcType)
        {
            var substitutedParams = funcType.ParamTypes.Select(p => Substitute(p, subs)).ToList();
            var substitutedReturn = Substitute(funcType.ReturnType, subs);
            return new TypeInfo.Function(substitutedParams, substitutedReturn, funcType.RequiredParams, funcType.HasRestParam);
        }
        else if (methodType is TypeInfo.OverloadedFunction overloadedFunc)
        {
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
        return methodType;
    }

    /// <summary>
    /// Type checks member access on a regular (non-generic) class instance.
    /// </summary>
    private TypeInfo CheckGetOnRegularInstance(TypeInfo.Class startClass, Token memberName)
    {
        string memberNameStr = memberName.Lexeme;
        TypeInfo? current = startClass;
        Dictionary<string, TypeInfo> substitutions = [];

        while (current != null)
        {
            // If current is an InstantiatedGeneric, build/extend the substitution map
            if (current is TypeInfo.InstantiatedGeneric igCurrent &&
                igCurrent.GenericDefinition is TypeInfo.GenericClass gcCurrent)
            {
                for (int i = 0; i < gcCurrent.TypeParams.Count && i < igCurrent.TypeArguments.Count; i++)
                {
                    substitutions[gcCurrent.TypeParams[i].Name] = igCurrent.TypeArguments[i];
                }
            }

            // Check for getter first
            var getters = GetGetters(current);
            if (getters != null && getters.TryGetValue(memberNameStr, out var getterType))
            {
                return substitutions.Count > 0 ? Substitute(getterType, substitutions) : getterType;
            }

            // Check access modifier
            AccessModifier access = AccessModifier.Public;
            var methodAccess = GetMethodAccess(current);
            var fieldAccess = GetFieldAccess(current);
            if (methodAccess != null && methodAccess.TryGetValue(memberNameStr, out var ma))
                access = ma;
            else if (fieldAccess != null && fieldAccess.TryGetValue(memberNameStr, out var fa))
                access = fa;

            var currentName = GetClassName(current);
            if (access == AccessModifier.Private && _currentClass?.Name != currentName)
            {
                throw new TypeCheckException($" Property '{memberNameStr}' is private and only accessible within class '{currentName}'.");
            }
            var currentClassForAccess = AsClass(current);
            if (access == AccessModifier.Protected && currentClassForAccess != null && !IsSubclassOf(_currentClass, currentClassForAccess))
            {
                throw new TypeCheckException($" Property '{memberNameStr}' is protected and only accessible within class '{currentName}' and its subclasses.");
            }

            var methods = GetMethods(current);
            if (methods != null && methods.TryGetValue(memberNameStr, out var methodType))
            {
                return substitutions.Count > 0 ? Substitute(methodType, substitutions) : methodType;
            }

            // Check for field
            var fieldTypes = GetFieldTypes(current);
            if (fieldTypes != null && fieldTypes.TryGetValue(memberNameStr, out var fieldType))
            {
                return substitutions.Count > 0 ? Substitute(fieldType, substitutions) : fieldType;
            }

            current = GetSuperclass(current);
        }
        return new TypeInfo.Any();
    }
}
