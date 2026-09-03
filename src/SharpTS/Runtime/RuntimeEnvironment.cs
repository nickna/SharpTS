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
    private Dictionary<string, SharpTSNamespace>? _namespaces;
    private string? _directBindingName;
    private RuntimeValue _directBindingValue;
    private bool _hasDirectBinding;

    public RuntimeEnvironment(RuntimeEnvironment? enclosing = null, bool? strictMode = null)
        : base(enclosing, strictMode)
    {
    }

    /// <summary>
    /// Creates a scope with one binding stored inline. Catch clauses use this
    /// path so catching a primitive does not box it or allocate a dictionary.
    /// The environment remains a distinct object, preserving closure identity.
    /// </summary>
    internal RuntimeEnvironment(
        RuntimeEnvironment enclosing,
        string directBindingName,
        RuntimeValue directBindingValue)
        : base(enclosing)
    {
        _directBindingName = directBindingName;
        _directBindingValue = directBindingValue;
        _hasDirectBinding = true;
    }

    public override IEnumerable<string> Names
    {
        get
        {
            if (_hasDirectBinding)
                yield return _directBindingName!;
            if (_values != null)
            {
                foreach (string name in _values.Keys)
                    yield return name;
            }
        }
    }

    public override void Define(string name, RuntimeValue value)
        => DefineValue(name, value);

    private void DefineValue(string name, RuntimeValue value)
    {
        if (_hasDirectBinding && name == _directBindingName)
        {
            _directBindingValue = value;
            return;
        }
        Values[name] = value;
    }

    public override RuntimeValue Get(string name)
    {
        if (TryGetLocal(name, out RuntimeValue value))
            return value;
        return Enclosing != null ? Enclosing.Get(name) : default;
    }

    public override bool IsDefined(string name) =>
        TryGetLocal(name, out _) || (Enclosing?.IsDefined(name) ?? false);

    public override bool IsDefinedLocally(string name) =>
        TryGetLocal(name, out _);

    public RuntimeValue Get(Token name)
    {
        if (TryGetLocal(name.Lexeme, out RuntimeValue value))
        {
            return value;
        }

        if (Enclosing != null) return Enclosing.Get(name);

        // ECMA-262 §9.4.2: resolving an unbound name is a ReferenceError. The name prefix
        // is what routes this to a guest ReferenceError at the catch binding.
        throw new Exception($"Runtime Error: ReferenceError: Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Attempts to get a variable value in a single scope chain traversal.
    /// </summary>
    public bool TryGet(string name, out RuntimeValue value)
    {
        if (TryGetLocal(name, out value))
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
        if (_hasDirectBinding && name.Lexeme == _directBindingName)
        {
            _directBindingValue = value;
            return;
        }
        if (_values != null)
        {
            ref var slot = ref CollectionsMarshal.GetValueRefOrNullRef(_values, name.Lexeme);
            if (!Unsafe.IsNullRef(ref slot))
            {
                slot = value;
                return;
            }
        }

        if (Enclosing != null)
        {
            Enclosing.Assign(name, value);
            return;
        }

        // ECMA-262 §9.4.2: resolving an unbound name is a ReferenceError. The name prefix
        // is what routes this to a guest ReferenceError at the catch binding.
        throw new Exception($"Runtime Error: ReferenceError: Undefined variable '{name.Lexeme}'.");
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
        RuntimeEnvironment environment = Ancestor(distance);
        return environment.TryGetLocal(name, out RuntimeValue value) ? value : default;
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance.
    /// </summary>
    public void AssignAt(int distance, Token name, RuntimeValue value)
    {
        Ancestor(distance).DefineRV(name.Lexeme, value);
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance (legacy compatibility).
    /// </summary>
    public void AssignAt(int distance, Token name, object? value)
    {
        Ancestor(distance).DefineRV(name.Lexeme, RuntimeValue.FromBoxed(value));
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
        if (_namespaces?.TryGetValue(name, out var existing) == true)
        {
            // Merge: combine members from both namespace declarations
            existing.Merge(ns);
        }
        else
        {
            (_namespaces ??= [])[name] = ns;
            // Also define in values so it can be looked up as a variable
            DefineRV(name, RuntimeValue.FromObject(ns));
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public SharpTSNamespace? GetNamespace(string name)
    {
        if (_namespaces?.TryGetValue(name, out var ns) == true)
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
        return _namespaces != null && _namespaces.TryGetValue(name, out var ns)
            ? ns
            : null;
    }

    /// <summary>
    /// Defines a variable with an unboxed <see cref="RuntimeValue"/> — the fast path used by
    /// V2 parameter binding. Stores the value directly via the base scope chain, avoiding the
    /// box-then-unbox round-trip through <see cref="RuntimeValue.FromBoxed"/> that the
    /// <see cref="object"/>-typed <see cref="Define(string, object?)"/> incurs.
    /// (Named <c>DefineRV</c> rather than overloading <c>Define</c> because that boxing overload,
    /// declared here, would otherwise shadow the base <c>Define(string, RuntimeValue)</c> by name.)
    /// </summary>
    public void DefineRV(string name, RuntimeValue value) => DefineValue(name, value);

    /// <summary>
    /// Defines a variable with a boxed value (legacy compatibility).
    /// Wraps the value in RuntimeValue.FromBoxed automatically.
    /// </summary>
    public void Define(string name, object? value) =>
        DefineValue(name, RuntimeValue.FromBoxed(value));

    private bool TryGetLocal(string name, out RuntimeValue value)
    {
        if (_hasDirectBinding && name == _directBindingName)
        {
            value = _directBindingValue;
            return true;
        }
        if (_values?.TryGetValue(name, out value) == true)
            return true;
        value = default;
        return false;
    }

}
