using System.Diagnostics;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript console object members.
/// </summary>
/// <remarks>
/// Contains method implementations for console.log, console.error, console.warn, console.info,
/// console.debug, console.clear, console.time/timeEnd/timeLog, console.assert, console.count/countReset,
/// console.table, console.dir, console.group/groupCollapsed/groupEnd, and console.trace.
/// Called by <see cref="Interpreter"/> when resolving console.* method calls.
/// Methods are returned as <see cref="BuiltInMethod"/> instances for uniform invocation.
/// </remarks>
/// <seealso cref="BuiltInMethod"/>
public static class ConsoleBuiltIns
{
    // Timer storage for console.time/timeEnd/timeLog (case-sensitive labels)
    private static readonly Dictionary<string, Stopwatch> _timers = new();

    // Counter storage for console.count/countReset
    private static readonly Dictionary<string, int> _counts = new();

    // Group indentation level (thread-static for thread safety)
    [ThreadStatic]
    private static int _groupIndentLevel;

    private static readonly BuiltInStaticMemberLookup _lookup =
        BuiltInStaticBuilder.Create()
            // Phase 1: Existing compiler methods + interpreter parity
            .Method("log", 0, int.MaxValue, Log)
            .Method("info", 0, int.MaxValue, Info)
            .Method("debug", 0, int.MaxValue, Debug)
            .Method("error", 0, int.MaxValue, Error)
            .Method("warn", 0, int.MaxValue, Warn)
            .Method("clear", 0, Clear)
            .Method("time", 0, 1, Time)
            .Method("timeEnd", 0, 1, TimeEnd)
            .Method("timeLog", 0, int.MaxValue, TimeLog)
            // Phase 2: New methods
            .Method("assert", 0, int.MaxValue, Assert)
            .Method("count", 0, 1, Count)
            .Method("countReset", 0, 1, CountReset)
            .Method("table", 1, 2, Table)
            .Method("dir", 1, 2, Dir)
            .Method("group", 0, int.MaxValue, Group)
            .Method("groupCollapsed", 0, int.MaxValue, GroupCollapsed)
            .Method("groupEnd", 0, GroupEnd)
            .Method("trace", 0, int.MaxValue, Trace)
            .Build();

    public static object? GetMember(string name)
        => _lookup.GetMember(name);

    // ===================== Helper Methods =====================

    /// <summary>
    /// Gets the current indentation string based on group level (2 spaces per level).
    /// </summary>
    private static string GetIndent()
        => _groupIndentLevel > 0 ? new string(' ', _groupIndentLevel * 2) : "";

    /// <summary>
    /// Converts a value to its string representation for console output.
    /// </summary>
    private static string Stringify(object? value)
    {
        if (value == null) return "null";
        if (value is SharpTSUndefined) return "undefined";
        if (value is double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            string text = d.ToString();
            // Remove trailing .0 for integers
            if (text.EndsWith(".0"))
                text = text[..^2];
            return text;
        }
        if (value is bool b) return b ? "true" : "false";
        if (value is SharpTSArray arr)
        {
            return "[" + string.Join(", ", arr.Elements.Select(Stringify)) + "]";
        }
        if (value is SharpTSObject obj)
        {
            var pairs = obj.Fields.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
            return "{ " + string.Join(", ", pairs) + " }";
        }
        if (value is SharpTSFunction or SharpTSArrowFunction or ISharpTSCallable)
        {
            return "[Function]";
        }
        if (value is SharpTSInstance instance)
        {
            return $"[{instance.GetClass().Name}]";
        }
        return value.ToString() ?? "null";
    }

