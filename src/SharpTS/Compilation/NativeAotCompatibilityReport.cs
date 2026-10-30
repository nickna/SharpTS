using System.Collections.Immutable;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>Generated-guest compatibility, independent of compiler-host AOT analysis.</summary>
public sealed record NativeAotCompatibilityReport(
    SharpTSRuntimeRequirements RuntimeRequirements,
    IReadOnlyList<string> FeatureReasons,
    bool HasExternalReferences,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Whether this guest fits the first experimental native profile.</summary>
    public bool IsCompatible => Diagnostics.Count == 0;

    /// <summary>Checks emitted capabilities and the explicit static reference set before writing output.</summary>
    public static NativeAotCompatibilityReport Analyze(SharpTSRuntimeRequirements requirements,
        IEnumerable<string> reasons, bool hasExternalReferences)
    {
        var features = reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (requirements != SharpTSRuntimeRequirements.None)
            diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.NativeGuestRuntimeDependency,
                "Experimental native guest output requires a standalone guest. Unsupported features: " +
                (features.IsEmpty ? requirements.ToString() : string.Join(", ", features)) +
                ". Use --target dll or --target exe for these features."));
        if (hasExternalReferences)
            diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.NativeGuestExternalReferences,
                "Experimental native guest output does not yet support third-party CLI, manifest or NuGet references. " +
                "Known BCL interop without external references is supported; use managed output for external libraries."));
        return new(requirements, features, hasExternalReferences, diagnostics.ToImmutable());
    }

    internal static NativeAotCompatibilityReport Analyze(SharpTSRuntimeRequirements requirements,
        IEnumerable<string> reasons, NativeManagedReferenceClosure closure) =>
        Analyze(requirements, reasons, false) with { HasExternalReferences = !closure.IsEmpty };

    /// <summary>Stops emission with the first named profile diagnostic.</summary>
    public void ThrowIfIncompatible()
    {
        if (Diagnostics.FirstOrDefault() is { } diagnostic) throw new CompileException(diagnostic);
    }
}
