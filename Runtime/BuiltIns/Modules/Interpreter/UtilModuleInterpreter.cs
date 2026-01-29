using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'util' module.
/// </summary>
public static class UtilModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the util module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["format"] = new BuiltInMethod("format", 0, int.MaxValue, Format),
            ["inspect"] = new BuiltInMethod("inspect", 1, 2, Inspect),
            ["isDeepStrictEqual"] = new BuiltInMethod("isDeepStrictEqual", 2, IsDeepStrictEqual),
            ["parseArgs"] = new BuiltInMethod("parseArgs", 0, 1, ParseArgs),
            ["toUSVString"] = new BuiltInMethod("toUSVString", 1, ToUSVString),
            ["stripVTControlCharacters"] = new BuiltInMethod("stripVTControlCharacters", 1, StripVTControlCharacters),
            ["getSystemErrorName"] = new BuiltInMethod("getSystemErrorName", 1, GetSystemErrorName),
            ["getSystemErrorMap"] = new BuiltInMethod("getSystemErrorMap", 0, GetSystemErrorMap),
            ["deprecate"] = new BuiltInMethod("deprecate", 2, 3, Deprecate),
            ["callbackify"] = new BuiltInMethod("callbackify", 1, Callbackify),
            ["promisify"] = new BuiltInMethod("promisify", 1, Promisify),
            ["inherits"] = new BuiltInMethod("inherits", 2, Inherits),
            ["TextEncoder"] = new BuiltInMethod("TextEncoder", 0, CreateTextEncoder),
            ["TextDecoder"] = new BuiltInMethod("TextDecoder", 0, 2, CreateTextDecoder),
            ["types"] = CreateTypesObject()
        };
    }

    private static SharpTSObject CreateTypesObject()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["isArray"] = new BuiltInMethod("isArray", 1, IsArray),
            ["isDate"] = new BuiltInMethod("isDate", 1, IsDate),
            ["isFunction"] = new BuiltInMethod("isFunction", 1, IsFunction),
            ["isNull"] = new BuiltInMethod("isNull", 1, IsNull),
            ["isUndefined"] = new BuiltInMethod("isUndefined", 1, IsUndefined),
            ["isPromise"] = new BuiltInMethod("isPromise", 1, IsPromise),
            ["isRegExp"] = new BuiltInMethod("isRegExp", 1, IsRegExp),
            ["isMap"] = new BuiltInMethod("isMap", 1, IsMap),
            ["isSet"] = new BuiltInMethod("isSet", 1, IsSet),
            ["isTypedArray"] = new BuiltInMethod("isTypedArray", 1, IsTypedArray),
            ["isNativeError"] = new BuiltInMethod("isNativeError", 1, IsNativeError),
            ["isBoxedPrimitive"] = new BuiltInMethod("isBoxedPrimitive", 1, IsBoxedPrimitive),
            ["isWeakMap"] = new BuiltInMethod("isWeakMap", 1, IsWeakMap),
            ["isWeakSet"] = new BuiltInMethod("isWeakSet", 1, IsWeakSet),
            ["isArrayBuffer"] = new BuiltInMethod("isArrayBuffer", 1, IsArrayBuffer)
        });
    }

    private static object? Format(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var format = args[0]?.ToString() ?? "";
        // Note: We can't early-return here even with 1 arg because we need to process %% escapes
        var result = new StringBuilder();
        var argIndex = 1;
        var i = 0;

        while (i < format.Length)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                var specifier = format[i + 1];
                switch (specifier)
                {
                    case 's': // String
                        result.Append(argIndex < args.Count ? args[argIndex++]?.ToString() ?? "undefined" : "%s");
                        i += 2;
                        continue;
                    case 'd': // Integer
                    case 'i':
                        if (argIndex < args.Count && args[argIndex] is double d)
                        {
                            result.Append((int)d);
                            argIndex++;
                        }
                        else
                            result.Append("%").Append(specifier);
                        i += 2;
                        continue;
                    case 'f': // Float
                        if (argIndex < args.Count && args[argIndex] is double f)
                        {
                            result.Append(f);
                            argIndex++;
                        }
                        else
                            result.Append("%f");
                        i += 2;
                        continue;
                    case 'j': // JSON
                        if (argIndex < args.Count)
                        {
                            result.Append(JsonSerializeValue(args[argIndex++]));
                        }
                        else
                            result.Append("%j");
                        i += 2;
                        continue;
                    case 'o': // Object
                    case 'O':
                        if (argIndex < args.Count)
                        {
                            result.Append(InspectValue(args[argIndex++], 2));
                        }
                        else
                            result.Append("%").Append(specifier);
                        i += 2;
                        continue;
                    case '%': // Literal %
                        result.Append('%');
                        i += 2;
                        continue;
                }
            }
            result.Append(format[i]);
            i++;
        }

        // Append remaining arguments
        while (argIndex < args.Count)
        {
            result.Append(' ');
            result.Append(args[argIndex++]?.ToString() ?? "undefined");
        }

        return result.ToString();
    }

    private static object? Inspect(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "undefined";

        int depth = 2;
        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            var depthVal = options.GetProperty("depth");
            if (depthVal is double d)
                depth = (int)d;
        }

        return InspectValue(args[0], depth);
    }

    private static string InspectValue(object? value, int depth, int currentDepth = 0)
    {
        if (value == null)
            return "null";

        if (currentDepth > depth)
            return "[Object]";

        return value switch
        {
            string s => $"'{s}'",
            double d => d.ToString(),
            bool b => b ? "true" : "false",
            SharpTSArray arr => InspectArray(arr, depth, currentDepth),
            SharpTSObject obj => InspectObject(obj, depth, currentDepth),
            SharpTSFunction func => $"[Function]",
            _ => value.ToString() ?? "undefined"
        };
    }

    private static string InspectArray(SharpTSArray arr, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Array]";

        var elements = arr.Elements.Select(e => InspectValue(e, depth, currentDepth + 1));
        return $"[ {string.Join(", ", elements)} ]";
    }

    private static string InspectObject(SharpTSObject obj, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Object]";

        var props = obj.Fields.Select(kv =>
            $"{kv.Key}: {InspectValue(kv.Value, depth, currentDepth + 1)}");
        return $"{{ {string.Join(", ", props)} }}";
    }

    private static string JsonSerializeValue(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return "undefined";
        }
    }

    private static object? IsDeepStrictEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            return false;

        return DeepStrictEqual(args[0], args[1], new HashSet<(object?, object?)>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Performs deep strict equality comparison following Node.js semantics.
    /// </summary>
    private static bool DeepStrictEqual(object? a, object? b, HashSet<(object?, object?)> seen)
    {
        // Same reference or both null
        if (ReferenceEquals(a, b))
            return true;

        // One is null, other is not
        if (a == null || b == null)
            return false;

        // Both undefined
        if (a is SharpTSUndefined && b is SharpTSUndefined)
            return true;

        // One is undefined, other is not
        if (a is SharpTSUndefined || b is SharpTSUndefined)
            return false;

        // Different types (strict equality)
        if (a.GetType() != b.GetType())
        {
            // Special case: both are numeric (double)
            if (a is double da && b is double db)
            {
                // NaN !== NaN in JavaScript, but util.isDeepStrictEqual(NaN, NaN) returns true
                if (double.IsNaN(da) && double.IsNaN(db))
                    return true;
                return da == db;
            }
            return false;
        }

        // Primitives
        if (a is string sa && b is string sb)
            return sa == sb;

        if (a is double d1 && b is double d2)
        {
            // NaN === NaN for deep strict equal
            if (double.IsNaN(d1) && double.IsNaN(d2))
                return true;
            return d1 == d2;
        }

        if (a is bool ba && b is bool bb)
            return ba == bb;

        // Circular reference detection
        var pair = (a, b);
        if (seen.Contains(pair))
            return true;
        seen.Add(pair);

        // Arrays
        if (a is SharpTSArray arrA && b is SharpTSArray arrB)
        {
            if (arrA.Elements.Count != arrB.Elements.Count)
                return false;

            for (int i = 0; i < arrA.Elements.Count; i++)
            {
                if (!DeepStrictEqual(arrA.Elements[i], arrB.Elements[i], seen))
                    return false;
            }
            return true;
        }

        // Objects
        if (a is SharpTSObject objA && b is SharpTSObject objB)
        {
            var keysA = objA.Fields.Keys.ToHashSet();
            var keysB = objB.Fields.Keys.ToHashSet();

            if (!keysA.SetEquals(keysB))
                return false;

            foreach (var key in keysA)
            {
                if (!DeepStrictEqual(objA.Fields[key], objB.Fields[key], seen))
                    return false;
            }
            return true;
        }

        // Buffers
        if (a is SharpTSBuffer bufA && b is SharpTSBuffer bufB)
        {
            return bufA.Data.SequenceEqual(bufB.Data);
        }

        // Dates
        if (a is SharpTSDate dateA && b is SharpTSDate dateB)
        {
            return dateA.GetTime() == dateB.GetTime();
        }

        // RegExp
        if (a is SharpTSRegExp regA && b is SharpTSRegExp regB)
        {
            return regA.Source == regB.Source && regA.Flags == regB.Flags;
        }

        // Maps
        if (a is SharpTSMap mapA && b is SharpTSMap mapB)
        {
            if (mapA.Size != mapB.Size)
                return false;

            foreach (var entry in mapA.InternalEntries)
            {
                if (!mapB.Has(entry.Key))
                    return false;
                if (!DeepStrictEqual(entry.Value, mapB.Get(entry.Key), seen))
                    return false;
            }
            return true;
        }

        // Sets
        if (a is SharpTSSet setA && b is SharpTSSet setB)
        {
            if (setA.Size != setB.Size)
                return false;

            // For sets, we need to check if each element in A has an equal element in B
            foreach (var elemA in setA.Values())
            {
                bool found = false;
                foreach (var elemB in setB.Values())
                {
                    if (DeepStrictEqual(elemA, elemB, new HashSet<(object?, object?)>(ReferenceEqualityComparer.Instance)))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            return true;
        }

        // Functions compare by reference only
        if (a is SharpTSFunction || a is SharpTSArrowFunction || a is BuiltInMethod)
            return ReferenceEquals(a, b);

        // Class instances
        if (a is SharpTSInstance instA && b is SharpTSInstance instB)
        {
            // Must be same class
            if (instA.RuntimeClass != instB.RuntimeClass)
                return false;

            // Compare all fields
            var keysA = instA.GetFieldNames().ToHashSet();
            var keysB = instB.GetFieldNames().ToHashSet();

            if (!keysA.SetEquals(keysB))
                return false;

            foreach (var key in keysA)
            {
                if (!DeepStrictEqual(instA.GetRawField(key), instB.GetRawField(key), seen))
                    return false;
            }
            return true;
        }

        // Default: use Object.Equals
        return Equals(a, b);
    }

    /// <summary>
    /// Comparer for reference equality in HashSet.
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<(object?, object?)>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals((object?, object?) x, (object?, object?) y)
            => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object?, object?) obj)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(obj.Item1),
                RuntimeHelpers.GetHashCode(obj.Item2));
    }

    // ===================== toUSVString Implementation =====================

    private static object? ToUSVString(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var input = args[0]?.ToString() ?? "";
        return ConvertToUSVString(input);
    }

    /// <summary>
    /// Converts a string to a well-formed Unicode string by replacing lone surrogates
    /// with the Unicode replacement character (U+FFFD).
    /// </summary>
    private static string ConvertToUSVString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new StringBuilder(input.Length);

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsHighSurrogate(c))
            {
                // Check if followed by a low surrogate
                if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    // Valid surrogate pair - keep both
                    result.Append(c);
                    result.Append(input[i + 1]);
                    i++; // Skip the low surrogate
                }
                else
                {
                    // Lone high surrogate - replace with U+FFFD
                    result.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                // Lone low surrogate (not preceded by high) - replace with U+FFFD
                result.Append('\uFFFD');
            }
            else
            {
                // Regular character
                result.Append(c);
            }
        }

        return result.ToString();
    }

    // ===================== parseArgs Implementation =====================

    private static object? ParseArgs(Interp interpreter, object? receiver, List<object?> args)
    {
        // Get config object (optional)
        var config = args.Count > 0 && args[0] is SharpTSObject configObj ? configObj : null;

        // Extract config properties
        var argsArray = GetArgsArray(config);
        var optionsDef = GetOptionsDef(config);
        var strict = GetBoolOption(config, "strict", true);
        var allowPositionals = GetBoolOption(config, "allowPositionals", !strict);
        var allowNegative = GetBoolOption(config, "allowNegative", false);
        var returnTokens = GetBoolOption(config, "tokens", false);

        // Initialize result
        var values = new Dictionary<string, object?>();
        var positionals = new List<object?>();
        var tokens = new List<object?>();

        // Apply defaults from options definitions
        foreach (var (name, optDef) in optionsDef)
        {
            if (optDef.TryGetValue("default", out var defaultVal) && defaultVal != null)
            {
                values[name] = defaultVal;
            }
        }

        // Parse arguments
        var i = 0;
        while (i < argsArray.Count)
        {
            var arg = argsArray[i]?.ToString() ?? "";

            if (arg == "--")
            {
                // Option terminator
                if (returnTokens)
                {
                    tokens.Add(new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["kind"] = "option-terminator",
                        ["index"] = (double)i
                    }));
                }
                i++;
                // Rest are positionals
                while (i < argsArray.Count)
                {
                    var positional = argsArray[i]?.ToString() ?? "";
                    if (!allowPositionals && strict)
                        throw new Exception($"Unexpected argument: {positional}");
                    positionals.Add(positional);
                    if (returnTokens)
                    {
                        tokens.Add(new SharpTSObject(new Dictionary<string, object?>
                        {
                            ["kind"] = "positional",
                            ["index"] = (double)i,
                            ["value"] = positional
                        }));
                    }
                    i++;
                }
                break;
            }
            else if (arg.StartsWith("--"))
            {
                // Long option
                i = ParseLongOption(arg, i, argsArray, optionsDef, values, tokens, strict, allowNegative, returnTokens);
            }
            else if (arg.StartsWith("-") && arg.Length > 1)
            {
                // Short option(s)
                i = ParseShortOptions(arg, i, argsArray, optionsDef, values, tokens, strict, returnTokens);
            }
            else
            {
                // Positional argument
                if (!allowPositionals && strict)
                    throw new Exception($"Unexpected argument: {arg}");
                positionals.Add(arg);
                if (returnTokens)
                {
                    tokens.Add(new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["kind"] = "positional",
                        ["index"] = (double)i,
                        ["value"] = arg
                    }));
                }
                i++;
            }
        }

        // Build result object
        var result = new Dictionary<string, object?>
        {
            ["values"] = new SharpTSObject(values),
            ["positionals"] = new SharpTSArray(positionals)
        };

        if (returnTokens)
        {
            result["tokens"] = new SharpTSArray(tokens);
        }

        return new SharpTSObject(result);
    }

    private static List<object?> GetArgsArray(SharpTSObject? config)
    {
        if (config != null)
        {
            var argsVal = config.GetProperty("args");
            if (argsVal is SharpTSArray arr)
                return arr.Elements;
        }

        // Default to empty list - process.argv access would require runtime environment lookup
        // In practice, users should always provide args explicitly
        return new List<object?>();
    }

    private static Dictionary<string, Dictionary<string, object?>> GetOptionsDef(SharpTSObject? config)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>();

        if (config == null)
            return result;

        var optionsVal = config.GetProperty("options");
        if (optionsVal is not SharpTSObject options)
            return result;

        foreach (var (name, value) in options.Fields)
        {
            if (value is SharpTSObject optDef)
            {
                result[name] = new Dictionary<string, object?>();
                foreach (var (key, val) in optDef.Fields)
                {
                    result[name][key] = val;
                }
            }
        }

        return result;
    }

    private static bool GetBoolOption(SharpTSObject? config, string name, bool defaultValue)
    {
        if (config == null)
            return defaultValue;

        var val = config.GetProperty(name);
        if (val is bool b)
            return b;

        return defaultValue;
    }

    private static int ParseLongOption(
        string arg,
        int index,
        List<object?> argsArray,
        Dictionary<string, Dictionary<string, object?>> optionsDef,
        Dictionary<string, object?> values,
        List<object?> tokens,
        bool strict,
        bool allowNegative,
        bool returnTokens)
    {
        var rawName = arg;
        string name;
        string? inlineValue = null;
        var hasInlineValue = false;

        // Check for inline value (--option=value)
        var eqIndex = arg.IndexOf('=');
        if (eqIndex > 0)
        {
            name = arg[2..eqIndex];
            inlineValue = arg[(eqIndex + 1)..];
            hasInlineValue = true;
        }
        else
        {
            name = arg[2..];
        }

        // Check for negation (--no-option)
        var isNegated = false;
        var originalName = name;
        if (allowNegative && name.StartsWith("no-"))
        {
            var positiveName = name[3..];
            if (optionsDef.TryGetValue(positiveName, out var posDef) &&
                posDef.TryGetValue("type", out var typeVal) &&
                typeVal?.ToString() == "boolean")
            {
                name = positiveName;
                isNegated = true;
            }
        }

        // Look up option definition
        if (!optionsDef.TryGetValue(name, out var optDef))
        {
            if (strict)
                throw new Exception($"Unknown option '--{originalName}'");

            // In non-strict mode, treat as boolean
            values[name] = !isNegated;
            index++;
            return index;
        }

        var optType = optDef.TryGetValue("type", out var t) ? t?.ToString() : "boolean";
        var multiple = optDef.TryGetValue("multiple", out var m) && m is true;

        object? value;

        if (optType == "boolean")
        {
            if (hasInlineValue)
            {
                if (strict)
                    throw new Exception($"Option '--{name}' does not take an argument");
                value = !isNegated;
            }
            else
            {
                value = !isNegated;
            }
            index++;
        }
        else // string
        {
            if (isNegated && strict)
                throw new Exception($"Option '--{name}' cannot be negated");

            if (hasInlineValue)
            {
                value = inlineValue;
                index++;
            }
            else if (index + 1 < argsArray.Count)
            {
                value = argsArray[index + 1]?.ToString() ?? "";
                index += 2;
            }
            else
            {
                if (strict)
                    throw new Exception($"Option '--{name}' requires an argument");
                value = "";
                index++;
            }
        }

        // Store value
        if (multiple)
        {
            if (!values.TryGetValue(name, out var existing) || existing is not SharpTSArray existingArr)
            {
                existingArr = new SharpTSArray(new List<object?>());
                values[name] = existingArr;
            }
            existingArr.Elements.Add(value);
        }
        else
        {
            values[name] = value;
        }

        // Add token
        if (returnTokens)
        {
            var tokenObj = new Dictionary<string, object?>
            {
                ["kind"] = "option",
                ["index"] = (double)(index - (optType == "string" && !hasInlineValue ? 2 : 1)),
                ["name"] = name,
                ["rawName"] = rawName.Split('=')[0],
                ["value"] = optType == "string" ? value : null,
                ["inlineValue"] = hasInlineValue
            };
            tokens.Add(new SharpTSObject(tokenObj));
        }

        return index;
    }

    private static int ParseShortOptions(
        string arg,
        int index,
        List<object?> argsArray,
        Dictionary<string, Dictionary<string, object?>> optionsDef,
        Dictionary<string, object?> values,
        List<object?> tokens,
        bool strict,
        bool returnTokens)
    {
        var shortOpts = arg[1..];

        for (var j = 0; j < shortOpts.Length; j++)
        {
            var shortChar = shortOpts[j].ToString();
            string? optName = null;
            Dictionary<string, object?>? optDef = null;

            // Find option by short alias
            foreach (var (name, def) in optionsDef)
            {
                if (def.TryGetValue("short", out var shortVal) && shortVal?.ToString() == shortChar)
                {
                    optName = name;
                    optDef = def;
                    break;
                }
            }

            if (optName == null || optDef == null)
            {
                if (strict)
                    throw new Exception($"Unknown option '-{shortChar}'");
                continue;
            }

            var optType = optDef.TryGetValue("type", out var t) ? t?.ToString() : "boolean";
            var multiple = optDef.TryGetValue("multiple", out var m) && m is true;

            object? value;

            if (optType == "boolean")
            {
                value = true;
            }
            else // string
            {
                // Check if remaining chars are the value
                if (j + 1 < shortOpts.Length)
                {
                    value = shortOpts[(j + 1)..];
                    j = shortOpts.Length; // Exit loop
                }
                else if (index + 1 < argsArray.Count)
                {
                    value = argsArray[index + 1]?.ToString() ?? "";
                    index++;
                }
                else
                {
                    if (strict)
                        throw new Exception($"Option '-{shortChar}' requires an argument");
                    value = "";
                }
            }

            // Store value
            if (multiple)
            {
                if (!values.TryGetValue(optName, out var existing) || existing is not SharpTSArray existingArr)
                {
                    existingArr = new SharpTSArray(new List<object?>());
                    values[optName] = existingArr;
                }
                existingArr.Elements.Add(value);
            }
            else
            {
                values[optName] = value;
            }

            // Add token
            if (returnTokens)
            {
                var tokenObj = new Dictionary<string, object?>
                {
                    ["kind"] = "option",
                    ["index"] = (double)index,
                    ["name"] = optName,
                    ["rawName"] = $"-{shortChar}",
                    ["value"] = optType == "string" ? value : null,
                    ["inlineValue"] = j + 1 < shortOpts.Length && optType == "string"
                };
                tokens.Add(new SharpTSObject(tokenObj));
            }
        }

        return index + 1;
    }

    // Type checking functions
    private static object? IsArray(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSArray;

    private static object? IsDate(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSDate;

    private static object? IsFunction(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && (args[0] is SharpTSFunction or SharpTSArrowFunction or BuiltInMethod);

    private static object? IsNull(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] == null;

    private static object? IsUndefined(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSUndefined;

    private static object? IsPromise(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSPromise;

    private static object? IsRegExp(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSRegExp;

    private static object? IsMap(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSMap;

    private static object? IsSet(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSSet;

    private static object? IsTypedArray(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSBuffer;

    private static object? IsNativeError(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSError;

    private static object? IsBoxedPrimitive(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0) return false;
        var value = args[0];
        // In JavaScript, boxed primitives are objects created with new String(), new Number(), new Boolean()
        // In SharpTS, we don't have explicit boxed primitive types, so this is always false
        // unless we encounter specific wrapper types in the future
        return false;
    }

    private static object? IsWeakMap(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSWeakMap;

    private static object? IsWeakSet(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSWeakSet;

    private static object? IsArrayBuffer(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0) return false;
        // ArrayBuffer is essentially the backing store for typed arrays
        // In SharpTS, we don't have a separate ArrayBuffer type, but Buffer has a backing array
        return args[0] is SharpTSBuffer;
    }

    // ===================== stripVTControlCharacters Implementation =====================

    /// <summary>
    /// Regex pattern to match ANSI escape sequences.
    /// Matches:
    /// - CSI sequences: ESC [ ... letter
    /// - OSC sequences: ESC ] ... BEL or ESC ] ... ESC \
    /// - Other escape sequences: ESC P, ESC X, ESC ^, ESC _ followed by content and ST
    /// </summary>
    private static readonly Regex _ansiEscapeRegex = new(
        @"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[PX^_][^\x1b]*\x1b\\|\x1b\[[0-9;]*m",
        RegexOptions.Compiled);

    private static object? StripVTControlCharacters(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var input = args[0]?.ToString() ?? "";
        return _ansiEscapeRegex.Replace(input, "");
    }

    // ===================== getSystemErrorName and getSystemErrorMap Implementation =====================

    /// <summary>
    /// Mapping of POSIX error codes to their names.
    /// Based on common Unix/Linux errno values (libuv uses these conventions).
    /// </summary>
    private static readonly Dictionary<int, string> _posixErrorNames = new()
    {
        [-1] = "EPERM",        // Operation not permitted
        [-2] = "ENOENT",       // No such file or directory
        [-3] = "ESRCH",        // No such process
        [-4] = "EINTR",        // Interrupted system call
        [-5] = "EIO",          // I/O error
        [-6] = "ENXIO",        // No such device or address
        [-7] = "E2BIG",        // Argument list too long
        [-8] = "ENOEXEC",      // Exec format error
        [-9] = "EBADF",        // Bad file number
        [-10] = "ECHILD",      // No child processes
        [-11] = "EAGAIN",      // Try again (same as EWOULDBLOCK)
        [-12] = "ENOMEM",      // Out of memory
        [-13] = "EACCES",      // Permission denied
        [-14] = "EFAULT",      // Bad address
        [-16] = "EBUSY",       // Device or resource busy
        [-17] = "EEXIST",      // File exists
        [-18] = "EXDEV",       // Cross-device link
        [-19] = "ENODEV",      // No such device
        [-20] = "ENOTDIR",     // Not a directory
        [-21] = "EISDIR",      // Is a directory
        [-22] = "EINVAL",      // Invalid argument
        [-23] = "ENFILE",      // File table overflow
        [-24] = "EMFILE",      // Too many open files
        [-25] = "ENOTTY",      // Not a typewriter
        [-26] = "ETXTBSY",     // Text file busy
        [-27] = "EFBIG",       // File too large
        [-28] = "ENOSPC",      // No space left on device
        [-29] = "ESPIPE",      // Illegal seek
        [-30] = "EROFS",       // Read-only file system
        [-31] = "EMLINK",      // Too many links
        [-32] = "EPIPE",       // Broken pipe
        [-33] = "EDOM",        // Math argument out of domain
        [-34] = "ERANGE",      // Math result not representable
        [-35] = "EDEADLK",     // Resource deadlock would occur
        [-36] = "ENAMETOOLONG", // File name too long
        [-37] = "ENOLCK",      // No record locks available
        [-38] = "ENOSYS",      // Function not implemented
        [-39] = "ENOTEMPTY",   // Directory not empty
        [-40] = "ELOOP",       // Too many symbolic links
        [-42] = "ENOMSG",      // No message of desired type
        [-43] = "EIDRM",       // Identifier removed
        [-60] = "ENOSTR",      // Device not a stream
        [-61] = "ENODATA",     // No data available
        [-62] = "ETIME",       // Timer expired
        [-63] = "ENOSR",       // Out of streams resources
        [-71] = "EPROTO",      // Protocol error
        [-74] = "EBADMSG",     // Bad message
        [-75] = "EOVERFLOW",   // Value too large for data type
        [-88] = "ENOTSOCK",    // Socket operation on non-socket
        [-89] = "EDESTADDRREQ", // Destination address required
        [-90] = "EMSGSIZE",    // Message too long
        [-91] = "EPROTOTYPE",  // Protocol wrong type for socket
        [-92] = "ENOPROTOOPT", // Protocol not available
        [-93] = "EPROTONOSUPPORT", // Protocol not supported
        [-95] = "EOPNOTSUPP",  // Operation not supported
        [-97] = "EAFNOSUPPORT", // Address family not supported
        [-98] = "EADDRINUSE",  // Address already in use
        [-99] = "EADDRNOTAVAIL", // Cannot assign requested address
        [-100] = "ENETDOWN",   // Network is down
        [-101] = "ENETUNREACH", // Network is unreachable
        [-102] = "ENETRESET",  // Network dropped connection on reset
        [-103] = "ECONNABORTED", // Software caused connection abort
        [-104] = "ECONNRESET", // Connection reset by peer
        [-105] = "ENOBUFS",    // No buffer space available
        [-106] = "EISCONN",    // Transport endpoint is already connected
        [-107] = "ENOTCONN",   // Transport endpoint is not connected
        [-110] = "ETIMEDOUT",  // Connection timed out
        [-111] = "ECONNREFUSED", // Connection refused
        [-112] = "EHOSTDOWN",  // Host is down
        [-113] = "EHOSTUNREACH", // No route to host
        [-114] = "EALREADY",   // Operation already in progress
        [-115] = "EINPROGRESS", // Operation now in progress
        [-116] = "ESTALE",     // Stale file handle
        [-122] = "EDQUOT",     // Disk quota exceeded
        [-125] = "ECANCELED",  // Operation canceled
    };

    /// <summary>
    /// Mapping of error names to their descriptions.
    /// </summary>
    private static readonly Dictionary<string, string> _errorDescriptions = new()
    {
        ["EPERM"] = "operation not permitted",
        ["ENOENT"] = "no such file or directory",
        ["ESRCH"] = "no such process",
        ["EINTR"] = "interrupted system call",
        ["EIO"] = "i/o error",
        ["ENXIO"] = "no such device or address",
        ["E2BIG"] = "argument list too long",
        ["ENOEXEC"] = "exec format error",
        ["EBADF"] = "bad file descriptor",
        ["ECHILD"] = "no child processes",
        ["EAGAIN"] = "resource temporarily unavailable",
        ["ENOMEM"] = "not enough memory",
        ["EACCES"] = "permission denied",
        ["EFAULT"] = "bad address",
        ["EBUSY"] = "resource busy or locked",
        ["EEXIST"] = "file already exists",
        ["EXDEV"] = "cross-device link not permitted",
        ["ENODEV"] = "no such device",
        ["ENOTDIR"] = "not a directory",
        ["EISDIR"] = "illegal operation on a directory",
        ["EINVAL"] = "invalid argument",
        ["ENFILE"] = "file table overflow",
        ["EMFILE"] = "too many open files",
        ["ENOTTY"] = "inappropriate ioctl for device",
        ["ETXTBSY"] = "text file is busy",
        ["EFBIG"] = "file too large",
        ["ENOSPC"] = "no space left on device",
        ["ESPIPE"] = "invalid seek",
        ["EROFS"] = "read-only file system",
        ["EMLINK"] = "too many links",
        ["EPIPE"] = "broken pipe",
        ["EDOM"] = "argument out of domain",
        ["ERANGE"] = "result too large",
        ["EDEADLK"] = "resource deadlock avoided",
        ["ENAMETOOLONG"] = "name too long",
        ["ENOLCK"] = "no locks available",
        ["ENOSYS"] = "function not implemented",
        ["ENOTEMPTY"] = "directory not empty",
        ["ELOOP"] = "too many symbolic links encountered",
        ["ENOMSG"] = "no message of desired type",
        ["EIDRM"] = "identifier removed",
        ["ENOSTR"] = "device not a stream",
        ["ENODATA"] = "no data available",
        ["ETIME"] = "timer expired",
        ["ENOSR"] = "out of streams resources",
        ["EPROTO"] = "protocol error",
        ["EBADMSG"] = "bad message",
        ["EOVERFLOW"] = "value too large for defined data type",
        ["ENOTSOCK"] = "socket operation on non-socket",
        ["EDESTADDRREQ"] = "destination address required",
        ["EMSGSIZE"] = "message too long",
        ["EPROTOTYPE"] = "protocol wrong type for socket",
        ["ENOPROTOOPT"] = "protocol not available",
        ["EPROTONOSUPPORT"] = "protocol not supported",
        ["EOPNOTSUPP"] = "operation not supported on socket",
        ["EAFNOSUPPORT"] = "address family not supported",
        ["EADDRINUSE"] = "address already in use",
        ["EADDRNOTAVAIL"] = "address not available",
        ["ENETDOWN"] = "network is down",
        ["ENETUNREACH"] = "network is unreachable",
        ["ENETRESET"] = "connection reset by network",
        ["ECONNABORTED"] = "connection aborted",
        ["ECONNRESET"] = "connection reset by peer",
        ["ENOBUFS"] = "no buffer space available",
        ["EISCONN"] = "socket is connected",
        ["ENOTCONN"] = "socket is not connected",
        ["ETIMEDOUT"] = "connection timed out",
        ["ECONNREFUSED"] = "connection refused",
        ["EHOSTDOWN"] = "host is down",
        ["EHOSTUNREACH"] = "host is unreachable",
        ["EALREADY"] = "connection already in progress",
        ["EINPROGRESS"] = "operation in progress",
        ["ESTALE"] = "stale file handle",
        ["EDQUOT"] = "disk quota exceeded",
        ["ECANCELED"] = "operation canceled",
    };

    private static object? GetSystemErrorName(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("getSystemErrorName requires an error code argument");

        var errno = args[0];
        if (errno is not double d)
            throw new Exception("The value of \"err\" is out of range");

        var errorCode = (int)d;
        if (_posixErrorNames.TryGetValue(errorCode, out var name))
            return name;

        // For unknown error codes, return generic name
        return $"Unknown system error {errorCode}";
    }

    private static object? GetSystemErrorMap(Interp interpreter, object? receiver, List<object?> args)
    {
        // Return a Map containing [errorCode] -> [name, description] entries
        var map = new SharpTSMap();
        foreach (var (code, name) in _posixErrorNames)
        {
            var description = _errorDescriptions.TryGetValue(name, out var desc) ? desc : "";
            var entry = new SharpTSArray(new List<object?> { name, description });
            map.Set((double)code, entry);
        }
        return map;
    }

    private static object? Deprecate(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("util.deprecate requires at least 2 arguments: fn, message");

        var fn = args[0];
        var message = args[1]?.ToString() ?? "";

        if (fn is ISharpTSCallable callable)
        {
            return new SharpTSDeprecatedFunction(callable, message);
        }

        if (fn is BuiltInMethod method)
        {
            return new SharpTSDeprecatedFunction(method, message);
        }

        throw new Exception("util.deprecate: first argument must be a function");
    }

    private static object? Callbackify(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("util.callbackify requires 1 argument: fn");

        var fn = args[0];

        if (fn is ISharpTSCallable callable)
        {
            return new SharpTSCallbackifiedFunction(callable);
        }

        if (fn is BuiltInMethod method)
        {
            return new SharpTSCallbackifiedFunction(method);
        }

        throw new Exception("util.callbackify: argument must be a function");
    }

    private static object? Promisify(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("util.promisify requires 1 argument: fn");

        var fn = args[0];

        if (fn is ISharpTSCallable callable)
        {
            return new SharpTSPromisifiedFunction(callable);
        }

        if (fn is BuiltInMethod method)
        {
            return new SharpTSPromisifiedFunction(method);
        }

        throw new Exception("util.promisify: argument must be a function");
    }

    private static object? Inherits(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("util.inherits requires 2 arguments: constructor, superConstructor");

        var ctor = args[0];
        var superCtor = args[1];

        // Set constructor.super_ = superConstructor
        if (ctor is SharpTSClass ctorClass)
        {
            ctorClass.SetStaticProperty("super_", superCtor);
        }
        else if (ctor is SharpTSFunction ctorFunc)
        {
            // For plain functions, we can't easily add properties
            // This is a limitation - real Node.js modifies the prototype chain
        }

        return null;
    }

    private static object? CreateTextEncoder(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSTextEncoder();
    }

    private static object? CreateTextDecoder(Interp interpreter, object? receiver, List<object?> args)
    {
        var encoding = "utf-8";
        var fatal = false;
        var ignoreBOM = false;

        if (args.Count > 0 && args[0] != null)
        {
            encoding = args[0]!.ToString() ?? "utf-8";
        }

        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            var fatalVal = options.GetProperty("fatal");
            if (fatalVal is true)
                fatal = true;

            var ignoreBOMVal = options.GetProperty("ignoreBOM");
            if (ignoreBOMVal is true)
                ignoreBOM = true;
        }

        return new SharpTSTextDecoder(encoding, fatal, ignoreBOM);
    }
}