    /// <summary>
    /// Formats a string with printf-style specifiers (%s, %d, %i, %o, %O, %j).
    /// </summary>
    /// <param name="format">The format string</param>
    /// <param name="args">The substitution arguments</param>
    /// <param name="argIndex">Starting index in args (typically 1 if format is args[0])</param>
    /// <returns>Formatted string plus any remaining unsubstituted arguments</returns>
    private static string FormatString(string format, List<object?> args, int argIndex)
    {
        var result = new StringBuilder();
        int currentArg = argIndex;
        int i = 0;

        while (i < format.Length)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                char specifier = format[i + 1];

                // Handle escaped %% -> %
                if (specifier == '%')
                {
                    result.Append('%');
                    i += 2;
                    continue;
                }

                // Handle format specifiers if we have remaining args
                if (currentArg < args.Count)
                {
                    var arg = args[currentArg];
                    switch (specifier)
                    {
                        case 's': // String
                            result.Append(Stringify(arg));
                            currentArg++;
                            i += 2;
                            continue;
                        case 'd': // Integer
                        case 'i':
                            result.Append(FormatAsInteger(arg));
                            currentArg++;
                            i += 2;
                            continue;
                        case 'f': // Float
                            result.Append(FormatAsFloat(arg));
                            currentArg++;
                            i += 2;
                            continue;
                        case 'o': // Object (expandable)
                        case 'O': // Object (generic)
                            result.Append(Stringify(arg));
                            currentArg++;
                            i += 2;
                            continue;
                        case 'j': // JSON
                            result.Append(FormatAsJson(arg));
                            currentArg++;
                            i += 2;
                            continue;
                    }
                }

                // Unknown specifier or no more args - output literally
                result.Append(format[i]);
                i++;
            }
            else
            {
                result.Append(format[i]);
                i++;
            }
        }

        // Append any remaining arguments not consumed by format specifiers
        while (currentArg < args.Count)
        {
            result.Append(' ');
            result.Append(Stringify(args[currentArg]));
            currentArg++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats a value as an integer for %d/%i specifier.
    /// </summary>
    private static string FormatAsInteger(object? value)
    {
        if (value == null) return "NaN";
        if (value is SharpTSUndefined) return "NaN";
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "NaN";
            return ((long)d).ToString();
        }
        if (value is bool b) return b ? "1" : "0";
        if (value is string s)
        {
            if (double.TryParse(s, out double parsed))
            {
                if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return "NaN";
                return ((long)parsed).ToString();
            }
            return "NaN";
        }
        return "NaN";
    }

    /// <summary>
    /// Formats a value as a float for %f specifier.
    /// </summary>
    private static string FormatAsFloat(object? value)
    {
        if (value == null) return "NaN";
        if (value is SharpTSUndefined) return "NaN";
        if (value is double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            return d.ToString();
        }
        if (value is bool b) return b ? "1" : "0";
        if (value is string s)
        {
            if (double.TryParse(s, out double parsed))
            {
                if (double.IsNaN(parsed)) return "NaN";
                if (double.IsPositiveInfinity(parsed)) return "Infinity";
                if (double.IsNegativeInfinity(parsed)) return "-Infinity";
                return parsed.ToString();
            }
            return "NaN";
        }
        return "NaN";
    }

    /// <summary>
    /// Formats a value as JSON for %j specifier.
    /// </summary>
    private static string FormatAsJson(object? value)
    {
        if (value == null) return "null";
        if (value is SharpTSUndefined) return "undefined";
        if (value is double d)
        {
            if (double.IsNaN(d)) return "null"; // JSON doesn't support NaN
            if (double.IsInfinity(d)) return "null"; // JSON doesn't support Infinity
            return d.ToString();
        }
        if (value is bool b) return b ? "true" : "false";
        if (value is string s) return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        if (value is SharpTSArray arr)
        {
            return "[" + string.Join(",", arr.Elements.Select(FormatAsJson)) + "]";
        }
        if (value is SharpTSObject obj)
        {
            var pairs = obj.Fields.Select(kv => $"\"{kv.Key}\":{FormatAsJson(kv.Value)}");
            return "{" + string.Join(",", pairs) + "}";
        }
        return Stringify(value);
    }

    /// <summary>
    /// Checks if a string contains format specifiers (including %% escape sequence).
    /// </summary>
    private static bool HasFormatSpecifiers(string str)
    {
        for (int i = 0; i < str.Length - 1; i++)
        {
            if (str[i] == '%')
            {
                char next = str[i + 1];
                // Include %% (escape sequence) and actual format specifiers
                if (next == '%' || next == 's' || next == 'd' || next == 'i' || next == 'f' ||
                    next == 'o' || next == 'O' || next == 'j')
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Determines if a value is truthy (for console.assert).
    /// </summary>
    private static bool IsTruthy(object? value)
    {
        if (value == null) return false;
        if (value is SharpTSUndefined) return false;
        if (value is bool b) return b;
        if (value is double d) return d != 0 && !double.IsNaN(d);
        if (value is string s) return s.Length > 0;
        return true;
    }

    /// <summary>
    /// Writes output to stdout with group indentation.
    /// </summary>
    private static void WriteOutput(string message)
    {
        Console.WriteLine(GetIndent() + message);
    }

    /// <summary>
    /// Writes output to stderr with group indentation.
    /// </summary>
    private static void WriteError(string message)
    {
        Console.Error.WriteLine(GetIndent() + message);
    }

    // ===================== Phase 1 Methods =====================

    private static object? Log(Interpreter _, List<object?> args)
    {
        if (args.Count == 0)
        {
            WriteOutput("");
        }
        else if (args.Count >= 1 && args[0] is string format && HasFormatSpecifiers(format))
        {
            // First argument is a format string with specifiers
            WriteOutput(FormatString(format, args, 1));
        }
        else
        {
            WriteOutput(string.Join(" ", args.Select(Stringify)));
        }
        return null;
    }

    private static object? Info(Interpreter interp, List<object?> args)
        => Log(interp, args);

    private static object? Debug(Interpreter interp, List<object?> args)
        => Log(interp, args);

    private static object? Error(Interpreter _, List<object?> args)
    {
        if (args.Count == 0)
        {
            WriteError("");
        }
        else
        {
            WriteError(string.Join(" ", args.Select(Stringify)));
        }
        return null;
    }

    private static object? Warn(Interpreter interp, List<object?> args)
        => Error(interp, args);

    private static object? Clear(Interpreter _, List<object?> args)
    {
        try
        {
            Console.Clear();
        }
        catch
        {
            // Ignore exceptions (e.g., when stdout is redirected)
        }
        return null;
    }

    private static object? Time(Interpreter _, List<object?> args)
    {
        string label = args.Count > 0 && args[0] != null ? Stringify(args[0]) : "default";
        _timers[label] = Stopwatch.StartNew();
        return null;
    }

    private static object? TimeEnd(Interpreter _, List<object?> args)
    {
        string label = args.Count > 0 && args[0] != null ? Stringify(args[0]) : "default";
        if (_timers.TryGetValue(label, out var sw))
        {
            sw.Stop();
            WriteOutput($"{label}: {sw.Elapsed.TotalMilliseconds}ms");
            _timers.Remove(label);
        }
        return null;
    }

    private static object? TimeLog(Interpreter _, List<object?> args)
    {
        string label = args.Count > 0 && args[0] != null ? Stringify(args[0]) : "default";
        if (_timers.TryGetValue(label, out var sw))
        {
            var elapsed = sw.Elapsed.TotalMilliseconds;
            if (args.Count > 1)
            {
                // Additional arguments are logged after the time
                var extraArgs = args.Skip(1).Select(Stringify);
                WriteOutput($"{label}: {elapsed}ms {string.Join(" ", extraArgs)}");
            }
            else
            {
                WriteOutput($"{label}: {elapsed}ms");
            }
        }
        return null;
    }

    // ===================== Phase 2 Methods =====================

    private static object? Assert(Interpreter _, List<object?> args)
    {
        // No condition provided or condition is falsy
        bool condition = args.Count > 0 && IsTruthy(args[0]);
        if (!condition)
        {
            if (args.Count > 1)
            {
                // Additional arguments are the assertion message
                var messageArgs = args.Skip(1).Select(Stringify);
                WriteError("Assertion failed: " + string.Join(" ", messageArgs));
            }
            else
            {
                WriteError("Assertion failed");
            }
        }
        return null;
    }

    private static object? Count(Interpreter _, List<object?> args)
    {
        string label = args.Count > 0 && args[0] != null ? Stringify(args[0]) : "default";
        if (!_counts.TryGetValue(label, out var count))
        {
            count = 0;
        }
        count++;
        _counts[label] = count;
        WriteOutput($"{label}: {count}");
        return null;
    }

    private static object? CountReset(Interpreter _, List<object?> args)
    {
        string label = args.Count > 0 && args[0] != null ? Stringify(args[0]) : "default";
        _counts[label] = 0;
        return null;
    }

    private static object? Table(Interpreter _, List<object?> args)
    {
        if (args.Count == 0) return null;

        var data = args[0];
        List<string>? columns = null;
        if (args.Count > 1 && args[1] is SharpTSArray colArr)
        {
            columns = colArr.Elements.Select(e => Stringify(e)).ToList();
        }

        // Handle array of objects
        if (data is SharpTSArray arr)
        {
            RenderArrayTable(arr, columns);
        }
        else if (data is SharpTSObject obj)
        {
            RenderObjectTable(obj, columns);
        }
        else
        {
            // For primitives, just log the value
            WriteOutput(Stringify(data));
        }
        return null;
    }

    private static void RenderArrayTable(SharpTSArray arr, List<string>? columnFilter)
    {
        if (arr.Elements.Count == 0)
        {
            WriteOutput("(empty array)");
            return;
        }

        // Collect all column names from objects in the array
        var allColumns = new HashSet<string> { "(index)" };
        var rows = new List<Dictionary<string, string>>();

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var row = new Dictionary<string, string> { ["(index)"] = i.ToString() };
            var element = arr.Elements[i];

            if (element is SharpTSObject obj)
            {
                foreach (var kv in obj.Fields)
                {
                    if (columnFilter == null || columnFilter.Contains(kv.Key))
                    {
                        allColumns.Add(kv.Key);
                        row[kv.Key] = TruncateColumn(Stringify(kv.Value));
                    }
                }
            }
            else
            {
                allColumns.Add("Values");
                row["Values"] = TruncateColumn(Stringify(element));
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

        RenderTable(columnList, rows);
    }

    private static void RenderObjectTable(SharpTSObject obj, List<string>? columnFilter)
    {
        if (obj.Fields.Count == 0)
        {
            WriteOutput("(empty object)");
            return;
        }

        var rows = new List<Dictionary<string, string>>();
        var allColumns = new HashSet<string> { "(index)", "Values" };

        foreach (var kv in obj.Fields)
        {
            if (columnFilter != null && !columnFilter.Contains(kv.Key)) continue;

            var row = new Dictionary<string, string>
            {
                ["(index)"] = kv.Key,
                ["Values"] = TruncateColumn(Stringify(kv.Value))
            };
            rows.Add(row);
        }

        var columnList = columnFilter != null
            ? new List<string> { "(index)" }.Concat(columnFilter.Where(c => allColumns.Contains(c))).ToList()
            : new List<string> { "(index)", "Values" };

        RenderTable(columnList, rows);
    }

    private static void RenderTable(List<string> columns, List<Dictionary<string, string>> rows)
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

        var indent = GetIndent();

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

    private static string TruncateColumn(string value)
    {
        const int maxWidth = 40;
        if (value.Length <= maxWidth) return value;
        return value[..(maxWidth - 3)] + "...";
    }

    private static object? Dir(Interpreter _, List<object?> args)
    {
        if (args.Count == 0) return null;

        var obj = args[0];
        // Options (depth, colors, etc.) are largely ignored for simplicity
        WriteOutput(InspectObject(obj, 0));
        return null;
    }

    private static string InspectObject(object? obj, int depth)
    {
        const int maxDepth = 2;
        var indent = new string(' ', depth * 2);

        if (obj == null) return "null";
        if (obj is SharpTSUndefined) return "undefined";
        if (obj is string s) return $"'{s}'";
        if (obj is double d) return Stringify(d);
        if (obj is bool b) return b ? "true" : "false";

        if (depth > maxDepth) return "[Object]";

        if (obj is SharpTSArray arr)
        {
            if (arr.Elements.Count == 0) return "[]";
            var sb = new StringBuilder("[\n");
            foreach (var elem in arr.Elements)
            {
                sb.Append(indent + "  " + InspectObject(elem, depth + 1) + ",\n");
            }
            sb.Append(indent + "]");
            return sb.ToString();
        }

        if (obj is SharpTSObject sobj)
        {
            if (sobj.Fields.Count == 0) return "{}";
            var sb = new StringBuilder("{\n");
            foreach (var kv in sobj.Fields)
            {
                sb.Append($"{indent}  {kv.Key}: {InspectObject(kv.Value, depth + 1)},\n");
            }
            sb.Append(indent + "}");
            return sb.ToString();
        }

        return Stringify(obj);
    }

    private static object? Group(Interpreter _, List<object?> args)
    {
        if (args.Count > 0)
        {
            WriteOutput(string.Join(" ", args.Select(Stringify)));
        }
        _groupIndentLevel++;
        return null;
    }

    private static object? GroupCollapsed(Interpreter interp, List<object?> args)
    {
        // In a terminal context, groupCollapsed behaves the same as group
        return Group(interp, args);
    }

    private static object? GroupEnd(Interpreter _, List<object?> args)
    {
        if (_groupIndentLevel > 0)
        {
            _groupIndentLevel--;
        }
        return null;
    }

    private static object? Trace(Interpreter _, List<object?> args)
    {
        var message = args.Count > 0 ? string.Join(" ", args.Select(Stringify)) : "";
        WriteOutput("Trace: " + message);

        // Print C# stack trace (TypeScript source mapping not available)
        var stackTrace = new StackTrace(true);
        var frames = stackTrace.GetFrames();
        foreach (var frame in frames.Skip(2)) // Skip ConsoleBuiltIns frames
        {
            var method = frame.GetMethod();
            if (method == null) continue;
            var className = method.DeclaringType?.Name ?? "?";
            var methodName = method.Name;
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();

            if (fileName != null)
            {
                WriteOutput($"    at {className}.{methodName} ({fileName}:{lineNumber})");
            }
            else
            {
                WriteOutput($"    at {className}.{methodName}");
            }
        }
        return null;
    }

    // ===================== Testing Helpers =====================

    /// <summary>
    /// Resets all console state (timers, counts, group level). Used for testing.
    /// </summary>
    internal static void ResetState()
    {
        _timers.Clear();
        _counts.Clear();
        _groupIndentLevel = 0;
    }
}
