using SharpTS.Diagnostics;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Thrown by the <see cref="TestHarness"/> module runners when the type checker records
/// error-severity diagnostics after <c>CheckModules</c> (#1226). <c>CheckModules</c> records
/// errors with recovery instead of throwing, and the interpreter/compiler usually runs the
/// ill-typed program just fine — so without this check a type-check regression in a stdlib
/// facade (or in a test program) is invisible to the suite. Mirrors the CLI
/// (<c>Program.RunModuleFile</c>), which aborts on any error-severity diagnostic. Tests that
/// intentionally run ill-typed programs opt out via <c>allowTypeErrors: true</c>.
/// </summary>
public sealed class TypeCheckDiagnosticException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public TypeCheckDiagnosticException(IReadOnlyList<Diagnostic> diagnostics)
        : base($"Module type checking produced {diagnostics.Count} error(s):\n  " +
               string.Join("\n  ", diagnostics.Select(d => d.ToString())))
    {
        Diagnostics = diagnostics;
    }
}
