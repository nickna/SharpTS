namespace SharpTS.Parsing;

/// <summary>
/// Directive-prologue queries shared by every phase (ECMA-262 §11.2.1: the
/// prologue is the longest leading run of directive statements). One
/// implementation replaces the four per-phase CheckForUseStrict copies
/// (Interpreter, SharpTSFunction, SharpTSArrowFunction, ILCompiler) so strict
/// -mode detection cannot drift between phases (2026-07 cleanup).
/// </summary>
public static class DirectivePrologue
{
    /// <summary>
    /// True when the directive prologue of <paramref name="statements"/>
    /// contains the "use strict" directive.
    /// </summary>
    public static bool HasUseStrict(List<Stmt>? statements)
    {
        if (statements == null) return false;
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
                // Keep scanning the rest of the directive prologue.
            }
            else
            {
                // First non-directive statement ends the prologue.
                break;
            }
        }
        return false;
    }
}
