using System.Globalization;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.DebugAdapter.Adapter;

internal sealed record DebugVariableValue(
    string Name,
    string Value,
    string Type,
    object? ExpandableValue = null,
    int? NamedVariables = null,
    int? IndexedVariables = null,
    string? EvaluateName = null);

internal static class DebugValueInspector
{
    private const int MaximumPreviewLength = 256;

    public static IReadOnlyList<DebugVariableValue> EnumerateScope(DebugScopeHandle scope)
    {
        IEnumerable<string> names = scope.Names ?? scope.Environment.Names;
        return names
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Describe(name, scope.Environment.Get(name), name))
            .ToArray();
    }

    public static IReadOnlyList<DebugVariableValue> EnumerateChildren(
        object value,
        int start,
        int? count)
    {
        start = Math.Max(0, start);
        int take = Math.Clamp(count ?? 100, 0, 1_000);
        IEnumerable<DebugVariableValue> children = value switch
        {
            SharpTSArray array => EnumerateArray(array),
            SharpTSObject obj => EnumerateObject(obj),
            SharpTSInstance instance => EnumerateInstance(instance),
            SharpTSMap map => EnumerateMap(map),
            SharpTSSet set => EnumerateSet(set),
            SharpTSError error => EnumerateError(error),
            DebugScopeHandle scope => EnumerateScope(scope),
            _ => [],
        };
        return children.Skip(start).Take(take).ToArray();
    }

    public static DebugVariableValue Describe(string name, object? raw, string? evaluateName = null)
    {
        object? value = raw is RuntimeValue runtimeValue ? runtimeValue.ToObject() : raw;
        return value switch
        {
            null => new(name, "null", "null", EvaluateName: evaluateName),
            SharpTSUndefined => new(name, "undefined", "undefined", EvaluateName: evaluateName),
            bool boolean => new(name, boolean ? "true" : "false", "boolean", EvaluateName: evaluateName),
            double number => new(name, FormatNumber(number), "number", EvaluateName: evaluateName),
            float number => new(name, FormatNumber(number), "number", EvaluateName: evaluateName),
            decimal number => new(name, number.ToString(CultureInfo.InvariantCulture), "number", EvaluateName: evaluateName),
            string text => new(name, Quote(text), "string", EvaluateName: evaluateName),
            char character => new(name, Quote(character.ToString()), "string", EvaluateName: evaluateName),
            SharpTSBigInt bigint => new(name, bigint.ToString(), "bigint", EvaluateName: evaluateName),
            SharpTSSymbol symbol => new(name, symbol.ToString(), "symbol", EvaluateName: evaluateName),
            SharpTSArray array => new(
                name, $"Array({array.Length})", "array", array,
                NamedVariables: array.NamedPropertyNames.Count(), IndexedVariables: array.Length,
                EvaluateName: evaluateName),
            SharpTSMap map => new(name, $"Map({map.Size})", "map", map,
                IndexedVariables: map.Size, EvaluateName: evaluateName),
            SharpTSSet set => new(name, $"Set({set.Size})", "set", set,
                IndexedVariables: set.Size, EvaluateName: evaluateName),
            SharpTSError error => new(name, $"{error.Name}: {Truncate(error.Message)}", error.Name, error,
                NamedVariables: ErrorChildCount(error), EvaluateName: evaluateName),
            SharpTSObject obj => new(name, "Object", "object", obj,
                NamedVariables: obj.Fields.Count + obj.AccessorPropertyNames.Count(), EvaluateName: evaluateName),
            SharpTSInstance instance => new(name, FormatInstance(instance), instance.RuntimeClass.Name, instance,
                NamedVariables: instance.GetFieldNames().Count(), EvaluateName: evaluateName),
            ISharpTSCallable callable => new(name, callable.ToString() ?? "<function>", "function", EvaluateName: evaluateName),
            DateTime date => new(name, date.ToString("O", CultureInfo.InvariantCulture), "Date", EvaluateName: evaluateName),
            _ => new(name, $"<{value.GetType().Name}>", value.GetType().Name, EvaluateName: evaluateName),
        };
    }

    private static IEnumerable<DebugVariableValue> EnumerateArray(SharpTSArray array)
    {
        for (int index = 0; index < array.Length; index++)
        {
            string name = index.ToString(CultureInfo.InvariantCulture);
            yield return array.HasIndex(index)
                ? Describe(name, array.GetRaw(index), $"[{name}]")
                : new DebugVariableValue(name, "<empty>", "undefined", EvaluateName: $"[{name}]");
        }

        foreach (string name in array.NamedPropertyNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (array.TryGetNamedAccessor(name, out _, out _))
                yield return new DebugVariableValue(name, "<accessor>", "accessor", EvaluateName: name);
            else
                yield return Describe(name, array.GetNamedProperty(name), name);
        }
    }

    private static IEnumerable<DebugVariableValue> EnumerateObject(SharpTSObject obj)
    {
        foreach ((string name, object? value) in obj.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            yield return Describe(name, value, name);
        foreach (string name in obj.AccessorPropertyNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!obj.Fields.ContainsKey(name))
                yield return new DebugVariableValue(name, "<accessor>", "accessor", EvaluateName: name);
        }
    }

    private static IEnumerable<DebugVariableValue> EnumerateInstance(SharpTSInstance instance)
    {
        foreach (string name in instance.GetFieldNames().OrderBy(name => name, StringComparer.Ordinal))
        {
            yield return instance.HasField(name)
                ? Describe(name, instance.GetRawField(name), name)
                : new DebugVariableValue(name, "<accessor>", "accessor", EvaluateName: name);
        }
    }

    private static IEnumerable<DebugVariableValue> EnumerateMap(SharpTSMap map)
    {
        int index = 0;
        foreach ((object? key, object? value) in map.InternalEntries)
        {
            var entry = new SharpTSObject(new Dictionary<string, object?>
            {
                ["key"] = key,
                ["value"] = value,
            });
            yield return Describe($"[{index++}]", entry);
        }
    }

    private static IEnumerable<DebugVariableValue> EnumerateSet(SharpTSSet set)
    {
        int index = 0;
        foreach (object? value in set)
            yield return Describe($"[{index++}]", value);
    }

    private static IEnumerable<DebugVariableValue> EnumerateError(SharpTSError error)
    {
        yield return Describe("name", error.Name, "name");
        yield return Describe("message", error.Message, "message");
        yield return Describe("stack", error.Stack, "stack");
        if (error.Code is not null) yield return Describe("code", error.Code, "code");
        if (error.Syscall is not null) yield return Describe("syscall", error.Syscall, "syscall");
        if (error.HasCause) yield return Describe("cause", error.Cause, "cause");
    }

    private static int ErrorChildCount(SharpTSError error) =>
        3 + (error.Code is null ? 0 : 1) + (error.Syscall is null ? 0 : 1) + (error.HasCause ? 1 : 0);

    private static string FormatInstance(SharpTSInstance instance)
    {
        object? message = instance.GetRawField("message");
        return message is string text
            ? $"{instance.RuntimeClass.Name}: {Truncate(text)}"
            : instance.ToString();
    }

    private static string Quote(string text) => $"\"{Truncate(text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\""))}\"";

    private static string Truncate(string text) => text.Length <= MaximumPreviewLength
        ? text
        : text[..MaximumPreviewLength] + "…";

    private static string FormatNumber(double number)
    {
        if (double.IsNaN(number)) return "NaN";
        if (double.IsPositiveInfinity(number)) return "Infinity";
        if (double.IsNegativeInfinity(number)) return "-Infinity";
        if (number == 0 && double.IsNegative(number)) return "-0";
        return number.ToString("R", CultureInfo.InvariantCulture);
    }
}
