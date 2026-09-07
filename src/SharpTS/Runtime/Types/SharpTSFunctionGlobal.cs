using System.Text.RegularExpressions;
using SharpTS.Parsing;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Global <c>Function</c> constructor. The zero-argument form produces the
/// empty anonymous function. Source construction is limited to the documented
/// return-this package-compatibility grammar in both execution modes.
/// </summary>
public sealed class SharpTSFunctionGlobal : ISharpTSCallable
{
    public static readonly SharpTSFunctionGlobal Instance = new();
    private readonly SharpTSFunctionPrototype _prototype = new();
    private SharpTSFunctionGlobal() { }

    public int Arity() => 1;

    public object? Call(Execution.Interpreter interpreter, List<object?> arguments)
    {
        string body = "";
        if (arguments.Count != 0
            && (arguments.Count != 1 || arguments[0] is not string source
                || !Regex.IsMatch(source, FunctionConstructorContract.ReturnThisPattern, RegexOptions.NonBacktracking)))
        {
            throw new Exception(FunctionConstructorContract.UnsupportedSourceMessage);
        }
        if (arguments.Count == 1)
            body = (string)arguments[0]!;

        // Use ordinary function execution and receiver binding. The supported
        // bodies reference no bindings; an isolated sloppy scope prevents the
        // caller's lexical variables, this, or strictness from leaking in.
        var tokens = new Lexer("const fn = function anonymous() {\n" + body + "\n};").ScanTokens();
        var declaration = (Stmt.Const)new Parser(tokens).ParseOrThrow()[0];
        return new SharpTSArrowFunction((Expr.ArrowFunction)declaration.Initializer,
            new RuntimeEnvironment(strictMode: false), hasOwnThis: true);
    }

    public object? GetMember(string name)
    {
        if (name == "prototype") return _prototype;
        return null;
    }

    public override string ToString() => "function Function() { [native code] }";
}

/// <summary>
/// <c>Function.prototype</c> accessor. Returns the unbound <c>call</c>/
/// <c>apply</c>/<c>bind</c> singletons that <see cref="BuiltIns.FunctionBuiltIns"/>
/// also exposes for instance-level dispatch — so
/// <c>Function.prototype.call</c> and <c>fn.call</c> resolve to the same
/// callable, and <c>Function.prototype.call.bind(hasOwn)</c> composes with
/// the BuiltInMethod rebind path that real-world test262 harness code (e.g.
/// <c>propertyHelper.js</c>) relies on.
/// </summary>
public sealed class SharpTSFunctionPrototype : ISharpTSMutableBuiltIn, ISharpTSSymbolPropertyBag
{
    private readonly PrototypePropertyOverlay _overlay = new(IsBuiltIn);

    public bool HasExtra(string name) => _overlay.HasExtra(name);
    public object? TryGetExtra(string name) => _overlay.GetProperty(name);
    public void SetExtra(string name, object? value) => _overlay.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _overlay.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _overlay.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _overlay.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _overlay.GetSetter(name);

    bool ISharpTSSymbolPropertyBag.HasSymbolProperty(SharpTSSymbol symbol)
        => _overlay.HasSymbolProperty(symbol);
    object? ISharpTSSymbolPropertyBag.GetBySymbol(SharpTSSymbol symbol)
        => _overlay.GetBySymbol(symbol);
    bool ISharpTSSymbolPropertyBag.TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
        => _overlay.TryGetSymbolAccessor(symbol, out getter, out setter);
    void ISharpTSSymbolPropertyBag.SetBySymbolStrict(
        SharpTSSymbol symbol, object? value, bool strictMode)
        => _overlay.SetBySymbolStrict(symbol, value, strictMode);

    private static bool IsBuiltIn(string name)
        => BuiltIns.FunctionBuiltIns.GetPrototypeMethod(name) != null
            || name is "toString" or "constructor";

    public bool HasOwnProperty(string name) => _overlay.HasOwnProperty(name);

    public bool DeleteProperty(string name) => _overlay.DeleteProperty(name);

    public IEnumerable<string> OwnEnumerableKeys() => _overlay.OwnEnumerableKeys();

    public object? GetMember(string name)
    {
        if (_overlay.TryGetOverride(name, out var value)) return value;
        var method = BuiltIns.FunctionBuiltIns.GetPrototypeMethod(name);
        if (method != null) return method;
        if (name == "toString") return SharpTSFunctionProtoToString.Instance;
        if (name == "constructor") return SharpTSFunctionGlobal.Instance;
        return null;
    }

    public override string ToString() => "[object Function]";
}

/// <summary>
/// Unbound <c>Function.prototype.toString</c>. When invoked via
/// <c>.call(fn)</c> or <c>.apply(fn)</c>, returns a native-source-like string
/// for the bound function — enough to satisfy lodash's regex-based native
/// detection.
/// </summary>
public sealed class SharpTSFunctionProtoToString : ISharpTSCallable
{
    public static readonly SharpTSFunctionProtoToString Instance = new();
    private readonly object? _boundThis;
    private SharpTSFunctionProtoToString(object? boundThis = null) { _boundThis = boundThis; }

    public int Arity() => 0;

    public object? Call(Execution.Interpreter interpreter, List<object?> arguments)
    {
        var target = _boundThis ?? (arguments.Count > 0 ? arguments[0] : null);
        return target?.ToString() ?? "function () { [native code] }";
    }

    public SharpTSFunctionProtoToString BindTo(object? thisArg) => new(thisArg);

    public override string ToString() => "function toString() { [native code] }";
}
