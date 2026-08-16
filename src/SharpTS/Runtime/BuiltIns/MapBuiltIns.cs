using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for the Map type.
/// </summary>
/// <remarks>
/// Implements the JavaScript Map API: get, set, has, delete, clear, keys, values, entries, forEach, size.
/// forEach callback receives (value, key, map) to match JavaScript semantics.
/// </remarks>
public static class MapBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSMap> _lookup =
        BuiltInTypeBuilder<SharpTSMap>.ForInstanceType()
            .Property("size", map => (double)map.Size)
            .MethodV2("get", 1, GetV2)
            .MethodV2("set", 2, SetV2)
            .MethodV2("has", 1, HasV2)
            .MethodV2("delete", 1, DeleteV2)
            .MethodV2("clear", 0, ClearV2)
            .MethodV2("keys", 0, (_, map, _) => RuntimeValue.FromObject(map.Keys()))
            .MethodV2("values", 0, (_, map, _) => RuntimeValue.FromObject(map.Values()))
            .MethodV2("entries", 0, (_, map, _) => RuntimeValue.FromObject(map.Entries()))
            .MethodV2("forEach", 1, ForEachV2)
            .Build();

    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("groupBy", 2, GroupByV2)
            .Build();

    public static object? GetStaticMethod(string name)
        => _staticLookup.GetMember(name);

    public static object? GetMember(SharpTSMap receiver, string name)
        => _lookup.GetMember(receiver, name);

    private static RuntimeValue GetV2(Interpreter _, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(map.Get(args[0].ToObject()));

    private static RuntimeValue SetV2(Interpreter _, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(map.Set(args[0].ToObject(), args[1].ToObject()));

    private static RuntimeValue HasV2(Interpreter _, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoolean(map.Has(args[0].ToObject()));

    private static RuntimeValue DeleteV2(Interpreter _, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoolean(map.Delete(args[0].ToObject()));

    private static RuntimeValue ClearV2(Interpreter _, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
    {
        map.Clear();
        return RuntimeValue.Undefined;
    }

    private static object? ForEach(Interpreter interp, SharpTSMap map, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: forEach requires a function argument.");

        // JavaScript Map.forEach callback receives (value, key, map)
        var callbackArgs = new List<object?>(3) { null, null, map };
        foreach (var kvp in map.InternalEntries)
        {
            callbackArgs[0] = kvp.Value;
            callbackArgs[1] = kvp.Key;
            callback.Call(interp, callbackArgs);
        }
        return null;
    }

    private static object? GroupBy(Interpreter interp, List<object?> args)
    {
        var iterable = args[0] as SharpTSArray
            ?? throw new Exception("TypeError: Map.groupBy requires an iterable as first argument");
        var callback = args[1] as ISharpTSCallable
            ?? throw new Exception("TypeError: Map.groupBy requires a function as second argument");

        var result = new SharpTSMap();
        var callbackArgs = new List<object?> { null, null };

        for (int i = 0; i < iterable.Length; i++)
        {
            var element = iterable[i];
            callbackArgs[0] = element;
            callbackArgs[1] = (double)i;
            var key = callback.Call(interp, callbackArgs);

            var existing = result.Get(key);
            if (existing == null)
            {
                existing = new SharpTSArray([]);
                result.Set(key, existing);
            }
            ((SharpTSArray)existing).Add(element);
        }

        return result;
    }

    private static RuntimeValue ForEachV2(Interpreter interp, SharpTSMap map, ReadOnlySpan<RuntimeValue> args)
    {
        ForEach(interp, map, new List<object?> { args[0].ToObject() });
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue GroupByV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
    {
        var list = new List<object?>(args.Length);
        foreach (var arg in args)
            list.Add(arg.ToObject());
        return RuntimeValue.FromBoxed(GroupBy(interp, list));
    }
}
