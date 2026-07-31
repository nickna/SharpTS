using System.Collections.Concurrent;
using SharpTS.Declaration;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;

namespace SharpTS.TypeSystem;

/// <summary>
/// Synthesizes a <see cref="TypeInfo.Class"/> for a .NET <see cref="Type"/> imported via a
/// <c>dotnet:</c> specifier, using <see cref="TypeInspector"/> reflection metadata. This is the
/// static-checking counterpart of the runtime <c>DotNetClass</c>/<c>DotNetInstance</c> wrappers:
/// the surface it exposes must match what the interop dispatch can actually reach.
/// </summary>
/// <remarks>
/// Member filtering delegates to <see cref="DotNetInteropClassifier"/> — the same rules the
/// <c>--gen-decl</c> discovery tool reports — so a member shown as <c>[unsupported]</c> there is
/// absent from the synthesized surface here, and the tool and the checker never disagree.
///
/// Slot-type mapping policy (kept deliberately conservative so the checker never reports a
/// false error for a call the runtime marshaller would accept):
/// <list type="bullet">
/// <item><c>void</c> → <c>void</c>; CLR numerics (incl. <c>decimal</c>) → <c>number</c>;
/// <c>string</c>/<c>char</c> → <c>string</c>; <c>bool</c> → <c>boolean</c> — mirroring
/// <c>DotNetMarshaller.WrapReturn</c> normalization.</item>
/// <item>The containing type itself → <c>Instance</c> of the synthesized class, so fluent
/// chains (<c>sb.append(...).append(...)</c>) stay statically typed.</item>
/// <item>Everything else (other .NET types, arrays, delegates, <c>Nullable&lt;T&gt;</c>,
/// enums) → <c>any</c>. At runtime these become <c>DotNetInstance</c> wrappers; typing them
/// <c>any</c> is accurate for what the checker can promise without synthesizing the transitive
/// closure of the BCL.</item>
/// </list>
/// Types that cannot be constructed (static classes, interfaces, enums, classes without a
/// public constructor) are marked abstract so <c>new</c> is rejected statically.
/// </remarks>
public static class DotNetTypeSynthesizer
{
    private static readonly ConcurrentDictionary<Type, TypeInfo.Class> _cache = new();
    private static readonly ConcurrentDictionary<TypeInfo.Class, Type> _clrTypes =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the synthesized class type for a .NET type. Cached per <see cref="Type"/> so all
    /// import sites across all modules share one <see cref="TypeInfo.Class"/> identity.
    /// </summary>
    public static TypeInfo.Class Synthesize(Type type)
    {
        var synthesized = _cache.GetOrAdd(type, Build);
        _clrTypes.TryAdd(synthesized, type);
        return synthesized;
    }

    /// <summary>Clears the synthesis cache. Used by tests to ensure isolation.</summary>
    public static void ClearCache()
    {
        _cache.Clear();
        _clrTypes.Clear();
    }

    /// <summary>Resolves a synthesized imported instance type back to its CLR type.</summary>
    public static bool TryGetClrType(TypeInfo type, out Type clrType)
    {
        if (type is TypeInfo.Instance instance &&
            instance.ResolvedClassType is TypeInfo.Class classType &&
            _clrTypes.TryGetValue(classType, out clrType!))
        {
            return true;
        }

        clrType = null!;
        return false;
    }

    /// <summary>
    /// Maps a statically known TypeScript call-argument shape to the CLR type used for
    /// generic-method inference. Unlike <see cref="TryGetClrType"/>, this includes guest
    /// primitives, arrays/tuples, and callable signatures.
    /// </summary>
    public static bool TryGetClrArgumentType(TypeInfo type, out Type clrType)
    {
        Type? resolved = type switch
        {
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or
                TypeInfo.NumberLiteral => typeof(double),
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or
                TypeInfo.BooleanLiteral => typeof(bool),
            TypeInfo.String or TypeInfo.StringLiteral => typeof(string),
            TypeInfo.Array array when TryGetClrArgumentType(
                array.ElementType, out var element) =>
                    ManagedDotNetInterop.MakeArrayType(element),
            TypeInfo.Tuple tuple => HomogeneousClrType(
                tuple.ElementTypes.Concat(
                    tuple.RestElementType != null
                        ? [tuple.RestElementType]
                        : Enumerable.Empty<TypeInfo>()),
                asArray: true),
            TypeInfo.Union union => HomogeneousClrType(
                union.FlattenedTypes, asArray: false),
            TypeInfo.Function function => GetDelegateType(function),
            TypeInfo.Instance when TryGetClrType(type, out var external) => external,
            _ => null
        };
        clrType = resolved!;
        return resolved != null;
    }

