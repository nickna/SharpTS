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
            TypeCategory.String => ResolveStringMemberType(memberName),
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
            TypeCategory.RegExp => ResolveRegExpMemberType(memberName),
            TypeCategory.Error when objType is TypeInfo.Error err =>
                BuiltInTypes.GetErrorMemberType(memberName, err.Name)
                ?? ResolveObjectPrototypeMember(memberName),
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

    private TypeInfo? ResolveStringMemberType(string memberName)
    {
        TypeInfo? member = BuiltInTypes.GetStringMemberType(memberName);
        return !Options.RespectLoadedLibraries && memberName == "match" && member is TypeInfo.Function function
            ? function with { ReturnType = TypeInfo.Any.Shared }
            : member;
    }

    private TypeInfo? ResolveRegExpMemberType(string memberName)
    {
        TypeInfo? member = BuiltInTypes.GetRegExpMemberType(memberName);
        return !Options.RespectLoadedLibraries && memberName == "exec" && member is TypeInfo.Function function
            ? function with { ReturnType = TypeInfo.Any.Shared }
            : member;
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
        if (tp.Constraint is not (null or TypeInfo.Any or TypeInfo.Unknown))
        {
            return CheckGetOnType(tp.Constraint, memberName);
        }
        throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.", tsCode: "TS2339");
    }

    /// <summary>
    /// Type checks static member access on a class type (Foo.staticProp).
    /// </summary>
    private TypeInfo CheckGetOnClass(TypeInfo classType, Token memberName)
    {
        TypeInfo? current = classType;
        TypeInfo? staticStringIndex = null;
        bool dynamicSuperclass = false;
        bool dotNetMappedSurface = false;
        while (current != null)
        {
            // A class extending an any-valued constructor keeps that dynamic
            // constructor's unknown static surface.
            if (current is TypeInfo.Any or TypeInfo.MutableClass { IsDynamicSuperclass: true })
            {
                dynamicSuperclass = true;
                break;
            }
            dotNetMappedSurface |= IsDotNetMappedConstructor(current);
            staticStringIndex ??= ClassInfoAccessor.Get(current,
                c => c.Core.StaticStringIndexType, gc => gc.Core.StaticStringIndexType);
            var staticMethods = GetStaticMethods(current);
            var staticProps = GetStaticProperties(current);
            string lookupName = ResolveDotNetStaticMemberName(current, memberName.Lexeme, staticMethods, staticProps);
            Token lookupMember = lookupName == memberName.Lexeme ? memberName
                : new Token(memberName.Type, lookupName, memberName.Literal, memberName.Line);
            if (staticMethods != null && staticMethods.TryGetValue(lookupName, out var staticMethodType))
            {
                EnforceStaticMemberAccess(current, lookupMember);
                ObserveSourceMemberSelection(memberName, current, MemberFacet.Static);
                return staticMethodType;
            }
            if (staticProps != null && staticProps.TryGetValue(lookupName, out var staticPropType))
            {
                if (TryGetAccessorSetterAccess(current, isStatic: true, lookupName, out _))
                    EnforceAccessorAccess(current, classType, lookupMember, isStatic: true, isWrite: false);
                else
                {
                    EnforceStaticMemberAccess(current, lookupMember);
                    ObservePendingGetterFieldRead(current, isStatic: true, lookupName);
                }
                ObserveSourceMemberSelection(memberName, current, MemberFacet.Static);
                return ResolveFieldStageGetter(current, isStatic: true, lookupName, staticPropType);
            }
            current = GetSuperclass(current);
        }

        // Constructors inherit Function and Object members independently of
        // the source class's declared static members. Keep this surface finite:
        // GetFunctionMemberType deliberately returns any for arbitrary names.
        if (memberName.Lexeme == "prototype")
            return TypeInfo.Any.Shared;
        // A bound constructor remains constructable. An ordinary Function
        // return type would make `new (C.bind(null))()` fail static checking.
        if (memberName.Lexeme == "bind")
            return new TypeInfo.Function([new TypeInfo.Array(TypeInfo.Any.Shared)],
                TypeInfo.Any.Shared, RequiredParams: 0, HasRestParam: true);
        if (_globalDeclarationEnvironment?.GetTypeBinding("Function") is { } declaredFunction &&
            RefreshCompletedInterfaceBinding(declaredFunction) is TypeInfo.Interface function &&
            ResolveNamedInterfaceMember(function, memberName.Lexeme) is { } declaredMember)
            return declaredMember;
        if (memberName.Lexeme is "call" or "apply" or "length" or "name")
            return BuiltInTypes.GetFunctionMemberType(memberName.Lexeme, classType)!;
        if (memberName.Lexeme is "caller" or "arguments")
            return TypeInfo.Any.Shared;
        if (ResolveObjectPrototypeMember(memberName.Lexeme) is { } objectMember)
            return objectMember;
        if (staticStringIndex is not null)
            return WithUncheckedUndefined(staticStringIndex);
        if (dynamicSuperclass)
            return TypeInfo.Any.Shared;
        // A DotNetType declaration can describe only part of the CLR surface.
        // The native binder resolves undeclared members, while declared members
        // above retain their argument, return, and access checks.
        if (dotNetMappedSurface)
            return TypeInfo.Any.Shared;

        throw new TypeCheckException(
            $"Property '{memberName.Lexeme}' does not exist on type 'typeof {GetClassName(classType)}'.",
            memberName.Line, tsCode: "TS2339");
    }

    private static string ResolveDotNetStaticMemberName(TypeInfo owner, string requested,
        IReadOnlyDictionary<string, TypeInfo>? methods, IReadOnlyDictionary<string, TypeInfo>? properties)
    {
        if (methods?.ContainsKey(requested) == true || properties?.ContainsKey(requested) == true
            || !IsDotNetMappedConstructor(owner))
            return requested;

        string clrName = Runtime.DotNet.DotNetTypeRegistry.ToPascalCase(requested);
        return (methods?.Keys ?? []).Concat(properties?.Keys ?? [])
            .FirstOrDefault(name => Runtime.DotNet.DotNetTypeRegistry.ToPascalCase(name) == clrName)
            ?? requested;
    }

    private static bool IsDotNetMappedConstructor(TypeInfo owner) => owner is TypeInfo.MutableClass mutable
        ? mutable.IsDotNetMapped
        : ClassInfoAccessor.Get(owner, c => c.Core.IsDotNetMapped, gc => gc.Core.IsDotNetMapped);

    private static TypeInfo? GetConstructorStringIndexType(TypeInfo classType)
    {
        TypeInfo? current = classType;
        for (int depth = 0; current is not null && depth < 256; depth++, current = GetSuperclass(current))
            if (ClassInfoAccessor.Get(current, c => c.Core.StaticStringIndexType,
                    gc => gc.Core.StaticStringIndexType) is { } indexType)
                return indexType;
        return null;
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
        var declaringId = EnumerateClassCores(declaringClass).First().DeclarationId;
        if (access == AccessModifier.Private && _currentClass?.Core.DeclarationId != declaringId)
        {
            throw new TypeCheckException($" Property '{memberName.Lexeme}' is private and only accessible within class '{declName}'.", tsCode: "TS2341");
        }
        if (access == AccessModifier.Protected)
        {
            // Generic definitions and instantiated generic bases participate in
            // the same lexical access check as ordinary class declarations.
            if (_currentClass != null && SourceDerivesFromDeclaration(_currentClass, declaringId))
                return;
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
        throw new TypeCheckException(
            $" Property '{memberName.Lexeme}' does not exist on type 'typeof {nsType.Name}'.",
            memberName.Line,
            tsCode: "TS2339");
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
        if (ResolveApparentFunctionMember(itf, memberName.Lexeme) is { } functionMember)
            return functionMember;
        if (ResolveObjectPrototypeMember(memberName.Lexeme) is { } protoMember)
        {
            return protoMember;
        }
        if (itf.StringIndexType is { } stringIndexType)
        {
            return WithUncheckedUndefined(stringIndexType);
        }
        string? suggestion = ClosestMemberSuggestion(
            memberName.Lexeme,
            itf.GetAllMembers().Select(member => member.Key).Distinct(StringComparer.Ordinal).ToList());
        if (suggestion is not null)
        {
            throw new TypeCheckException(
                $"Property '{memberName.Lexeme}' does not exist on type '{itf.Name}'. Did you mean '{suggestion}'?",
                memberName.Line,
                tsCode: "TS2551");
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
        if (ResolveApparentFunctionMember(record, memberName.Lexeme) is { } functionMember)
            return functionMember;
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

        if (TrySelectInstanceAccessor(instance, memberNameStr, out var accessor))
        {
            EnforceAccessorAccess(accessor.DeclaringType, instance.ResolvedClassType,
                memberName, isStatic: false, isWrite: false);
            ObserveSourceMemberSelection(memberName, accessor.DeclaringType, MemberFacet.Instance);
            return SelectedInstanceAccessorReadType(accessor, memberNameStr);
        }

        // Handle instantiated generic class (e.g., Box<number>)
        if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
            ig.GenericDefinition is TypeInfo.GenericClass gc)
        {
            return CheckGetOnGenericInstance(gc, ig.TypeArguments, memberName);
        }

        // Handle regular class instance. ResolvedClassType unwraps MutableClass to its
        // frozen Class — needed when signature collection created this Instance before
        // the class was frozen (e.g., method return types on @DotNetType shims).
        if (instance.ResolvedClassType is TypeInfo.Class instanceClassType)
        {
            try
            {
                return CheckGetOnRegularInstance(instanceClassType, memberName);
            }
            catch (TypeCheckException ex) when (ex.Diagnostic.TsCode == "TS2339")
            {
                if (_currentModule != null &&
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
                throw;
            }
        }

        return TypeInfo.Any.Shared;
    }

    /// <summary>
    /// Type checks member access on a generic class instance.
    /// </summary>
    private TypeInfo CheckGetOnGenericInstance(TypeInfo.GenericClass gc, List<TypeInfo> typeArgs, Token memberToken)
    {
        string memberName = memberToken.Lexeme;
        // Build substitution map from type parameters to type arguments
        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gc.TypeParams.Count; i++)
            subs[gc.TypeParams[i].Name] = typeArgs[i];

        if (TryGetDefaultLibraryPromiseType(gc, out _))
            return CheckGetOnRegularInstance(new TypeInfo.Class(gc.Core), memberToken, subs);

        // Check for getter first
        if (gc.Getters?.TryGetValue(memberName, out var getterType) == true)
        {
            ObserveSourceMemberSelection(memberToken, gc, MemberFacet.Instance);
            return Substitute(getterType, subs);
        }

        // Check for field
        if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
        {
            ObservePendingGetterFieldRead(gc, isStatic: false, memberName);
            ObserveSourceMemberSelection(memberToken, gc, MemberFacet.Instance);
            return Substitute(fieldType, subs);
        }

        // Check for method
        if (gc.Methods.TryGetValue(memberName, out var methodType))
        {
            ObserveSourceMemberSelection(memberToken, gc, MemberFacet.Instance);
            return SubstituteMethodType(methodType, subs);
        }

        // Check superclass if any
        if (gc.Superclass != null)
        {
            TypeInfo? currentSuper = gc.Superclass;
            var fieldSubstitutions = subs;
            while (currentSuper != null)
            {
                fieldSubstitutions = ComposeInheritedFieldReadSubstitutions(currentSuper, fieldSubstitutions);
                var superMethods = GetMethods(currentSuper);
                var superFields = GetFieldTypes(currentSuper);
                if (superMethods != null && superMethods.TryGetValue(memberName, out var superMethod))
                {
                    ObserveSourceMemberSelection(memberToken, currentSuper, MemberFacet.Instance);
                    return superMethod;
                }
                if (superFields != null && superFields.TryGetValue(memberName, out var superField))
                {
                    ObservePendingGetterFieldRead(currentSuper, isStatic: false, memberName);
                    ObserveSourceMemberSelection(memberToken, currentSuper, MemberFacet.Instance);
                    return SubstituteConstructorMember(superField, fieldSubstitutions);
                }
                currentSuper = GetSuperclass(currentSuper);
            }
        }

        return TypeInfo.Any.Shared;
    }

    private Dictionary<string, TypeInfo> ComposeInheritedFieldReadSubstitutions(
        TypeInfo current, Dictionary<string, TypeInfo> substitutions)
    {
        if (current is not TypeInfo.InstantiatedGeneric
            { GenericDefinition: TypeInfo.GenericClass generic } instantiated)
            return substitutions;
        var arguments = instantiated.TypeArguments
            .Select(argument => SubstituteConstructorMember(argument, substitutions)).ToList();
        return GenericClassSubs(generic, arguments);
    }

    /// <summary>
    /// Substitutes type parameters in a method type.
    /// </summary>
    private TypeInfo SubstituteMethodType(TypeInfo methodType, Dictionary<string, TypeInfo> subs)
    {
        if (methodType is TypeInfo.GenericFunction or TypeInfo.OverloadSet)
            return Substitute(methodType, subs);
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
    private TypeInfo CheckGetOnRegularInstance(
        TypeInfo.Class startClass, Token memberName,
        Dictionary<string, TypeInfo>? promiseSubstitutions = null)
    {
        string memberNameStr = memberName.Lexeme;
        TypeInfo? current = startClass;
        bool promiseHeritage = TryGetDefaultLibraryPromiseType(startClass, out _);
        Dictionary<string, TypeInfo> substitutions = promiseSubstitutions ?? [];
        Dictionary<string, TypeInfo> fieldSubstitutions = [];
        TypeInfo Sub(TypeInfo type) => promiseHeritage
            ? SubstituteConstructorMember(type, substitutions)
            : substitutions.Count > 0 ? Substitute(type, substitutions) : type;

        while (current != null)
        {
            // Built-in globals resolve to `any` in value position, so an `extends Error` /
            // `extends Promise<T>` clause is represented by a name-only MutableClass placeholder.
            // Preserve the real built-in member surface at that point in the hierarchy while still
            // rejecting genuinely unknown properties on ordinary user classes.
            if (current is TypeInfo.MutableClass placeholder &&
                ResolveBuiltInSuperclassMember(placeholder.Name, memberNameStr) is { } builtInMember)
            {
                return builtInMember;
            }

            if (!promiseHeritage)
                fieldSubstitutions = ComposeInheritedFieldReadSubstitutions(current, fieldSubstitutions);

            // If current is an InstantiatedGeneric, build/extend the substitution map
            if (current is TypeInfo.InstantiatedGeneric igCurrent &&
                igCurrent.GenericDefinition is TypeInfo.GenericClass gcCurrent)
            {
                if (promiseHeritage)
                    substitutions = ComposePromiseHeritageSubstitutions(current, substitutions);
                else
                {
                    for (int i = 0; i < gcCurrent.TypeParams.Count && i < igCurrent.TypeArguments.Count; i++)
                        substitutions[gcCurrent.TypeParams[i].Name] = igCurrent.TypeArguments[i];
                }
            }

            // Check for getter first
            var getters = GetGetters(current);
            if (getters != null && getters.TryGetValue(memberNameStr, out var getterType))
            {
                ObserveSourceMemberSelection(memberName, current, MemberFacet.Instance);
                return Sub(getterType);
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
                ObserveSourceMemberSelection(memberName, current, MemberFacet.Instance);
                return Sub(methodType);
            }

            // Check for field
            var fieldTypes = GetFieldTypes(current);
            if (fieldTypes != null && fieldTypes.TryGetValue(memberNameStr, out var fieldType))
            {
                ObservePendingGetterFieldRead(current, isStatic: false, memberNameStr);
                ObserveSourceMemberSelection(memberName, current, MemberFacet.Instance);
                return promiseHeritage ? Sub(fieldType) : SubstituteConstructorMember(fieldType, fieldSubstitutions);
            }

            current = GetSuperclass(current);
        }
        if (memberNameStr == "constructor")
            return startClass;
        throw new TypeCheckException(
            $"Property '{memberNameStr}' does not exist on type '{startClass.Name}'.",
            memberName.Line,
            tsCode: "TS2339");
    }

    private static TypeInfo? ResolveBuiltInSuperclassMember(string className, string memberName)
    {
        if (BuiltInNames.IsErrorTypeName(className))
            return BuiltInTypes.GetErrorMemberType(memberName, className);

        TypeInfo? builtIn = className switch
        {
            BuiltInNames.Promise => new TypeInfo.Promise(TypeInfo.Any.Shared),
            BuiltInNames.Date => TypeInfo.Date.Shared,
            BuiltInNames.RegExp => TypeInfo.RegExp.Shared,
            BuiltInNames.Map => new TypeInfo.Map(TypeInfo.Any.Shared, TypeInfo.Any.Shared),
            BuiltInNames.Set => new TypeInfo.Set(TypeInfo.Any.Shared),
            BuiltInNames.WeakMap => new TypeInfo.WeakMap(TypeInfo.Any.Shared, TypeInfo.Any.Shared),
            BuiltInNames.WeakSet => new TypeInfo.WeakSet(TypeInfo.Any.Shared),
            BuiltInNames.WeakRef => new TypeInfo.WeakRef(TypeInfo.Any.Shared),
            BuiltInNames.FinalizationRegistry => new TypeInfo.FinalizationRegistry(TypeInfo.Any.Shared),
            BuiltInNames.EventEmitter => TypeInfo.EventEmitter.Shared,
            BuiltInNames.AbortController => TypeInfo.AbortController.Shared,
            BuiltInNames.AbortSignal => TypeInfo.AbortSignal.Shared,
            _ => null,
        };
        if (builtIn is not null)
            return BuiltInTypes.GetInstanceMemberType(builtIn, memberName);

        if (className is BuiltInNames.Array or "ReadonlyArray")
            return BuiltInTypes.GetArrayMemberType(memberName, TypeInfo.Any.Shared);
        return BuiltInNames.IsTypedArrayName(className) ? TypeInfo.Any.Shared : null;
    }
}
