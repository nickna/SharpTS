using System.Globalization;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared base for the <c>Intl.*</c> formatter value-type wrappers (NumberFormat,
/// DateTimeFormat, RelativeTimeFormat, PluralRules, ListFormat, DisplayNames,
/// Collator, Segmenter).
/// </summary>
/// <remarks>
/// All eight previously repeated, near-verbatim (#1136): the locale-resolution
/// ctor preamble, the options-dispatch + dictionary normalization, the
/// primary-language extraction, and the <c>resolvedOptions</c> member. Those are
/// centralized here; each formatter keeps only its own option fields, its format
/// method(s), and its <see cref="GetResolvedOptions"/> body.
///
/// Interpreter and compiled mode share these very classes (compiled IL calls into
/// them via <c>RuntimeTypes.Intl</c>), so this refactor is automatically
/// behaviour-identical across both.
/// </remarks>
public abstract class SharpTSIntlFormatterBase
{
    /// <summary>The resolved BCP-47 locale name (a canonical CultureInfo name, or "en-US").</summary>
    protected string _locale = "en-US";

    /// <summary>
    /// Resolves a JS locale argument (null/string, with <c>'_'</c> normalized to
    /// <c>'-'</c>) to a <see cref="CultureInfo"/>, setting <see cref="_locale"/> to its
    /// canonical name (falling back to "en-US"). Returns the culture for subclasses that
    /// need <c>culture.NumberFormat</c>/<c>CompareInfo</c>/etc.
    /// </summary>
    protected CultureInfo ResolveLocale(object? locale)
        => ResolveCulture((locale?.ToString() ?? "").Replace('_', '-'));

    /// <summary>
    /// Resolves an already-normalized base-locale string to a <see cref="CultureInfo"/>
    /// and sets <see cref="_locale"/>. Used directly by DateTimeFormat, which parses
    /// BCP-47 extensions before resolving the base locale.
    /// </summary>
    protected CultureInfo ResolveCulture(string baseLocale)
    {
        CultureInfo culture;
        try
        {
            culture = string.IsNullOrEmpty(baseLocale)
                ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(baseLocale);
        }
        catch
        {
            culture = CultureInfo.InvariantCulture;
        }

        _locale = culture.Name;
        if (string.IsNullOrEmpty(_locale))
            _locale = "en-US"; // Invariant falls back to en-US for display

        return culture;
    }

    /// <summary>
    /// The primary language subtag of <see cref="_locale"/> (e.g. "en-US" → "en").
    /// </summary>
    protected string PrimaryLanguage
    {
        get
        {
            int dashIndex = _locale.IndexOf('-');
            return dashIndex >= 0
                ? _locale[..dashIndex].ToLowerInvariant()
                : _locale.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Normalizes a JS options argument to a key/value sequence: a
    /// <see cref="SharpTSObject"/>'s fields, an <see cref="IDictionary{TKey,TValue}"/>,
    /// or <c>null</c> when no options object was provided.
    /// </summary>
    protected static IEnumerable<KeyValuePair<string, object?>>? NormalizeOptions(object? options)
        => options switch
        {
            SharpTSObject obj => obj.Fields,
            IDictionary<string, object?> dict => dict,
            _ => null
        };

    /// <summary>
    /// Builds the <c>resolvedOptions()</c> dictionary for this formatter.
    /// </summary>
    public abstract Dictionary<string, object?> GetResolvedOptions();

    /// <summary>
    /// JS-facing resolvedOptions method for compiled-mode reflection dispatch.
    /// </summary>
    public object? resolvedOptions() => GetResolvedOptions();

    /// <summary>
    /// Member dispatch for interpreter access. Subclasses override to add their
    /// format-specific members and chain to <c>base.GetMember</c> for resolvedOptions.
    /// </summary>
    public virtual object? GetMember(string name)
    {
        return name switch
        {
            "resolvedOptions" => BuiltInMethod.CreateV2("resolvedOptions", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSObject(GetResolvedOptions()));
            }),
            _ => null
        };
    }
}
