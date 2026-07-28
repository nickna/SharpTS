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
            .MethodV2("replace", 2, ReplaceV2)
            .MethodV2("split", 1, 2, specLength: 2, SplitV2)
            .MethodV2("match", 1, MatchV2)
            .MethodV2("matchAll", 1, MatchAllV2)
            .MethodV2("search", 1, SearchV2)
            .MethodV2("includes", 0, int.MaxValue, specLength: 1, IncludesV2)
            .MethodV2("startsWith", 0, int.MaxValue, specLength: 1, StartsWithV2)
            .MethodV2("endsWith", 0, int.MaxValue, specLength: 1, EndsWithV2)
            .MethodV2("slice", 0, int.MaxValue, specLength: 2, SliceV2)
            .MethodV2("substr", 0, int.MaxValue, specLength: 2, SubstrV2)
            .MethodV2("repeat", 1, RepeatV2)
            .MethodV2("padStart", 1, 2, PadStartV2)
            .MethodV2("padEnd", 1, 2, PadEndV2)
            .MethodV2("charCodeAt", 0, int.MaxValue, specLength: 1, CharCodeAtV2)
            .MethodV2("codePointAt", 0, int.MaxValue, specLength: 1, CodePointAtV2)
            .MethodV2("concat", 0, int.MaxValue, specLength: 1, ConcatV2)
            .MethodV2("lastIndexOf", 0, int.MaxValue, specLength: 1, LastIndexOfV2)
            .MethodV2("trimStart", 0, TrimStartV2)
            .MethodV2("trimEnd", 0, TrimEndV2)
            .MethodV2("replaceAll", 2, ReplaceAllV2)
            .MethodV2("at", 0, int.MaxValue, specLength: 1, AtV2)
            .MethodV2("normalize", 0, 1, NormalizeV2)
            .MethodV2("localeCompare", 1, LocaleCompareV2)
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
            .MethodV2("fromCharCode", 0, int.MaxValue, FromCharCodeV2)
            .MethodV2("fromCodePoint", 0, int.MaxValue, FromCodePointV2)
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

    private static RuntimeValue ReplaceV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var replacement = interpreter.ToStringForBuiltInArgument(args[1].ToObject());

        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            return RuntimeValue.FromString(regex.Replace(str, replacement));
        }

        var search = interpreter.ToStringForBuiltInArgument(args[0].ToObject());
        var index = str.IndexOf(search);
        if (index < 0) return RuntimeValue.FromString(str);
        return RuntimeValue.FromString(str.Substring(0, index) + replacement + str.Substring(index + search.Length));
    }

    private static RuntimeValue SplitV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        int? limit = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumber() : null;

        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            string[] parts = regex.Split(str);
            IEnumerable<string> resultParts = limit.HasValue && limit.Value >= 0
                ? parts.Take(limit.Value)
                : parts;
            return RuntimeValue.FromObject(new SharpTSArray(resultParts.Select(p => (object?)p).ToList()));
        }

        var separator = args[0].ToObject()?.ToString() ?? "";
        string[] stringParts;
        if (separator == "")
        {
            stringParts = str.Select(c => c.ToString()).ToArray();
        }
        else
        {
            stringParts = str.Split(separator);
        }

        if (limit.HasValue && limit.Value >= 0)
        {
            stringParts = stringParts.Take(limit.Value).ToArray();
        }

        var elements = stringParts.Select(p => (object?)p).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(elements));
    }

    private static RuntimeValue MatchV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            if (regex.Global)
            {
                var matches = regex.MatchAll(str);
                if (matches.Count == 0) return RuntimeValue.Null;
                return RuntimeValue.FromObject(new SharpTSArray(matches));
            }
            else
            {
                return RuntimeValue.FromBoxed(regex.Exec(str));
            }
        }

        var search = args[0].ToObject()?.ToString() ?? "";
        var index = str.IndexOf(search);
        if (index < 0) return RuntimeValue.Null;
        return RuntimeValue.FromObject(new SharpTSArray([(object?)search]));
    }

    private static RuntimeValue MatchAllV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            if (!regex.Global)
                throw new Exception("TypeError: String.prototype.matchAll called with a non-global RegExp argument");
            var matchObjects = regex.MatchAllObjects(str);
            return RuntimeValue.FromObject(new SharpTSArray(matchObjects.Select(m => (object?)m).ToList()));
        }

        var pattern = args[0].ToObject()?.ToString() ?? "";
        var tempRegex = new SharpTSRegExp(System.Text.RegularExpressions.Regex.Escape(pattern), "g");
        var results = tempRegex.MatchAllObjects(str);
        return RuntimeValue.FromObject(new SharpTSArray(results.Select(m => (object?)m).ToList()));
    }

    private static RuntimeValue SearchV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            return RuntimeValue.FromNumber(regex.Search(str));
        }

        var search = args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromNumber(str.IndexOf(search));
    }

    private static RuntimeValue StringRawV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("TypeError: String.raw requires at least 1 argument.");

        object? stringsArg = args[0].ToObject();
        IReadOnlyList<object?>? rawStrings = null;

        if (stringsArg is SharpTSTemplateStringsArray tsa)
        {
            rawStrings = tsa.Raw;
        }
        else if (stringsArg is SharpTSObject obj)
        {
            var rawProp = obj.GetProperty("raw");
            if (rawProp is SharpTSArray rawArr)
                rawStrings = rawArr;
        }
        else if (stringsArg is SharpTSArray arr)
        {
            if (stringsArg is ISharpTSPropertyAccessor accessor)
            {
                var rawProp = accessor.GetProperty("raw");
                if (rawProp is SharpTSArray rawArr)
                    rawStrings = rawArr;
            }
            if (rawStrings == null)
            {
                rawStrings = arr;
            }
        }

        if (rawStrings == null || rawStrings.Count == 0)
            return RuntimeValue.EmptyString;

        var result = new StringBuilder();
        for (int i = 0; i < rawStrings.Count; i++)
        {
            result.Append(rawStrings[i]?.ToString() ?? "");
            if (i < args.Length - 1 && i < rawStrings.Count - 1)
            {
                result.Append(args[i + 1].ToObject()?.ToString() ?? "");
            }
        }

        return RuntimeValue.FromString(result.ToString());
    }

    private static RuntimeValue FromCharCodeV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.EmptyString;

        if (args.Length == 1)
        {
            var code = (int)NumArg(args[0]);
            return RuntimeValue.FromString(((char)(code & 0xFFFF)).ToString());
        }

        var chars = new char[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var code = (int)NumArg(args[i]);
            chars[i] = (char)(code & 0xFFFF);
        }
        return RuntimeValue.FromString(new string(chars));
    }

    /// <summary>
    /// ToNumber-coerces a numeric argument, routing a non-number (notably a
    /// boxed <c>new Number(x)</c> wrapper) through the interpreter's ToNumber so
    /// it unwraps to its primitive instead of throwing on <c>AsNumber()</c> (#708).
    /// </summary>
    private static double NumArg(RuntimeValue rv)
        => rv.Kind == ValueKind.Number ? rv.AsNumber() : Interpreter.ToNumber(rv.ToObject());

    private static RuntimeValue FromCodePointV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.EmptyString;

        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            // ECMA-262 §22.1.2.2: each code point must be an integral Number in
            // [0, 0x10FFFF]; NaN / Infinity / fractional values throw RangeError.
            var num = NumArg(arg);
            if (!double.IsInteger(num) || num < 0 || num > 0x10FFFF)
                throw new Exception($"RangeError: Invalid code point {num}");
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
        => RuntimeValue.FromString(str.Trim());

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

    private static RuntimeValue RepeatV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var count = (int)args[0].AsNumber();
        if (count < 0) throw new Exception("Runtime Error: Invalid count value for repeat()");
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
        => RuntimeValue.FromString(str.TrimStart());

    private static RuntimeValue TrimEndV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromString(str.TrimEnd());

    private static RuntimeValue AtV2(Interpreter interpreter, string str, ReadOnlySpan<RuntimeValue> args)
    {
        double relativeIndex = IntegerArgument(interpreter, args, 0, 0);
        double position = relativeIndex >= 0
            ? relativeIndex
            : str.Length + relativeIndex;
        if (position < 0 || position >= str.Length) return RuntimeValue.Undefined;
        return RuntimeValue.FromString(str[(int)position].ToString());
    }

    private static RuntimeValue PadStartV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var targetLength = (int)args[0].AsNumber();
        var padString = args.Length > 1 ? args[1].AsString() : " ";
        if (targetLength <= str.Length || padString.Length == 0) return RuntimeValue.FromString(str);
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

    private static RuntimeValue PadEndV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var targetLength = (int)args[0].AsNumber();
        var padString = args.Length > 1 ? args[1].AsString() : " ";
        if (targetLength <= str.Length || padString.Length == 0) return RuntimeValue.FromString(str);
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

    private static RuntimeValue ReplaceAllV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var replacement = args[1].ToObject()?.ToString() ?? "";

        if (args[0].ToObject() is SharpTSRegExp regex)
        {
            // String.prototype.replaceAll requires a global RegExp (spec §22.1.3.18).
            if (!regex.Global)
                throw new Exception("TypeError: String.prototype.replaceAll called with a non-global RegExp argument");
            return RuntimeValue.FromString(regex.Replace(str, replacement));
        }

        var search = args[0].ToObject()?.ToString() ?? "";
        if (search.Length == 0)
        {
            // ECMA-262 22.1.3.20: empty search inserts replacement at every
            // position 0..length (between each char + start + end).
            // E.g. "a".replaceAll("","_") → "_a_".
            var sb = new StringBuilder();
            for (int i = 0; i <= str.Length; i++)
            {
                sb.Append(replacement);
                if (i < str.Length) sb.Append(str[i]);
            }
            return RuntimeValue.FromString(sb.ToString());
        }
        return RuntimeValue.FromString(str.Replace(search, replacement));
    }

    private static RuntimeValue NormalizeV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var form = args.Length > 0 && args[0].IsString ? args[0].AsString() : "NFC";
        var normForm = form switch
        {
            "NFC" => System.Text.NormalizationForm.FormC,
            "NFD" => System.Text.NormalizationForm.FormD,
            "NFKC" => System.Text.NormalizationForm.FormKC,
            "NFKD" => System.Text.NormalizationForm.FormKD,
            _ => throw new Exception($"RangeError: The normalization form should be one of NFC, NFD, NFKC, NFKD.")
        };
        return RuntimeValue.FromString(str.Normalize(normForm));
    }

    private static RuntimeValue LocaleCompareV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        var that = args[0].AsString();
        var result = string.Compare(str, that, StringComparison.CurrentCulture);
        return RuntimeValue.FromNumber(result < 0 ? -1 : result > 0 ? 1 : 0);
    }

    private static RuntimeValue ConcatV2(Interpreter _, string str, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.FromString(str);
        if (args.Length == 1) return RuntimeValue.FromString(string.Concat(str, args[0].AsString()));
        var sb = new StringBuilder(str);
        foreach (var arg in args)
        {
            sb.Append(arg.AsString());
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
        if (value is SharpTSRegExp)
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

    #endregion
}
