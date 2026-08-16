using System.Reflection.Emit;
using SharpTS.Compilation.Symbols;

namespace SharpTS.Compilation;

/// <summary>
/// Manages local variable declarations and scoping during IL compilation.
/// Supports proper variable shadowing where inner scopes can declare variables
/// with the same name as outer scopes.
/// </summary>
/// <remarks>
/// Tracks <see cref="LocalBuilder"/> instances by name with block-scoped lifetime.
/// Uses a stack-based approach per variable name to support shadowing - when an
/// inner scope declares a variable with the same name as an outer scope, the inner
/// variable is pushed onto the stack. When the scope exits, the inner variable is
/// popped and the outer variable becomes visible again.
/// Used by <see cref="CompilationContext"/> and <see cref="ILEmitter"/> for variable
/// declaration and lookup during code generation.
/// </remarks>
/// <seealso cref="CompilationContext"/>
/// <seealso cref="ILEmitter"/>
public class LocalsManager(ILGenerator il)
{
    // Stack-based storage to support variable shadowing
    // Each variable name maps to a stack of (LocalBuilder, Type, Tag, ScopeDepth) entries. Tag is an
    // optional, emitter-defined marker attached to the binding (e.g. the Expr.ArrowFunction node of a
    // non-capturing non-escaping direct-call arrow, #858 follow-up): it lets a call site key off the
    // *actual in-scope binding* rather than the bare name, so a same-named parameter/local in another
    // scope (which carries no tag) can never be mistaken for it. Block-scoped like the local itself.
    // ScopeDepth records the _scopes depth at declaration (method root = 1), so shadow-sensitive
    // consumers can tell a block-scoped binding from a method-root one (#1222).
    private readonly Dictionary<string, Stack<(LocalBuilder Local, Type Type, object? Tag, int ScopeDepth)>> _localStacks = [];

    // Track which variables were declared in each scope for cleanup
    private readonly Stack<List<string>> _scopes = new([[]]);

    /// <summary>
    /// Receives declarations and scope boundaries for debug symbols. Null unless the build asked
    /// for them. Every binding the user wrote is declared here; scratch slots go straight to the
    /// <see cref="ILGenerator"/>, so listening here names user variables and nothing else.
    /// </summary>
    internal ILocalSymbolSink? SymbolSink { get; set; }

    public LocalBuilder DeclareLocal(string name, Type type) => DeclareLocal(name, type, tag: null);

    public LocalBuilder DeclareLocal(string name, Type type, object? tag)
    {
        var local = il.DeclareLocal(type);

        // Get or create the stack for this variable name
        if (!_localStacks.TryGetValue(name, out var stack))
        {
            stack = new Stack<(LocalBuilder, Type, object?, int)>();
            _localStacks[name] = stack;
        }

        // Push the new local onto the stack (shadows any outer variable with same name)
        stack.Push((local, type, tag, _scopes.Count));

        // Track that this name was declared in the current scope
        _scopes.Peek().Add(name);

        SymbolSink?.LocalDeclared(name, local);

        return local;
    }

    public LocalBuilder? GetLocal(string name)
    {
        if (_localStacks.TryGetValue(name, out var stack) && stack.Count > 0)
        {
            return stack.Peek().Local;
        }
        return null;
    }

    public bool TryGetLocal(string name, out LocalBuilder local)
    {
        if (_localStacks.TryGetValue(name, out var stack) && stack.Count > 0)
        {
            local = stack.Peek().Local;
            return true;
        }
        local = null!;
        return false;
    }

    /// <summary>
    /// Registers an already-declared local variable (for async state machine emission).
    /// </summary>
    public void RegisterLocal(string name, LocalBuilder local)
    {
        if (!_localStacks.TryGetValue(name, out var stack))
        {
            stack = new Stack<(LocalBuilder, Type, object?, int)>();
            _localStacks[name] = stack;
        }

        stack.Push((local, local.LocalType, null, _scopes.Count));

        if (_scopes.Count > 0)
            _scopes.Peek().Add(name);

        SymbolSink?.LocalDeclared(name, local);
    }

    /// <summary>
    /// Gets the CLR type of a local variable.
    /// Returns null if the local doesn't exist.
    /// </summary>
    public Type? GetLocalType(string name)
    {
        if (_localStacks.TryGetValue(name, out var stack) && stack.Count > 0)
        {
            return stack.Peek().Type;
        }
        return null;
    }

    public bool HasLocal(string name) =>
        _localStacks.TryGetValue(name, out var stack) && stack.Count > 0;

    /// <summary>
    /// Returns the in-scope local for <paramref name="name"/> ONLY when its current binding
    /// was declared inside a nested (block) scope; null for method-root locals and unbound
    /// names. A module-level binding never has such a local — module bindings live on the
    /// entry-point display class or a static field and their declarations never reach
    /// <see cref="DeclareLocal(string, Type)"/> — so a nested-scope local whose name also has
    /// a module-level home is always a lexical shadow of that binding (#1222).
    /// </summary>
    public LocalBuilder? GetNestedScopeLocal(string name)
    {
        if (_localStacks.TryGetValue(name, out var stack) && stack.Count > 0)
        {
            var binding = stack.Peek();
            if (binding.ScopeDepth > 1)
                return binding.Local;
        }
        return null;
    }

    /// <summary>
    /// Gets the optional emitter-defined tag attached to the in-scope binding for <paramref name="name"/>
    /// (see <see cref="DeclareLocal(string, Type, object?)"/>). Returns false when the name is unbound or
    /// its current binding carries no tag.
    /// </summary>
    public bool TryGetTag(string name, out object? tag)
    {
        if (_localStacks.TryGetValue(name, out var stack) && stack.Count > 0)
        {
            tag = stack.Peek().Tag;
            return tag != null;
        }
        tag = null;
        return false;
    }

    /// <summary>
    /// Returns true if we're inside a nested scope (scope depth > 1).
    /// Used to determine if variable shadowing should occur.
    /// </summary>
    public bool IsInNestedScope => _scopes.Count > 1;

    /// <summary>
    /// Returns the current scope depth. Base scope is 1, first nested scope is 2, etc.
    /// </summary>
    public int ScopeDepth => _scopes.Count;

    public void EnterScope()
    {
        _scopes.Push([]);
        SymbolSink?.ScopeEntered();
    }

    public void ExitScope()
    {
        SymbolSink?.ScopeExited();

        var scope = _scopes.Pop();
        foreach (var name in scope)
        {
            // Pop the innermost variable for this name
            // This restores visibility to any shadowed outer variable
            if (_localStacks.TryGetValue(name, out var stack))
            {
                stack.Pop();
                // Clean up empty stacks to avoid memory bloat
                if (stack.Count == 0)
                {
                    _localStacks.Remove(name);
                }
            }
        }
    }
}
