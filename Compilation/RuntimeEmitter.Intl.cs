using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the Intl.* support methods into the $Runtime class. Every method is a
    /// late-bound <see cref="EmitReflectionHelper"/> wrapper over the same-named
    /// RuntimeTypes static (standalone-DLL soft dependency; the Intl feature records
    /// <see cref="EmittedRuntime.RequireSharpTSRuntime"/> so SharpTS.dll is co-located).
    /// </summary>
    private void EmitIntlMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Intl.NumberFormat
        runtime.CreateIntlNumberFormat = EmitReflectionHelper(typeBuilder, "CreateIntlNumberFormat", 2);
        runtime.IntlNumberFormatFormat = EmitReflectionHelper(typeBuilder, "IntlNumberFormatFormat", 2);
        runtime.IntlNumberFormatResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlNumberFormatResolvedOptions", 1);

        // Intl.DateTimeFormat
        runtime.CreateIntlDateTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlDateTimeFormat", 2);
        runtime.IntlDateTimeFormatFormat = EmitReflectionHelper(typeBuilder, "IntlDateTimeFormatFormat", 2);
        runtime.IntlDateTimeFormatResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlDateTimeFormatResolvedOptions", 1);
        runtime.IntlDateTimeFormatFormatToParts = EmitReflectionHelper(typeBuilder, "IntlDateTimeFormatFormatToParts", 2);
        runtime.IntlDateTimeFormatFormatRange = EmitReflectionHelper(typeBuilder, "IntlDateTimeFormatFormatRange", 3);
        runtime.IntlDateTimeFormatFormatRangeToParts = EmitReflectionHelper(typeBuilder, "IntlDateTimeFormatFormatRangeToParts", 3);

        // Intl.Collator
        runtime.CreateIntlCollator = EmitReflectionHelper(typeBuilder, "CreateIntlCollator", 2);
        runtime.IntlCollatorCompare = EmitReflectionHelper(typeBuilder, "IntlCollatorCompare", 3);
        runtime.IntlCollatorResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlCollatorResolvedOptions", 1);

        // Intl.PluralRules
        runtime.CreateIntlPluralRules = EmitReflectionHelper(typeBuilder, "CreateIntlPluralRules", 2);
        runtime.IntlPluralRulesSelect = EmitReflectionHelper(typeBuilder, "IntlPluralRulesSelect", 2);
        runtime.IntlPluralRulesResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlPluralRulesResolvedOptions", 1);

        // Intl.RelativeTimeFormat
        runtime.CreateIntlRelativeTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlRelativeTimeFormat", 2);
        runtime.IntlRelativeTimeFormatFormat = EmitReflectionHelper(typeBuilder, "IntlRelativeTimeFormatFormat", 3);
        runtime.IntlRelativeTimeFormatResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlRelativeTimeFormatResolvedOptions", 1);

        // Intl.ListFormat
        runtime.CreateIntlListFormat = EmitReflectionHelper(typeBuilder, "CreateIntlListFormat", 2);
        runtime.IntlListFormatFormat = EmitReflectionHelper(typeBuilder, "IntlListFormatFormat", 2);
        runtime.IntlListFormatFormatToParts = EmitReflectionHelper(typeBuilder, "IntlListFormatFormatToParts", 2);
        runtime.IntlListFormatResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlListFormatResolvedOptions", 1);

        // Intl.DisplayNames
        runtime.CreateIntlDisplayNames = EmitReflectionHelper(typeBuilder, "CreateIntlDisplayNames", 2);
        runtime.IntlDisplayNamesOf = EmitReflectionHelper(typeBuilder, "IntlDisplayNamesOf", 2);
        runtime.IntlDisplayNamesResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlDisplayNamesResolvedOptions", 1);

        // Intl.Segmenter
        runtime.CreateIntlSegmenter = EmitReflectionHelper(typeBuilder, "CreateIntlSegmenter", 2);
        runtime.IntlSegmenterSegment = EmitReflectionHelper(typeBuilder, "IntlSegmenterSegment", 2);
        runtime.IntlSegmenterResolvedOptions = EmitReflectionHelper(typeBuilder, "IntlSegmenterResolvedOptions", 1);
    }
}
