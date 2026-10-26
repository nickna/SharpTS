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
        internal List<(string Name, int Slot, bool IsUser)> Locals { get; } = [];

        /// <summary>True for the method-body scope, whose end is the end of the method.</summary>
        internal bool IsOpen => EndOffset < 0;
    }

    private readonly List<Scope> _scopes = [];
    private readonly Stack<Scope> _open = new();
    private readonly List<(string Name, int Slot, StateMachineDebugSymbols.Scope Scope)> _projectedDisplayClasses = [];

    internal MethodBase Method { get; } = method;
    internal StateMachineDebugSymbols? HoistedSymbols { get; set; }

    /// <summary>All scopes recorded, in the order they were opened.</summary>
    internal IReadOnlyList<Scope> Scopes => _scopes;

    /// <summary>Whether anything worth writing was recorded.</summary>
    internal bool HasLocals => _projectedDisplayClasses.Count > 0 || _scopes.Any(scope => scope.Locals.Count > 0);

    internal void AddProjectedDisplayClassLocal(string name, LocalBuilder local, StateMachineDebugSymbols.Scope scope)
        => _projectedDisplayClasses.Add((name, local.LocalIndex, scope));

    internal IReadOnlyList<Scope> GetScopesWithProjections()
    {
        var scopes = _scopes.Select(original =>
        {
            var copy = new Scope(original.StartOffset) { EndOffset = original.EndOffset };
            copy.Locals.AddRange(original.Locals);
            return copy;
        }).ToList();
        foreach (var (name, slot, lexicalScope) in _projectedDisplayClasses)
        {
            if (!lexicalScope.IsRoot && (lexicalScope.StartOffset == int.MaxValue
                || lexicalScope.EndOffset <= lexicalScope.StartOffset)) continue;
            int start = lexicalScope.IsRoot ? 0 : lexicalScope.StartOffset;
            int end = lexicalScope.IsRoot ? -1 : lexicalScope.EndOffset;
            var scope = scopes.FirstOrDefault(scope => scope.StartOffset == start && scope.EndOffset == end);
            if (scope is null)
            {
                scope = new Scope(start) { EndOffset = end };
                scopes.Add(scope);
            }
            scope.Locals.Add((name, slot, false));
        }
        return scopes;
    }

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

        var binding = HoistedSymbols?.GetBinding(name);
        // Catch/loop lowering may keep a scratch IL slot even when the authoritative binding
        // lives in a field. Naming that reset-on-reentry slot would shadow the correct field.
        if (binding?.Field is not null || binding?.DisplayClassSlot is not null) return;
        _open.Peek().Locals.Add((binding?.SourceName ?? name, local.LocalIndex, binding is not null));
    }
}
