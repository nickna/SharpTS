using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class StringBuiltIns
{
    public static object? GetMember(string receiver, string name)
    {
        return name switch
        {
            "length" => (double)receiver.Length,

            "charAt" => new BuiltInMethod("charAt", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var index = (int)(double)args[0]!;
                if (index < 0 || index >= str.Length) return "";
                return str[index].ToString();
            }),

            "substring" => new BuiltInMethod("substring", 1, 2, (_, recv, args) =>
            {
                var str = (string)recv!;
                var start = Math.Max(0, (int)(double)args[0]!);
                var end = args.Count > 1 ? (int)(double)args[1]! : str.Length;
                if (start >= str.Length) return "";
                if (end > str.Length) end = str.Length;
                if (end <= start) return "";
                return str.Substring(start, end - start);
            }),

            "indexOf" => new BuiltInMethod("indexOf", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                return (double)str.IndexOf(search);
            }),

            "toUpperCase" => new BuiltInMethod("toUpperCase", 0, (_, recv, _) =>
            {
                return ((string)recv!).ToUpper();
            }),

            "toLowerCase" => new BuiltInMethod("toLowerCase", 0, (_, recv, _) =>
            {
                return ((string)recv!).ToLower();
            }),

            "trim" => new BuiltInMethod("trim", 0, (_, recv, _) =>
            {
                return ((string)recv!).Trim();
            }),

            "replace" => new BuiltInMethod("replace", 2, (_, recv, args) =>
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
            }),

            "split" => new BuiltInMethod("split", 1, (_, recv, args) =>
            {
                var str = (string)recv!;

                // Handle RegExp separator
                if (args[0] is SharpTSRegExp regex)
                {
                    var parts = regex.Split(str);
                    return new SharpTSArray(parts.Select(p => (object?)p).ToList());
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
                var elements = stringParts.Select(p => (object?)p).ToList();
                return new SharpTSArray(elements);
            }),

            "match" => new BuiltInMethod("match", 1, (_, recv, args) =>
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
            }),

            "search" => new BuiltInMethod("search", 1, (_, recv, args) =>
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
            }),

            "includes" => new BuiltInMethod("includes", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                return str.Contains(search);
            }),

            "startsWith" => new BuiltInMethod("startsWith", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                return str.StartsWith(search);
            }),

            "endsWith" => new BuiltInMethod("endsWith", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                return str.EndsWith(search);
            }),

            "slice" => new BuiltInMethod("slice", 1, 2, (_, recv, args) =>
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
            }),

            "repeat" => new BuiltInMethod("repeat", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var count = (int)(double)args[0]!;
                if (count < 0) throw new Exception("Runtime Error: Invalid count value for repeat()");
                if (count == 0 || str.Length == 0) return "";
                return string.Concat(Enumerable.Repeat(str, count));
            }),

            "padStart" => new BuiltInMethod("padStart", 1, 2, (_, recv, args) =>
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
            }),

            "padEnd" => new BuiltInMethod("padEnd", 1, 2, (_, recv, args) =>
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
            }),

            "charCodeAt" => new BuiltInMethod("charCodeAt", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var index = (int)(double)args[0]!;
                if (index < 0 || index >= str.Length) return double.NaN;
                return (double)str[index];
            }),

            "concat" => new BuiltInMethod("concat", 0, int.MaxValue, (_, recv, args) =>
            {
                var str = (string)recv!;
                var parts = new List<string> { str };
                foreach (var arg in args)
                {
                    parts.Add(arg?.ToString() ?? "");
                }
                return string.Concat(parts);
            }),

            "lastIndexOf" => new BuiltInMethod("lastIndexOf", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                return (double)str.LastIndexOf(search);
            }),

            "trimStart" => new BuiltInMethod("trimStart", 0, (_, recv, _) =>
            {
                return ((string)recv!).TrimStart();
            }),

            "trimEnd" => new BuiltInMethod("trimEnd", 0, (_, recv, _) =>
            {
                return ((string)recv!).TrimEnd();
            }),

            "replaceAll" => new BuiltInMethod("replaceAll", 2, (_, recv, args) =>
            {
                var str = (string)recv!;
                var search = (string)args[0]!;
                var replacement = (string)args[1]!;
                if (search.Length == 0) return str;
                return str.Replace(search, replacement);
            }),

            "at" => new BuiltInMethod("at", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var index = (int)(double)args[0]!;
                // Handle negative indices
                if (index < 0) index = str.Length + index;
                if (index < 0 || index >= str.Length) return null;
                return str[index].ToString();
            }),

            _ => null
        };
    }
}
