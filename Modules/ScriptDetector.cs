using SharpTS.Parsing;

namespace SharpTS.Modules;

/// <summary>
/// Detects whether a file is a script or module based on TypeScript semantics.
/// </summary>
/// <remarks>
/// In TypeScript:
/// - **Scripts**: Files without any import/export statements - share global scope
/// - **Modules**: Files with import/export statements - have isolated scope
///
/// Triple-slash path references are primarily designed for script files,
/// where referenced file declarations merge into the global scope.
/// </remarks>
public static class ScriptDetector
{
    /// <summary>
    /// Determines if a file is a script (no import/export) or module.
    /// </summary>
    /// <param name="statements">The parsed statements from the file.</param>
    /// <returns>True if the file is a script (no imports or exports), false if it's a module.</returns>
    public static bool IsScriptFile(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            // Import statement makes this a module
            if (stmt is Stmt.Import)
                return false;

            // Export statement makes this a module
            if (stmt is Stmt.Export)
                return false;

            // Namespace with IsExported = true makes this a module
            if (stmt is Stmt.Namespace { IsExported: true })
                return false;

            // ImportAlias with IsExported = true makes this a module (export import X = ...)
            if (stmt is Stmt.ImportAlias { IsExported: true })
                return false;

            // ImportRequire makes this a module (import x = require(...))
            if (stmt is Stmt.ImportRequire)
                return false;
        }

        return true;
    }
}
