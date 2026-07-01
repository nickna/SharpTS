using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// Manages variable scopes during runtime interpretation.
/// </summary>
/// <remarks>
/// Implements a linked list of scopes via the <see cref="ScopeChain{TValue,TSelf}.Enclosing"/> property.
/// Each scope holds variable bindings in a dictionary. Variable lookup (Get) and
/// assignment (Assign) walk up the scope chain until found. Used by <see cref="Interpreter"/>
/// for lexical scoping and by <see cref="SharpTSFunction"/> for closures.
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
public class RuntimeEnvironment : ScopeChain<RuntimeValue, RuntimeEnvironment>
{
    private readonly Dictionary<string, SharpTSNamespace> _namespaces = [];

    public RuntimeEnvironment(RuntimeEnvironment? enclosing = null, bool? strictMode = null)
        : base(enclosing, strictMode)
    {
    }

    public RuntimeValue Get(Token name)
    {
        if (_values.TryGetValue(name.Lexeme, out var value))
        {
            return value;
        }

        if (Enclosing != null) return Enclosing.Get(name);

        throw new Exception($"Runtime Error: Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Attempts to get a variable value in a single scope chain traversal.
    /// </summary>
    public bool TryGet(string name, out RuntimeValue value)
    {
        if (_values.TryGetValue(name, out value))
        {
            return true;
        }

        if (Enclosing != null)
        {
            return Enclosing.TryGet(name, out value);
        }

        value = RuntimeValue.Undefined;
        return false;
    }

    public void Assign(Token name, RuntimeValue value)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrNullRef(_values, name.Lexeme);
        if (!Unsafe.IsNullRef(ref slot))
        {
            slot = value;
            return;
        }

        if (Enclosing != null)
        {
            Enclosing.Assign(name, value);
            return;
        }

        throw new Exception($"Runtime Error: Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Assigns a variable with a boxed value (legacy compatibility).
    /// </summary>
    public void Assign(Token name, object? value) => Assign(name, RuntimeValue.FromBoxed(value));

    /// <summary>
    /// Gets a variable value at a specific scope distance.
    /// </summary>
    public RuntimeValue GetAt(int distance, string name)
    {
        return Ancestor(distance)._values.GetValueOrDefault(name);
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance.
    /// </summary>
    public void AssignAt(int distance, Token name, RuntimeValue value)
    {
        Ancestor(distance)._values[name.Lexeme] = value;
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance (legacy compatibility).
    /// </summary>
    public void AssignAt(int distance, Token name, object? value)
    {
        Ancestor(distance)._values[name.Lexeme] = RuntimeValue.FromBoxed(value);
    }

    /// <summary>
    /// Traverses up the scope chain a specific number of steps.
    /// </summary>
    private RuntimeEnvironment Ancestor(int distance)
    {
        RuntimeEnvironment environment = this;
        for (int i = 0; i < distance; i++)
        {
            environment = environment.Enclosing!;
        }
        return environment;
    }

    /// <summary>
    /// Defines or merges a namespace in the current scope.
    /// If a namespace with the same name already exists, merges the members.
    /// </summary>
    public void DefineNamespace(string name, SharpTSNamespace ns)
    {
        if (_namespaces.TryGetValue(name, out var existing))
        {
            // Merge: combine members from both namespace declarations
            existing.Merge(ns);
        }
        else
        {
            _namespaces[name] = ns;
            // Also define in values so it can be looked up as a variable
            _values[name] = RuntimeValue.FromObject(ns);
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public SharpTSNamespace? GetNamespace(string name)
    {
        if (_namespaces.TryGetValue(name, out var ns))
            return ns;
        return Enclosing?.GetNamespace(name);
    }

    /// <summary>
    /// Gets a namespace by name from THIS scope only (no chain traversal).
    /// Use when deciding whether to merge vs. create a new namespace declaration —
    /// avoids treating a same-named namespace in an enclosing scope as a merge target (#746).
    /// </summary>
    public SharpTSNamespace? GetLocalNamespace(string name)
    {
        _namespaces.TryGetValue(name, out var ns);
        return ns;
    }

    /// <summary>
    /// Defines a variable with an unboxed <see cref="RuntimeValue"/> — the fast path used by
    /// V2 parameter binding. Stores the value directly via the base scope chain, avoiding the
    /// box-then-unbox round-trip through <see cref="RuntimeValue.FromBoxed"/> that the
    /// <see cref="object"/>-typed <see cref="Define(string, object?)"/> incurs.
    /// (Named <c>DefineRV</c> rather than overloading <c>Define</c> because that boxing overload,
    /// declared here, would otherwise shadow the base <c>Define(string, RuntimeValue)</c> by name.)
    /// </summary>
    public void DefineRV(string name, RuntimeValue value) => base.Define(name, value);

    /// <summary>
    /// Defines a variable with a boxed value (legacy compatibility).
    /// Wraps the value in RuntimeValue.FromBoxed automatically.
    /// </summary>
    public void Define(string name, object? value) => _values[name] = RuntimeValue.FromBoxed(value);

}
