using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Maps a guest-visible vm.Module facade (a Dictionary) back to its <see cref="VmModuleBase"/>.
/// Uses a ConditionalWeakTable so modules can be collected normally and so the linker can
/// return facades that we resolve back to their backing module.
/// </summary>
public static class VmModuleRegistry
{
    private static readonly ConditionalWeakTable<object, VmModuleBase> _modules = new();

    public static void Register(object facade, VmModuleBase module) => _modules.AddOrUpdate(facade, module);

    public static bool IsModule(object? facade) => facade != null && _modules.TryGetValue(facade, out _);

    public static VmModuleBase? Get(object? facade)
        => facade != null && _modules.TryGetValue(facade, out var m) ? m : null;
}

/// <summary>
/// Base class for the vm ESM-in-vm Module API (vm.Module). Implements the shared status
/// state machine (unlinked → linking → linked → evaluating → evaluated/errored), the
/// guest-visible facade dictionary, the namespace, and link/evaluate scaffolding.
/// Subclasses (<see cref="VmSourceTextModule"/>, vm.SyntheticModule) supply how
/// dependencies are discovered and how the module body is evaluated.
/// </summary>
public abstract class VmModuleBase
{
    private static int _nextId;

    public string Identifier { get; protected init; } = "";
    public object? ContextObject { get; protected init; }
    public string Status { get; private set; } = "unlinked";
    public object? Error { get; private set; }

    /// <summary>Exported name → value. Serves as the (live-after-evaluation) namespace object.</summary>
    protected readonly Dictionary<string, object?> Namespace = new();

    /// <summary>Resolved dependency modules, keyed by specifier (populated by link()).</summary>
    protected readonly Dictionary<string, VmModuleBase> Linked = new();

    /// <summary>The guest-visible facade. Its status/error/namespace entries are kept in sync.</summary>
    protected Dictionary<string, object?> Facade = null!;

    /// <summary>The user-supplied importModuleDynamically hook, if any (wired in #1156).</summary>
    public object? ImportModuleDynamically { get; protected init; }

    protected static int NextId() => Interlocked.Increment(ref _nextId);

    public abstract IReadOnlyList<string> DependencySpecifiers { get; }

    protected void SetStatus(string status)
    {
        Status = status;
        if (Facade != null) Facade["status"] = status;
    }

    protected void SetError(object? error)
    {
        Error = error;
        if (Facade != null) Facade["error"] = error;
    }

    /// <summary>
    /// Builds the guest-visible facade dictionary (status / namespace / identifier /
    /// dependencySpecifiers / error / context + the link/evaluate/instantiate/createCachedData
    /// methods). Works in both modes: the interpreter reads dictionaries via the IDictionary
    /// fallback, compiled code via GetFieldsProperty.
    /// </summary>
    public Dictionary<string, object?> BuildFacade(string sourceForCache)
    {
        var depsArray = new SharpTSArray(DependencySpecifiers.Select(s => (object?)s).ToList());

        Facade = new Dictionary<string, object?>
        {
            ["status"] = Status,
            ["identifier"] = Identifier,
            ["dependencySpecifiers"] = depsArray,
            ["namespace"] = Namespace,
            ["error"] = null,
            ["context"] = ContextObject,
            ["link"] = BuiltInMethod.CreateV2("link", 1, 1, (interp, recv, a) =>
            {
                var linker = a.Length > 0 ? a[0].ToObject() : null;
                Link(interp, linker);
                // Synchronous-completing; resolves to undefined (see class remarks).
                return RuntimeValue.Undefined;
            }),
            ["evaluate"] = BuiltInMethod.CreateV2("evaluate", 0, 1, (interp, recv, a) =>
            {
                Evaluate(interp);
                return RuntimeValue.Undefined;
            }),
            ["instantiate"] = BuiltInMethod.CreateV2("instantiate", 0, 0, (interp, recv, a) =>
            {
                // Legacy no-op: linking already prepared the graph.
                return RuntimeValue.Undefined;
            }),
            ["createCachedData"] = BuiltInMethod.CreateV2("createCachedData", 0, 0, (interp, recv, a) =>
                RuntimeValue.FromObject(SharpTSBuffer.FromString(sourceForCache, "utf8"))),
        };

        ConfigureFacade(Facade);
        VmModuleRegistry.Register(Facade, this);
        return Facade;
    }

