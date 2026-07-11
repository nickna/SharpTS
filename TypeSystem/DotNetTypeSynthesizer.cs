using System.Collections.Concurrent;
using SharpTS.Declaration;
using SharpTS.Parsing;

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

    /// <summary>
    /// Returns the synthesized class type for a .NET type. Cached per <see cref="Type"/> so all
    /// import sites across all modules share one <see cref="TypeInfo.Class"/> identity.
    /// </summary>
    public static TypeInfo.Class Synthesize(Type type) => _cache.GetOrAdd(type, Build);

    /// <summary>Clears the synthesis cache. Used by tests to ensure isolation.</summary>
    public static void ClearCache() => _cache.Clear();

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

        foreach (var prop in meta.Properties)
        {
            if (prop.IsIndexer || !prop.CanRead) continue;
            if (DotNetInteropClassifier.UnsupportedSlotReason(prop.PropertyType) != null) continue;
            if (mc.FieldTypes.TryAdd(prop.TypeScriptName, MapSlot(prop.PropertyType, type, mc)) && !prop.CanWrite)
                mc.ReadonlyFields.Add(prop.TypeScriptName);
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
            if (m.Parameters.Any(p => DotNetInteropClassifier.UnsupportedSlotReason(p.ParameterType) != null))
                continue;
            signatures.Add(BuildSignature(m.Parameters, MapSlot(m.ReturnType, self, mc), self, mc));
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
            if (p.IsParams && i == parameters.Count - 1)
            {
                // params-array: the checker models rest params as an array-typed final slot.
                paramTypes.Add(new TypeInfo.Array(TypeInfo.Any.Shared));
                hasRest = true;
                break;
            }
            paramTypes.Add(MapSlot(p.ParameterType, self, mc));
            if (!p.IsOptional) requiredParams = i + 1;
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
        if (clrType == typeof(string) || clrType == typeof(char)) return TypeInfo.String.Shared;
        if (clrType == typeof(bool)) return TypeInfo.Primitive.Boolean;

        if (clrType == typeof(double) || clrType == typeof(float) ||
            clrType == typeof(int) || clrType == typeof(uint) ||
            clrType == typeof(long) || clrType == typeof(ulong) ||
            clrType == typeof(short) || clrType == typeof(ushort) ||
            clrType == typeof(byte) || clrType == typeof(sbyte) ||
            clrType == typeof(decimal))
        {
            return TypeInfo.Primitive.Number;
        }

        // The containing type itself: keeps fluent chains statically typed. Instance wraps the
        // MutableClass; Instance.ResolvedClassType resolves to the frozen Class after Freeze().
        if (clrType == self) return new TypeInfo.Instance(mc);

        return TypeInfo.Any.Shared;
    }
}
