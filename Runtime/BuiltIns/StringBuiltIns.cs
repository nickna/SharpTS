using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class StringBuiltIns
{
    // Cache unbound methods to avoid allocation on every access
    private static readonly BuiltInMethod _charAt = new("charAt", 1, CharAt);
    private static readonly BuiltInMethod _substring = new("substring", 1, 2, Substring);
    private static readonly BuiltInMethod _indexOf = new("indexOf", 1, IndexOf);
    private static readonly BuiltInMethod _toUpperCase = new("toUpperCase", 0, ToUpperCase);
    private static readonly BuiltInMethod _toLowerCase = new("toLowerCase", 0, ToLowerCase);
    private static readonly BuiltInMethod _trim = new("trim", 0, Trim);
    private static readonly BuiltInMethod _replace = new("replace", 2, Replace);
    private static readonly BuiltInMethod _split = new("split", 1, 2, Split); // optional limit parameter
    private static readonly BuiltInMethod _match = new("match", 1, Match);
    private static readonly BuiltInMethod _search = new("search", 1, Search);
    private static readonly BuiltInMethod _includes = new("includes", 1, Includes);
    private static readonly BuiltInMethod _startsWith = new("startsWith", 1, StartsWith);
    private static readonly BuiltInMethod _endsWith = new("endsWith", 1, EndsWith);
    private static readonly BuiltInMethod _slice = new("slice", 1, 2, Slice);
    private static readonly BuiltInMethod _repeat = new("repeat", 1, Repeat);
    private static readonly BuiltInMethod _padStart = new("padStart", 1, 2, PadStart);
    private static readonly BuiltInMethod _padEnd = new("padEnd", 1, 2, PadEnd);
    private static readonly BuiltInMethod _charCodeAt = new("charCodeAt", 1, CharCodeAt);
    private static readonly BuiltInMethod _concat = new("concat", 0, int.MaxValue, Concat);
    private static readonly BuiltInMethod _lastIndexOf = new("lastIndexOf", 1, LastIndexOf);
    private static readonly BuiltInMethod _trimStart = new("trimStart", 0, TrimStart);
    private static readonly BuiltInMethod _trimEnd = new("trimEnd", 0, TrimEnd);
    private static readonly BuiltInMethod _replaceAll = new("replaceAll", 2, ReplaceAll);
    private static readonly BuiltInMethod _at = new("at", 1, At);

    public static object? GetMember(string receiver, string name)
    {
        return name switch
        {
            "length" => (double)receiver.Length,
            "charAt" => _charAt.Bind(receiver),
            "substring" => _substring.Bind(receiver),
            "indexOf" => _indexOf.Bind(receiver),
            "toUpperCase" => _toUpperCase.Bind(receiver),
            "toLowerCase" => _toLowerCase.Bind(receiver),
            "trim" => _trim.Bind(receiver),
            "replace" => _replace.Bind(receiver),
            "split" => _split.Bind(receiver),
            "match" => _match.Bind(receiver),
            "search" => _search.Bind(receiver),
            "includes" => _includes.Bind(receiver),
            "startsWith" => _startsWith.Bind(receiver),
            "endsWith" => _endsWith.Bind(receiver),
            "slice" => _slice.Bind(receiver),
            "repeat" => _repeat.Bind(receiver),
            "padStart" => _padStart.Bind(receiver),
            "padEnd" => _padEnd.Bind(receiver),
            "charCodeAt" => _charCodeAt.Bind(receiver),
            "concat" => _concat.Bind(receiver),
            "lastIndexOf" => _lastIndexOf.Bind(receiver),
            "trimStart" => _trimStart.Bind(receiver),
            "trimEnd" => _trimEnd.Bind(receiver),
            "replaceAll" => _replaceAll.Bind(receiver),
            "at" => _at.Bind(receiver),
            _ => null
        };
    }

    private static object? CharAt(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var index = (int)(double)args[0]!;
        if (index < 0 || index >= str.Length) return "";
        return str[index].ToString();
    }

    private static object? Substring(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var start = Math.Max(0, (int)(double)args[0]!);
        var end = args.Count > 1 ? (int)(double)args[1]! : str.Length;
        if (start >= str.Length) return "";
        if (end > str.Length) end = str.Length;
        if (end <= start) return "";
        return str.Substring(start, end - start);
    }

    private static object? IndexOf(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        return (double)str.IndexOf(search);
    }

    private static object? ToUpperCase(Interpreter _, object? recv, List<object?> _args)
    {
        return ((string)recv!).ToUpper();
    }

    private static object? ToLowerCase(Interpreter _, object? recv, List<object?> _args)
    {
        return ((string)recv!).ToLower();
    }

    private static object? Trim(Interpreter _, object? recv, List<object?> _args)
    {
        return ((string)recv!).Trim();
    }

    private static object? Replace(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
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

    private static object? Split(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
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

    private static object? Match(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;

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

    private static object? Search(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;

        // Handle RegExp pattern
        if (args[0] is SharpTSRegExp regex)
        {
            return (double)regex.Search(str);
        }

        // String pattern
        var search = args[0]?.ToString() ?? "";
        return (double)str.IndexOf(search);
    }

    private static object? Includes(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        return str.Contains(search);
    }

    private static object? StartsWith(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        return str.StartsWith(search);
    }

    private static object? EndsWith(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        return str.EndsWith(search);
    }

    private static object? Slice(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
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

    private static object? Repeat(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var count = (int)(double)args[0]!;
        if (count < 0) throw new Exception("Runtime Error: Invalid count value for repeat()");
        if (count == 0 || str.Length == 0) return "";
        return string.Concat(Enumerable.Repeat(str, count));
    }

    private static object? PadStart(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var targetLength = (int)(double)args[0]!;
        var padString = args.Count > 1 ? (string)args[1]! : " ";

        if (targetLength <= str.Length || padString.Length == 0) return str;

        var padLength = targetLength - str.Length;
        var fullPads = padLength / padString.Length;
        var remainder = padLength % padString.Length;
        var padding = string.Concat(Enumerable.Repeat(padString, fullPads)) + padString.Substring(0, remainder);
        return padding + str;
    }

    private static object? PadEnd(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var targetLength = (int)(double)args[0]!;
        var padString = args.Count > 1 ? (string)args[1]! : " ";

        if (targetLength <= str.Length || padString.Length == 0) return str;

        var padLength = targetLength - str.Length;
        var fullPads = padLength / padString.Length;
        var remainder = padLength % padString.Length;
        var padding = string.Concat(Enumerable.Repeat(padString, fullPads)) + padString.Substring(0, remainder);
        return str + padding;
    }

    private static object? CharCodeAt(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var index = (int)(double)args[0]!;
        if (index < 0 || index >= str.Length) return double.NaN;
        return (double)str[index];
    }

    private static object? Concat(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var parts = new List<string> { str };
        foreach (var arg in args)
        {
            parts.Add(arg?.ToString() ?? "");
        }
        return string.Concat(parts);
    }

    private static object? LastIndexOf(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        return (double)str.LastIndexOf(search);
    }

    private static object? TrimStart(Interpreter _, object? recv, List<object?> _args)
    {
        return ((string)recv!).TrimStart();
    }

    private static object? TrimEnd(Interpreter _, object? recv, List<object?> _args)
    {
        return ((string)recv!).TrimEnd();
    }

    private static object? ReplaceAll(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var search = (string)args[0]!;
        var replacement = (string)args[1]!;
        if (search.Length == 0) return str;
        return str.Replace(search, replacement);
    }

    private static object? At(Interpreter _, object? recv, List<object?> args)
    {
        var str = (string)recv!;
        var index = (int)(double)args[0]!;
        // Handle negative indices
        if (index < 0) index = str.Length + index;
        if (index < 0 || index >= str.Length) return null;
        return str[index].ToString();
    }
}