    /// <summary>Subclass hook to add type-specific facade members (e.g. SyntheticModule.setExport).</summary>
    protected virtual void ConfigureFacade(Dictionary<string, object?> facade) { }

    /// <summary>Sets a named export value (vm.SyntheticModule.setExport); updates the namespace.</summary>
    protected void SetExport(string name, object? value)
    {
        Namespace[name] = value;
        if (Facade != null) Facade["namespace"] = Namespace;
    }

    /// <summary>
    /// link(linker): resolves every dependency specifier through the user linker and
    /// recursively links the resulting modules. unlinked → linking → linked.
    /// NOTE: completes synchronously (the linker is expected to return a Module, or a
    /// settled promise of one); the facade method returns undefined so `await module.link(..)`
    /// works identically in interpreter and compiled mode (a cross-boundary SharpTSPromise is
    /// not unwrapped by compiled await).
    /// </summary>
    public void Link(Interp? interp, object? linker)
    {
        if (Status != "unlinked")
            throw new Exception($"TypeError: module.link() called on a module with status '{Status}'");

        SetStatus("linking");
        try
        {
            foreach (var specifier in DependencySpecifiers)
            {
                var resolved = CallGuest(interp, linker, thisArg: null, [specifier, Facade]);
                resolved = UnwrapSettled(resolved);
                var child = VmModuleRegistry.Get(resolved)
                    ?? throw new Exception($"TypeError: linker for '{specifier}' did not return a vm.Module");
                Linked[specifier] = child;
                if (child.Status == "unlinked")
                    child.Link(interp, linker);
            }
            SetStatus("linked");
        }
        catch (Runtime.Exceptions.ThrowException te)
        {
            SetError(te.Value);
            SetStatus("errored");
            throw;
        }
        catch (Exception ex)
        {
            // A compiled linker throws a host exception (often wrapped in
            // TargetInvocationException by reflection). Reconstruct a guest Error and
            // re-raise it as a ThrowException so guest try/catch sees it in both modes.
            var err = ErrorBuiltIns.CreateError("Error", [Unwrap(ex).Message]);
            SetError(err);
            SetStatus("errored");
            throw new Runtime.Exceptions.ThrowException(err);
        }
    }

    private static Exception Unwrap(Exception ex)
        => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

    /// <summary>
    /// evaluate(): evaluates dependencies first, then this module's body.
    /// linked → evaluating → evaluated, or → errored (and records error) on failure.
    /// </summary>
    public void Evaluate(Interp? interp)
    {
        if (Status == "evaluated") return;
        if (Status == "errored")
            throw new Runtime.Exceptions.ThrowException(Error);
        if (Status != "linked" && Status != "evaluating")
            throw new Exception($"TypeError: module.evaluate() called on a module with status '{Status}'");

        SetStatus("evaluating");
        try
        {
            foreach (var child in Linked.Values)
                if (child.Status != "evaluated")
                    child.Evaluate(interp);

            EvaluateBody(interp);
            SetStatus("evaluated");
        }
        catch (Runtime.Exceptions.ThrowException te)
        {
            SetError(te.Value);
            SetStatus("errored");
            throw;
        }
        catch (Exception ex)
        {
            SetError(ErrorBuiltIns.CreateError("Error", [Unwrap(ex).Message]));
            SetStatus("errored");
            throw;
        }
    }

    /// <summary>Subclass hook: run the module body, populating <see cref="Namespace"/>.</summary>
    protected abstract void EvaluateBody(Interp? interp);

