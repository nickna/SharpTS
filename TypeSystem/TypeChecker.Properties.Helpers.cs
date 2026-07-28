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
            TypeCategory.WeakRef when objType is TypeInfo.WeakRef wr =>
                BuiltInTypes.GetWeakRefMemberType(memberName, wr.TargetType),
            TypeCategory.FinalizationRegistry when objType is TypeInfo.FinalizationRegistry fr =>
                BuiltInTypes.GetFinalizationRegistryMemberType(memberName, fr.TargetType),
            TypeCategory.Date => BuiltInTypes.GetDateInstanceMemberType(memberName),
            TypeCategory.RegExp => BuiltInTypes.GetRegExpMemberType(memberName),
            TypeCategory.Error when objType is TypeInfo.Error err =>
                BuiltInTypes.GetErrorMemberType(memberName, err.Name),
            TypeCategory.Timeout => BuiltInTypes.GetTimeoutMemberType(memberName),
            TypeCategory.Buffer => BuiltInTypes.GetBufferMemberType(memberName),
            TypeCategory.Function when objType is TypeInfo.Function func =>
                BuiltInTypes.GetFunctionMemberType(memberName, func),
            TypeCategory.Function when objType is TypeInfo.GenericFunction gf =>
                BuiltInTypes.GetFunctionMemberType(memberName,
                    new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType, gf.ParamNames)),
            TypeCategory.Function when objType is TypeInfo.OverloadedFunction of =>
                BuiltInTypes.GetFunctionMemberType(memberName, of.Implementation),
            TypeCategory.AbortController => BuiltInTypes.GetAbortControllerMemberType(memberName),
            TypeCategory.AbortSignal => BuiltInTypes.GetAbortSignalMemberType(memberName),
            TypeCategory.Iterator when objType is TypeInfo.Iterator iter =>
                BuiltInTypes.GetIteratorMemberType(memberName, iter.ElementType),
            TypeCategory.Iterable when objType is TypeInfo.Iterable iterable =>
                BuiltInTypes.GetIterableMemberType(memberName, iterable.ElementType),
            TypeCategory.Generator when objType is TypeInfo.Generator gen =>
                BuiltInTypes.GetIteratorMemberType(memberName, gen.YieldType),
            TypeCategory.AsyncGenerator when objType is TypeInfo.AsyncGenerator asyncGen =>
                BuiltInTypes.GetIteratorMemberType(memberName, asyncGen.YieldType),
            TypeCategory.AsyncGenerator when objType is TypeInfo.AsyncIterable asyncIter =>
                BuiltInTypes.GetIteratorMemberType(memberName, asyncIter.ElementType),
            TypeCategory.AsyncGenerator when objType is TypeInfo.AsyncIterator asyncIt =>
                BuiltInTypes.GetIteratorMemberType(memberName, asyncIt.ElementType),
            TypeCategory.Promise when objType is TypeInfo.Promise promise =>
                BuiltInTypes.GetPromiseMemberType(memberName, promise.ValueType),
            TypeCategory.EventEmitter =>
                BuiltInTypes.GetEventEmitterMemberType(memberName),
            _ => null
        };
    }

    /// <summary>
    /// Computes the union of all element types in a tuple (including rest element).
    /// Returns a single type if all elements are the same, or a Union type otherwise.
    /// </summary>
    private static TypeInfo ComputeTupleElementUnion(TypeInfo.Tuple tuple)
    {
        var allTypes = tuple.ElementTypes.ToList();
        if (tuple.RestElementType != null)
            allTypes.Add(tuple.RestElementType);
        var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
        return unique.Count == 0
            ? TypeInfo.Any.Shared
            : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
    }

    /// <summary>
    /// Resolves array member type for a tuple by computing the union of element types.
    /// </summary>
    private TypeInfo? ResolveArrayMemberForTuple(TypeInfo.Tuple tuple, string memberName)
    {
        return BuiltInTypes.GetArrayMemberType(memberName, ComputeTupleElementUnion(tuple));
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
        throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.", tsCode: "TS2339");
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
                EnforceStaticMemberAccess(current, memberName);
                return staticMethodType;
            }
            if (staticProps != null && staticProps.TryGetValue(memberName.Lexeme, out var staticPropType))
            {
                EnforceStaticMemberAccess(current, memberName);
                return staticPropType;
            }
            current = GetSuperclass(current);
        }
        return TypeInfo.Any.Shared;
    }

    /// <summary>
    /// Enforces <c>private</c>/<c>protected</c> visibility for a static member (method or
    /// field/property) declared on <paramref name="declaringClass"/> when accessed as
    /// <c>Class.member</c>. Static members carry their own access maps
    /// (<see cref="ClassMetadataCore.StaticMethodAccessMap"/> /
    /// <see cref="ClassMetadataCore.StaticFieldAccessMap"/>), distinct from instance members,
    /// so a same-named static/instance pair don't clobber each other's visibility (issue #722).
    /// No-op for public members or non-class types.
    /// </summary>
    private void EnforceStaticMemberAccess(TypeInfo declaringClass, Token memberName)
    {
        AccessModifier access;
        var staticMethodAccess = GetStaticMethodAccess(declaringClass);
        var staticFieldAccess = GetStaticFieldAccess(declaringClass);
        if (staticMethodAccess != null && staticMethodAccess.TryGetValue(memberName.Lexeme, out var ma))
            access = ma;
        else if (staticFieldAccess != null && staticFieldAccess.TryGetValue(memberName.Lexeme, out var fa))
            access = fa;
        else
            return;
        if (access == AccessModifier.Public)
            return;

        var declName = GetClassName(declaringClass);
        if (access == AccessModifier.Private && _currentClass?.Name != declName)
        {
            throw new TypeCheckException($" Property '{memberName.Lexeme}' is private and only accessible within class '{declName}'.", tsCode: "TS2341");
        }
        var declClass = AsClass(declaringClass);
        if (access == AccessModifier.Protected && declClass != null && !IsSubclassOf(_currentClass, declClass))
        {
            throw new TypeCheckException($" Property '{memberName.Lexeme}' is protected and only accessible within class '{declName}' and its subclasses.", tsCode: "TS2445");
        }
    }

    /// <summary>
    /// Type checks member access on a namespace type.
    /// </summary>
    private TypeInfo CheckGetOnNamespace(TypeInfo.Namespace nsType, Token memberName)
    {
        // Expression-space lookup must prefer the value facet when a namespace
        // exports both a type and a value with the same name (for example
        // `interface Intl.PluralRules` plus `const Intl.PluralRules`).
        if (nsType.Values.GetValueOrDefault(memberName.Lexeme)
            is { } valueMember)
        {
            if (nsType.GetValueBinding(memberName.Lexeme) is { } valueBinding)
                Bindings.Bind(
                    memberName,
                    CurrentSourceDocument,
                    valueBinding);
            return valueMember;
        }
        if (nsType.Types.GetValueOrDefault(memberName.Lexeme)
            is { } typeMember)
        {
            BindingSymbol? binding =
                nsType.GetValueBinding(memberName.Lexeme)
                ?? nsType.GetTypeBinding(memberName.Lexeme);
            if (binding is not null)
                Bindings.Bind(
                    memberName,
                    CurrentSourceDocument,
                    binding);
            return typeMember;
        }
        throw new TypeCheckException($" '{memberName.Lexeme}' does not exist on namespace '{nsType.Name}'.", tsCode: "TS2694");
    }

    /// <summary>
    /// Type checks member access on an enum type.
    /// </summary>
    private TypeInfo CheckGetOnEnum(TypeInfo.Enum enumType, Token memberName)
    {
        if (enumType.Members.ContainsKey(memberName.Lexeme))
        {
            // An enum member access is typed as the ENUM (tsc types `E.A` as the literal type
            // `E.A`, assignable to `E` but not to other enums or from arbitrary numbers) — not as
            // the member's underlying primitive. The enum-as-actual compatibility rules still let
            // it flow into number/string positions.
            return enumType;
        }
        throw new TypeCheckException($" '{memberName.Lexeme}' does not exist on enum '{enumType.Name}'.", tsCode: "TS2339");
    }

    /// <summary>
    /// Resolves a member inherited from <c>Object.prototype</c>, available on every object type
    /// (`hasOwnProperty`, `toString`, `valueOf`, ...). Returns null if <paramref name="name"/> is
    /// not an Object.prototype member. Used as a fallback by the object-like member-access paths
    /// before reporting TS2339.
    /// </summary>
    private static TypeInfo? ResolveObjectPrototypeMember(string name)
    {
        var boolean = TypeInfo.Primitive.Boolean;
        var str = TypeInfo.String.Shared;
        var anyArg = new List<TypeInfo> { TypeInfo.Any.Shared };
        return name switch
        {
            "hasOwnProperty" => new TypeInfo.Function(anyArg, boolean, RequiredParams: 1),
            "isPrototypeOf" => new TypeInfo.Function(anyArg, boolean, RequiredParams: 1),
            "propertyIsEnumerable" => new TypeInfo.Function(anyArg, boolean, RequiredParams: 1),
            "toString" => new TypeInfo.Function([], str),
            "toLocaleString" => new TypeInfo.Function([], str),
            "valueOf" => new TypeInfo.Function([], TypeInfo.Object.Shared),
            "constructor" => new TypeInfo.Function([], TypeInfo.Any.Shared),
            _ => null
        };
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
                return _strictNullChecks && itf.GetAllOptionalMembers().Contains(memberName.Lexeme)
                    ? CreateUnion(member.Value, TypeInfo.Undefined.Shared)
                    : member.Value;
            }
        }
        if (ResolveObjectPrototypeMember(memberName.Lexeme) is { } protoMember)
        {
            return protoMember;
        }
        throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on interface '{itf.Name}'.", line: memberName.Line, tsCode: "TS2339");
    }

    /// <summary>
    /// Type checks member access on a record/object literal type.
    /// </summary>
    private TypeInfo CheckGetOnRecord(TypeInfo.Record record, Token memberName)
    {
        if (record.Fields.TryGetValue(memberName.Lexeme, out var fieldType))
        {
            return _strictNullChecks && record.IsFieldOptional(memberName.Lexeme)
                ? CreateUnion(fieldType, TypeInfo.Undefined.Shared)
                : fieldType;
        }
        if (record.StringIndexType != null)
        {
            return WithUncheckedUndefined(record.StringIndexType);
        }
        if (ResolveObjectPrototypeMember(memberName.Lexeme) is { } protoMember)
        {
            return protoMember;
        }
        throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{record}'.", tsCode: "TS2339");
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

        // Handle regular class instance. ResolvedClassType unwraps MutableClass to its
        // frozen Class — needed when signature collection created this Instance before
        // the class was frozen (e.g., method return types on @DotNetType shims).
        if (instance.ResolvedClassType is TypeInfo.Class instanceClassType)
        {
            var regularMember = CheckGetOnRegularInstance(instanceClassType, memberName);
            if (regularMember is TypeInfo.Any &&
                _currentModule != null &&
                DotNetTypeSynthesizer.TryGetClrType(instance, out var receiverClr) &&
                Runtime.DotNet.DotNetTypeRegistry.GetMethods(
                    receiverClr, memberNameStr, isStatic: false).Length == 0 &&
                DotNetTypeSynthesizer.TryBuildExtensionMember(
                    instance,
                    memberNameStr,
                    _currentModule.DotNetExtensionTypes,
                    out var extensionMember))
            {
                return extensionMember;
            }
            return regularMember;
        }

        return TypeInfo.Any.Shared;
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

        return TypeInfo.Any.Shared;
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
                throw new TypeCheckException($" Property '{memberNameStr}' is private and only accessible within class '{currentName}'.", tsCode: "TS2341");
            }
            var currentClassForAccess = AsClass(current);
            if (access == AccessModifier.Protected && currentClassForAccess != null && !IsSubclassOf(_currentClass, currentClassForAccess))
            {
                throw new TypeCheckException($" Property '{memberNameStr}' is protected and only accessible within class '{currentName}' and its subclasses.", tsCode: "TS2445");
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
        return TypeInfo.Any.Shared;
    }
}
