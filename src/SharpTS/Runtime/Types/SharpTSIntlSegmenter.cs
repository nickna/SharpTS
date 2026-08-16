using System.Globalization;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.Segmenter.
/// Provides locale-aware text segmentation by grapheme, word, or sentence boundaries.
/// </summary>
public class SharpTSIntlSegmenter : SharpTSIntlFormatterBase
{
    private string _granularity;

    public SharpTSIntlSegmenter(object? locale, object? options)
    {
        ResolveLocale(locale);

        // Default granularity
        _granularity = "grapheme";

        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts);
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("granularity", out var granVal) && granVal is string g)
            _granularity = g;
    }

    /// <summary>
    /// Segments the input string according to the granularity setting.
    /// Returns a SharpTSIntlSegments (which is a List&lt;object?&gt;) for iteration compatibility.
    /// </summary>
    public SharpTSIntlSegments SegmentText(string input)
    {
        return new SharpTSIntlSegments(input, _granularity);
    }

    public override Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["granularity"] = _granularity,
        };
    }

    /// <summary>
    /// JS-facing segment() method for compiled mode reflection dispatch.
    /// </summary>
    public object? segment(object? input)
    {
        return SegmentText(input?.ToString() ?? "");
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "segment" => BuiltInMethod.CreateV2("segment", 1, (_, _, args) =>
            {
                string input = (args.Length > 0 ? args[0].ToObject() : null)?.ToString() ?? "";
                return RuntimeValue.FromBoxed(SegmentText(input));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.Segmenter]";
}