    /// <summary>The exported bindings (used by dependents that import this module).</summary>
    public IReadOnlyDictionary<string, object?> Exports => Namespace;

    /// <summary>
    /// Invokes a guest callable (an interpreter ISharpTSCallable or a compiled
    /// $TSFunction) with the given <c>this</c> value and boxed args. Used for the
    /// linker (this = null) and the SyntheticModule evaluateCallback (this = the
    /// module facade, so <c>this.setExport(...)</c> resolves).
    /// </summary>
    protected static object? CallGuest(Interp? interp, object? callable, object? thisArg, object?[] args)
    {
        if (callable is ISharpTSCallable c)
            return FunctionBuiltIns.CallWithThis(interp!, c, thisArg, args.ToList());
        if (callable == null)
            throw new Exception("TypeError: callback is not a function");

        var type = callable.GetType();
        if (ManagedEmittedShapeReflection.IsShape(type, ManagedEmittedShape.Function))
        {
            var withThis = ManagedEmittedShapeReflection.GetPublicMethod(
                type, ManagedEmittedShape.Function, "InvokeWithThis",
                [typeof(object), typeof(object[])]);
            if (withThis != null)
                return withThis.Invoke(callable, [thisArg, args]);

            var emittedInvoke = ManagedEmittedShapeReflection.GetPublicMethod(
                type, ManagedEmittedShape.Function, "Invoke", [typeof(object[])]);
            if (emittedInvoke != null)
                return emittedInvoke.Invoke(callable, [args]);
        }

        // Managed .NET interop also accepts an arbitrary object exposing
        // Invoke(object[]) through the managed-only structural compatibility
        // seam.
        var invoke = ManagedStructuralClrReflection.TryGetPublicMethodBySignature(
            type, "Invoke", [typeof(object[])]);
        if (invoke != null)
            return invoke.Invoke(callable, [args]);
        throw new Exception("TypeError: callback is not a function");
    }

    /// <summary>If a settled SharpTSPromise was returned by the linker, unwrap to its value.</summary>
    protected static object? UnwrapSettled(object? value)
    {
        if (value is SharpTSPromise p && p.Task.IsCompletedSuccessfully)
            return p.Task.Result;
        return value;
    }
}

/// <summary>
/// vm.SourceTextModule — an ES module compiled from source text and run inside the vm's
/// hosted interpreter. Dependencies are discovered by scanning the source's top-level
/// import statements; link() resolves them via the user linker; evaluate() runs the body
/// (imports bound from linked namespaces, exports collected into the namespace).
/// </summary>
public sealed class VmSourceTextModule : VmModuleBase
{
    private readonly List<Stmt> _statements;
    private readonly List<string> _dependencySpecifiers;

    public override IReadOnlyList<string> DependencySpecifiers => _dependencySpecifiers;

    public VmSourceTextModule(string code, object? options)
    {
        Identifier = VmModuleInterpreter.GetStringOption(options, "identifier") ?? $"vm:module({NextId()})";
        ContextObject = VmModuleInterpreter.GetOption(options, "context");
        ImportModuleDynamically = VmModuleInterpreter.GetOption(options, "importModuleDynamically");

        _statements = VmModuleInterpreter.ParseCode(code, Identifier);

        // Dependency specifiers = the module paths of the top-level (non type-only) imports,
        // in source order, de-duplicated (matching Node).
        _dependencySpecifiers = new List<string>();
        var seen = new HashSet<string>();
        foreach (var stmt in _statements)
            if (stmt is Stmt.Import imp && !imp.IsTypeOnly && seen.Add(imp.ModulePath))
                _dependencySpecifiers.Add(imp.ModulePath);
    }

