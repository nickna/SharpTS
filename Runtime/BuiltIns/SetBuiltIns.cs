using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for the Set type.
/// </summary>
/// <remarks>
/// Implements the JavaScript Set API: add, has, delete, clear, keys, values, entries, forEach, size.
/// forEach callback receives (value, value, set) to match JavaScript semantics (value is passed twice).
/// </remarks>
public static class SetBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSSet> _lookup =
        BuiltInTypeBuilder<SharpTSSet>.ForInstanceType()
            .Property("size", set => (double)set.Size)
            .Method("add", 1, Add)
            .Method("has", 1, Has)
            .Method("delete", 1, Delete)
            .Method("clear", 0, Clear)
            .Method("keys", 0, Keys)
            .Method("values", 0, Values)
            .Method("entries", 0, Entries)
            .Method("forEach", 1, ForEach)
            // ES2025 Set Operations
            .Method("union", 1, Union)
            .Method("intersection", 1, Intersection)
            .Method("difference", 1, Difference)
            .Method("symmetricDifference", 1, SymmetricDifference)
            .Method("isSubsetOf", 1, IsSubsetOf)
            .Method("isSupersetOf", 1, IsSupersetOf)
            .Method("isDisjointFrom", 1, IsDisjointFrom)
            .Build();

    public static object? GetMember(SharpTSSet receiver, string name)
        => _lookup.GetMember(receiver, name);

    private static object? Add(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var value = args[0];
        if (value == null)
            throw new Exception("Runtime Error: Set value cannot be null.");
        return set.Add(value);
    }

    private static object? Has(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var value = args[0];
        if (value == null)
            throw new Exception("Runtime Error: Set value cannot be null.");
        return set.Has(value);
    }

    private static object? Delete(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var value = args[0];
        if (value == null)
            throw new Exception("Runtime Error: Set value cannot be null.");
        return set.Delete(value);
    }

    private static object? Clear(Interpreter _, SharpTSSet set, List<object?> args)
    {
        set.Clear();
        return null;
    }

    private static object? Keys(Interpreter _, SharpTSSet set, List<object?> args)
        => set.Keys();

    private static object? Values(Interpreter _, SharpTSSet set, List<object?> args)
        => set.Values();

    private static object? Entries(Interpreter _, SharpTSSet set, List<object?> args)
        => set.Entries();

    private static object? ForEach(Interpreter interp, SharpTSSet set, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: forEach requires a function argument.");

        // JavaScript Set.forEach callback receives (value, value, set)
        // The value is passed twice (for consistency with Map.forEach API)
        var callbackArgs = new List<object?>(3) { null, null, set };
        foreach (var value in set.InternalValues)
        {
            callbackArgs[0] = value;
            callbackArgs[1] = value;
            callback.Call(interp, callbackArgs);
        }
        return null;
    }

    // ES2025 Set Operations
    private static object? Union(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: union requires a Set argument.");
        return set.Union(other);
    }

    private static object? Intersection(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: intersection requires a Set argument.");
        return set.Intersection(other);
    }

    private static object? Difference(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: difference requires a Set argument.");
        return set.Difference(other);
    }

    private static object? SymmetricDifference(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: symmetricDifference requires a Set argument.");
        return set.SymmetricDifference(other);
    }

    private static object? IsSubsetOf(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: isSubsetOf requires a Set argument.");
        return set.IsSubsetOf(other);
    }

    private static object? IsSupersetOf(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: isSupersetOf requires a Set argument.");
        return set.IsSupersetOf(other);
    }

    private static object? IsDisjointFrom(Interpreter _, SharpTSSet set, List<object?> args)
    {
        var other = args[0] as SharpTSSet
            ?? throw new Exception("Runtime Error: isDisjointFrom requires a Set argument.");
        return set.IsDisjointFrom(other);
    }
}