    private static Type? HomogeneousClrType(
        IEnumerable<TypeInfo> types,
        bool asArray)
    {
        var resolved = new List<Type>();
        foreach (var type in types)
        {
            if (!TryGetClrArgumentType(type, out var clr))
                return null;
            resolved.Add(clr);
        }
        if (resolved.Count == 0)
            return asArray ? typeof(object[]) : null;
        Type first = resolved[0];
        if (resolved.Any(type => type != first))
            return null;
        return asArray ? ManagedDotNetInterop.MakeArrayType(first) : first;
    }

    private static Type? GetDelegateType(TypeInfo.Function function)
    {
        var parameterTypes = new List<Type>(function.ParamTypes.Count);
        foreach (var parameter in function.ParamTypes)
        {
            if (!TryGetClrArgumentType(parameter, out var parameterType))
                return null;
            parameterTypes.Add(parameterType);
        }

        try
        {
            if (function.ReturnType is TypeInfo.Void)
                return ManagedDotNetInterop.GetActionType(parameterTypes.ToArray());
            if (!TryGetClrArgumentType(function.ReturnType, out var returnType))
                return null;
            parameterTypes.Add(returnType);
            return ManagedDotNetInterop.GetFuncType(parameterTypes.ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a user-defined CLR binary operator when at least one operand is a synthesized
    /// interop instance. This is intentionally absent for ordinary TS values.
    /// </summary>
    internal static bool TryResolveBinaryOperator(
        TypeInfo left,
        TypeInfo right,
        TokenType token,
        out TypeInfo result)
    {
        Type? leftClr = TryGetClrType(left, out var resolvedLeft) ? resolvedLeft : null;
        Type? rightClr = TryGetClrType(right, out var resolvedRight) ? resolvedRight : null;
        if (leftClr == null && rightClr == null)
        {
            result = null!;
            return false;
        }

        foreach (var method in DotNetOperatorResolver.GetBinaryCandidates(token, leftClr, rightClr))
        {
            var parameters = method.GetParameters();
            if (!OperatorParameterAccepts(parameters[0].ParameterType, left, leftClr) ||
                !OperatorParameterAccepts(parameters[1].ParameterType, right, rightClr))
            {
                continue;
            }

            result = MapOperatorResult(method.ReturnType, left, leftClr, right, rightClr);
            return true;
        }

        result = null!;
        return false;
    }

    internal static bool TryResolveUnaryOperator(
        TypeInfo operand,
        TokenType token,
        out TypeInfo result)
    {
        if (!TryGetClrType(operand, out var operandClr))
        {
            result = null!;
            return false;
        }

        foreach (var method in DotNetOperatorResolver.GetUnaryCandidates(token, operandClr))
        {
            if (!OperatorParameterAccepts(
                    method.GetParameters()[0].ParameterType, operand, operandClr))
            {
                continue;
            }
            result = MapOperatorResult(
                method.ReturnType, operand, operandClr, operand, operandClr);
            return true;
        }

        result = null!;
        return false;
    }

    internal static bool TryResolveCompoundOperator(
        TypeInfo left,
        TypeInfo right,
        TokenType token,
        out TypeInfo result)
    {
        TokenType? binary = DotNetOperatorResolver.GetBinaryTokenForCompound(token);
        if (binary != null)
            return TryResolveBinaryOperator(left, right, binary.Value, out result);
        result = null!;
        return false;
    }

    internal static bool TryResolveIncrementOperator(
        TypeInfo operand,
        TokenType token,
        out TypeInfo result)
    {
        if (!TryGetClrType(operand, out var operandClr))
        {
            result = null!;
            return false;
        }

        foreach (var method in DotNetOperatorResolver.GetIncrementCandidates(
                     token, operandClr))
        {
            if (!OperatorParameterAccepts(
                    method.GetParameters()[0].ParameterType, operand, operandClr))
            {
                continue;
            }
            result = MapOperatorResult(
                method.ReturnType, operand, operandClr, operand, operandClr);
            return true;
        }

        result = null!;
        return false;
    }

    /// <summary>Builds the module-scoped TypeScript member exposed by imported extension methods.</summary>
    internal static bool TryBuildExtensionMember(
        TypeInfo receiver,
        string memberName,
        IEnumerable<Type> containers,
        out TypeInfo member)
    {
        if (!TryGetClrType(receiver, out var receiverClr))
        {
            member = null!;
            return false;
        }

        var signatures = new List<TypeInfo.Function>();
        foreach (var method in DotNetExtensionMethodResolver.GetReceiverClosedCandidates(
                     containers, memberName, receiverClr))
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0 ||
                !parameters[0].ParameterType.IsAssignableFrom(receiverClr) ||
                DotNetInteropClassifier.UnsupportedSlotReason(method.ReturnType) != null ||
                parameters.Skip(1).Any(p =>
                    DotNetInteropClassifier.UnsupportedParameterReason(p.ParameterType) != null))
            {
                continue;
            }

            var visibleTypes = new List<TypeInfo>();
            int required = 0;
            bool hasRest = false;
            for (int i = 1; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.ParameterType.IsByRef && parameter.IsOut)
                    continue;
                if (parameter.IsDefined(
                        typeof(ParamArrayAttribute), inherit: false) &&
                    i == parameters.Length - 1)
                {
                    visibleTypes.Add(new TypeInfo.Array(TypeInfo.Any.Shared));
                    hasRest = true;
                    break;
                }
                Type parameterType = parameter.ParameterType.IsByRef
                    ? parameter.ParameterType.GetElementType()!
                    : parameter.ParameterType;
                visibleTypes.Add(MapExternalSlot(parameterType, receiver, receiverClr));
                if (!parameter.IsOptional)
                    required = visibleTypes.Count;
            }

            TypeInfo returnType = MapExternalSlot(method.ReturnType, receiver, receiverClr);
            var outputs = parameters.Skip(1)
                .Where(p => p.ParameterType.IsByRef && !p.IsIn)
                .Select(p => MapExternalSlot(
                    p.ParameterType.GetElementType()!, receiver, receiverClr))
                .ToList();
            if (outputs.Count > 0)
            {
                if (method.ReturnType != typeof(void))
                    outputs.Insert(0, returnType);
                returnType = TypeInfo.Tuple.FromTypes(outputs, outputs.Count);
            }

            signatures.Add(new TypeInfo.Function(
                visibleTypes, returnType, required, hasRest));
        }

        member = signatures.Count switch
        {
            0 => null!,
            1 => signatures[0],
            _ => new TypeInfo.OverloadedFunction(
                signatures, MostPermissiveSignature(signatures))
        };
        return signatures.Count > 0;
    }