    protected override void EvaluateBody(Interp? interp)
    {
        var sub = interp != null ? new Interp(interp.Out, interp.Error) : new Interp();
        var env = sub.Environment;

        // Seed the contextified object's properties (if a vm context was supplied).
        if (ContextObject != null)
            foreach (var (key, value) in VmContext.ExtractProperties(ContextObject))
                env.Define(key, value);

        // Bind imported names from the linked dependency namespaces.
        foreach (var stmt in _statements)
        {
            if (stmt is not Stmt.Import imp || imp.IsTypeOnly) continue;
            if (!Linked.TryGetValue(imp.ModulePath, out var dep)) continue;
            var depExports = dep.Exports;

            if (imp.NamespaceImport != null)
                env.Define(imp.NamespaceImport.Lexeme, new Dictionary<string, object?>(depExports));
            if (imp.DefaultImport != null)
                env.Define(imp.DefaultImport.Lexeme, depExports.GetValueOrDefault("default"));
            if (imp.NamedImports != null)
                foreach (var spec in imp.NamedImports)
                {
                    if (spec.IsTypeOnly) continue;
                    var local = (spec.LocalName ?? spec.Imported).Lexeme;
                    env.Define(local, depExports.GetValueOrDefault(spec.Imported.Lexeme));
                }
        }

        // Build the runnable statement list (imports dropped; exports unwrapped to their
        // declarations) and record which names are exported.
        var toRun = new List<Stmt>();
        var exported = new List<(string local, string exported)>();
        foreach (var stmt in _statements)
        {
            switch (stmt)
            {
                case Stmt.Import:
                    continue;
                case Stmt.Export exp:
                    CollectExport(exp, toRun, exported);
                    break;
                default:
                    toRun.Add(stmt);
                    break;
            }
        }

        // Route dynamic import() inside the module body through importModuleDynamically (#1156).
        if (ImportModuleDynamically != null)
        {
            var hook = VmModuleInterpreter.BuildDynamicImportHook(ImportModuleDynamically, Facade, interp);
            if (hook != null)
                sub.SetVmDynamicImportHook(hook);
        }

        var resolver = new VariableResolver(sub);
        resolver.Resolve(toRun);
        sub.InterpretRepl(toRun);

        // Snapshot the exported bindings into the namespace.
        foreach (var (local, exportedName) in exported)
            if (sub.Environment.TryGet(local, out var v))
                Namespace[exportedName] = v.ToObject();

        sub.Dispose();
    }

    private static void CollectExport(Stmt.Export exp, List<Stmt> toRun, List<(string, string)> exported)
    {
        if (exp.IsTypeOnly)
            return;

        if (exp.Declaration != null)
        {
            toRun.Add(exp.Declaration);
            foreach (var name in DeclaredNames(exp.Declaration))
                exported.Add((name, name));
        }
        else if (exp.NamedExports != null)
        {
            foreach (var spec in exp.NamedExports)
            {
                if (spec.IsTypeOnly)
                    continue;
                exported.Add((spec.LocalName.Lexeme, (spec.ExportedName ?? spec.LocalName).Lexeme));
            }
        }
        else if (exp.IsDefaultExport && exp.DefaultExpr != null)
        {
            // export default <expr>  →  const <synthetic> = <expr>; namespace["default"] = <synthetic>
            var name = "__vmDefaultExport__";
            var token = new Token(TokenType.IDENTIFIER, name, null, exp.Keyword.Line);
            toRun.Add(new Stmt.Const(token, null, exp.DefaultExpr));
            exported.Add((name, "default"));
        }
    }

    private static IEnumerable<string> DeclaredNames(Stmt declaration) => declaration switch
    {
        Stmt.Var v => [v.Name.Lexeme],
        Stmt.Const c => [c.Name.Lexeme],
        Stmt.Function f => [f.Name.Lexeme],
        Stmt.Class cl => [cl.Name.Lexeme],
        _ => [],
    };
}

