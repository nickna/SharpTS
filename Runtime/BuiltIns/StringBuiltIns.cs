using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class StringBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<string> _lookup =
        BuiltInTypeBuilder<string>.ForInstanceType()
            .Property("length", s => (double)s.Length)
            .Method("charAt", 1, CharAt)
            .Method("substring", 1, 2, Substring)
            .Method("indexOf", 1, IndexOf)
            .Method("toUpperCase", 0, ToUpperCase)
            .Method("toLowerCase", 0, ToLowerCase)
            .Method("trim", 0, Trim)
            .Method("replace", 2, Replace)
            .Method("split", 1, 2, Split)
            .Method("match", 1, Match)
            .Method("search", 1, Search)
            .Method("includes", 1, Includes)
            .Method("startsWith", 1, StartsWith)
            .Method("endsWith", 1, EndsWith)
            .Method("slice", 1, 2, Slice)
            .Method("repeat", 1, Repeat)
            .Method("padStart", 1, 2, PadStart)
            .Method("padEnd", 1, 2, PadEnd)
            .Method("charCodeAt", 1, CharCodeAt)
            .Method("concat", 0, int.MaxValue, Concat)
            .Method("lastIndexOf", 1, LastIndexOf)
            .Method("trimStart", 0, TrimStart)
            .Method("trimEnd", 0, TrimEnd)
            .Method("replaceAll", 2, ReplaceAll)
            .Method("at", 1, At)
            .Build();

    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .Method("raw", 1, int.MaxValue, StringRaw)
            .Build();

    public static object? GetMember(string receiver, string name)
        => _lookup.GetMember(receiver, name);

    /// <summary>
    /// Gets a static member (method) from the String namespace.
    /// Currently only supports String.raw for tagged templates.
    /// </summary>
    public static object? GetStaticMember(string name)
        => _staticLookup.GetMember(name);

    private static object? CharAt(Interpreter _, string str, List<object?> args)
    {
        var index = (int)(double)args[0]!;
        if (index < 0 || index >= str.Length) return "";
        return str[index].ToString();
    }

    private static object? Substring(Interpreter _, string str, List<object?> args)
    {
        var start = Math.Max(0, (int)(double)args[0]!);
        var end = args.Count > 1 ? (int)(double)args[1]! : str.Length;
        if (start >= str.Length) return "";
        if (end > str.Length) end = str.Length;
        if (end <= start) return "";
        return str.Substring(start, end - start);
    }

    private static object? IndexOf(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        return (double)str.IndexOf(search);
    }

    private static object? ToUpperCase(Interpreter _, string str, List<object?> args)
        => str.ToUpper();

    private static object? ToLowerCase(Interpreter _, string str, List<object?> args)
        => str.ToLower();

    private static object? Trim(Interpreter _, string str, List<object?> args)
        => str.Trim();

    private static object? Replace(Interpreter _, string str, List<object?> args)
    {
        var replacement = args[1]?.ToString() ?? "";

        // Handle RegExp pattern
        if (args[0] is SharpTSRegExp regex)
        {
            return regex.Replace(str, replacement);
        }

        // String pattern: JavaScript replace() only replaces the first occurrence
        var search = args[0]?.ToString() ?? "";
        var index = str.IndexOf(search);
        if (index < 0) return str;
        return str.Substring(0, index) + replacement + str.Substring(index + search.Length);
    }

    private static object? Split(Interpreter _, string str, List<object?> args)
    {
        int? limit = args.Count > 1 && args[1] is double d ? (int)d : null;

        // Handle RegExp separator
        if (args[0] is SharpTSRegExp regex)
        {
            string[] parts = regex.Split(str);
            // Apply limit if specified
            IEnumerable<string> resultParts = limit.HasValue && limit.Value >= 0
                ? parts.Take(limit.Value)
                : parts;
            return new SharpTSArray(resultParts.Select(p => (object?)p).ToList());
        }

        // String separator
        var separator = args[0]?.ToString() ?? "";
        string[] stringParts;
        if (separator == "")
        {
            // Empty separator splits into individual characters
            stringParts = str.Select(c => c.ToString()).ToArray();
        }
        else
        {
            stringParts = str.Split(separator);
        }

        // Apply limit if specified (JavaScript behavior: limit restricts number of results)
        if (limit.HasValue && limit.Value >= 0)
        {
            stringParts = stringParts.Take(limit.Value).ToArray();
        }

        var elements = stringParts.Select(p => (object?)p).ToList();
        return new SharpTSArray(elements);
    }

    private static object? Match(Interpreter _, string str, List<object?> args)
    {
        // Handle RegExp pattern
        if (args[0] is SharpTSRegExp regex)
        {
            if (regex.Global)
            {
                // Global match: return array of all matches
                var matches = regex.MatchAll(str);
                if (matches.Count == 0) return null;
                return new SharpTSArray(matches.Select(m => (object?)m).ToList());
            }
            else
            {
                // Non-global: same as exec()
                return regex.Exec(str);
            }
        }

        // String pattern: find first occurrence
        var search = args[0]?.ToString() ?? "";
        var index = str.IndexOf(search);
        if (index < 0) return null;
        return new SharpTSArray([(object?)search]);
    }

    private static object? Search(Interpreter _, string str, List<object?> args)
    {
        // Handle RegExp pattern
        if (args[0] is SharpTSRegExp regex)
        {
            return (double)regex.Search(str);
        }

        // String pattern
        var search = args[0]?.ToString() ?? "";
        return (double)str.IndexOf(search);
    }

    private static object? Includes(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        return str.Contains(search);
    }

    private static object? StartsWith(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        return str.StartsWith(search);
    }

    private static object? EndsWith(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        return str.EndsWith(search);
    }

    private static object? Slice(Interpreter _, string str, List<object?> args)
    {
        var start = (int)(double)args[0]!;
        var end = args.Count > 1 ? (int)(double)args[1]! : str.Length;

        // Handle negative indices (from end of string)
        if (start < 0) start = Math.Max(0, str.Length + start);
        if (end < 0) end = Math.Max(0, str.Length + end);

        // Clamp to valid range
        start = Math.Min(start, str.Length);
        end = Math.Min(end, str.Length);

        if (end <= start) return "";
        return str.Substring(start, end - start);
    }

    private static object? Repeat(Interpreter _, string str, List<object?> args)
    {
        var count = (int)(double)args[0]!;
        if (count < 0) throw new Exception("Runtime Error: Invalid count value for repeat()");
        if (count == 0 || str.Length == 0) return "";
        return string.Concat(Enumerable.Repeat(str, count));
    }

    private static object? PadStart(Interpreter _, string str, List<object?> args)
    {
        var targetLength = (int)(double)args[0]!;
        var padString = args.Count > 1 ? (string)args[1]! : " ";

        if (targetLength <= str.Length || padString.Length == 0) return str;

        var padLength = targetLength - str.Length;
        var fullPads = padLength / padString.Length;
        var remainder = padLength % padString.Length;
        var padding = string.Concat(Enumerable.Repeat(padString, fullPads)) + padString.Substring(0, remainder);
        return padding + str;
    }

    private static object? PadEnd(Interpreter _, string str, List<object?> args)
    {
        var targetLength = (int)(double)args[0]!;
        var padString = args.Count > 1 ? (string)args[1]! : " ";

        if (targetLength <= str.Length || padString.Length == 0) return str;

        var padLength = targetLength - str.Length;
        var fullPads = padLength / padString.Length;
        var remainder = padLength % padString.Length;
        var padding = string.Concat(Enumerable.Repeat(padString, fullPads)) + padString.Substring(0, remainder);
        return str + padding;
    }

    private static object? CharCodeAt(Interpreter _, string str, List<object?> args)
    {
        var index = (int)(double)args[0]!;
        if (index < 0 || index >= str.Length) return double.NaN;
        return (double)str[index];
    }

    private static object? Concat(Interpreter _, string str, List<object?> args)
    {
        var parts = new List<string> { str };
        foreach (var arg in args)
        {
            parts.Add(arg?.ToString() ?? "");
        }
        return string.Concat(parts);
    }

    private static object? LastIndexOf(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        return (double)str.LastIndexOf(search);
    }

    private static object? TrimStart(Interpreter _, string str, List<object?> args)
        => str.TrimStart();

    private static object? TrimEnd(Interpreter _, string str, List<object?> args)
        => str.TrimEnd();

    private static object? ReplaceAll(Interpreter _, string str, List<object?> args)
    {
        var search = (string)args[0]!;
        var replacement = (string)args[1]!;
        if (search.Length == 0) return str;
        return str.Replace(search, replacement);
    }

    private static object? At(Interpreter _, string str, List<object?> args)
    {
        var index = (int)(double)args[0]!;
        // Handle negative indices
        if (index < 0) index = str.Length + index;
        if (index < 0 || index >= str.Length) return null;
        return str[index].ToString();
    }

    /// <summary>
    /// String.raw tag function implementation.
    /// Returns raw strings from template literals with substitutions.
    /// </summary>
    private static object? StringRaw(Interpreter _, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("TypeError: String.raw requires at least 1 argument.");

        // First argument should have a 'raw' property
        object? stringsArg = args[0];
        IList<object?>? rawStrings = null;

        if (stringsArg is SharpTSTemplateStringsArray tsa)
        {
            rawStrings = tsa.Raw.Elements;
        }
        else if (stringsArg is SharpTSObject obj)
        {
            var rawProp = obj.GetProperty("raw");
            if (rawProp is SharpTSArray rawArr)
                rawStrings = rawArr.Elements;
        }
        else if (stringsArg is SharpTSArray arr)
        {
            // Check if array has a 'raw' property (via SharpTSTemplateStringsArray)
            if (stringsArg is ISharpTSPropertyAccessor accessor)
            {
                var rawProp = accessor.GetProperty("raw");
                if (rawProp is SharpTSArray rawArr)
                    rawStrings = rawArr.Elements;
            }
            if (rawStrings == null)
            {
                // Use the array elements directly as raw strings
                rawStrings = arr.Elements;
            }
        }

        if (rawStrings == null || rawStrings.Count == 0)
            return "";

        var result = new StringBuilder();
        for (int i = 0; i < rawStrings.Count; i++)
        {
            result.Append(rawStrings[i]?.ToString() ?? "");
            if (i < args.Count - 1 && i < rawStrings.Count - 1)
            {
                result.Append(args[i + 1]?.ToString() ?? "");
            }
        }

        return result.ToString();
    }
}
