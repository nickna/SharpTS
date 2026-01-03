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
                var search = (string)args[0]!;
                var replacement = (string)args[1]!;
                // JavaScript replace() only replaces the first occurrence
                var index = str.IndexOf(search);
                if (index < 0) return str;
                return str.Substring(0, index) + replacement + str.Substring(index + search.Length);
            }),

            "split" => new BuiltInMethod("split", 1, (_, recv, args) =>
            {
                var str = (string)recv!;
                var separator = (string)args[0]!;
                string[] parts;
                if (separator == "")
                {
                    // Empty separator splits into individual characters
                    parts = str.Select(c => c.ToString()).ToArray();
                }
                else
                {
                    parts = str.Split(separator);
                }
                var elements = parts.Select(p => (object?)p).ToList();
                return new SharpTSArray(elements);
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

            _ => null
        };
    }
}
