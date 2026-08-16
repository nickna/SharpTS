using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    // Intl constructor factories, called from compiled code via reflection for
    // standalone DLL compatibility. Instance methods (format/resolvedOptions/…)
    // need no statics here: calls dispatch reflectively on the returned
    // SharpTSIntl* objects directly.

    public static object CreateIntlNumberFormat(object? locale, object? options)
    {
        return new SharpTSIntlNumberFormat(locale, options);
    }

    public static object CreateIntlDateTimeFormat(object? locale, object? options)
    {
        return new SharpTSIntlDateTimeFormat(locale, options);
    }

    /// <summary>
    /// Formats a timestamp for Date.prototype.toLocale*{Date,Time,}String with locale/options (#539).
    /// Called from compiled code via reflection (soft SharpTS dependency) so arg-less toLocale* stays
    /// standalone. Delegates to the shared SharpTSDate.FormatToLocale logic.
    /// </summary>
    public static object FormatDateToLocale(double epochMs, int kind, object? locale, object? options)
    {
        return SharpTSDate.FormatToLocale(epochMs, kind, locale, options);
    }

    public static object CreateIntlCollator(object? locale, object? options)
    {
        return new SharpTSIntlCollator(locale, options);
    }

    public static object CreateIntlPluralRules(object? locale, object? options)
    {
        return new SharpTSIntlPluralRules(locale, options);
    }

    public static object CreateIntlRelativeTimeFormat(object? locale, object? options)
    {
        return new SharpTSIntlRelativeTimeFormat(locale, options);
    }

    public static object CreateIntlListFormat(object? locale, object? options)
    {
        return new SharpTSIntlListFormat(locale, options);
    }

    public static object CreateIntlDisplayNames(object? locale, object? options)
    {
        return new SharpTSIntlDisplayNames(locale, options);
    }

    public static object CreateIntlSegmenter(object? locale, object? options)
    {
        return new SharpTSIntlSegmenter(locale, options);
    }
}
