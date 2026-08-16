using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Collects one method's named locals and the lexical scopes they live in, as the method is
/// emitted.
/// </summary>
/// <remarks>
/// <para>Scopes nest, so they are tracked with a stack: opening one records the IL offset it starts
/// at, closing it records the offset it ends at. The scope <see cref="LocalsManager"/> starts life
/// with — the method body itself — is never explicitly closed, so its length is filled in from the
/// finished method's IL size (see <see cref="DebugInfoCollector.BuildPdbMetadata"/>).</para>
///
/// <para>A debugger resolves a name by walking outwards from the innermost scope containing the
/// current instruction, which is what makes shadowing work: an inner <c>let x</c> is found before
/// the outer one, and only for the range where it is actually in scope.</para>
/// </remarks>
internal sealed class MethodLocalSymbols(MethodBase method, ILGenerator il) : ILocalSymbolSink
{
    /// <summary>A lexical scope: an IL range and the names declared directly in it.</summary>
    internal sealed class Scope(int startOffset)
    {
        internal int StartOffset { get; } = startOffset;
        internal int EndOffset { get; set; } = -1;
        internal List<(string Name, int Slot)> Locals { get; } = [];

        /// <summary>True for the method-body scope, whose end is the end of the method.</summary>
        internal bool IsOpen => EndOffset < 0;
    }

    private readonly List<Scope> _scopes = [];
    private readonly Stack<Scope> _open = new();

    internal MethodBase Method { get; } = method;

    /// <summary>All scopes recorded, in the order they were opened.</summary>
    internal IReadOnlyList<Scope> Scopes => _scopes;

    /// <summary>Whether anything worth writing was recorded.</summary>
    internal bool HasLocals => _scopes.Any(scope => scope.Locals.Count > 0);

    /// <summary>Opens the method-body scope. Called once, before emission.</summary>
    internal void Begin()
    {
        var root = new Scope(0);
        _scopes.Add(root);
        _open.Push(root);
    }

    public void ScopeEntered()
    {
        var scope = new Scope(il.ILOffset);
        _scopes.Add(scope);
        _open.Push(scope);
    }

    public void ScopeExited()
    {
        // Defensive: an emitter that closes more scopes than it opened must not take the method
        // body's scope with it, or every local would lose its range.
        if (_open.Count <= 1) return;

        _open.Pop().EndOffset = il.ILOffset;
    }

    public void LocalDeclared(string name, LocalBuilder local)
    {
        if (_open.Count == 0) return;

        _open.Peek().Locals.Add((name, local.LocalIndex));
    }
}
