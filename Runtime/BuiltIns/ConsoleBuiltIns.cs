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
            .MethodV2("log", 0, int.MaxValue, LogV2)
            .MethodV2("info", 0, int.MaxValue, InfoV2)
            .MethodV2("debug", 0, int.MaxValue, DebugV2)
            .MethodV2("error", 0, int.MaxValue, ErrorV2)
            .MethodV2("warn", 0, int.MaxValue, WarnV2)
            .MethodV2("clear", 0, ClearV2)
            .MethodV2("time", 0, 1, TimeV2)
            .MethodV2("timeEnd", 0, 1, TimeEndV2)
            .MethodV2("timeLog", 0, int.MaxValue, TimeLogV2)
            // Phase 2: New methods
            .MethodV2("assert", 0, int.MaxValue, AssertV2)
            .MethodV2("count", 0, 1, CountV2)
            .MethodV2("countReset", 0, 1, CountResetV2)
            .MethodV2("table", 1, 2, TableV2)
            .MethodV2("dir", 1, 2, DirV2)
            .MethodV2("group", 0, int.MaxValue, GroupV2)
            .MethodV2("groupCollapsed", 0, int.MaxValue, GroupCollapsedV2)
            .MethodV2("groupEnd", 0, GroupEndV2)
            .MethodV2("trace", 0, int.MaxValue, TraceV2)
            .Build();

    public static object? GetMember(string name)
        => _lookup.GetMember(name);

    /// <summary>Member names for REPL autocomplete.</summary>
    public static IEnumerable<string> MemberNames => _lookup.MemberNames;

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
            // Single source of truth: the correct ECMA-262 Number::toString, shared
            // with compiled output (RuntimeTypes.FormatNumber).
            return Compilation.RuntimeTypes.FormatNumber(d);
        }
        if (value is bool b) return b ? "true" : "false";
        if (value is SharpTSArray arr)
        {
            return "[" + string.Join(", ", arr.Select(Stringify)) + "]";
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
    /// Stringifies a RuntimeValue by extracting the boxed object.
    /// </summary>
    private static string StringifyRV(RuntimeValue value)
        => Stringify(value.ToObject());

    /// <summary>
    /// Joins a span of RuntimeValue arguments with space separator, stringifying each.
    /// </summary>
    private static string JoinArgs(ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return "";
        if (args.Length == 1) return StringifyRV(args[0]);
        var sb = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(StringifyRV(args[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats a string with printf-style specifiers using RuntimeValue span args (%s, %d, %i, %o, %O, %j).
    /// </summary>
    private static string FormatStringV2(string format, ReadOnlySpan<RuntimeValue> args, int argIndex)
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
                if (currentArg < args.Length)
                {
                    var arg = args[currentArg].ToObject();
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
        while (currentArg < args.Length)
        {
            result.Append(' ');
            result.Append(StringifyRV(args[currentArg]));
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
            return "[" + string.Join(",", arr.Select(FormatAsJson)) + "]";
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
    /// Writes output to stdout with group indentation.
    /// </summary>
    private static void WriteOutput(TextWriter writer, string message)
    {
        writer.WriteLine(GetIndent() + message);
    }

    /// <summary>
    /// Writes output to stderr with group indentation.
    /// </summary>
    private static void WriteError(TextWriter writer, string message)
    {
        writer.WriteLine(GetIndent() + message);
    }

    // ===================== V2 Methods =====================

    private static RuntimeValue LogV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
        {
            WriteOutput(i.Out, "");
        }
        else if (args[0].ToObject() is string format && HasFormatSpecifiers(format))
        {
            WriteOutput(i.Out, FormatStringV2(format, args, 1));
        }
        else
        {
            WriteOutput(i.Out, JoinArgs(args));
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue InfoV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => LogV2(interp, receiver, args);

    private static RuntimeValue DebugV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => LogV2(interp, receiver, args);

    private static RuntimeValue ErrorV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
        {
            WriteError(i.Error, "");
        }
        else
        {
            WriteError(i.Error, JoinArgs(args));
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue WarnV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => ErrorV2(interp, receiver, args);

    private static RuntimeValue ClearV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        try
        {
            Console.Clear();
        }
        catch
        {
            // Ignore exceptions (e.g., when stdout is redirected)
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue TimeV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string label = args.Length > 0 && args[0].ToObject() != null ? StringifyRV(args[0]) : "default";
        _timers[label] = Stopwatch.StartNew();
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue TimeEndV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string label = args.Length > 0 && args[0].ToObject() != null ? StringifyRV(args[0]) : "default";
        if (_timers.TryGetValue(label, out var sw))
        {
            sw.Stop();
            WriteOutput(i.Out, $"{label}: {sw.Elapsed.TotalMilliseconds}ms");
            _timers.Remove(label);
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue TimeLogV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string label = args.Length > 0 && args[0].ToObject() != null ? StringifyRV(args[0]) : "default";
        if (_timers.TryGetValue(label, out var sw))
        {
            var elapsed = sw.Elapsed.TotalMilliseconds;
            if (args.Length > 1)
            {
                // Additional arguments are logged after the time
                var sb = new StringBuilder();
                for (int j = 1; j < args.Length; j++)
                {
                    if (j > 1) sb.Append(' ');
                    sb.Append(StringifyRV(args[j]));
                }
                WriteOutput(i.Out, $"{label}: {elapsed}ms {sb}");
            }
            else
            {
                WriteOutput(i.Out, $"{label}: {elapsed}ms");
            }
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue AssertV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // No condition provided or condition is falsy
        bool condition = args.Length > 0 && args[0].IsTruthy();
        if (!condition)
        {
            if (args.Length > 1)
            {
                // Additional arguments are the assertion message
                var sb = new StringBuilder();
                for (int j = 1; j < args.Length; j++)
                {
                    if (j > 1) sb.Append(' ');
                    sb.Append(StringifyRV(args[j]));
                }
                WriteError(i.Error, "Assertion failed: " + sb);
            }
            else
            {
                WriteError(i.Error, "Assertion failed");
            }
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue CountV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string label = args.Length > 0 && args[0].ToObject() != null ? StringifyRV(args[0]) : "default";
        if (!_counts.TryGetValue(label, out var count))
        {
            count = 0;
        }
        count++;
        _counts[label] = count;
        WriteOutput(i.Out, $"{label}: {count}");
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue CountResetV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string label = args.Length > 0 && args[0].ToObject() != null ? StringifyRV(args[0]) : "default";
        _counts[label] = 0;
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue TableV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.Undefined;

        var data = args[0].ToObject();
        List<string>? columns = null;
        if (args.Length > 1 && args[1].ToObject() is SharpTSArray colArr)
        {
            columns = colArr.Select(e => Stringify(e)).ToList();
        }

        // Handle array of objects
        if (data is SharpTSArray arr)
        {
            RenderArrayTable(i.Out, arr, columns);
        }
        else if (data is SharpTSObject obj)
        {
            RenderObjectTable(i.Out, obj, columns);
        }
        else
        {
            // For primitives, just log the value
            WriteOutput(i.Out, Stringify(data));
        }
        return RuntimeValue.Undefined;
    }

    private static void RenderArrayTable(TextWriter writer, SharpTSArray arr, List<string>? columnFilter)
    {
        if (arr.Length == 0)
        {
            WriteOutput(writer, "(empty array)");
            return;
        }

        // Collect all column names from objects in the array
        var allColumns = new HashSet<string> { "(index)" };
        var rows = new List<Dictionary<string, string>>();

        for (int i = 0; i < arr.Length; i++)
        {
            var row = new Dictionary<string, string> { ["(index)"] = i.ToString() };
            var element = arr[i];

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

        RenderTable(writer, columnList, rows);
    }

    private static void RenderObjectTable(TextWriter writer, SharpTSObject obj, List<string>? columnFilter)
    {
        if (obj.Fields.Count == 0)
        {
            WriteOutput(writer, "(empty object)");
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

        RenderTable(writer, columnList, rows);
    }

    private static void RenderTable(TextWriter writer, List<string> columns, List<Dictionary<string, string>> rows)
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
        writer.WriteLine(indent + separator);

        // Header row
        var header = "|" + string.Join("|", columns.Select(c => $" {c.PadRight(widths[c])} ")) + "|";
        writer.WriteLine(indent + header);
        writer.WriteLine(indent + separator);

        // Data rows
        foreach (var row in rows)
        {
            var rowStr = "|" + string.Join("|", columns.Select(c =>
            {
                var val = row.TryGetValue(c, out var v) ? v : "";
                return $" {val.PadRight(widths[c])} ";
            })) + "|";
            writer.WriteLine(indent + rowStr);
        }
        writer.WriteLine(indent + separator);
    }

    private static string TruncateColumn(string value)
    {
        const int maxWidth = 40;
        if (value.Length <= maxWidth) return value;
        return value[..(maxWidth - 3)] + "...";
    }

    private static RuntimeValue DirV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.Undefined;

        var obj = args[0].ToObject();
        // Options (depth, colors, etc.) are largely ignored for simplicity
        WriteOutput(i.Out, InspectObject(obj, 0));
        return RuntimeValue.Undefined;
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
            if (arr.Length == 0) return "[]";
            var sb = new StringBuilder("[\n");
            foreach (var elem in arr)
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

    private static RuntimeValue GroupV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0)
        {
            WriteOutput(i.Out, JoinArgs(args));
        }
        _groupIndentLevel++;
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue GroupCollapsedV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // In a terminal context, groupCollapsed behaves the same as group
        return GroupV2(interp, receiver, args);
    }

    private static RuntimeValue GroupEndV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_groupIndentLevel > 0)
        {
            _groupIndentLevel--;
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue TraceV2(Interpreter i, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var message = args.Length > 0 ? JoinArgs(args) : "";
        WriteOutput(i.Out, "Trace: " + message);

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
                WriteOutput(i.Out, $"    at {className}.{methodName} ({fileName}:{lineNumber})");
            }
            else
            {
                WriteOutput(i.Out, $"    at {className}.{methodName}");
            }
        }
        return RuntimeValue.Undefined;
    }

}
