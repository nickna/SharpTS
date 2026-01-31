using System.Reflection.Emit;

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
    // Each variable name maps to a stack of (LocalBuilder, Type) pairs
    private readonly Dictionary<string, Stack<(LocalBuilder Local, Type Type)>> _localStacks = [];

    // Track which variables were declared in each scope for cleanup
    private readonly Stack<List<string>> _scopes = new([[]]);

    public LocalBuilder DeclareLocal(string name, Type type)
    {
        var local = il.DeclareLocal(type);

        // Get or create the stack for this variable name
        if (!_localStacks.TryGetValue(name, out var stack))
        {
            stack = new Stack<(LocalBuilder, Type)>();
            _localStacks[name] = stack;
        }

        // Push the new local onto the stack (shadows any outer variable with same name)
        stack.Push((local, type));

        // Track that this name was declared in the current scope
        _scopes.Peek().Add(name);

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
            stack = new Stack<(LocalBuilder, Type)>();
            _localStacks[name] = stack;
        }

        stack.Push((local, local.LocalType));

        if (_scopes.Count > 0)
            _scopes.Peek().Add(name);
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
    }

    public void ExitScope()
    {
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
