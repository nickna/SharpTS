using System.Reflection.Emit;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Receives the named locals and lexical scopes a method declares, so a debugger can show variables
/// by their TypeScript names instead of slot numbers.
/// </summary>
/// <remarks>
/// <see cref="LocalsManager"/> reports through this rather than knowing about symbols directly, and
/// it is the right place to listen: every binding the user wrote passes through it, while spill and
/// scratch slots are declared straight on the <see cref="ILGenerator"/> and never reach it. Naming
/// exactly what arrives here is therefore the whole of "name only user-visible locals".
/// </remarks>
internal interface ILocalSymbolSink
{
    /// <summary>A named binding was declared in the current scope.</summary>
    void LocalDeclared(string name, LocalBuilder local);

    /// <summary>A lexical scope opened at the current IL offset.</summary>
    void ScopeEntered();

    /// <summary>The innermost lexical scope closed at the current IL offset.</summary>
    void ScopeExited();
}
