using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Manages local variable declarations and scoping during IL compilation.
/// </summary>
/// <remarks>
/// Tracks <see cref="LocalBuilder"/> instances by name with block-scoped lifetime.
/// Maintains a scope stack to properly dispose of locals when exiting blocks.
/// Used by <see cref="CompilationContext"/> and <see cref="ILEmitter"/> for variable
/// declaration and lookup during code generation.
/// </remarks>
/// <seealso cref="CompilationContext"/>
/// <seealso cref="ILEmitter"/>
public class LocalsManager
{
    private readonly ILGenerator _il;
    private readonly Dictionary<string, LocalBuilder> _locals = [];
    private readonly Dictionary<string, Type> _localTypes = [];
    private readonly Stack<HashSet<string>> _scopes = new();

    public LocalsManager(ILGenerator il)
    {
        _il = il;
        _scopes.Push([]); // Global scope
    }

    public LocalBuilder DeclareLocal(string name, Type type)
    {
        var local = _il.DeclareLocal(type);
        _locals[name] = local;
        _localTypes[name] = type;
        _scopes.Peek().Add(name);
        return local;
    }

    public LocalBuilder? GetLocal(string name)
    {
        return _locals.TryGetValue(name, out var local) ? local : null;
    }

    /// <summary>
    /// Gets the CLR type of a local variable.
    /// Returns null if the local doesn't exist.
    /// </summary>
    public Type? GetLocalType(string name)
    {
        return _localTypes.GetValueOrDefault(name);
    }

    public bool HasLocal(string name) => _locals.ContainsKey(name);

    public void EnterScope()
    {
        _scopes.Push([]);
    }

    public void ExitScope()
    {
        var scope = _scopes.Pop();
        foreach (var name in scope)
        {
            _locals.Remove(name);
            _localTypes.Remove(name);
        }
    }
}