    private static bool OperatorParameterAccepts(Type target, TypeInfo source, Type? sourceClr)
    {
        if (sourceClr != null)
            return target.IsAssignableFrom(sourceClr);
        if (source is TypeInfo.Any or TypeInfo.Inferred or TypeInfo.Unknown)
            return true;
        if (source is TypeInfo.Null)
            return !target.IsValueType || Nullable.GetUnderlyingType(target) != null;
        if (source is TypeInfo.String or TypeInfo.StringLiteral)
            return target == typeof(string) || target == typeof(char);
        if (source is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral)
            return target == typeof(bool);
        if (source is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral)
            return IsNumeric(target) || target.IsEnum;
        return target == typeof(object);
    }

    private static TypeInfo MapOperatorResult(
        Type returnType,
        TypeInfo left,
        Type? leftClr,
        TypeInfo right,
        Type? rightClr)
    {
        if (returnType == leftClr) return left;
        if (returnType == rightClr) return right;
        var nullable = Nullable.GetUnderlyingType(returnType);
        if (nullable != null)
        {
            return new TypeInfo.Union(
                [MapOperatorResult(nullable, left, leftClr, right, rightClr), TypeInfo.Null.Shared]);
        }
        if (returnType == typeof(bool)) return TypeInfo.Primitive.Boolean;
        if (returnType == typeof(string) || returnType == typeof(char)) return TypeInfo.String.Shared;
        if (IsNumeric(returnType)) return TypeInfo.Primitive.Number;
        return TypeInfo.Any.Shared;
    }

    private static TypeInfo MapExternalSlot(
        Type type,
        TypeInfo receiver,
        Type receiverClr) =>
        MapOperatorResult(type, receiver, receiverClr, receiver, receiverClr);

