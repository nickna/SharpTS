using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Tracks which objects have been "contextified" via vm.createContext().
/// Uses a ConditionalWeakTable so objects can be garbage collected normally.
/// </summary>
public static class VmContext
{
    private static readonly ConditionalWeakTable<object, VmContextMarker> _contexts = new();

    /// <summary>
    /// Tags an object as a vm context. If contextObject is null, creates a new SharpTSObject.
    /// Returns the contextified object. The optional createContext options
    /// (name / origin / codeGeneration / microtaskMode) are stored on the marker and
    /// honored when code runs against this context.
    /// </summary>
    public static object Create(object? contextObject, VmContextOptions? options = null)
    {
        var obj = contextObject ?? new SharpTSObject(new Dictionary<string, object?>());
        var marker = _contexts.GetOrCreateValue(obj);
        if (options != null)
            marker.Options = options;
        return obj;
    }

    /// <summary>
    /// Returns whether the object has been contextified via createContext().
    /// </summary>
    public static bool IsContext(object? obj)
    {
        if (obj == null) return false;
        return _contexts.TryGetValue(obj, out _);
    }

    /// <summary>
    /// Returns the createContext options associated with a contextified object, or null
    /// if the object is not a context or was created without options.
    /// </summary>
    public static VmContextOptions? GetOptions(object? obj)
    {
        if (obj != null && _contexts.TryGetValue(obj, out var marker))
            return marker.Options;
        return null;
    }

    /// <summary>
    /// Extracts properties from a context object as a dictionary for seeding a RuntimeEnvironment.
    /// </summary>
    public static Dictionary<string, object?> ExtractProperties(object? contextObject)
    {
        var props = new Dictionary<string, object?>();
        if (contextObject is SharpTSObject tsObj)
        {
            foreach (var kv in tsObj.Fields)
                props[kv.Key] = kv.Value;
        }
        else if (contextObject is Dictionary<string, object?> dict)
        {
            foreach (var kv in dict)
                props[kv.Key] = kv.Value;
        }
        else if (contextObject != null)
        {
            // Compiler-emitted $Object uses the managed-shape boundary. Preserve
            // the existing arbitrary CLR "Fields" fallback for managed interop
            // through the structural compatibility seam.
            var type = contextObject.GetType();
            var fieldsProp = ManagedEmittedShapeReflection.IsShape(
                    type, ManagedEmittedShape.HasFields)
                ? ManagedEmittedShapeReflection.GetPublicProperty(
                    type, ManagedEmittedShape.HasFields, "Fields")
                : ManagedStructuralClrReflection.TryGetPublicPropertyByName(type, "Fields");
            if (fieldsProp?.GetValue(contextObject) is IEnumerable<KeyValuePair<string, object?>> fields)
            {
                foreach (var kv in fields)
                    props[kv.Key] = kv.Value;
            }
        }
        // Wrap any compiled callables (e.g. $TSFunction) so the sub-interpreter can call them
        WrapCompiledCallables(props);

        return props;
    }

    /// <summary>
    /// Wraps values that look like compiled callables ($TSFunction) in ISharpTSCallable adapters
    /// so the sub-interpreter can invoke them. Compiled functions have an Invoke(object, object[])
    /// method but don't implement ISharpTSCallable.
    /// </summary>
    private static void WrapCompiledCallables(Dictionary<string, object?> props)
    {
        foreach (var key in props.Keys.ToList())
            props[key] = WrapCompiledCallable(props[key]);
    }

    internal static object? WrapCompiledCallable(object? value)
    {
        if (value == null || value is ISharpTSCallable || value is BuiltInMethod) return value;
        // Preserve the existing managed shape/structural compatibility boundary.
        var type = value.GetType();
        var invokeMethod = ManagedEmittedShapeReflection.IsShape(type, ManagedEmittedShape.Function)
            ? ManagedEmittedShapeReflection.GetPublicMethod(type, ManagedEmittedShape.Function, "Invoke", [typeof(object[])])
            : ManagedStructuralClrReflection.TryGetPublicMethodBySignature(type, "Invoke", [typeof(object[])]);
        return invokeMethod is null ? value : new CompiledCallableAdapter(value, invokeMethod);
    }

    internal static object? WrapVmCompiledCallable(object? value)
    {
        if (value is null or ISharpTSCallable) return value;
        var type = value.GetType();
        if (!ManagedEmittedShapeReflection.IsShape(type, ManagedEmittedShape.Function)) return value;
        var invoke = ManagedEmittedShapeReflection.GetPublicMethod(type, ManagedEmittedShape.Function, "Invoke", [typeof(object[])]);
        return invoke is null ? value : new CompiledCallableAdapter(value, invoke);
    }

