namespace SharpTS.Diagnostics.Exceptions;

/// <summary>
/// Signals that the current runtime SKU cannot provide a capability that is available from the
/// managed SharpTS host. Carries the stable <c>SHARPTS007</c> diagnostic across service and CLI
/// boundaries so callers never need to infer the condition from prose.
/// </summary>
public sealed class ManagedBuildRequiredException : SharpTSException
{
    /// <summary>Creates a managed-build-required diagnostic.</summary>
    public ManagedBuildRequiredException(string feature, string? context = null)
        : base(CreateDiagnostic(feature, context))
    {
    }

    /// <summary>Creates a managed-build-required diagnostic with its underlying failure.</summary>
    public ManagedBuildRequiredException(string feature, string? context, Exception innerException)
        : base(CreateDiagnostic(feature, context), innerException)
    {
    }

    /// <summary>
    /// Creates the structured diagnostic for non-throwing CLI gates that terminate immediately.
    /// </summary>
    public static Diagnostic CreateDiagnostic(string feature, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        string message =
            $"{feature} is not available in the native SharpTS build — use the managed build.";
        if (!string.IsNullOrWhiteSpace(context))
            message += $" {context}";

        return new Diagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCode.ManagedBuildRequired,
            message);
    }
}
