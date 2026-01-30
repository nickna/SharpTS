using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ObjectBuiltIns
{
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .Method("keys", 1, Keys)
            .Method("values", 1, Values)
            .Method("entries", 1, Entries)
            .Method("fromEntries", 1, FromEntries)
            .Method("hasOwn", 2, HasOwn)
            .Method("assign", 1, int.MaxValue, Assign)
            .Method("freeze", 1, Freeze)
            .Method("seal", 1, Seal)
            .Method("isFrozen", 1, IsFrozen)
            .Method("isSealed", 1, IsSealed)
            .Build();

    /// <summary>
    /// Get static methods on the Object namespace (e.g., Object.keys())
    /// </summary>
    public static object? GetStaticMethod(string name)
        => _staticLookup.GetMember(name);

    private static object? Keys(Interpreter _, List<object?> args)
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
    }

    private static object? Values(Interpreter _, List<object?> args)
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
    }

    private static object? Entries(Interpreter _, List<object?> args)
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
    }

    private static object? FromEntries(Interpreter interpreter, List<object?> args)
    {
        if (args[0] == null)
            throw new Exception("Runtime Error: Object.fromEntries() requires an iterable argument");

        var elements = interpreter.GetIterableElements(args[0]);
        Dictionary<string, object?> result = [];

        foreach (var element in elements)
        {
            if (element is SharpTSArray pair && pair.Elements.Count >= 2)
            {
                string key = pair.Get(0)?.ToString() ?? "";
                result[key] = pair.Get(1);
            }
            else if (element is List<object?> listPair && listPair.Count >= 2)
            {
                string key = listPair[0]?.ToString() ?? "";
                result[key] = listPair[1];
            }
            else
            {
                throw new Exception("Runtime Error: Object.fromEntries() requires [key, value] pairs");
            }
        }
        return new SharpTSObject(result);
    }

    private static object? HasOwn(Interpreter _, List<object?> args)
    {
        var key = args[1]?.ToString() ?? "";
        return args[0] switch
        {
            SharpTSObject obj => obj.Fields.ContainsKey(key),
            SharpTSInstance inst => inst.GetFieldNames().Contains(key),
            _ => false
        };
    }

    private static object? Assign(Interpreter _, List<object?> args)
    {
        // Object.assign(target, ...sources)
        if (args.Count == 0 || args[0] == null)
            throw new Exception("Runtime Error: Object.assign() requires a target object");

        // Handle SharpTSObject target
        if (args[0] is SharpTSObject targetObj)
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] == null) continue;

                if (args[i] is SharpTSObject srcObj)
                {
                    foreach (var kv in srcObj.Fields)
                        targetObj.SetProperty(kv.Key, kv.Value);
                }
                else if (args[i] is SharpTSInstance srcInst)
                {
                    foreach (var key in srcInst.GetFieldNames())
                        targetObj.SetProperty(key, srcInst.GetRawField(key));
                }
            }
            return args[0];
        }

        // Handle SharpTSInstance target
        if (args[0] is SharpTSInstance targetInst)
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] == null) continue;

                if (args[i] is SharpTSObject srcObj)
                {
                    foreach (var kv in srcObj.Fields)
                        targetInst.SetRawField(kv.Key, kv.Value);
                }
                else if (args[i] is SharpTSInstance srcInst)
                {
                    foreach (var key in srcInst.GetFieldNames())
                        targetInst.SetRawField(key, srcInst.GetRawField(key));
                }
            }
            return args[0];
        }

        throw new Exception("Runtime Error: Object.assign() target must be an object");
    }

    private static object? Freeze(Interpreter _, List<object?> args)
    {
        // Object.freeze(obj) - freezes the object and returns it
        switch (args[0])
        {
            case SharpTSObject obj:
                obj.Freeze();
                return obj;
            case SharpTSInstance inst:
                inst.Freeze();
                return inst;
            case SharpTSArray arr:
                arr.Freeze();
                return arr;
            default:
                // Non-objects are returned unchanged (JavaScript behavior)
                return args[0];
        }
    }

    private static object? Seal(Interpreter _, List<object?> args)
    {
        // Object.seal(obj) - seals the object and returns it
        switch (args[0])
        {
            case SharpTSObject obj:
                obj.Seal();
                return obj;
            case SharpTSInstance inst:
                inst.Seal();
                return inst;
            case SharpTSArray arr:
                arr.Seal();
                return arr;
            default:
                // Non-objects are returned unchanged (JavaScript behavior)
                return args[0];
        }
    }

    private static object? IsFrozen(Interpreter _, List<object?> args)
    {
        // Object.isFrozen(obj) - returns true if the object is frozen
        return args[0] switch
        {
            SharpTSObject obj => obj.IsFrozen,
            SharpTSInstance inst => inst.IsFrozen,
            SharpTSArray arr => arr.IsFrozen,
            // Non-extensible primitives are considered frozen in JavaScript
            _ => true
        };
    }

    private static object? IsSealed(Interpreter _, List<object?> args)
    {
        // Object.isSealed(obj) - returns true if the object is sealed
        return args[0] switch
        {
            SharpTSObject obj => obj.IsSealed,
            SharpTSInstance inst => inst.IsSealed,
            SharpTSArray arr => arr.IsSealed,
            // Non-extensible primitives are considered sealed in JavaScript
            _ => true
        };
    }

    /// <summary>
    /// Creates a new object with all properties from source except those in excludeKeys.
    /// Used for object rest patterns: const { x, ...rest } = obj;
    /// </summary>
    public static SharpTSObject ObjectRest(object? source, IEnumerable<object?> excludeKeys)
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