/// <summary>
/// Constructor for vm.SourceTextModule — <c>new vm.SourceTextModule(code[, options])</c>.
/// Returns the module's guest-visible facade dictionary (works in both modes).
/// </summary>
public sealed class VmSourceTextModuleConstructor : ISharpTSCallable
{
    public int Arity() => 1;

    public object? Call(Interp interpreter, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string code)
            throw new Exception("vm.SourceTextModule requires a code string");

        var options = args.Count > 1 ? args[1] : null;
        var module = new VmSourceTextModule(code, options);
        return module.BuildFacade(code);
    }
}

/// <summary>
/// vm.SyntheticModule — a module whose exports are defined programmatically rather than
/// from source. <c>exportNames</c> are declared up front; the <c>evaluateCallback</c> runs
/// at evaluation with <c>this</c> bound to the module and calls <c>this.setExport(name, value)</c>
/// to populate them. Has no source-level dependencies; participates in link()/evaluate() and
/// the status state machine like SourceTextModule.
/// </summary>
public sealed class VmSyntheticModule : VmModuleBase
{
    private static readonly string[] _noDeps = [];
    private readonly List<string> _exportNames;
    private readonly object? _evaluateCallback;

    public override IReadOnlyList<string> DependencySpecifiers => _noDeps;

    public VmSyntheticModule(IEnumerable<string> exportNames, object? evaluateCallback, object? options)
    {
        _exportNames = exportNames.ToList();
        _evaluateCallback = evaluateCallback;
        Identifier = VmModuleInterpreter.GetStringOption(options, "identifier") ?? $"vm:synthetic-module({NextId()})";
        ContextObject = VmModuleInterpreter.GetOption(options, "context");
    }

    protected override void ConfigureFacade(Dictionary<string, object?> facade)
    {
        facade["setExport"] = BuiltInMethod.CreateV2("setExport", 2, 2, (interp, recv, a) =>
        {
            var name = a.Length > 0 ? a[0].ToObject() as string : null;
            var value = a.Length > 1 ? a[1].ToObject() : null;
            DoSetExport(name, value);
            return RuntimeValue.Undefined;
        });
    }

    private void DoSetExport(string? name, object? value)
    {
        if (Status == "unlinked")
            throw new Exception("TypeError [ERR_VM_MODULE_STATUS]: Module must be linked before calling setExport()");
        if (name == null || !_exportNames.Contains(name))
            throw new Exception($"TypeError [ERR_VM_MODULE_NOT_MODULE]: '{name}' is not a declared export of this SyntheticModule");
        SetExport(name, value);
    }

    protected override void EvaluateBody(Interp? interp)
    {
        // Run the evaluate callback with `this` bound to the module facade so the
        // idiomatic `this.setExport(...)` resolves (in both interpreter and compiled mode).
        if (_evaluateCallback != null)
            CallGuest(interp, _evaluateCallback, thisArg: Facade, []);
    }
}

/// <summary>
/// Constructor for vm.SyntheticModule —
/// <c>new vm.SyntheticModule(exportNames, evaluateCallback[, options])</c>.
/// </summary>
public sealed class VmSyntheticModuleConstructor : ISharpTSCallable
{
    public int Arity() => 2;

    public object? Call(Interp interpreter, List<object?> args)
    {
        var exportNames = ExtractStringList(args.Count > 0 ? args[0] : null);
        var evaluateCallback = args.Count > 1 ? args[1] : null;
        var options = args.Count > 2 ? args[2] : null;
        var module = new VmSyntheticModule(exportNames, evaluateCallback, options);
        return module.BuildFacade(string.Join(",", exportNames));
    }

    private static List<string> ExtractStringList(object? value)
    {
        var result = new List<string>();
        IEnumerable<object?>? items = value switch
        {
            SharpTSArray arr => arr,
            List<object?> list => list,
            _ => null,
        };
        if (items != null)
            foreach (var item in items)
                if (item is string s)
                    result.Add(s);
        return result;
    }
}
