using SharpTS.Runtime.Types;

namespace SharpTS.Repl;

/// <summary>
/// Rich value formatter for REPL output display.
/// Formats runtime values with type-aware coloring and structure display.
/// </summary>
internal static class ValueFormatter
{
    private const int DefaultMaxDepth = 2;
    private const int MaxArrayItems = 100;
    private const int MaxObjectKeys = 50;
    private const int MaxStringLength = 250;
    private const int MaxOutputLength = 10_000;

    // ANSI escape codes for terminal colors
    private const string Reset = "\x1b[0m";
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Magenta = "\x1b[35m";
    private const string Cyan = "\x1b[36m";
    private const string Gray = "\x1b[90m";
    private const string BrightGreen = "\x1b[92m";

    /// <summary>
    /// Formats a runtime value for display in the REPL.
    /// </summary>
    public static string Format(object? value, int maxDepth = DefaultMaxDepth)
    {
        var result = FormatValue(value, 0, maxDepth, new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance));

        // Truncate if the total output exceeds the cap
        if (result.Length > MaxOutputLength)
        {
            result = result[..MaxOutputLength] + $"{Reset}\n{Gray}... output truncated ({result.Length} chars total){Reset}";
        }

        return result;
    }

    private static string FormatValue(object? value, int depth, int maxDepth, HashSet<object> seen)
    {
        if (value is null) return $"{Magenta}null{Reset}";
        if (value is SharpTSUndefined) return $"{Gray}undefined{Reset}";

        if (value is bool b)
            return $"{Magenta}{(b ? "true" : "false")}{Reset}";

        if (value is double d)
        {
            if (double.IsNaN(d)) return $"{Yellow}NaN{Reset}";
            if (double.IsPositiveInfinity(d)) return $"{Yellow}Infinity{Reset}";
            if (double.IsNegativeInfinity(d)) return $"{Yellow}-Infinity{Reset}";
            var text = d.ToString();
            if (text.EndsWith(".0")) text = text[..^2];
            return $"{Yellow}{text}{Reset}";
        }

        if (value is int i)
            return $"{Yellow}{i}{Reset}";

        if (value is long l)
            return $"{Yellow}{l}{Reset}";

        if (value is System.Numerics.BigInteger bi)
            return $"{Yellow}{bi}n{Reset}";

        if (value is string s)
        {
            if (s.Length > MaxStringLength)
                s = s[..MaxStringLength] + "...";
            // Escape special characters for display
            s = s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            return $"{Green}'{s}'{Reset}";
        }

        if (value is SharpTSSymbol sym)
            return $"{BrightGreen}Symbol({sym}){Reset}";

        // Reference types - check for circular references
        if (!seen.Add(value))
            return $"{Cyan}[Circular]{Reset}";

        try
        {
            return FormatReferenceValue(value, depth, maxDepth, seen);
        }
        finally
        {
            seen.Remove(value);
        }
    }

    private static string FormatReferenceValue(object value, int depth, int maxDepth, HashSet<object> seen)
    {
        if (value is SharpTSArray array)
            return FormatArray(array, depth, maxDepth, seen);

        if (value is SharpTSObject obj)
            return FormatObject(obj, depth, maxDepth, seen);

        if (value is SharpTSInstance instance)
            return FormatInstance(instance, depth, maxDepth, seen);

        if (value is SharpTSClass cls)
            return $"{Cyan}[class {cls.Name}]{Reset}";

        if (value is SharpTSFunction or SharpTSArrowFunction or ISharpTSCallable)
        {
            // SharpTSFunction.ToString() returns "<fn name>", arrow returns "<fn anonymous>"
            var desc = value.ToString() ?? "Function";
            return $"{Cyan}[{desc}]{Reset}";
        }

        if (value is SharpTSPromise promise)
            return FormatPromise(promise);

        if (value is SharpTSRegExp regex)
            return $"{Magenta}/{regex.Source}/{regex.Flags}{Reset}";

        if (value is SharpTSDate date)
            return FormatDate(date);

        if (value is SharpTSError error)
            return $"{Magenta}{error.Name}: {error.Message}{Reset}";

        if (value is SharpTSInstance inst && inst.GetClass() is SharpTSErrorClass)
            return $"{Magenta}{SharpTSErrorClass.ErrorToString(inst)}{Reset}";

        if (value is SharpTSMap map)
            return FormatMap(map, depth, maxDepth, seen);

        if (value is SharpTSSet set)
            return FormatSet(set, depth, maxDepth, seen);

        if (value is Dictionary<string, object?> dict)
            return FormatDictionary(dict, depth, maxDepth, seen);

        return value.ToString() ?? "null";
    }

    private static string FormatArray(SharpTSArray array, int depth, int maxDepth, HashSet<object> seen)
    {
        if (array.Elements.Count == 0) return "[]";

        if (depth >= maxDepth)
            return $"[Array({array.Elements.Count})]";

        var items = new List<string>();
        int count = Math.Min(array.Elements.Count, MaxArrayItems);

        for (int i = 0; i < count; i++)
        {
            items.Add(FormatValue(array.Elements[i], depth + 1, maxDepth, seen));
        }

        if (array.Elements.Count > MaxArrayItems)
            items.Add($"{Gray}... {array.Elements.Count - MaxArrayItems} more items{Reset}");

        // Single-line if short, multi-line if long
        var singleLine = $"[ {string.Join(", ", items)} ]";
        if (singleLine.Length <= 72) return singleLine;

        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);
        return $"[\n{indent}{string.Join($",\n{indent}", items)}\n{closingIndent}]";
    }

    private static string FormatObject(SharpTSObject obj, int depth, int maxDepth, HashSet<object> seen)
    {
        var names = obj.PropertyNames.ToList();
        if (names.Count == 0) return "{}";

        if (depth >= maxDepth)
            return $"[Object({names.Count})]";

        var entries = new List<string>();
        int count = Math.Min(names.Count, MaxObjectKeys);

        for (int i = 0; i < count; i++)
        {
            var key = names[i];
            var val = obj.GetProperty(key);
            entries.Add($"{key}: {FormatValue(val, depth + 1, maxDepth, seen)}");
        }

        if (names.Count > MaxObjectKeys)
            entries.Add($"{Gray}... {names.Count - MaxObjectKeys} more properties{Reset}");

        var singleLine = $"{{ {string.Join(", ", entries)} }}";
        if (singleLine.Length <= 72) return singleLine;

        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);
        return $"{{\n{indent}{string.Join($",\n{indent}", entries)}\n{closingIndent}}}";
    }

    private static string FormatInstance(SharpTSInstance instance, int depth, int maxDepth, HashSet<object> seen)
    {
        var className = instance.GetClass().Name;
        var fields = instance.GetFieldNames().ToList();

        if (fields.Count == 0)
            return $"{className} {{}}";

        if (depth >= maxDepth)
            return $"{className} {{...}}";

        var entries = new List<string>();
        int count = Math.Min(fields.Count, MaxObjectKeys);

        for (int i = 0; i < count; i++)
        {
            var key = fields[i];
            var val = instance.GetRawField(key);
            entries.Add($"{key}: {FormatValue(val, depth + 1, maxDepth, seen)}");
        }

        if (fields.Count > MaxObjectKeys)
            entries.Add($"{Gray}... {fields.Count - MaxObjectKeys} more properties{Reset}");

        var singleLine = $"{className} {{ {string.Join(", ", entries)} }}";
        if (singleLine.Length <= 72) return singleLine;

        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);
        return $"{className} {{\n{indent}{string.Join($",\n{indent}", entries)}\n{closingIndent}}}";
    }

    private static string FormatPromise(SharpTSPromise promise)
    {
        if (!promise.IsCompleted)
            return $"Promise {{ {Gray}<pending>{Reset} }}";

        if (promise.IsFaulted)
            return $"Promise {{ {Magenta}<rejected>{Reset} }}";

        return $"Promise {{ {Cyan}<resolved>{Reset} }}";
    }

    private static string FormatDate(SharpTSDate date)
    {
        try
        {
            return date.ToISOString();
        }
        catch
        {
            return "Invalid Date";
        }
    }

    private static string FormatMap(SharpTSMap map, int depth, int maxDepth, HashSet<object> seen)
    {
        if (map.Size == 0) return "Map(0) {}";

        if (depth >= maxDepth)
            return $"Map({map.Size}) {{...}}";

        var entries = new List<string>();
        int count = 0;
        // SharpTSMap implements IEnumerable<object?>, yielding [key, value] arrays
        foreach (object? entry in map)
        {
            if (count >= MaxObjectKeys) break;
            if (entry is SharpTSArray pair && pair.Elements.Count >= 2)
            {
                entries.Add($"{FormatValue(pair.Elements[0], depth + 1, maxDepth, seen)} => {FormatValue(pair.Elements[1], depth + 1, maxDepth, seen)}");
            }
            count++;
        }

        if (map.Size > MaxObjectKeys)
            entries.Add($"{Gray}... {map.Size - MaxObjectKeys} more entries{Reset}");

        return $"Map({map.Size}) {{ {string.Join(", ", entries)} }}";
    }

    private static string FormatSet(SharpTSSet set, int depth, int maxDepth, HashSet<object> seen)
    {
        if (set.Size == 0) return "Set(0) {}";

        if (depth >= maxDepth)
            return $"Set({set.Size}) {{...}}";

        var items = new List<string>();
        int count = 0;
        // SharpTSSet implements IEnumerable<object?>, yielding values directly
        foreach (object? val in set)
        {
            if (count >= MaxArrayItems) break;
            items.Add(FormatValue(val, depth + 1, maxDepth, seen));
            count++;
        }

        if (set.Size > MaxArrayItems)
            items.Add($"{Gray}... {set.Size - MaxArrayItems} more items{Reset}");

        return $"Set({set.Size}) {{ {string.Join(", ", items)} }}";
    }

    private static string FormatDictionary(Dictionary<string, object?> dict, int depth, int maxDepth, HashSet<object> seen)
    {
        if (dict.Count == 0) return "{}";

        if (depth >= maxDepth)
            return $"[Object({dict.Count})]";

        var entries = new List<string>();
        int count = 0;

        foreach (var (key, val) in dict)
        {
            if (count >= MaxObjectKeys) break;
            entries.Add($"{key}: {FormatValue(val, depth + 1, maxDepth, seen)}");
            count++;
        }

        if (dict.Count > MaxObjectKeys)
            entries.Add($"{Gray}... {dict.Count - MaxObjectKeys} more properties{Reset}");

        var singleLine = $"{{ {string.Join(", ", entries)} }}";
        if (singleLine.Length <= 72) return singleLine;

        var indent = new string(' ', (depth + 1) * 2);
        var closingIndent = new string(' ', depth * 2);
        return $"{{\n{indent}{string.Join($",\n{indent}", entries)}\n{closingIndent}}}";
    }
}
