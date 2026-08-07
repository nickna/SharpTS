using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// A lightweight callable wrapper around built-in constructor factory functions.
/// Registered as a global variable so that <c>typeof Map</c>, <c>val instanceof Map</c>,
/// passing <c>Map</c> as a value, and <c>Map.groupBy()</c> all work correctly.
/// </summary>
public sealed class SharpTSBuiltInConstructor : ISharpTSCallable
{
    private static readonly SharpTSDate DatePrototype = new(double.NaN)
    {
        IsPrototype = true,
    };

    public string Name { get; }
    private readonly BuiltInConstructorFactory.ConstructorHandler _factory;

    public SharpTSBuiltInConstructor(string name, BuiltInConstructorFactory.ConstructorHandler factory)
    {
        Name = name;
        _factory = factory;
    }

    public int Arity() => 0;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        // ECMA-262 §22.2.4.1: the `RegExp(...)` call form (NewTarget undefined)
        // does an IsRegExp brand check + same-constructor identity short-circuit
        // that needs interpreter access (Get(@@match)/Get("constructor") may
        // invoke user getters). Route it through the interpreter-aware helper;
        // the static `_factory` can't see the interpreter.
        if (Name == BuiltInNames.RegExp)
            return RegExpBuiltIns.ConstructRegExp(interpreter, arguments, isCallForm: true);
        return _factory(arguments);
    }

    /// <summary>
    /// Resolves static methods like <c>Map.groupBy()</c> via the built-in registry.
    /// Called by the interpreter's GetFieldsProperty dispatch chain via reflection.
    /// </summary>
    public object? GetMember(string name)
    {
        // ECMA-262 §22.2.6: RegExp.prototype is a regular object carrying the
        // five well-known-symbol-keyed protocol methods (@@match, @@matchAll,
        // @@replace, @@search, @@split). Surfacing it lets bracket access like
        // `RegExp.prototype[Symbol.match]` resolve to the callable.
        // RegExp.prototype lookup intentionally falls through to the caller
        // here — the receiver-aware path in Interpreter.EvaluateGetOnFallback
        // returns the per-Interpreter prototype because this constructor is
        // a process-wide singleton (Interpreter._globalConstants is static
        // readonly), so storing prototype state here would leak realm-local
        // mutations (delete / defineProperty) across all interpreters in
        // the process. RegExpBuiltIns.BuildPrototype() is still the source
        // of the per-realm object the Interpreter caches.

        if (name == "prototype" && Name == BuiltInNames.Date)
            return DatePrototype;

        return BuiltInRegistry.Instance.GetStaticMethod(Name, name);
    }

    public override string ToString() => $"function {Name}() {{ [native code] }}";
}
