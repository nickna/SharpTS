using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ObjectBuiltIns
{
    /// <summary>
    /// Get static methods on the Object namespace (e.g., Object.keys())
    /// </summary>
    public static object? GetStaticMethod(string name)
    {
        return name switch
        {
            "keys" => new BuiltInMethod("keys", 1, (_, _, args) =>
            {
                if (args[0] is SharpTSObject obj)
                {
                    var keys = obj.Fields.Keys.Select(k => (object?)k).ToList();
                    return new SharpTSArray(keys);
                }
                if (args[0] is SharpTSInstance inst)
                {
                    var keys = inst.GetFieldNames().Select(k => (object?)k).ToList();
                    return new SharpTSArray(keys);
                }
                throw new Exception("Object.keys() requires an object argument");
            }),
            "values" => new BuiltInMethod("values", 1, (_, _, args) =>
            {
                if (args[0] is SharpTSObject obj)
                {
                    var values = obj.Fields.Values.ToList();
                    return new SharpTSArray(values);
                }
                if (args[0] is SharpTSInstance inst)
                {
                    var values = inst.GetFieldNames().Select(n => inst.GetRawField(n)).ToList();
                    return new SharpTSArray(values);
                }
                throw new Exception("Object.values() requires an object argument");
            }),
            "entries" => new BuiltInMethod("entries", 1, (_, _, args) =>
            {
                if (args[0] is SharpTSObject obj)
                {
                    var entries = obj.Fields.Select(kv =>
                        (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
                    return new SharpTSArray(entries);
                }
                if (args[0] is SharpTSInstance inst)
                {
                    var entries = inst.GetFieldNames().Select(n =>
                        (object?)new SharpTSArray([(object?)n, inst.GetRawField(n)])).ToList();
                    return new SharpTSArray(entries);
                }
                throw new Exception("Object.entries() requires an object argument");
            }),
            _ => null
        };
    }

    /// <summary>
    /// Creates a new object with all properties from source except those in excludeKeys.
    /// Used for object rest patterns: const { x, ...rest } = obj;
    /// </summary>
    public static SharpTSObject ObjectRest(object? source, List<object?> excludeKeys)
    {
        HashSet<string> excludeSet = new(excludeKeys.Where(k => k != null).Select(k => k!.ToString()!));
        Dictionary<string, object?> result = [];

        if (source is SharpTSObject obj)
        {
            foreach (var key in obj.Fields.Keys)
            {
                if (!excludeSet.Contains(key))
                    result[key] = obj.Fields[key];
            }
        }
        else if (source is SharpTSInstance inst)
        {
            foreach (var key in inst.GetFieldNames())
            {
                if (!excludeSet.Contains(key))
                    result[key] = inst.GetRawField(key);
            }
        }

        return new SharpTSObject(result);
    }
}
