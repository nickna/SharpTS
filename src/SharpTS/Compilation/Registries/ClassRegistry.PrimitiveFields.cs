using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;

namespace SharpTS.Compilation.Registries;

public sealed partial class ClassRegistry
{
    private readonly Dictionary<TypeBuilder, Dictionary<string, GuestDataField>> _guestDataFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, TypeBuilder> _guestFieldOwners = new(StringComparer.Ordinal);

    internal void RegisterGuestFieldOwner(string name, TypeBuilder owner) => _guestFieldOwners.Add(name, owner);

    internal void RegisterGuestDataField(GuestDataField field)
    {
        MethodBuilder[] methods = [field.GuestGetter, field.GuestSetter, field.HasRawOverride];
        if (methods.Any(method => method.DeclaringType != field.DeclaringType || method.IsStatic)
            || field.BackingField != null && field.BackingField.DeclaringType != field.DeclaringType)
            throw new InvalidOperationException("Guest field handles must belong to their declaring instance owner.");
        if (!_guestDataFields.TryGetValue(field.DeclaringType, out var fields))
            _guestDataFields[field.DeclaringType] = fields = new(StringComparer.Ordinal);
        if (!fields.TryAdd(field.SourceName, field))
            throw new InvalidOperationException("Source guest field already registered.");
    }

    internal IEnumerable<GuestDataField> GetOwnGuestDataFields(TypeBuilder owner) =>
        _guestDataFields.TryGetValue(owner, out var fields) ? fields.Values : [];

    internal bool TryGetOwnGuestDataField(TypeBuilder owner, string name, [NotNullWhen(true)] out GuestDataField? field)
    {
        field = _guestDataFields.GetValueOrDefault(owner)?.GetValueOrDefault(name);
        return field != null;
    }

    public bool TryGetPrimitiveGuestField(string className, string sourceName,
        [NotNullWhen(true)] out PrimitiveGuestField? field)
    {
        field = null;
        if (!_builders.TryGetValue(className, out var owner))
            _guestFieldOwners.TryGetValue(className, out owner);
        if (owner == null) return false;
        for (Type? current = owner; current != null; current = current.BaseType)
        {
            var definition = current.IsConstructedGenericType ? current.GetGenericTypeDefinition() : current;
            if (definition is not TypeBuilder builder) return false;
            if (TryGetOwnGuestDataField(builder, sourceName, out var selected))
            {
                if (!selected.IsPrimitive) return false;
                field = selected.Primitive;
                return true;
            }
            if (HasOwnGuestAccessorBarrier(builder, sourceName)) return false;
        }
        return false;
    }

    internal bool HasOwnGuestAccessorBarrier(TypeBuilder owner, string name) =>
        _instancePrototypeBarriers.TryGetValue(owner, out var names) && (names.Contains(name) || names.Contains(null))
        || _sourceInstanceGetters.Any(entry => entry.Key.DeclaringType == owner && (entry.Value == name || entry.Value == null));

    public bool CanUseNativePrimitiveField(PrimitiveGuestField field)
    {
        if (NativeOwnProperties?.ContainsOwner(field.DeclaringType) == true) return false;
        foreach (var owner in _builders.Values.Concat(_guestFieldOwners.Values).Distinct<TypeBuilder>(ReferenceEqualityComparer.Instance))
        {
            if (ReferenceEquals(owner, field.DeclaringType)) continue;
            bool derived = false;
            for (Type? parent = owner.BaseType; parent != null; parent = parent.BaseType)
            {
                var definition = parent.IsConstructedGenericType ? parent.GetGenericTypeDefinition() : parent;
                if (ReferenceEquals(definition, field.DeclaringType)) { derived = true; break; }
            }
            if (!derived) continue;
            if (HasOwnGuestAccessorBarrier(owner, field.SourceName)) return false;
            if (TryGetOwnGuestDataField(owner, field.SourceName, out var selected)
                && (!selected.IsPrimitive || selected.NativeType != field.NativeType)) return false;
        }
        return true;
    }
}
