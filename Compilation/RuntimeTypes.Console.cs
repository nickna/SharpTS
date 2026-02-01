using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Console

    // Thread-static group level for compiled mode
    [ThreadStatic]
    private static int _consoleGroupLevel;

    /// <summary>
    /// Gets the current indentation string based on group level (2 spaces per level).
    /// </summary>
    public static string GetConsoleIndent()
        => _consoleGroupLevel > 0 ? new string(' ', _consoleGroupLevel * 2) : "";

    /// <summary>
    /// Increments the console group level.
    /// </summary>
    public static void IncrementConsoleGroup() => _consoleGroupLevel++;

    /// <summary>
    /// Decrements the console group level if positive.
    /// </summary>
    public static void DecrementConsoleGroup()
    {
        if (_consoleGroupLevel > 0) _consoleGroupLevel--;
    }

    public static void ConsoleLog(object? value)
    {
        Console.WriteLine(GetConsoleIndent() + Stringify(value));
    }

    public static void ConsoleLogMultiple(object?[] values)
    {
        Console.WriteLine(GetConsoleIndent() + string.Join(" ", values.Select(Stringify)));
    }

    /// <summary>
    /// Renders console.table with ASCII table formatting.
    /// </summary>
    public static void ConsoleTable(object? data, object? columns)
    {
        if (data == null)
        {
            Console.WriteLine(GetConsoleIndent() + Stringify(data));
            return;
        }

        List<string>? columnFilter = null;
        if (columns is List<object?> colList)
        {
            columnFilter = colList.Select(c => Stringify(c)).ToList();
        }

        // Handle List<object?> (array)
        if (data is List<object?> list)
        {
            RenderArrayTableCompiled(list, columnFilter);
        }
        // Handle Dictionary<string, object?> (object)
        else if (data is Dictionary<string, object?> dict)
        {
            RenderObjectTableCompiled(dict, columnFilter);
        }
        else
        {
            // For primitives, just log the value
            Console.WriteLine(GetConsoleIndent() + Stringify(data));
        }
    }

    private static void RenderArrayTableCompiled(List<object?> arr, List<string>? columnFilter)
    {
        if (arr.Count == 0)
        {
            Console.WriteLine(GetConsoleIndent() + "(empty array)");
            return;
        }

        // Collect all column names from objects in the array
        var allColumns = new HashSet<string> { "(index)" };
        var rows = new List<Dictionary<string, string>>();

        for (int i = 0; i < arr.Count; i++)
        {
            var row = new Dictionary<string, string> { ["(index)"] = i.ToString() };
            var element = arr[i];

            if (element is Dictionary<string, object?> obj)
            {
                foreach (var kv in obj)
                {
                    if (columnFilter == null || columnFilter.Contains(kv.Key))
                    {
                        allColumns.Add(kv.Key);
                        row[kv.Key] = TruncateTableColumn(Stringify(kv.Value));
                    }
                }
            }
            else
            {
                allColumns.Add("Values");
                row["Values"] = TruncateTableColumn(Stringify(element));
            }
            rows.Add(row);
        }

        // Build column list
        var columnList = new List<string> { "(index)" };
        if (columnFilter != null)
        {
            columnList.AddRange(columnFilter.Where(c => allColumns.Contains(c)));
        }
        else
        {
            columnList.AddRange(allColumns.Where(c => c != "(index)").OrderBy(c => c));
        }

        RenderTableCompiled(columnList, rows);
    }

    private static void RenderObjectTableCompiled(Dictionary<string, object?> obj, List<string>? columnFilter)
    {
        if (obj.Count == 0)
        {
            Console.WriteLine(GetConsoleIndent() + "(empty object)");
            return;
        }

        var rows = new List<Dictionary<string, string>>();
        var allColumns = new HashSet<string> { "(index)", "Values" };

        foreach (var kv in obj)
        {
            if (columnFilter != null && !columnFilter.Contains(kv.Key)) continue;

            var row = new Dictionary<string, string>
            {
                ["(index)"] = kv.Key,
                ["Values"] = TruncateTableColumn(Stringify(kv.Value))
            };
            rows.Add(row);
        }

        var columnList = columnFilter != null
            ? new List<string> { "(index)" }.Concat(columnFilter.Where(c => allColumns.Contains(c))).ToList()
            : new List<string> { "(index)", "Values" };

        RenderTableCompiled(columnList, rows);
    }

    private static void RenderTableCompiled(List<string> columns, List<Dictionary<string, string>> rows)
    {
        // Calculate column widths
        var widths = columns.ToDictionary(c => c, c => c.Length);
        foreach (var row in rows)
        {
            foreach (var col in columns)
            {
                if (row.TryGetValue(col, out var val) && val.Length > widths[col])
                {
                    widths[col] = Math.Min(val.Length, 40);
                }
            }
        }

        var indent = GetConsoleIndent();

        // Header separator
        var separator = "+" + string.Join("+", columns.Select(c => new string('-', widths[c] + 2))) + "+";
        Console.WriteLine(indent + separator);

        // Header row
        var header = "|" + string.Join("|", columns.Select(c => $" {c.PadRight(widths[c])} ")) + "|";
        Console.WriteLine(indent + header);
        Console.WriteLine(indent + separator);

        // Data rows
        foreach (var row in rows)
        {
            var rowStr = "|" + string.Join("|", columns.Select(c =>
            {
                var val = row.TryGetValue(c, out var v) ? v : "";
                return $" {val.PadRight(widths[c])} ";
            })) + "|";
            Console.WriteLine(indent + rowStr);
        }
        Console.WriteLine(indent + separator);
    }

    private static string TruncateTableColumn(string value)
    {
        const int maxWidth = 40;
        if (value.Length <= maxWidth) return value;
        return value[..(maxWidth - 3)] + "...";
    }

    /// <summary>
    /// Renders console.dir with recursive object inspection.
    /// </summary>
    public static void ConsoleDir(object? obj)
    {
        Console.WriteLine(GetConsoleIndent() + InspectObjectCompiled(obj, 0));
    }

    private static string InspectObjectCompiled(object? obj, int depth)
    {
        const int maxDepth = 2;
        var indentSpaces = new string(' ', depth * 2);

        if (obj == null) return "null";
        if (obj is string s) return $"'{s}'";
        if (obj is double d) return Stringify(d);
        if (obj is bool b) return b ? "true" : "false";

        if (depth > maxDepth) return "[Object]";

        if (obj is List<object?> list)
        {
            if (list.Count == 0) return "[]";
            var sb = new StringBuilder("[\n");
            foreach (var elem in list)
            {
                sb.Append(indentSpaces + "  " + InspectObjectCompiled(elem, depth + 1) + ",\n");
            }
            sb.Append(indentSpaces + "]");
            return sb.ToString();
        }

        if (obj is Dictionary<string, object?> dict)
        {
            if (dict.Count == 0) return "{}";
            var sb = new StringBuilder("{\n");
            foreach (var kv in dict)
            {
                sb.Append($"{indentSpaces}  {kv.Key}: {InspectObjectCompiled(kv.Value, depth + 1)},\n");
            }
            sb.Append(indentSpaces + "}");
            return sb.ToString();
        }

        return Stringify(obj);
    }

    /// <summary>
    /// Formats a value as JSON for the %j format specifier.
    /// </summary>
    public static string FormatAsJson(object? value)
    {
        if (value == null) return "null";
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (value is bool b) return b ? "true" : "false";
        if (value is string s)
        {
            // Escape special characters for JSON
            s = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{s}\"";
        }
        if (value is List<object?> list)
        {
            return "[" + string.Join(",", list.Select(FormatAsJson)) + "]";
        }
        if (value is Dictionary<string, object?> dict)
        {
            var pairs = dict.Select(kv => $"\"{kv.Key}\":{FormatAsJson(kv.Value)}");
            return "{" + string.Join(",", pairs) + "}";
        }
        // Fallback for other types
        return Stringify(value);
    }

    #endregion
}