    private static TypeInfo.Class Build(Type type)
    {
        var meta = new TypeInspector().Inspect(type, includeInherited: true);
        var mc = new TypeInfo.MutableClass(type.Name);

        foreach (var group in meta.Methods.GroupBy(m => m.TypeScriptName, StringComparer.Ordinal))
        {
            var member = BuildMethodGroup(group, type, mc);
            if (member != null) mc.Methods.TryAdd(group.Key, member);
        }

        foreach (var group in meta.StaticMethods.GroupBy(m => m.TypeScriptName, StringComparer.Ordinal))
        {
            var member = BuildMethodGroup(group, type, mc);
            if (member != null) mc.StaticMethods.TryAdd(group.Key, member);
        }

        // TypeInspector deliberately keeps generic method definitions out of ordinary
        // declaration metadata. Synthesize their callable surface directly so TypeScript
        // calls can infer method type arguments (or accept explicit ones) while the runtime
        // closes the same MethodInfo immediately before overload resolution.
        AddGenericMethodGroups(type, mc, isStatic: false);
        AddGenericMethodGroups(type, mc, isStatic: true);

        foreach (var prop in meta.Properties)
        {
            if (prop.IsIndexer || !prop.CanRead) continue;
            if (DotNetInteropClassifier.UnsupportedSlotReason(prop.PropertyType) != null) continue;
            if (mc.FieldTypes.TryAdd(prop.TypeScriptName, MapSlot(prop.PropertyType, type, mc)) && !prop.CanWrite)
                mc.ReadonlyFields.Add(prop.TypeScriptName);
        }

        // A CLR indexer maps directly to the corresponding TypeScript class index signature.
        // TypeScript bracket syntax carries one key, so multi-parameter CLR indexers remain
        // unavailable (call their get_Item/set_Item accessor explicitly if needed).
        foreach (var indexer in ManagedDotNetInterop.GetProperties(
                     type,
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.Instance))
        {
            var indexParameters = indexer.GetIndexParameters();
            if (indexParameters.Length != 1 || !indexer.CanRead) continue;
            if (DotNetInteropClassifier.UnsupportedSlotReason(indexer.PropertyType) != null) continue;
            if (DotNetInteropClassifier.UnsupportedSlotReason(indexParameters[0].ParameterType) != null) continue;

            var valueType = MapSlot(indexer.PropertyType, type, mc);
            var keyType = Nullable.GetUnderlyingType(indexParameters[0].ParameterType)
                          ?? indexParameters[0].ParameterType;
            if (keyType == typeof(string) || keyType == typeof(char))
                mc.StringIndexType ??= valueType;
            else if (IsNumeric(keyType) || keyType.IsEnum)
                mc.NumberIndexType ??= valueType;
        }

        foreach (var field in meta.Fields)
        {
            if (DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) != null) continue;
            if (mc.FieldTypes.TryAdd(field.TypeScriptName, MapSlot(field.FieldType, type, mc)) && field.IsReadonly)
                mc.ReadonlyFields.Add(field.TypeScriptName);
        }

        foreach (var prop in meta.StaticProperties)
        {
            if (prop.IsIndexer || !prop.CanRead) continue;
            if (DotNetInteropClassifier.UnsupportedSlotReason(prop.PropertyType) != null) continue;
            mc.StaticProperties.TryAdd(prop.TypeScriptName, MapSlot(prop.PropertyType, type, mc));
        }

