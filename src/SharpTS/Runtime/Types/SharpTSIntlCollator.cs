using System.Globalization;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.Collator.
/// Provides locale-aware string comparison using .NET's CompareInfo.
/// </summary>
public class SharpTSIntlCollator : SharpTSIntlFormatterBase
{
    private string _usage;
    private string _sensitivity;
    private bool _ignorePunctuation;
    private bool _numeric;
    private string _caseFirst;
    private readonly CompareInfo _compareInfo;
    private readonly CompareOptions _compareOptions;

    public SharpTSIntlCollator(object? locale, object? options)
    {
        var culture = ResolveLocale(locale);
        _compareInfo = culture.CompareInfo;

        // Defaults
        _usage = "sort";
        _sensitivity = "variant";
        _ignorePunctuation = false;
        _numeric = false;
        _caseFirst = "false";

        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts);

        _compareOptions = BuildCompareOptions();
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("usage", out var usageVal) && usageVal is string u)
            _usage = u;

        if (dict.TryGetValue("sensitivity", out var sensVal) && sensVal is string s)
            _sensitivity = s;

        if (dict.TryGetValue("ignorePunctuation", out var ignoreVal))
        {
            if (ignoreVal is bool b) _ignorePunctuation = b;
            else if (ignoreVal is string gs) _ignorePunctuation = gs != "false";
        }

        if (dict.TryGetValue("numeric", out var numVal))
        {
            if (numVal is bool b) _numeric = b;
            else if (numVal is string ns) _numeric = ns != "false";
        }

        if (dict.TryGetValue("caseFirst", out var cfVal) && cfVal is string cf)
            _caseFirst = cf;
    }

    private CompareOptions BuildCompareOptions()
    {
        var opts = CompareOptions.None;

        switch (_sensitivity)
        {
            case "base":
                // Ignore case and accents
                opts |= CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
                break;
            case "accent":
                // Ignore case but distinguish accents
                opts |= CompareOptions.IgnoreCase;
                break;
            case "case":
                // Distinguish case but ignore accents
                opts |= CompareOptions.IgnoreNonSpace;
                break;
            case "variant":
            default:
                // Distinguish everything
                break;
        }

        if (_ignorePunctuation)
            opts |= CompareOptions.IgnoreSymbols;

        if (_numeric)
            opts |= CompareOptions.StringSort;

        return opts;
    }

    /// <summary>
    /// Compares two strings according to the locale and options.
    /// Returns negative, zero, or positive value.
    /// </summary>
    public double CompareStrings(string x, string y)
    {
        int result = _compareInfo.Compare(x, y, _compareOptions);
        // JS spec says return value should be -1, 0, or 1 (implementation-defined, but convention)
        return result < 0 ? -1.0 : result > 0 ? 1.0 : 0.0;
    }

    public override Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["usage"] = _usage,
            ["sensitivity"] = _sensitivity,
            ["ignorePunctuation"] = _ignorePunctuation,
            ["numeric"] = _numeric,
            ["caseFirst"] = _caseFirst,
            ["collation"] = "default",
        };
    }

    /// <summary>
    /// JS-facing compare method for compiled mode reflection dispatch.
    /// </summary>
    public object? compare(object? x, object? y)
    {
        string sx = x?.ToString() ?? "";
        string sy = y?.ToString() ?? "";
        return CompareStrings(sx, sy);
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "compare" => BuiltInMethod.CreateV2("compare", 2, (_, _, args) =>
            {
                string x = (args.Length > 0 ? args[0].ToObject() : null)?.ToString() ?? "";
                string y = (args.Length > 1 ? args[1].ToObject() : null)?.ToString() ?? "";
                return RuntimeValue.FromBoxed(CompareStrings(x, y));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.Collator]";
}
