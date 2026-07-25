namespace SharpTS.TypeSystem;

/// <summary>
/// Member enumeration for REPL autocomplete.
/// </summary>
/// <remarks>
/// Completion needs the *apparent members* of a type — the names you can legally write after a dot.
/// That is very close to what <c>keyof</c> already computes, so this reuses the existing
/// <see cref="ExtractKeys"/> machinery rather than duplicating a second member table. The one place
/// keyof semantics differ from completion semantics is classes: <c>ExtractKeys</c>'s Class case does
/// not walk the superclass chain, so classes and instances are routed through
/// <c>CollectPublicInstanceMembers</c> instead, which does (and also yields member types for tooltips).
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// The member names of <paramref name="type"/> paired with each member's type where it is known,
    /// for REPL autocomplete. Returns an empty list for types with no apparent members.
    /// </summary>
    internal List<(string Name, TypeInfo? Type)> GetCompletionMembers(TypeInfo type)
    {
        List<(string, TypeInfo?)> members = [];

        // A literal type's apparent members are those of its base primitive — `const s = "x"` infers
        // the literal type `"x"`, and `s.` must still offer the string methods.
        type = type switch
        {
            TypeInfo.StringLiteral => TypeInfo.String.Shared,
            TypeInfo.NumberLiteral => new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER),
            TypeInfo.BooleanLiteral => new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN),
            _ => type,
        };

        // An *instance* of a class: instance members, walking the superclass chain so inherited
        // members are offered too.
        if (type is TypeInfo.Instance inst && inst.ResolvedClassType is TypeInfo.Class instCls)
        {
            foreach (var (name, memberType) in CollectPublicInstanceMembers(instCls))
                members.Add((name, memberType));
            return members;
        }

        switch (type)
        {
            // A *reference* to the class itself (`MyClass.` rather than `new MyClass().`) exposes
            // statics, not instance members. Keyof-style member enumeration returns the instance
            // side for a Class, so this case must be handled separately or `MyClass.` would wrongly
            // offer instance methods.
            case TypeInfo.Class cls:
                CollectPublicStatics(cls, members);
                return members;

            // Interfaces expose inherited members through GetAllMembers; records carry their fields
            // directly. Both give us member types, which ExtractKeys (name-only) would throw away.
            case TypeInfo.Interface itf:
                foreach (var (name, memberType) in itf.GetAllMembers())
                    members.Add((name, memberType));
                return members;

            case TypeInfo.Record rec:
                foreach (var (name, memberType) in rec.Fields)
                    members.Add((name, memberType));
                return members;

            case TypeInfo.Enum en:
                foreach (var name in en.Members.Keys)
                    members.Add((name, null));
                return members;
        }

        // Everything else — string/array/tuple apparent members, generics, unions (common members
        // only), intersections, and the structurally-modelled built-ins (Date, Map, Set, Promise,
        // Error, …) — comes from the keyof projection.
        foreach (var key in ExtractKeys(type))
        {
            // Index-signature key types (string/number/symbol) and unresolved `keyof T` are not
            // member names; only string literals are.
            if (key is not TypeInfo.StringLiteral literal) continue;

            var name = literal.Value;
            if (name.Length == 0) continue;

            // Tuples contribute their element indices ("0", "1", …) as keys. `x.0` is not valid
            // syntax, so they are not completion candidates.
            if (char.IsAsciiDigit(name[0])) continue;

            members.Add((name, Runtime.BuiltIns.BuiltInTypes.GetInstanceMemberType(type, name)));
        }

        return members;
    }

    /// <summary>
    /// Collects the public static methods and properties of a class and its superclasses.
    /// Derived statics shadow inherited ones, mirroring <c>CollectPublicInstanceMembers</c>.
    /// </summary>
    private static void CollectPublicStatics(TypeInfo.Class cls, List<(string, TypeInfo?)> members)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            var core = c.Core;
            foreach (var (name, type) in core.StaticMethods)
                if (IsPublicMember(core.StaticMethodAccessMap, name) && seen.Add(name))
                    members.Add((name, type));
            foreach (var (name, type) in core.StaticProperties)
                if (IsPublicMember(core.StaticFieldAccessMap, name) && seen.Add(name))
                    members.Add((name, type));
            current = GetSuperclass(current);
        }
    }
}
