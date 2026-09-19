using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the Intl.* constructor factories into the $Runtime class. Each is a
    /// late-bound <see cref="EmitReflectionHelper"/> wrapper over the same-named
    /// RuntimeTypes static (standalone-DLL soft dependency; the Intl feature records
    /// <see cref="EmittedDeploymentRequirements.Require"/> so SharpTS.dll is co-located).
    /// Instance methods (format/resolvedOptions/…) need no stubs: the factories return
    /// SharpTSIntl* runtime objects and calls dispatch reflectively onto those directly.
    /// </summary>
    private void EmitIntlMethods(TypeBuilder typeBuilder, EmittedIntlRuntime intl)
    {
        intl.CreateNumberFormat = EmitReflectionHelper(typeBuilder, "CreateIntlNumberFormat", 2);
        intl.CreateDateTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlDateTimeFormat", 2);
        intl.CreateCollator = EmitReflectionHelper(typeBuilder, "CreateIntlCollator", 2);
        intl.CreatePluralRules = EmitReflectionHelper(typeBuilder, "CreateIntlPluralRules", 2);
        intl.CreateRelativeTimeFormat = EmitReflectionHelper(typeBuilder, "CreateIntlRelativeTimeFormat", 2);
        intl.CreateListFormat = EmitReflectionHelper(typeBuilder, "CreateIntlListFormat", 2);
        intl.CreateDisplayNames = EmitReflectionHelper(typeBuilder, "CreateIntlDisplayNames", 2);
        intl.CreateSegmenter = EmitReflectionHelper(typeBuilder, "CreateIntlSegmenter", 2);
    }
}
