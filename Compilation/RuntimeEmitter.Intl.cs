using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the Intl.* constructor factories into the $Runtime class. Each is a
    /// late-bound <see cref="EmitReflectionHelper"/> wrapper over the same-named
    /// RuntimeTypes static (standalone-DLL soft dependency; the Intl feature records
    /// <see cref="EmittedRuntime.RequireSharpTSRuntime"/> so SharpTS.dll is co-located).
    /// Instance methods (format/resolvedOptions/…) need no stubs: the factories return
    /// SharpTSIntl* runtime objects and calls dispatch reflectively onto those directly.
    /// </summary>
    private void EmitIntlMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.CreateIntlNumberFormat = EmitReflectionHelper(typeBuilder, "CreateIntlNumberFormat", 2);
        runtime.CreateIntlDateTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlDateTimeFormat", 2);
        runtime.CreateIntlCollator = EmitReflectionHelper(typeBuilder, "CreateIntlCollator", 2);
        runtime.CreateIntlPluralRules = EmitReflectionHelper(typeBuilder, "CreateIntlPluralRules", 2);
        runtime.CreateIntlRelativeTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlRelativeTimeFormat", 2);
        runtime.CreateIntlListFormat = EmitReflectionHelper(typeBuilder, "CreateIntlListFormat", 2);
        runtime.CreateIntlDisplayNames = EmitReflectionHelper(typeBuilder, "CreateIntlDisplayNames", 2);
        runtime.CreateIntlSegmenter = EmitReflectionHelper(typeBuilder, "CreateIntlSegmenter", 2);
    }
}
