using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class StringBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<string> _lookup =
        BuiltInTypeBuilder<string>.ForInstanceType()
            .Property("length", s => (double)s.Length)
            .MethodV2("[Symbol.iterator]", 0, static (_, value, _) =>
                RuntimeValue.FromObject(new SharpTSIterator(
                    value.EnumerateRunes().Select(r => (object?)r.ToString()))))
            .MethodV2("charAt", 0, int.MaxValue, specLength: 1, CharAtV2)
            // Spec lengths (ECMA-262 §22.1.3) are metadata, independent of
            // runtime argument acceptance: JavaScript methods coerce omitted
            // values and ignore extras. concat is variadic with spec length 1.
            .MethodV2("substring", 0, int.MaxValue, specLength: 2, SubstringV2)
            .MethodV2("indexOf", 0, int.MaxValue, specLength: 1, IndexOfV2)
            .MethodV2("toUpperCase", 0, ToUpperCaseV2)
            .MethodV2("toLowerCase", 0, ToLowerCaseV2)
            .MethodV2("toLocaleUpperCase", 0, ToUpperCaseV2)
            .MethodV2("toLocaleLowerCase", 0, ToLowerCaseV2)
            .MethodV2("trim", 0, TrimV2)
            .MethodV2("replace", 0, int.MaxValue, specLength: 2, ReplaceV2)
            .MethodV2("split", 0, int.MaxValue, specLength: 2, SplitV2)
            .MethodV2("match", 0, int.MaxValue, specLength: 1, MatchV2)
            .MethodV2("matchAll", 1, MatchAllV2)
            .MethodV2("search", 0, int.MaxValue, specLength: 1, SearchV2)
            .MethodV2("includes", 0, int.MaxValue, specLength: 1, IncludesV2)
            .MethodV2("startsWith", 0, int.MaxValue, specLength: 1, StartsWithV2)
            .MethodV2("endsWith", 0, int.MaxValue, specLength: 1, EndsWithV2)
            .MethodV2("slice", 0, int.MaxValue, specLength: 2, SliceV2)
            .MethodV2("substr", 0, int.MaxValue, specLength: 2, SubstrV2)
            .MethodV2("repeat", 0, int.MaxValue, specLength: 1, RepeatV2)
            .MethodV2("padStart", 0, int.MaxValue, specLength: 1, PadStartV2)
            .MethodV2("padEnd", 0, int.MaxValue, specLength: 1, PadEndV2)
            .MethodV2("charCodeAt", 0, int.MaxValue, specLength: 1, CharCodeAtV2)
            .MethodV2("codePointAt", 0, int.MaxValue, specLength: 1, CodePointAtV2)
            .MethodV2("concat", 0, int.MaxValue, specLength: 1, ConcatV2)
            .MethodV2("lastIndexOf", 0, int.MaxValue, specLength: 1, LastIndexOfV2)
            .MethodV2("trimStart", 0, TrimStartV2)
            .MethodV2("trimEnd", 0, TrimEndV2)
            .MethodV2("replaceAll", 2, ReplaceAllV2)
            .MethodV2("at", 0, int.MaxValue, specLength: 1, AtV2)
            .MethodV2("normalize", 0, int.MaxValue, specLength: 0, NormalizeV2)
            .MethodV2("localeCompare", 0, int.MaxValue, specLength: 1, LocaleCompareV2)
            .MethodV2("isWellFormed", 0, IsWellFormedV2)
            .MethodV2("toWellFormed", 0, ToWellFormedV2)
            // ECMA-262 §22.1.3.28/.31: String.prototype.toString and valueOf both
            // return thisStringValue. Needed so `(new String("x")).toString()` and
            // ToPrimitive(string-wrapper) unwrap to the primitive instead of
            // resolving Object.prototype.toString ("[object Object]").
            .MethodV2("toString", 0, (Interpreter _, string s, ReadOnlySpan<RuntimeValue> _)
                => RuntimeValue.FromString(s))
            .MethodV2("valueOf", 0, (Interpreter _, string s, ReadOnlySpan<RuntimeValue> _)
                => RuntimeValue.FromString(s))
            .Build();

    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("raw", 1, int.MaxValue, StringRawV2)
            .MethodV2("fromCharCode", 0, int.MaxValue, specLength: 1, FromCharCodeV2)
            .MethodV2("fromCodePoint", 0, int.MaxValue, specLength: 1, FromCodePointV2)
            .Build();

    public static object? GetMember(string receiver, string name)
        => _lookup.GetMember(receiver, name);

    /// <summary>
    /// Gets a static member (method) from the String namespace.
    /// Currently only supports String.raw for tagged templates.
    /// </summary>
    public static object? GetStaticMember(string name)
        => _staticLookup.GetMember(name);

    /// <summary>Static member names for REPL autocomplete.</summary>
    public static IEnumerable<string> StaticMemberNames => _staticLookup.MemberNames;

    /// <summary>
    /// Returns the unbound <see cref="BuiltInMethod"/> for a
    /// String.prototype.* method, or null if no such method exists. Used by
    /// <see cref="Types.SharpTSStringPrototype"/> so
    /// <c>String.prototype.trim.call(value)</c> resolves to the same
    /// implementation as <c>"...".trim()</c>.
    /// </summary>
    public static BuiltInMethod? GetPrototypeMethod(string name)
        => _lookup.GetMethod(name);

    internal static bool TryInvokeCustomReplace(
        Interpreter interpreter,
        object receiver,
        List<object?> arguments,
        bool requireGlobalRegExp,
        out object? result)
    {
        object? searchValue = arguments.Count > 0
            ? arguments[0]
            : SharpTSUndefined.Instance;
        if (searchValue is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }

        if (requireGlobalRegExp
            && searchValue is SharpTSRegExp regex
            && !regex.Global)
        {
            throw new ThrowException(new SharpTSTypeError(
                "String.prototype.replaceAll called with a non-global RegExp argument"));
        }

        object? replaceMethod = interpreter.GetSymbolPropertyValue(
            searchValue, SharpTSSymbol.Replace);

        if (replaceMethod is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }
        if (replaceMethod is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.replace property is not callable"));
        }

        object? replaceValue = arguments.Count > 1
            ? arguments[1]
            : SharpTSUndefined.Instance;
        result = FunctionBuiltIns.CallWithThis(
            interpreter, callable, searchValue, [receiver, replaceValue]);
        return true;
    }

    internal static bool TryInvokeCustomMatch(
        Interpreter interpreter,
        object receiver,
        List<object?> arguments,
        out object? result)
    {
        object? pattern = arguments.Count > 0
            ? arguments[0]
            : SharpTSUndefined.Instance;
        if (pattern is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }

        object? matcher = interpreter.GetSymbolPropertyValue(
            pattern, SharpTSSymbol.Match);
        if (matcher is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }
        if (matcher is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.match property is not callable"));
        }

        result = FunctionBuiltIns.CallWithThis(
            interpreter, callable, pattern, [receiver]);
        return true;
    }

    internal static bool TryInvokeCustomSearch(
        Interpreter interpreter,
        object receiver,
        List<object?> arguments,
        out object? result)
    {
        object? pattern = arguments.Count > 0
            ? arguments[0]
            : SharpTSUndefined.Instance;
        if (pattern is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }

        object? searcher = interpreter.GetSymbolPropertyValue(
            pattern, SharpTSSymbol.Search);
        if (searcher is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }
        if (searcher is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.search property is not callable"));
        }

        result = FunctionBuiltIns.CallWithThis(
            interpreter, callable, pattern, [receiver]);
        return true;
    }

    internal static bool TryInvokeCustomSplit(
        Interpreter interpreter,
        object receiver,
        List<object?> arguments,
        out object? result)
    {
        object? separator = arguments.Count > 0
            ? arguments[0]
            : SharpTSUndefined.Instance;
        if (separator is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }

        object? splitter = interpreter.GetSymbolPropertyValue(
            separator, SharpTSSymbol.Split);
        if (separator is SharpTSRegExp && splitter is BuiltInMethod)
        {
            result = null;
            return false;
        }
        if (splitter is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }
        if (splitter is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.split property is not callable"));
        }

        object? limit = arguments.Count > 1
            ? arguments[1]
            : SharpTSUndefined.Instance;
        result = FunctionBuiltIns.CallWithThis(
            interpreter, callable, separator, [receiver, limit]);
        return true;
    }

    internal static bool TryInvokeCustomMatchAll(
        Interpreter interpreter,
        object receiver,
        List<object?> arguments,
        out object? result)
    {
        object? pattern = arguments.Count > 0
            ? arguments[0]
            : SharpTSUndefined.Instance;
        if (pattern is null or SharpTSUndefined or SharpTSRegExp)
        {
            result = null;
            return false;
        }

        object? matcher = interpreter.GetSymbolPropertyValue(
            pattern, SharpTSSymbol.MatchAll);
        if (matcher is null or SharpTSUndefined)
        {
            result = null;
            return false;
        }
        if (matcher is not ISharpTSCallable callable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Symbol.matchAll property is not callable"));
        }

        result = FunctionBuiltIns.CallWithThis(
            interpreter, callable, pattern, [receiver]);
        return true;
    }

    private static RuntimeValue IsWellFormedV2(
        Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        for (int i = 0; i < str.Length; i++)
        {
            char codeUnit = str[i];
            if (char.IsHighSurrogate(codeUnit))
            {
                if (i + 1 >= str.Length || !char.IsLowSurrogate(str[i + 1]))
                    return RuntimeValue.False;
                i++;
            }
            else if (char.IsLowSurrogate(codeUnit))
            {
                return RuntimeValue.False;
            }
        }

        return RuntimeValue.True;
    }

    private static RuntimeValue ToWellFormedV2(
        Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        StringBuilder? result = null;
        for (int i = 0; i < str.Length; i++)
        {
            char codeUnit = str[i];
            bool isUnpaired = char.IsHighSurrogate(codeUnit)
                ? i + 1 >= str.Length || !char.IsLowSurrogate(str[i + 1])
                : char.IsLowSurrogate(codeUnit)
                    && (i == 0 || !char.IsHighSurrogate(str[i - 1]));

            if (isUnpaired)
            {
                result ??= new StringBuilder(str, 0, i, str.Length);
                result.Append('\uFFFD');
            }
            else if (result is not null)
            {
                result.Append(codeUnit);
            }
        }

        return RuntimeValue.FromString(result?.ToString() ?? str);
    }

    private static RuntimeValue ReplaceV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? searchValue = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        object? replaceValue = args.Length > 1
            ? args[1].ToObject()
            : SharpTSUndefined.Instance;

        if (searchValue is not (null or SharpTSUndefined))
        {
            object? replaceMethod = interpreter.GetSymbolPropertyValue(
                searchValue, SharpTSSymbol.Replace);
            if (replaceMethod is ISharpTSCallable customReplace)
            {
                object? customResult = FunctionBuiltIns.CallWithThis(
                    interpreter, customReplace, searchValue, [str, replaceValue]);
                return RuntimeValue.FromBoxed(customResult);
            }
            if (replaceMethod is not (null or SharpTSUndefined))
            {
                throw new ThrowException(new SharpTSTypeError(
                    "Symbol.replace property is not callable"));
            }
        }

        if (searchValue is SharpTSRegExp regex)
        {
            if (replaceValue is ISharpTSCallable regexReplacer)
            {
                return RuntimeValue.FromString(regex.Replace(str, match =>
                {
                    var callbackArgs = new List<object?>(match.Groups.Count + 2)
                    {
                        match.Value
                    };
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        var group = match.Groups[i];
                        callbackArgs.Add(group.Success
                            ? group.Value
                            : SharpTSUndefined.Instance);
                    }
                    callbackArgs.Add((double)match.Index);
                    callbackArgs.Add(str);

                    object? result = FunctionBuiltIns.CallWithThis(
                        interpreter, regexReplacer, SharpTSUndefined.Instance,
                        callbackArgs);
                    return interpreter.ToStringForBuiltInArgument(result);
                }));
            }

            var regexReplacement = interpreter.ToStringForBuiltInArgument(replaceValue);
            return RuntimeValue.FromString(regex.Replace(str, regexReplacement));
        }

        var search = interpreter.ToStringForBuiltInArgument(searchValue);
        bool functionalReplace = replaceValue is ISharpTSCallable;
        string? replacementText = functionalReplace
            ? null
            : interpreter.ToStringForBuiltInArgument(replaceValue);
        var index = str.IndexOf(search);
        if (index < 0) return RuntimeValue.FromString(str);

        string replacement;
        if (replaceValue is ISharpTSCallable replacer)
        {
            object? result = FunctionBuiltIns.CallWithThis(
                interpreter, replacer, SharpTSUndefined.Instance,
                [search, (double)index, str]);
            replacement = interpreter.ToStringForBuiltInArgument(result);
        }
        else
        {
            replacement = ExpandStringReplacement(
                replacementText!, str, search, index);
        }

        return RuntimeValue.FromString(
            str[..index] + replacement + str[(index + search.Length)..]);
    }

    private static string ExpandStringReplacement(
        string replacement, string input, string matched, int index)
    {
        var result = new StringBuilder(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                result.Append(replacement[i]);
                continue;
            }

            switch (replacement[i + 1])
            {
                case '$': result.Append('$'); i++; break;
                case '&': result.Append(matched); i++; break;
                case '`': result.Append(input.AsSpan(0, index)); i++; break;
                case '\'': result.Append(input.AsSpan(index + matched.Length)); i++; break;
                default: result.Append('$'); break;
            }
        }
        return result.ToString();
    }

    private static RuntimeValue SplitV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? separatorValue = ArgumentOrUndefined(args, 0);
        if (separatorValue is not (null or SharpTSUndefined))
        {
            object? splitter = interpreter.GetSymbolPropertyValue(
                separatorValue, SharpTSSymbol.Split);
            if (splitter is not (null or SharpTSUndefined)
                && !(separatorValue is SharpTSRegExp && splitter is BuiltInMethod))
            {
                if (splitter is not ISharpTSCallable callable)
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Symbol.split property is not callable"));
                }

                object? limitValue = ArgumentOrUndefined(args, 1);
                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, separatorValue, [str, limitValue]));
            }
        }

        uint limit = args.Length < 2 || args[1].IsUndefined
            ? uint.MaxValue
            : ToUint32(interpreter.ToNumberWithPrimitive(args[1].ToObject()));

        if (separatorValue is SharpTSUndefined)
        {
            return RuntimeValue.FromObject(limit == 0
                ? new SharpTSArray()
                : new SharpTSArray([(object?)str]));
        }

        if (separatorValue is SharpTSRegExp regex)
        {
            if (limit == 0)
                return RuntimeValue.FromObject(new SharpTSArray());

            string[] parts = regex.Split(str);
            IEnumerable<string> resultParts = limit < parts.Length
                ? parts.Take((int)limit)
                : parts;
            return RuntimeValue.FromObject(new SharpTSArray(resultParts.Select(p => (object?)p).ToList()));
        }

        var separator = interpreter.ToStringForBuiltInArgument(separatorValue);
        if (limit == 0)
            return RuntimeValue.FromObject(new SharpTSArray());

        string[] stringParts;
        if (separator == "")
        {
            stringParts = str.Select(c => c.ToString()).ToArray();
        }
        else
        {
            stringParts = str.Split(separator);
        }

        if (limit < stringParts.Length)
        {
            stringParts = stringParts.Take((int)limit).ToArray();
        }

        var elements = stringParts.Select(p => (object?)p).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(elements));
    }

    private static uint ToUint32(double number)
    {
        if (!double.IsFinite(number) || number == 0)
            return 0;

        const double modulus = 4294967296d;
        double integer = Math.Truncate(number);
        double modulo = integer % modulus;
        if (modulo < 0)
            modulo += modulus;
        return (uint)modulo;
    }

    private static RuntimeValue MatchV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? pattern = ArgumentOrUndefined(args, 0);
        if (pattern is not (null or SharpTSUndefined))
        {
            object? matcher = interpreter.GetSymbolPropertyValue(
                pattern, SharpTSSymbol.Match);
            if (matcher is not (null or SharpTSUndefined))
            {
                if (matcher is not ISharpTSCallable callable)
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Symbol.match property is not callable"));
                }

                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, pattern, [str]));
            }
        }

        string source = pattern is SharpTSUndefined
            ? ""
            : interpreter.ToStringForBuiltInArgument(pattern);
        var created = new SharpTSRegExp(source);
        object? createdMatcher = interpreter.GetSymbolPropertyValue(
            created, SharpTSSymbol.Match);
        if (createdMatcher is not ISharpTSCallable createdCallable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Constructed RegExp Symbol.match property is not callable"));
        }
        return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
            interpreter, createdCallable, created, [str]));
    }

    private static RuntimeValue MatchAllV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? pattern = ArgumentOrUndefined(args, 0);
        if (pattern is not (null or SharpTSUndefined or SharpTSRegExp))
        {
            object? matcher = interpreter.GetSymbolPropertyValue(
                pattern, SharpTSSymbol.MatchAll);
            if (matcher is not (null or SharpTSUndefined))
            {
                if (matcher is not ISharpTSCallable callable)
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Symbol.matchAll property is not callable"));
                }

                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, pattern, [str]));
            }
        }

        if (pattern is SharpTSRegExp regex)
        {
            object? flagsValue = interpreter.GetPropertyValue(regex, "flags");
            if (flagsValue is null or SharpTSUndefined)
                throw new ThrowException(new SharpTSTypeError(
                    "String.prototype.matchAll requires RegExp flags"));
            string flags = interpreter.ToStringForBuiltInArgument(flagsValue);
            if (!flags.Contains('g'))
                throw new ThrowException(new SharpTSTypeError(
                    "String.prototype.matchAll called with a non-global RegExp argument"));
            object? matcher = interpreter.GetSymbolPropertyValue(
                regex, SharpTSSymbol.MatchAll);
            if (matcher is not (null or SharpTSUndefined))
            {
                if (matcher is not ISharpTSCallable callable)
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Symbol.matchAll property is not callable"));
                }
                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, regex, [str]));
            }
        }

        var source = pattern is SharpTSUndefined
            ? ""
            : pattern is SharpTSRegExp sourceRegex
                ? sourceRegex.Source
            : interpreter.ToStringForBuiltInArgument(pattern);
        var tempRegex = new SharpTSRegExp(source, "g");
        object? createdMatcher = interpreter.GetSymbolPropertyValue(
            tempRegex, SharpTSSymbol.MatchAll);
        if (createdMatcher is not ISharpTSCallable createdCallable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Constructed RegExp Symbol.matchAll property is not callable"));
        }
        return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
            interpreter, createdCallable, tempRegex, [str]));
    }

    private static RuntimeValue SearchV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? pattern = ArgumentOrUndefined(args, 0);
        if (pattern is not (null or SharpTSUndefined))
        {
            object? searcher = interpreter.GetSymbolPropertyValue(
                pattern, SharpTSSymbol.Search);
            if (searcher is not (null or SharpTSUndefined))
            {
                if (searcher is not ISharpTSCallable callable)
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Symbol.search property is not callable"));
                }

                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, pattern, [str]));
            }
        }

        var source = pattern is SharpTSUndefined
            ? ""
            : interpreter.ToStringForBuiltInArgument(pattern);
        var created = new SharpTSRegExp(source);
        object? createdSearcher = interpreter.GetSymbolPropertyValue(
            created, SharpTSSymbol.Search);
        if (createdSearcher is not ISharpTSCallable createdCallable)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Constructed RegExp Symbol.search property is not callable"));
        }
        return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
            interpreter, createdCallable, created, [str]));
    }

    private static RuntimeValue StringRawV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new ThrowException(new SharpTSTypeError(
                "String.raw requires a template object"));

        object? stringsArg = args[0].ToObject();
        if (stringsArg is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "String.raw template cannot be null or undefined"));

        object? raw = stringsArg is SharpTSTemplateStringsArray template
            ? new SharpTSArray(template.Raw.ToList())
            : interpreter.GetPropertyValue(stringsArg, "raw");
        if (raw is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "String.raw template.raw cannot be null or undefined"));

        long literalSegments = ArrayBuiltIns.ToLength(
            interpreter.GetPropertyValue(raw, "length"), interpreter);
        if (literalSegments == 0)
            return RuntimeValue.EmptyString;

        var result = new StringBuilder();
        for (long i = 0; i < literalSegments; i++)
        {
            string key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.Append(interpreter.ToStringForBuiltInArgument(
                interpreter.GetPropertyValue(raw, key)));
            if (i + 1 < literalSegments && i + 1 < args.Length)
            {
                result.Append(interpreter.ToStringForBuiltInArgument(
                    args[(int)i + 1].ToObject()));
            }
        }

        return RuntimeValue.FromString(result.ToString());
    }

    private static RuntimeValue FromCharCodeV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.EmptyString;

        if (args.Length == 1)
        {
            return RuntimeValue.FromString(
                ((char)ToUint16(NumArg(interpreter, args[0]))).ToString());
        }

        var chars = new char[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            chars[i] = (char)ToUint16(NumArg(interpreter, args[i]));
        }
        return RuntimeValue.FromString(new string(chars));
    }

    /// <summary>
    /// ToNumber-coerces a numeric argument, routing a non-number (notably a
    /// boxed <c>new Number(x)</c> wrapper) through the interpreter's ToNumber so
    /// it unwraps to its primitive instead of throwing on <c>AsNumber()</c> (#708).
    /// </summary>
    private static double NumArg(Interpreter interpreter, RuntimeValue rv)
        => rv.Kind == ValueKind.Number
            ? Interpreter.ToNumber(rv)
            : interpreter.ToNumberWithPrimitive(rv.ToObject());

    private static ushort ToUint16(double number)
    {
        if (number == 0 || double.IsNaN(number) || double.IsInfinity(number))
            return 0;
        const double Modulus = 65536d;
        double modulo = Math.Truncate(number) % Modulus;
        if (modulo < 0) modulo += Modulus;
        return (ushort)modulo;
    }

    private static RuntimeValue FromCodePointV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.EmptyString;

        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            // ECMA-262 §22.1.2.2: each code point must be an integral Number in
            // [0, 0x10FFFF]; NaN / Infinity / fractional values throw RangeError.
            var num = NumArg(interpreter, arg);
            if (!double.IsInteger(num) || num < 0 || num > 0x10FFFF)
                throw new ThrowException(new SharpTSRangeError(
                    $"Invalid code point {num}"));
            AppendCodePoint(sb, (int)num);
        }
        return RuntimeValue.FromString(sb.ToString());
    }

    /// <summary>
    /// ECMA-262 §11.1.3 UTF16EncodeCodePoint. Unlike <see cref="char.ConvertFromUtf32"/>,
    /// this accepts lone surrogates (0xD800–0xDFFF): JS strings are sequences of
    /// UTF-16 code units, so <c>String.fromCodePoint(0xDC00)</c> yields a one-unit
    /// string holding that surrogate. .NET strings are likewise UTF-16, so a lone
    /// surrogate is representable as a single <see cref="char"/>.
    /// </summary>
    internal static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp <= 0xFFFF)
        {
            sb.Append((char)cp);
        }
        else
        {
            cp -= 0x10000;
            sb.Append((char)((cp >> 10) + 0xD800));
            sb.Append((char)((cp & 0x3FF) + 0xDC00));
        }
    }

    #region V2 Implementations (RuntimeValue — no boxing)

    private static RuntimeValue CharAtV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double position = IntegerArgument(interpreter, args, 0, 0);
        if (position < 0 || position >= str.Length) return RuntimeValue.EmptyString;
        return RuntimeValue.FromString(str[(int)position].ToString());
    }

    private static RuntimeValue SubstringV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int start = ClampPosition(IntegerArgument(interpreter, args, 0, 0), str.Length);
        int end = args.Length < 2 || args[1].IsUndefined
            ? str.Length
            : ClampPosition(IntegerArgument(interpreter, args, 1, 0), str.Length);
        if (start > end) (start, end) = (end, start);
        return RuntimeValue.FromString(str.Substring(start, end - start));
    }

    private static RuntimeValue IndexOfV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string search = StringArgument(interpreter, args, 0);
        int fromIndex = ClampPosition(IntegerArgument(interpreter, args, 1, 0), str.Length);
        return RuntimeValue.FromNumber(
            str.IndexOf(search, fromIndex, StringComparison.Ordinal));
    }

    private static RuntimeValue ToUpperCaseV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(str.ToUpper());

    private static RuntimeValue ToLowerCaseV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(str.ToLower());

    private static RuntimeValue TrimV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(TrimEcmaWhitespace(str, trimStart: true, trimEnd: true));

    private static RuntimeValue IncludesV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string search = SearchStringArgument(interpreter, args, "includes");
        int position = ClampPosition(IntegerArgument(interpreter, args, 1, 0), str.Length);
        return RuntimeValue.FromBoolean(
            str.IndexOf(search, position, StringComparison.Ordinal) >= 0);
    }

    private static RuntimeValue StartsWithV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string search = SearchStringArgument(interpreter, args, "startsWith");
        int position = ClampPosition(IntegerArgument(interpreter, args, 1, 0), str.Length);
        return position + search.Length <= str.Length
            ? RuntimeValue.FromBoolean(
                str.AsSpan(position, search.Length).SequenceEqual(search))
            : RuntimeValue.False;
    }

    private static RuntimeValue EndsWithV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string search = SearchStringArgument(interpreter, args, "endsWith");
        int end = args.Length < 2 || args[1].IsUndefined
            ? str.Length
            : ClampPosition(IntegerArgument(interpreter, args, 1, 0), str.Length);
        int start = end - search.Length;
        return start >= 0
            ? RuntimeValue.FromBoolean(
                str.AsSpan(start, search.Length).SequenceEqual(search))
            : RuntimeValue.False;
    }

    private static RuntimeValue SliceV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int start = RelativePosition(
            IntegerArgument(interpreter, args, 0, 0), str.Length);
        int end = args.Length < 2 || args[1].IsUndefined
            ? str.Length
            : RelativePosition(
                IntegerArgument(interpreter, args, 1, 0), str.Length);
        if (end <= start) return RuntimeValue.EmptyString;
        return RuntimeValue.FromString(str.Substring(start, end - start));
    }

    // Legacy String.prototype.substr(start[, length]) — Annex B §B.2.2.1
    private static RuntimeValue SubstrV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int start = RelativePosition(
            IntegerArgument(interpreter, args, 0, 0), str.Length);

        int length;
        if (args.Length < 2 || args[1].IsUndefined)
        {
            length = str.Length - start;
        }
        else
        {
            double lengthArg = IntegerArgument(interpreter, args, 1, 0);
            length = ClampPosition(lengthArg, str.Length - start);
        }
        if (length <= 0) return RuntimeValue.EmptyString;
        length = Math.Min(length, str.Length - start);
        return RuntimeValue.FromString(str.Substring(start, length));
    }

    private static RuntimeValue RepeatV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double rawCount = IntegerArgument(interpreter, args, 0, 0);
        if (rawCount < 0 || double.IsPositiveInfinity(rawCount))
            throw new ThrowException(new SharpTSRangeError(
                "Invalid count value for repeat"));
        if (rawCount > int.MaxValue
            || (str.Length > 0 && rawCount > int.MaxValue / str.Length))
            throw new ThrowException(new SharpTSRangeError(
                "Invalid string length"));
        int count = (int)rawCount;
        if (count == 0 || str.Length == 0) return RuntimeValue.EmptyString;
        if (count == 1) return RuntimeValue.FromString(str);
        return RuntimeValue.FromString(string.Create(str.Length * count, (str, count), static (span, state) =>
        {
            var (s, c) = state;
            var srcSpan = s.AsSpan();
            for (int i = 0; i < c; i++)
                srcSpan.CopyTo(span.Slice(i * s.Length, s.Length));
        }));
    }

    private static RuntimeValue CharCodeAtV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double position = IntegerArgument(interpreter, args, 0, 0);
        if (position < 0 || position >= str.Length) return RuntimeValue.NaN;
        return RuntimeValue.FromNumber(str[(int)position]);
    }

    private static RuntimeValue LastIndexOfV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string search = StringArgument(interpreter, args, 0);
        double rawPosition = args.Length < 2 || args[1].IsUndefined
            ? double.PositiveInfinity
            : interpreter.ToNumberWithPrimitive(args[1].ToObject());
        rawPosition = double.IsNaN(rawPosition)
            ? double.PositiveInfinity
            : ToIntegerOrInfinity(rawPosition);
        int position = ClampPosition(rawPosition, str.Length);
        int start = Math.Min(position, str.Length - search.Length);
        if (start < 0) return RuntimeValue.FromNumber(-1);
        if (search.Length == 0) return RuntimeValue.FromNumber(position);
        int searchEnd = Math.Min(str.Length, start + search.Length);
        return RuntimeValue.FromNumber(
            str.AsSpan(0, searchEnd).LastIndexOf(search));
    }

    private static RuntimeValue TrimStartV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(TrimEcmaWhitespace(str, trimStart: true, trimEnd: false));

    private static RuntimeValue TrimEndV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(TrimEcmaWhitespace(str, trimStart: false, trimEnd: true));

    private static string TrimEcmaWhitespace(string value, bool trimStart, bool trimEnd)
    {
        int start = 0;
        int end = value.Length - 1;
        if (trimStart)
        {
            while (start <= end && IsEcmaWhitespace(value[start]))
                start++;
        }
        if (trimEnd)
        {
            while (end >= start && IsEcmaWhitespace(value[end]))
                end--;
        }
        return start == 0 && end == value.Length - 1
            ? value
            : value.Substring(start, end - start + 1);
    }

    private static bool IsEcmaWhitespace(char value)
        => char.IsWhiteSpace(value) || value == '\uFEFF';

    private static RuntimeValue AtV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double relativeIndex = IntegerArgument(interpreter, args, 0, 0);
        double position = relativeIndex >= 0
            ? relativeIndex
            : str.Length + relativeIndex;
        if (position < 0 || position >= str.Length) return RuntimeValue.Undefined;
        return RuntimeValue.FromString(str[(int)position].ToString());
    }

    private static RuntimeValue PadStartV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int targetLength = PadTargetLength(interpreter, args);
        if (targetLength <= str.Length) return RuntimeValue.FromString(str);
        string padString = args.Length < 2 || args[1].IsUndefined
            ? " "
            : StringArgument(interpreter, args, 1);
        if (padString.Length == 0) return RuntimeValue.FromString(str);
        var padLength = targetLength - str.Length;
        return RuntimeValue.FromString(string.Create(targetLength, (str, padString, padLength), static (span, state) =>
        {
            var (s, pad, pLen) = state;
            var padSpan = pad.AsSpan();
            int pos = 0;
            while (pos < pLen)
            {
                int copyLen = Math.Min(pad.Length, pLen - pos);
                padSpan.Slice(0, copyLen).CopyTo(span.Slice(pos, copyLen));
                pos += copyLen;
            }
            s.AsSpan().CopyTo(span.Slice(pLen));
        }));
    }

    private static RuntimeValue PadEndV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int targetLength = PadTargetLength(interpreter, args);
        if (targetLength <= str.Length) return RuntimeValue.FromString(str);
        string padString = args.Length < 2 || args[1].IsUndefined
            ? " "
            : StringArgument(interpreter, args, 1);
        if (padString.Length == 0) return RuntimeValue.FromString(str);
        var padLength = targetLength - str.Length;
        return RuntimeValue.FromString(string.Create(targetLength, (str, padString, padLength), static (span, state) =>
        {
            var (s, pad, pLen) = state;
            s.AsSpan().CopyTo(span);
            var padSpan = pad.AsSpan();
            int pos = s.Length;
            while (pos < span.Length)
            {
                int copyLen = Math.Min(pad.Length, span.Length - pos);
                padSpan.Slice(0, copyLen).CopyTo(span.Slice(pos, copyLen));
                pos += copyLen;
            }
        }));
    }

    private static RuntimeValue CodePointAtV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double position = IntegerArgument(interpreter, args, 0, 0);
        if (position < 0 || position >= str.Length) return RuntimeValue.Undefined;
        int index = (int)position;
        if (char.IsHighSurrogate(str[index]) && index + 1 < str.Length && char.IsLowSurrogate(str[index + 1]))
            return RuntimeValue.FromNumber(char.ConvertToUtf32(str[index], str[index + 1]));
        return RuntimeValue.FromNumber(str[index]);
    }

    private static RuntimeValue ReplaceAllV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        object? searchValue = args[0].ToObject();
        object? replaceValue = args[1].ToObject();

        if (searchValue is SharpTSRegExp regex)
        {
            // String.prototype.replaceAll requires a global RegExp (spec §22.1.3.18).
            string flags = interpreter.ToStringForBuiltInArgument(
                interpreter.GetProperty(searchValue, "flags"));
            if (!flags.Contains('g'))
                throw new ThrowException(new SharpTSTypeError(
                    "String.prototype.replaceAll called with a non-global RegExp argument"));
        }

        if (searchValue is not (null or SharpTSUndefined))
        {
            object? replaceMethod = interpreter.GetSymbolPropertyValue(
                searchValue, SharpTSSymbol.Replace);
            if (replaceMethod is ISharpTSCallable callable)
            {
                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, callable, searchValue, [str, replaceValue]));
            }
            if (replaceMethod is not (null or SharpTSUndefined))
            {
                throw new ThrowException(new SharpTSTypeError(
                    "Symbol.replace is not callable"));
            }
        }

        string search = interpreter.ToStringForBuiltInArgument(searchValue);
        bool functionalReplace = replaceValue is ISharpTSCallable;
        string? replacementText = functionalReplace
            ? null
            : interpreter.ToStringForBuiltInArgument(replaceValue);
        var result = new StringBuilder(str.Length);
        int sourcePosition = 0;
        while (sourcePosition <= str.Length)
        {
            int matchPosition = search.Length == 0
                ? sourcePosition
                : str.IndexOf(search, sourcePosition, StringComparison.Ordinal);
            if (matchPosition < 0) break;

            result.Append(str, sourcePosition, matchPosition - sourcePosition);
            if (replaceValue is ISharpTSCallable replacer)
            {
                object? replacement = FunctionBuiltIns.CallWithThis(
                    interpreter, replacer, SharpTSUndefined.Instance,
                    [search, (double)matchPosition, str]);
                result.Append(interpreter.ToStringForBuiltInArgument(replacement));
            }
            else
            {
                result.Append(ExpandStringReplacement(
                    replacementText!, str, search, matchPosition));
            }

            sourcePosition = matchPosition + Math.Max(1, search.Length);
            if (search.Length == 0 && matchPosition < str.Length)
                result.Append(str[matchPosition]);
        }
        if (sourcePosition < str.Length)
            result.Append(str, sourcePosition, str.Length - sourcePosition);
        return RuntimeValue.FromString(result.ToString());
    }

    private static RuntimeValue NormalizeV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string form = args.Length == 0 || args[0].IsUndefined
            ? "NFC"
            : StringArgument(interpreter, args, 0);
        var normForm = form switch
        {
            "NFC" => System.Text.NormalizationForm.FormC,
            "NFD" => System.Text.NormalizationForm.FormD,
            "NFKC" => System.Text.NormalizationForm.FormKC,
            "NFKD" => System.Text.NormalizationForm.FormKD,
            _ => throw new ThrowException(new SharpTSRangeError(
                "The normalization form should be one of NFC, NFD, NFKC, NFKD"))
        };
        return RuntimeValue.FromString(str.Normalize(normForm));
    }

    private static RuntimeValue LocaleCompareV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        string that = StringArgument(interpreter, args, 0);
        // ECMA-402 requires canonically equivalent strings to compare equal,
        // independent of the host culture's normalization behavior.
        var result = string.Compare(
            str.Normalize(NormalizationForm.FormC),
            that.Normalize(NormalizationForm.FormC),
            StringComparison.CurrentCulture);
        return RuntimeValue.FromNumber(result < 0 ? -1 : result > 0 ? 1 : 0);
    }

    private static RuntimeValue ConcatV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.FromString(str);
        if (args.Length == 1)
            return RuntimeValue.FromString(string.Concat(
                str, StringArgument(interpreter, args, 0)));
        var sb = new StringBuilder(str);
        for (int i = 0; i < args.Length; i++)
        {
            sb.Append(StringArgument(interpreter, args, i));
        }
        return RuntimeValue.FromString(sb.ToString());
    }

    private static object? ArgumentOrUndefined(
        ReadOnlySpan<RuntimeValue> args,
        int index)
        => index < args.Length
            ? args[index].ToObject()
            : SharpTSUndefined.Instance;

    private static string StringArgument(
        Interpreter interpreter,
        ReadOnlySpan<RuntimeValue> args,
        int index)
        => interpreter.ToStringForBuiltInArgument(
            ArgumentOrUndefined(args, index));

    private static string SearchStringArgument(
        Interpreter interpreter,
        ReadOnlySpan<RuntimeValue> args,
        string methodName)
    {
        object? value = ArgumentOrUndefined(args, 0);
        object? matcher = value is null or SharpTSUndefined
            ? SharpTSUndefined.Instance
            : interpreter.GetSymbolPropertyValue(value, SharpTSSymbol.Match);
        bool isRegExp = matcher is null or SharpTSUndefined
            ? value is SharpTSRegExp
            : Compilation.RuntimeTypes.IsTruthy(matcher);
        if (isRegExp)
            throw new ThrowException(new SharpTSTypeError(
                $"String.prototype.{methodName} does not accept a RegExp"));
        return interpreter.ToStringForBuiltInArgument(value);
    }

    private static double IntegerArgument(
        Interpreter interpreter,
        ReadOnlySpan<RuntimeValue> args,
        int index,
        double defaultValue)
    {
        if (index >= args.Length)
            return defaultValue;

        double number = interpreter.ToNumberWithPrimitive(args[index].ToObject());
        return ToIntegerOrInfinity(number);
    }

    private static double ToIntegerOrInfinity(double number)
    {
        if (double.IsNaN(number) || number == 0) return 0;
        if (double.IsInfinity(number)) return number;
        return Math.Truncate(number);
    }

    private static int ClampPosition(double position, int length)
    {
        if (position <= 0) return 0;
        if (position >= length) return length;
        return (int)position;
    }

    private static int RelativePosition(double position, int length)
    {
        if (position == double.NegativeInfinity) return 0;
        if (position < 0)
            return (int)Math.Max(length + position, 0);
        return ClampPosition(position, length);
    }

    private static int PadTargetLength(
        Interpreter interpreter,
        ReadOnlySpan<RuntimeValue> args)
    {
        double targetLength = IntegerArgument(interpreter, args, 0, 0);
        if (targetLength <= 0) return 0;
        if (targetLength > int.MaxValue)
            throw new ThrowException(new SharpTSRangeError(
                "Invalid string length"));
        return (int)targetLength;
    }

    #endregion
}
