using System.Globalization;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.ListFormat.
/// Provides locale-aware list formatting ("A, B, and C").
/// </summary>
public class SharpTSIntlListFormat : SharpTSIntlFormatterBase
{
    private string _style; // "long", "short", "narrow"
    private string _type;  // "conjunction", "disjunction", "unit"
    private readonly string _lang;

    public SharpTSIntlListFormat(object? locale, object? options)
    {
        ResolveLocale(locale);
        _lang = PrimaryLanguage;

        // Defaults
        _style = "long";
        _type = "conjunction";

        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts);
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("style", out var styleVal) && styleVal is string s)
            _style = s;

        if (dict.TryGetValue("type", out var typeVal) && typeVal is string t)
            _type = t;
    }

    /// <summary>
    /// Formats a list of items according to the locale and options.
    /// </summary>
    public string FormatList(List<string> items)
    {
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];

        string conjunction = GetConjunction();
        string separator = GetSeparator();

        if (items.Count == 2)
        {
            // "A and B" / "A or B" / "A, B"
            if (_type == "unit" && _style != "long")
            {
                return string.Join(separator, items);
            }
            return $"{items[0]} {conjunction} {items[1]}";
        }

        // 3+ items: "A, B, and C" / "A, B, or C" / "A, B, C"
        if (_type == "unit")
        {
            return string.Join(separator, items);
        }

        // Oxford comma for English conjunction/disjunction
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                if (i == items.Count - 1)
                {
                    // Last item
                    if (_lang == "en" && items.Count > 2)
                    {
                        sb.Append($",{GetSpace()}{conjunction} ");
                    }
                    else
                    {
                        sb.Append($"{GetSpace()}{conjunction} ");
                    }
                }
                else
                {
                    sb.Append($",{GetSpace()}");
                }
            }
            sb.Append(items[i]);
        }

        return sb.ToString();
    }

    private string GetConjunction()
    {
        if (_type == "disjunction")
        {
            return _lang switch
            {
                "de" => "oder",
                "fr" => "ou",
                "es" => "o",
                "it" => "o",
                "pt" => "ou",
                "nl" => "of",
                "ja" => "または",
                "zh" => "或",
                _ => "or"
            };
        }

        // conjunction (default)
        return _lang switch
        {
            "de" => "und",
            "fr" => "et",
            "es" => "y",
            "it" => "e",
            "pt" => "e",
            "nl" => "en",
            "ja" => "、",
            "zh" => "和",
            "ru" => "и",
            "ko" => "및",
            _ => "and"
        };
    }

    private string GetSeparator()
    {
        return _style switch
        {
            "narrow" => _type == "unit" ? " " : ", ",
            "short" => _type == "unit" ? ", " : ", ",
            _ => ", "
        };
    }

    private string GetSpace()
    {
        return _style == "narrow" ? "" : " ";
    }

    /// <summary>
    /// Formats the list into parts.
    /// </summary>
    public SharpTSArray GetFormattedParts(List<string> items)
    {
        var parts = new List<object?>();

        if (items.Count == 0)
            return new SharpTSArray(parts);

        if (items.Count == 1)
        {
            parts.Add(MakePart("element", items[0]));
            return new SharpTSArray(parts);
        }

        string formatted = FormatList(items);

        // Build parts by finding each element within the formatted string
        int pos = 0;
        for (int i = 0; i < items.Count; i++)
        {
            string item = items[i];
            int idx = formatted.IndexOf(item, pos, StringComparison.Ordinal);
            if (idx > pos)
            {
                // Literal separator before this element
                parts.Add(MakePart("literal", formatted[pos..idx]));
            }
            parts.Add(MakePart("element", item));
            pos = idx + item.Length;
        }

        if (pos < formatted.Length)
        {
            parts.Add(MakePart("literal", formatted[pos..]));
        }

        return new SharpTSArray(parts);
    }

    private static SharpTSObject MakePart(string type, string value)
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["type"] = type,
            ["value"] = value,
        });
    }

    public override Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["type"] = _type,
            ["style"] = _style,
        };
    }

    /// <summary>
    /// Extracts a list of strings from a JS array argument.
    /// </summary>
    private static List<string> ToStringList(object? items)
    {
        if (items is SharpTSArray arr)
        {
            return arr.Select(e => e?.ToString() ?? "").ToList();
        }
        if (items is List<object?> list)
        {
            return list.Select(e => e?.ToString() ?? "").ToList();
        }
        if (items is IEnumerable<object?> enumerable)
        {
            return enumerable.Select(e => e?.ToString() ?? "").ToList();
        }
        return [];
    }

    /// <summary>
    /// JS-facing format method for compiled mode reflection dispatch.
    /// </summary>
    public object? format(object? items)
    {
        return FormatList(ToStringList(items));
    }

    /// <summary>
    /// JS-facing formatToParts method for compiled mode reflection dispatch.
    /// Returns List&lt;object?&gt; of Dictionary&lt;string,object?&gt; for compiled mode iteration.
    /// </summary>
    public object? formatToParts(object? items)
    {
        var arr = GetFormattedParts(ToStringList(items));
        // Convert to compiled-mode representation: List<object?> of Dictionary<string,object?>
        var list = new List<object?>();
        foreach (var elem in arr)
        {
            if (elem is SharpTSObject obj)
                list.Add(new Dictionary<string, object?>(obj.Fields));
            else
                list.Add(elem);
        }
        return list;
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "format" => BuiltInMethod.CreateV2("format", 1, (_, _, args) =>
            {
                var items = ToStringList(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.FromBoxed(FormatList(items));
            }),
            "formatToParts" => BuiltInMethod.CreateV2("formatToParts", 1, (_, _, args) =>
            {
                var items = ToStringList(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.FromBoxed(GetFormattedParts(items));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.ListFormat]";
}
