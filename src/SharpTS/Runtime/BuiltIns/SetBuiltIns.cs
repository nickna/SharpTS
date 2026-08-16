using SharpTS.Execution;
using SharpTS.Runtime;
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
            .MethodV2("add", 1, AddV2)
            .MethodV2("has", 1, HasV2)
            .MethodV2("delete", 1, DeleteV2)
            .MethodV2("clear", 0, ClearV2)
            .MethodV2("keys", 0, (_, set, _) => RuntimeValue.FromObject(set.Keys()))
            .MethodV2("values", 0, (_, set, _) => RuntimeValue.FromObject(set.Values()))
            .MethodV2("entries", 0, (_, set, _) => RuntimeValue.FromObject(set.Entries()))
            .MethodV2("forEach", 1, ForEachV2)
            // ES2025 Set Operations
            .MethodV2("union", 1, UnionV2)
            .MethodV2("intersection", 1, IntersectionV2)
            .MethodV2("difference", 1, DifferenceV2)
            .MethodV2("symmetricDifference", 1, SymmetricDifferenceV2)
            .MethodV2("isSubsetOf", 1, IsSubsetOfV2)
            .MethodV2("isSupersetOf", 1, IsSupersetOfV2)
            .MethodV2("isDisjointFrom", 1, IsDisjointFromV2)
            .Build();

    public static object? GetMember(SharpTSSet receiver, string name)
        => _lookup.GetMember(receiver, name);

    private static RuntimeValue AddV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var value = args[0].ToObject()
            ?? throw new Exception("Runtime Error: Set value cannot be null.");
        return RuntimeValue.FromObject(set.Add(value));
    }

    private static RuntimeValue HasV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var value = args[0].ToObject()
            ?? throw new Exception("Runtime Error: Set value cannot be null.");
        return RuntimeValue.FromBoolean(set.Has(value));
    }

    private static RuntimeValue DeleteV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var value = args[0].ToObject()
            ?? throw new Exception("Runtime Error: Set value cannot be null.");
        return RuntimeValue.FromBoolean(set.Delete(value));
    }

    private static RuntimeValue ClearV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        set.Clear();
        return RuntimeValue.Undefined;
    }

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

    private static RuntimeValue ForEachV2(Interpreter interp, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        ForEach(interp, set, new List<object?> { args[0].ToObject() });
        return RuntimeValue.Undefined;
    }

    // ES2025 Set Operations
    private static RuntimeValue UnionV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: union requires a Set argument.");
        return RuntimeValue.FromObject(set.Union(other));
    }

    private static RuntimeValue IntersectionV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: intersection requires a Set argument.");
        return RuntimeValue.FromObject(set.Intersection(other));
    }

    private static RuntimeValue DifferenceV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: difference requires a Set argument.");
        return RuntimeValue.FromObject(set.Difference(other));
    }

    private static RuntimeValue SymmetricDifferenceV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: symmetricDifference requires a Set argument.");
        return RuntimeValue.FromObject(set.SymmetricDifference(other));
    }

    private static RuntimeValue IsSubsetOfV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: isSubsetOf requires a Set argument.");
        return RuntimeValue.FromBoolean(set.IsSubsetOf(other));
    }

    private static RuntimeValue IsSupersetOfV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: isSupersetOf requires a Set argument.");
        return RuntimeValue.FromBoolean(set.IsSupersetOf(other));
    }

    private static RuntimeValue IsDisjointFromV2(Interpreter _, SharpTSSet set, ReadOnlySpan<RuntimeValue> args)
    {
        var other = args[0].ToObject() as SharpTSSet
            ?? throw new Exception("Runtime Error: isDisjointFrom requires a Set argument.");
        return RuntimeValue.FromBoolean(set.IsDisjointFrom(other));
    }
}