        foreach (var field in meta.StaticFields)
        {
            if (DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) != null) continue;
            mc.StaticProperties.TryAdd(field.TypeScriptName, MapSlot(field.FieldType, type, mc));
        }

        // Enum members surface as static readonly values of the enum's own type
        // (DayOfWeek.Monday: DayOfWeek). CLR casing is kept — that is what users see in .NET
        // docs and what the runtime member lookup resolves.
        foreach (var enumMember in meta.EnumMembers)
        {
            mc.StaticProperties.TryAdd(enumMember.Name, new TypeInfo.Instance(mc));
        }

        // DOM-style event subscription is provided by the runtime wrappers whenever the type
        // exposes .NET events (see DotNetEventBinder); mirror it on the static surface too.
        if (meta.HasEvents)
        {
            var subscribe = new TypeInfo.Function(
                [TypeInfo.String.Shared, TypeInfo.Any.Shared], TypeInfo.Void.Shared, RequiredParams: 2);
            mc.Methods.TryAdd("addEventListener", subscribe);
            mc.Methods.TryAdd("removeEventListener", subscribe);
            mc.StaticMethods.TryAdd("addEventListener", subscribe);
            mc.StaticMethods.TryAdd("removeEventListener", subscribe);
        }

        BuildConstructor(meta, type, mc);

        return mc.Freeze();
    }

    private static void AddGenericMethodGroups(
        Type self,
        TypeInfo.MutableClass mc,
        bool isStatic)
    {
        var flags = System.Reflection.BindingFlags.Public |
                    (isStatic
                        ? System.Reflection.BindingFlags.Static
                        : System.Reflection.BindingFlags.Instance);
        var target = isStatic ? mc.StaticMethods : mc.Methods;
        foreach (var group in ManagedDotNetInterop.GetMethods(self, flags)
                     .Where(m => m.IsGenericMethodDefinition && !m.IsSpecialName)
                     .GroupBy(
                         m => DotNetTypeMapper.ToTypeScriptMethodName(m.Name),
                         StringComparer.Ordinal))
        {
            var genericSignatures = group
                .Select(m => BuildGenericMethodSignature(m, self, mc))
                .Where(s => s != null)
                .Cast<TypeInfo.GenericFunction>()
                .ToList();
            if (genericSignatures.Count == 0)
                continue;

            TypeInfo member;
            if (genericSignatures.Count == 1)
            {
                member = genericSignatures[0];
            }
            else if (GenericSignaturesShareParameters(genericSignatures))
            {
                var first = genericSignatures[0];
                var functions = genericSignatures.Select(g => new TypeInfo.Function(
                    g.ParamTypes, g.ReturnType, g.RequiredParams, g.HasRestParam)).ToList();
                member = new TypeInfo.GenericOverloadedFunction(
                    first.TypeParams, functions, MostPermissiveSignature(functions));
            }
            else
            {
                // The current TypeInfo model has no overload set whose individual signatures
                // own different generic parameter lists. Runtime dispatch still supports it.
                member = TypeInfo.Any.Shared;
            }

            // Mixed generic/non-generic CLR overload sets also cannot be represented faithfully
            // by the existing TypeInfo union. Keep the call available and let CLR overload
            // resolution enforce it rather than publishing an incorrect static signature.
            target[group.Key] = target.ContainsKey(group.Key)
                ? TypeInfo.Any.Shared
                : member;
        }
    }

    private static TypeInfo.GenericFunction? BuildGenericMethodSignature(
        System.Reflection.MethodInfo method,
        Type self,
        TypeInfo.MutableClass mc)
    {
        var genericParameters = method.GetGenericArguments();
        if (!IsSupportedGenericMethodSlot(
                method.ReturnType, genericParameters, isParameter: false))
        {
            return null;
        }

        var parameters = method.GetParameters();
        if (parameters.Any(p => !IsSupportedGenericMethodSlot(
                p.ParameterType, genericParameters, isParameter: true)))
        {
            return null;
        }

        var typeParameters = genericParameters
            .Select(p => new TypeInfo.TypeParameter(p.Name))
            .ToList();
        var substitutions = genericParameters
            .Select((parameter, index) => (parameter, typeParameters[index]))
            .ToDictionary(pair => pair.parameter, pair => pair.Item2);

        var parameterTypes = new List<TypeInfo>();
        int required = 0;
        bool hasRest = false;
        foreach (var parameter in parameters)
        {
            if (parameter.ParameterType.IsByRef && parameter.IsOut)
                continue;

            Type parameterType = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()!
                : parameter.ParameterType;
            if (parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false))
            {
                parameterTypes.Add(new TypeInfo.Array(
                    MapGenericSlot(
                        parameterType.GetElementType()!, self, mc, substitutions)));
                hasRest = true;
                break;
            }

            parameterTypes.Add(MapGenericSlot(parameterType, self, mc, substitutions));
            if (!parameter.IsOptional)
                required = parameterTypes.Count;
        }

        TypeInfo returnType = MapGenericSlot(
            method.ReturnType, self, mc, substitutions);
        var outputs = parameters
            .Where(p => p.ParameterType.IsByRef && !p.IsIn)
            .Select(p => MapGenericSlot(
                p.ParameterType.GetElementType()!, self, mc, substitutions))
            .ToList();
        if (outputs.Count > 0)
        {
            if (method.ReturnType != typeof(void))
                outputs.Insert(0, returnType);
            returnType = TypeInfo.Tuple.FromTypes(outputs, outputs.Count);
        }

        return new TypeInfo.GenericFunction(
            typeParameters, parameterTypes, returnType, required, hasRest);
    }

    private static bool GenericSignaturesShareParameters(
        IReadOnlyList<TypeInfo.GenericFunction> signatures)
    {
        var first = signatures[0].TypeParams;
        return signatures.Skip(1).All(signature =>
            signature.TypeParams.Count == first.Count &&
            signature.TypeParams.Select(p => p.Name)
                .SequenceEqual(first.Select(p => p.Name), StringComparer.Ordinal));
    }

    private static bool IsSupportedGenericMethodSlot(
        Type slot,
        IReadOnlyCollection<Type> methodParameters,
        bool isParameter) =>
        DotNetInteropClassifier.UnsupportedGenericMethodSlotReason(
            slot, methodParameters, isParameter) == null;

    private static TypeInfo MapGenericSlot(
        Type clrType,
        Type self,
        TypeInfo.MutableClass mc,
        IReadOnlyDictionary<Type, TypeInfo.TypeParameter> substitutions)
    {
        if (substitutions.TryGetValue(clrType, out var parameter))
            return parameter;
        if (clrType.IsArray)
        {
            return new TypeInfo.Array(
                MapGenericSlot(clrType.GetElementType()!, self, mc, substitutions));
        }
        if (typeof(Delegate).IsAssignableFrom(clrType) &&
            ManagedDotNetInterop.GetMethod(clrType, "Invoke") is { } genericInvoke)
        {
            return new TypeInfo.Function(
                genericInvoke.GetParameters()
                    .Select(parameter => MapGenericSlot(
                        parameter.ParameterType, self, mc, substitutions))
                    .ToList(),
                MapGenericSlot(genericInvoke.ReturnType, self, mc, substitutions),
                genericInvoke.GetParameters().Length);
        }

        var nullableUnderlying = Nullable.GetUnderlyingType(clrType);
        if (nullableUnderlying != null)
        {
            return new TypeInfo.Union(
                [MapGenericSlot(nullableUnderlying, self, mc, substitutions),
                    TypeInfo.Null.Shared]);
        }

        return MapSlot(clrType, self, mc);
    }

    /// <summary>
    /// Builds the callable type for one JS-facing method name: a single <see cref="TypeInfo.Function"/>,
    /// an <see cref="TypeInfo.OverloadedFunction"/> for overload sets, or null when every overload
    /// has an unmarshalable slot (the member is then absent, matching the discovery tool's verdict).
    /// </summary>
    private static TypeInfo? BuildMethodGroup(IEnumerable<MethodMetadata> overloads, Type self, TypeInfo.MutableClass mc)
    {
        var signatures = new List<TypeInfo.Function>();
        foreach (var m in overloads)
        {
            if (DotNetInteropClassifier.UnsupportedSlotReason(m.ReturnType) != null)
                continue;
            if (m.Parameters.Any(p => DotNetInteropClassifier.UnsupportedParameterReason(p.ParameterType) != null))
                continue;
            var returnType = MapSlot(m.ReturnType, self, mc);
            var tupleOutputs = m.Parameters
                .Where(p => p.IsByRef && !p.IsIn)
                .Select(p => MapSlot(p.ParameterType.GetElementType()!, self, mc))
                .ToList();
            if (tupleOutputs.Count > 0)
            {
                if (m.ReturnType != typeof(void))
                    tupleOutputs.Insert(0, returnType);
                returnType = TypeInfo.Tuple.FromTypes(tupleOutputs, tupleOutputs.Count);
            }
            signatures.Add(BuildSignature(m.Parameters, returnType, self, mc));
        }

        return signatures.Count switch
        {
            0 => null,
            1 => signatures[0],
            _ => new TypeInfo.OverloadedFunction(signatures, MostPermissiveSignature(signatures)),
        };
    }

    private static void BuildConstructor(TypeMetadata meta, Type type, TypeInfo.MutableClass mc)
    {
        // Static classes, interfaces, and enums cannot be `new`ed; classes without a public
        // constructor can't either. Marking the synthesized class abstract makes the checker
        // reject `new X()` with the standard abstract-class error.
        if (meta.IsStatic || meta.IsInterface || meta.IsEnum)
        {
            mc.IsAbstract = true;
            return;
        }

        var signatures = new List<TypeInfo.Function>();
        foreach (var ctor in meta.Constructors)
        {
            if (ctor.Parameters.Any(p => DotNetInteropClassifier.UnsupportedSlotReason(p.ParameterType) != null))
                continue;
            signatures.Add(BuildSignature(ctor.Parameters, TypeInfo.Void.Shared, type, mc));
        }

        // Value types always support default construction (`new Guid()` → default(Guid)).
        if (type.IsValueType && !signatures.Any(s => s.MinArity == 0))
        {
            signatures.Add(new TypeInfo.Function([], TypeInfo.Void.Shared));
        }

        if (signatures.Count == 0)
        {
            mc.IsAbstract = true;
            return;
        }

        mc.Methods["constructor"] = signatures.Count == 1
            ? signatures[0]
            : new TypeInfo.OverloadedFunction(signatures, MostPermissiveSignature(signatures));
    }

    private static TypeInfo.Function BuildSignature(
        List<ParameterMetadata> parameters, TypeInfo returnType, Type self, TypeInfo.MutableClass mc)
    {
        var paramTypes = new List<TypeInfo>(parameters.Count);
        int requiredParams = 0;
        bool hasRest = false;

        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.IsByRef && p.IsOut)
                continue;
            if (p.IsParams && i == parameters.Count - 1)
            {
                // params-array: the checker models rest params as an array-typed final slot.
                paramTypes.Add(new TypeInfo.Array(TypeInfo.Any.Shared));
                hasRest = true;
                break;
            }
            var parameterType = p.ParameterType.IsByRef
                ? p.ParameterType.GetElementType()!
                : p.ParameterType;
            paramTypes.Add(MapSlot(parameterType, self, mc));
            if (!p.IsOptional) requiredParams = paramTypes.Count;
        }

        return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest);
    }

    /// <summary>
    /// A catch-all implementation signature for overload sets: `(...args: any[]) => any`.
    /// Only the individual signatures participate in call checking.
    /// </summary>
    private static TypeInfo.Function MostPermissiveSignature(List<TypeInfo.Function> signatures)
        => new([new TypeInfo.Array(TypeInfo.Any.Shared)], CommonReturnType(signatures), RequiredParams: 0, HasRestParam: true);

    /// <summary>
    /// When every overload agrees on the return type, keep it (preserves chaining through
    /// members resolved via the implementation signature); otherwise fall back to any.
    /// </summary>
    private static TypeInfo CommonReturnType(List<TypeInfo.Function> signatures)
    {
        var first = signatures[0].ReturnType;
        for (int i = 1; i < signatures.Count; i++)
        {
            if (!Equals(signatures[i].ReturnType, first)) return TypeInfo.Any.Shared;
        }
        return first;
    }

    /// <summary>
    /// Maps one CLR slot (parameter or return type) to the TypeScript-visible type. See the
    /// class remarks for the policy; must stay aligned with <c>DotNetMarshaller</c>.
    /// </summary>
    private static TypeInfo MapSlot(Type clrType, Type self, TypeInfo.MutableClass mc)
    {
        if (clrType == typeof(void)) return TypeInfo.Void.Shared;

        var nullableUnderlying = Nullable.GetUnderlyingType(clrType);
        if (nullableUnderlying != null)
        {
            return new TypeInfo.Union(
                [MapSlot(nullableUnderlying, self, mc), TypeInfo.Null.Shared]);
        }

        if (clrType == typeof(string) || clrType == typeof(char)) return TypeInfo.String.Shared;
        if (clrType == typeof(bool)) return TypeInfo.Primitive.Boolean;

        if (IsNumeric(clrType)) return TypeInfo.Primitive.Number;

        // The containing type itself: keeps fluent chains statically typed. Instance wraps the
        // MutableClass; Instance.ResolvedClassType resolves to the frozen Class after Freeze().
        if (clrType == self) return new TypeInfo.Instance(mc);

        if (typeof(Delegate).IsAssignableFrom(clrType) &&
            ManagedDotNetInterop.GetMethod(clrType, "Invoke") is { } invoke)
        {
            return new TypeInfo.Function(
                invoke.GetParameters()
                    .Select(parameter => MapSlot(parameter.ParameterType, self, mc))
                    .ToList(),
                MapSlot(invoke.ReturnType, self, mc),
                invoke.GetParameters().Length);
        }

        return TypeInfo.Any.Shared;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(double) || type == typeof(float) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(decimal);
}