    /// <summary>
    /// Writes mutations back from a RuntimeEnvironment to the context object.
    /// </summary>
    public static void WriteBack(object? contextObject, Dictionary<string, object?> originalProperties,
        RuntimeEnvironment env, SharpTSGlobalThis? globalObject = null)
    {
        var properties = new Dictionary<string, object?>();
        foreach (var name in originalProperties.Keys)
            if (env.TryGet(name, out var value))
                properties[name] = value.ToObject();
        // The actual global object owns classic var storage. Its user data
        // records include newly declared globals without reading accessors or
        // exposing lexical bindings and the intrinsic inventory.
        if (globalObject is not null)
            foreach (var (name, value) in globalObject.UserOwnDataProperties())
                properties[name] = value;

        if (contextObject is not null)
            foreach (var value in properties.Values)
                BuiltIns.Modules.Interpreter.VmModuleInterpreter.RecordVmPublishedValue(value);

        if (BuiltIns.Modules.Interpreter.VmExecutionHost.Current is { } host)
            foreach (var name in properties.Keys.ToArray())
                properties[name] = host.FromVm(properties[name]);

        if (contextObject is SharpTSObject tsObj)
        {
            foreach (var (name, value) in properties)
                tsObj.SetProperty(name, value);
        }
        else if (contextObject is Dictionary<string, object?> dict)
        {
            foreach (var (name, value) in properties)
                dict[name] = value;
        }
        else if (contextObject != null)
        {
            // Compiler-emitted $Object uses the managed-shape boundary. Preserve
            // custom CLR SetProperty shapes as the managed interop fallback.
            var type = contextObject.GetType();
            var setMethod = ManagedEmittedShapeReflection.IsShape(
                    type, ManagedEmittedShape.HasFields)
                ? ManagedEmittedShapeReflection.GetPublicMethod(
                    type, ManagedEmittedShape.HasFields, "SetProperty",
                    [typeof(string), typeof(object)])
                : ManagedStructuralClrReflection.TryGetPublicMethodBySignature(
                    type, "SetProperty", [typeof(string), typeof(object)]);
            if (setMethod != null)
            {
                foreach (var (name, value) in properties)
                    setMethod.Invoke(contextObject, [name, value]);
            }
        }
    }

    internal static void ApplyPromiseIntrinsicState(object? contextObject, Interpreter interpreter)
    {
        if (contextObject is null) return;
        var marker = _contexts.GetOrCreateValue(contextObject);
        marker.PromiseRealm ??= interpreter.PromiseRealmState;
        interpreter.UsePromiseIntrinsicState(marker.PromiseRealm);
    }

    private sealed class VmContextMarker
    {
        public VmContextOptions? Options { get; set; }
        internal PromiseIntrinsicState? PromiseRealm { get; set; }
    }
}

/// <summary>
/// Parsed createContext() options that affect how code runs against the context.
/// </summary>
/// <param name="Name">Human-readable context name (createContext <c>name</c>).</param>
/// <param name="Origin">Context origin URL (createContext <c>origin</c>).</param>
/// <param name="CodeGenerationStrings">When false, <c>eval</c> / <c>new Function</c> in the
/// context throw (codeGeneration.strings:false).</param>
/// <param name="CodeGenerationWasm">When false, WebAssembly compilation in the context is
/// disallowed (codeGeneration.wasm:false). SharpTS has no Wasm engine, so this is recorded
/// but not separately enforced.</param>
/// <param name="MicrotaskMode">When "afterEvaluate", the microtask queue is drained after
/// each evaluation against the context.</param>
public sealed record VmContextOptions(
    string? Name = null,
    string? Origin = null,
    bool CodeGenerationStrings = true,
    bool CodeGenerationWasm = true,
    string? MicrotaskMode = null)
{
    public bool DrainMicrotasks => MicrotaskMode == "afterEvaluate";
}

/// <summary>
/// Wraps a compiled callable ($TSFunction or similar) as ISharpTSCallable so the
/// interpreter can invoke it. Used at the vm module boundary when compiled functions
/// are passed in context objects.
/// </summary>
internal sealed class CompiledCallableAdapter(object target, MethodInfo invokeMethod) : ISharpTSCallable
{
    private readonly BuiltIns.Modules.Interpreter.VmExecutionHost? _host = BuiltIns.Modules.Interpreter.VmExecutionHost.Current;
    public int Arity() => 0; // Unknown arity for compiled functions

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        using var scope = _host?.Enter();
        var owner = Execution.VmEvaluationLease.CurrentInterpreter;
        var args = arguments.Select(value =>
        {
            if (owner is not null) BuiltIns.Modules.Interpreter.VmModuleInterpreter.RecordVmBoundaryValue(value, owner);
            return _host is null ? value : _host.FromVm(value);
        }).ToArray();
        var result = invokeMethod.Invoke(target, [args]);
        return _host is null ? result : _host.ToVm(result);
    }

    public override string ToString() => target.ToString() ?? "<compiled function>";
}
