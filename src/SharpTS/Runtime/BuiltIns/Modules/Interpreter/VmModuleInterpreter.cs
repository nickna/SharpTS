using SharpTS.Diagnostics;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'vm' module.
/// Provides dynamic code compilation and execution within isolated contexts.
/// </summary>
public static class VmModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the vm module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["runInNewContext"] = BuiltInMethod.CreateV2("runInNewContext", 1, 3, RunInNewContext),
            ["runInThisContext"] = BuiltInMethod.CreateV2("runInThisContext", 1, 2, RunInThisContext),
            ["runInContext"] = BuiltInMethod.CreateV2("runInContext", 2, 3, RunInContext),
            ["createContext"] = BuiltInMethod.CreateV2("createContext", 0, 2, CreateContext),
            ["isContext"] = BuiltInMethod.CreateV2("isContext", 1, 1, IsContext),
            ["compileFunction"] = BuiltInMethod.CreateV2("compileFunction", 1, 3, CompileFunction),
            ["measureMemory"] = BuiltInMethod.CreateV2("measureMemory", 0, 1, MeasureMemory),
            ["constants"] = VmConstants.Create(),
            ["Script"] = new VmScriptConstructor(),
            ["SourceTextModule"] = new VmSourceTextModuleConstructor(),
            ["SyntheticModule"] = new VmSyntheticModuleConstructor()
        };
    }

    /// <summary>
    /// vm.runInNewContext(code, contextObject?, options?) — executes code in a fresh, isolated context.
    /// Context properties become variables; mutations are written back.
    /// </summary>
    private static RuntimeValue RunInNewContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string code)
            throw new Exception("vm.runInNewContext requires a code string");

        var contextObject = args.Length > 1 ? args[1].ToObject() : null;
        var options = args.Length > 2 ? args[2].ToObject() : null;
        var timeout = GetTimeoutOption(options);
        var origin = ScriptOrigin.From(options);
        var hook = BuildDynamicImportHook(GetOption(options, "importModuleDynamically"), null, interpreter);
        return RuntimeValue.FromBoxed(WithScriptOrigin(origin,
            () => ExecuteInNewContext(code, contextObject, interpreter, timeout, origin.Filename, hook)));
    }

    /// <summary>
    /// vm.runInThisContext(code, options?) — executes code in the caller's scope.
    /// </summary>
    private static RuntimeValue RunInThisContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string code)
            throw new Exception("vm.runInThisContext requires a code string");

        var options = args.Length > 1 ? args[1].ToObject() : null;
        var timeout = GetTimeoutOption(options);
        var origin = ScriptOrigin.From(options);
        return RuntimeValue.FromBoxed(WithScriptOrigin(origin,
            () => ExecuteInCurrentContext(code, interpreter, timeout, origin.Filename)));
    }

    /// <summary>
    /// vm.runInContext(code, contextifiedObject, options?) — executes code in an
    /// already-contextified object (created via vm.createContext()). Throws a TypeError
    /// if the second argument has not been contextified.
    /// </summary>
    private static RuntimeValue RunInContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string code)
            throw new Exception("vm.runInContext requires a code string");

        var contextObject = args.Length > 1 ? args[1].ToObject() : null;
        if (!VmContext.IsContext(contextObject))
            throw new Exception("TypeError [ERR_INVALID_ARG_TYPE]: The \"contextifiedObject\" argument must be of type vm.Context.");

        var options = args.Length > 2 ? args[2].ToObject() : null;
        var timeout = GetTimeoutOption(options);
        var origin = ScriptOrigin.From(options);
        var hook = BuildDynamicImportHook(GetOption(options, "importModuleDynamically"), null, interpreter);
        return RuntimeValue.FromBoxed(WithScriptOrigin(origin,
            () => ExecuteInNewContext(code, contextObject, interpreter, timeout, origin.Filename, hook)));
    }

    /// <summary>
    /// vm.createContext(contextObject?, options?) — tags an object as a vm context.
    /// Options (name / origin / codeGeneration / microtaskMode) are stored on the
    /// context and honored when code runs against it.
    /// </summary>
    private static RuntimeValue CreateContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var contextObject = args.Length > 0 ? args[0].ToObject() : null;
        var options = args.Length > 1 ? ParseContextOptions(args[1].ToObject()) : null;
        return RuntimeValue.FromObject(VmContext.Create(contextObject, options));
    }

    /// <summary>
    /// Parses a createContext()/runInNewContext() options object into VmContextOptions.
    /// Reads name, origin, codeGeneration:{ strings, wasm }, and microtaskMode.
    /// </summary>
    internal static VmContextOptions? ParseContextOptions(object? options)
    {
        if (options == null) return null;

        var name = GetStringOption(options, "name");
        var originUrl = GetStringOption(options, "origin");
        var microtaskMode = GetStringOption(options, "microtaskMode");

        var codeGenStrings = true;
        var codeGenWasm = true;
        if (GetOption(options, "codeGeneration") is { } codeGen)
        {
            codeGenStrings = GetBoolOption(codeGen, "strings", true);
            codeGenWasm = GetBoolOption(codeGen, "wasm", true);
        }

        // Only allocate options when something non-default was supplied.
        if (name == null && originUrl == null && microtaskMode == null && codeGenStrings && codeGenWasm)
            return null;

        return new VmContextOptions(name, originUrl, codeGenStrings, codeGenWasm, microtaskMode);
    }

    /// <summary>
    /// Applies a context's createContext options to a freshly-created sub-interpreter:
    /// disables eval/new Function when codeGeneration.strings is false.
    /// (microtaskMode draining happens after evaluation — see <see cref="DrainContextMicrotasks"/>.)
    /// </summary>
    private static void ApplyContextOptions(Interp subInterpreter, VmContextOptions? options)
    {
        if (options is { CodeGenerationStrings: false })
            subInterpreter.DisableCodeGenerationFromStrings();
    }

    /// <summary>Drains the sub-interpreter's microtask queue when microtaskMode is 'afterEvaluate'.</summary>
    private static void DrainContextMicrotasks(Interp subInterpreter, VmContextOptions? options)
    {
        if (options is { DrainMicrotasks: true })
            subInterpreter.ProcessMicrotasks();
    }

    /// <summary>
    /// vm.measureMemory([options]) — returns a Promise resolving to a best-effort,
    /// GC-derived memory measurement. V8's per-context detailed breakdown has no .NET
    /// equivalent (#1150 ceiling), so the estimate/range come from GC.GetTotalMemory and
    /// the per-context split is omitted.
    /// </summary>
    private static RuntimeValue MeasureMemory(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(SharpTSPromise.Resolve(MeasureMemoryResultObject()));
    }

    /// <summary>
    /// Builds the (best-effort, GC-derived) measureMemory result payload as plain
    /// dictionaries so it is readable in both modes (compiled GetFieldsProperty
    /// dispatches dictionaries; a SharpTSObject's fields are not visible across the
    /// runtime boundary). The compiled VmMeasureMemory helper calls this directly and
    /// wraps the result in a native $Promise (a cross-boundary SharpTSPromise is not
    /// unwrapped by compiled await).
    /// </summary>
    public static object MeasureMemoryResultObject()
    {
        var bytes = (double)GC.GetTotalMemory(forceFullCollection: false);
        var range = new SharpTSArray(new List<object?> { bytes, bytes });
        var total = new Dictionary<string, object?>
        {
            ["jsMemoryEstimate"] = bytes,
            ["jsMemoryRange"] = range,
        };
        return new Dictionary<string, object?> { ["total"] = total };
    }

    /// <summary>
    /// vm.isContext(obj) — returns whether an object has been contextified.
    /// </summary>
    private static RuntimeValue IsContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args.Length > 0 ? args[0].ToObject() : null;
        return RuntimeValue.FromBoolean(VmContext.IsContext(obj));
    }

    /// <summary>
    /// Executes code in a fresh interpreter with context object properties seeded as variables.
    /// Mutations to context variables are written back to the context object.
    /// </summary>
    internal static object? ExecuteInNewContext(string code, object? contextObject, Interp? parentInterpreter = null, int timeout = -1, string? filename = null, Func<string, object?>? dynamicImportHook = null)
    {
        // Parse the code
        var statements = ParseCode(code, filename);

        // Extract context properties
        var contextProps = VmContext.ExtractProperties(contextObject);

        // Create fresh interpreter inheriting parent's output writers
        var subInterpreter = parentInterpreter != null
            ? new Interp(parentInterpreter.Out, parentInterpreter.Error)
            : new Interp();
        var env = subInterpreter.Environment;

        foreach (var (key, value) in contextProps)
            env.Define(key, value);

        // Honor the context's createContext options (codeGeneration / microtaskMode).
        var contextOptions = VmContext.GetOptions(contextObject);
        ApplyContextOptions(subInterpreter, contextOptions);
        if (dynamicImportHook != null)
            subInterpreter.SetVmDynamicImportHook(dynamicImportHook);

        // Apply timeout if specified
        CancellationTokenSource? cts = null;
        if (timeout > 0)
        {
            cts = new CancellationTokenSource(timeout);
            subInterpreter.SetVmTimeoutToken(cts.Token);
        }

        try
        {
            // Resolve variables and execute
            var resolver = new VariableResolver(subInterpreter);
            resolver.Resolve(statements);

            var result = subInterpreter.InterpretRepl(statements);

            // Drain queued microtasks when microtaskMode:'afterEvaluate'.
            DrainContextMicrotasks(subInterpreter, contextOptions);

            // Write mutations back to context object
            VmContext.WriteBack(contextObject, contextProps, subInterpreter.Environment);

            return result;
        }
        finally
        {
            subInterpreter.Dispose();
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Executes code in the current interpreter's scope.
    /// </summary>
    internal static object? ExecuteInCurrentContext(string code, Interp interpreter, int timeout = -1, string? filename = null)
    {
        var statements = ParseCode(code, filename);

        var resolver = new VariableResolver(interpreter);
        resolver.Resolve(statements);

        CancellationTokenSource? cts = null;
        if (timeout > 0)
        {
            cts = new CancellationTokenSource(timeout);
            interpreter.SetVmTimeoutToken(cts.Token);
        }

        try
        {
            return interpreter.InterpretRepl(statements);
        }
        finally
        {
            if (cts != null)
            {
                interpreter.SetVmTimeoutToken(default);
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Executes pre-parsed statements in a new context.
    /// Used by Script.runInNewContext and Script.runInContext.
    /// </summary>
    internal static object? ExecuteParsedInNewContext(List<Stmt> statements, object? contextObject, Interp? parentInterpreter = null, int timeout = -1, Func<string, object?>? dynamicImportHook = null)
    {
        var contextProps = VmContext.ExtractProperties(contextObject);

        var subInterpreter = parentInterpreter != null
            ? new Interp(parentInterpreter.Out, parentInterpreter.Error)
            : new Interp();
        var env = subInterpreter.Environment;

        foreach (var (key, value) in contextProps)
            env.Define(key, value);

        var contextOptions = VmContext.GetOptions(contextObject);
        ApplyContextOptions(subInterpreter, contextOptions);
        if (dynamicImportHook != null)
            subInterpreter.SetVmDynamicImportHook(dynamicImportHook);

        CancellationTokenSource? cts = null;
        if (timeout > 0)
        {
            cts = new CancellationTokenSource(timeout);
            subInterpreter.SetVmTimeoutToken(cts.Token);
        }

        try
        {
            var resolver = new VariableResolver(subInterpreter);
            resolver.Resolve(statements);

            var result = subInterpreter.InterpretRepl(statements);

            DrainContextMicrotasks(subInterpreter, contextOptions);

            VmContext.WriteBack(contextObject, contextProps, subInterpreter.Environment);

            return result;
        }
        finally
        {
            subInterpreter.Dispose();
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Executes pre-parsed statements in the current interpreter's scope.
    /// Used by Script.runInThisContext.
    /// </summary>
    internal static object? ExecuteParsedInCurrentContext(List<Stmt> statements, Interp interpreter, int timeout = -1)
    {
        var resolver = new VariableResolver(interpreter);
        resolver.Resolve(statements);

        CancellationTokenSource? cts = null;
        if (timeout > 0)
        {
            cts = new CancellationTokenSource(timeout);
            interpreter.SetVmTimeoutToken(cts.Token);
        }

        try
        {
            return interpreter.InterpretRepl(statements);
        }
        finally
        {
            if (cts != null)
            {
                interpreter.SetVmTimeoutToken(default);
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// vm.compileFunction(code, params?, options?) — compiles a function body with named parameters.
    /// Returns a callable function. Equivalent to new Function(params, code) with vm context control.
    /// </summary>
    private static RuntimeValue CompileFunction(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string code)
            throw new Exception("vm.compileFunction requires a code string");

        // Extract params array (string[])
        var paramNames = new List<string>();
        if (args.Length > 1 && !args[1].IsNull)
        {
            IEnumerable<object?> items;
            if (args[1].ToObject() is List<object?> paramList)
                items = paramList;
            else if (args[1].ToObject() is SharpTSArray paramArray)
                items = paramArray;
            else
                items = [];

            foreach (var p in items)
            {
                if (p is string name)
                    paramNames.Add(name);
                else
                    throw new Exception("SyntaxError: Invalid parameter name");
            }
        }

        // Validate parameter names are valid identifiers
        foreach (var name in paramNames)
        {
            if (string.IsNullOrEmpty(name) || !IsValidIdentifier(name))
                throw new Exception($"SyntaxError: Invalid parameter name '{name}'");
        }

        // Extract options
        object? parsingContext = null;
        List<object?>? contextExtensions = null;
        var rawOptions = args.Length > 2 && !args[2].IsNull ? args[2].ToObject() : null;
        var origin = ScriptOrigin.From(rawOptions);

        if (rawOptions != null)
        {
            var options = VmContext.ExtractProperties(rawOptions);
            if (options.TryGetValue("parsingContext", out var ctx))
            {
                if (ctx != null && !VmContext.IsContext(ctx))
                    throw new Exception("TypeError: parsingContext must be a vm.createContext()-ed object");
                parsingContext = ctx;
            }
            if (options.TryGetValue("contextExtensions", out var ext))
            {
                if (ext is List<object?> extList)
                    contextExtensions = extList;
                else if (ext is SharpTSArray extArray)
                    contextExtensions = extArray.ToList();
            }
            // produceCachedData is accepted but cachedData is a documented .NET ceiling
            // (#1150); compileFunction returns a bare callable that can't carry a
            // cachedData property, so the marker is not surfaced here.
        }

        // Wrap code as function body and parse
        // Ensure code ends with semicolon since the parser requires them
        var trimmedCode = code.TrimEnd();
        if (trimmedCode.Length > 0 && !trimmedCode.EndsWith(';') && !trimmedCode.EndsWith('}'))
            trimmedCode += ";";
        var paramsStr = string.Join(", ", paramNames);
        var wrappedCode = $"function __vmfn__({paramsStr}) {{ {trimmedCode} }}";
        var funcDeclStatements = ParseCode(wrappedCode, origin.Filename);

        var importModuleDynamically = GetOption(rawOptions, "importModuleDynamically");

        // Return a callable that executes the function body in a fresh interpreter
        var compiledFn = BuiltInMethod.CreateV2("compiledFunction", 0, paramNames.Count,
            (interp, recv, callArgs) =>
            {
                var boxedCallArgs = new List<object?>(callArgs.Length);
                for (int i = 0; i < callArgs.Length; i++)
                    boxedCallArgs.Add(callArgs[i].ToObject());
                var hook = BuildDynamicImportHook(importModuleDynamically, null, interp);
                return RuntimeValue.FromBoxed(WithScriptOrigin(origin,
                    () => ExecuteCompiledFunction(funcDeclStatements, paramNames, boxedCallArgs, parsingContext, contextExtensions, interp, hook)));
            });

        return RuntimeValue.FromObject(compiledFn);
    }

    /// <summary>
    /// Executes a compiled function by creating a fresh interpreter,
    /// seeding context/extensions/params, and running the function body.
    /// </summary>
    private static object? ExecuteCompiledFunction(
        List<Stmt> funcDeclStatements,
        List<string> paramNames,
        List<object?> callArgs,
        object? parsingContext,
        List<object?>? contextExtensions,
        Interp? parentInterpreter = null,
        Func<string, object?>? dynamicImportHook = null)
    {
        var subInterpreter = parentInterpreter != null
            ? new Interp(parentInterpreter.Out, parentInterpreter.Error)
            : new Interp();
        var env = subInterpreter.Environment;
        if (dynamicImportHook != null)
            subInterpreter.SetVmDynamicImportHook(dynamicImportHook);

        // Seed parsingContext variables
        if (parsingContext != null)
        {
            var contextProps = VmContext.ExtractProperties(parsingContext);
            foreach (var (key, value) in contextProps)
                env.Define(key, value);
        }

        // Seed contextExtensions (layered, in order)
        if (contextExtensions != null)
        {
            foreach (var ext in contextExtensions)
            {
                if (ext != null)
                {
                    var extProps = VmContext.ExtractProperties(ext);
                    foreach (var (key, value) in extProps)
                        env.Define(key, value);
                }
            }
        }

        // Define temporary arg variables and build the call expression
        var argPlaceholders = new List<string>();
        for (int i = 0; i < paramNames.Count; i++)
        {
            var placeholder = $"__vmarg{i}__";
            argPlaceholders.Add(placeholder);
            var argVal = i < callArgs.Count ? callArgs[i] : null;
            env.Define(placeholder, argVal);
        }

        // Build code: define function then call it
        // The funcDeclStatements contain the function declaration; we combine it with a call
        var callCode = $"__vmfn__({string.Join(", ", argPlaceholders)});";
        var callStatements = ParseCode(callCode);

        var allStatements = new List<Stmt>(funcDeclStatements);
        allStatements.AddRange(callStatements);

        var resolver = new VariableResolver(subInterpreter);
        resolver.Resolve(allStatements);
        var result = subInterpreter.InterpretRepl(allStatements);

        subInterpreter.Dispose();
        return result;
    }

    /// <summary>
    /// Reads a single option by name from an options object (SharpTSObject, Dictionary,
    /// or an emitted $Object via reflection). Returns null if absent.
    /// </summary>
    internal static object? GetOption(object? options, string key)
    {
        switch (options)
        {
            case null:
                return null;
            case SharpTSObject obj:
                return obj.GetProperty(key);
            case Dictionary<string, object?> dict:
                return dict.TryGetValue(key, out var val) ? val : null;
            default:
                // Compiler-emitted $Object uses the managed-shape boundary.
                // Preserve other CLR "Fields" shapes for managed .NET interop.
                var type = options.GetType();
                var fieldsProp = ManagedEmittedShapeReflection.IsShape(
                        type, ManagedEmittedShape.HasFields)
                    ? ManagedEmittedShapeReflection.GetPublicProperty(
                        type, ManagedEmittedShape.HasFields, "Fields")
                    : ManagedStructuralClrReflection.TryGetPublicPropertyByName(type, "Fields");
                if (fieldsProp?.GetValue(options) is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    foreach (var kv in fields)
                        if (kv.Key == key)
                            return kv.Value;
                }
                return null;
        }
    }

    /// <summary>Reads a string option, returning <paramref name="fallback"/> if absent/non-string.</summary>
    internal static string? GetStringOption(object? options, string key, string? fallback = null)
        => GetOption(options, key) is string s ? s : fallback;

    /// <summary>Reads an integer option (stored as double), returning <paramref name="fallback"/> if absent.</summary>
    internal static int GetIntOption(object? options, string key, int fallback = 0)
        => GetOption(options, key) is double d ? (int)d : fallback;

    /// <summary>Reads a boolean option, returning <paramref name="fallback"/> if absent.</summary>
    internal static bool GetBoolOption(object? options, string key, bool fallback = false)
        => GetOption(options, key) is bool b ? b : fallback;

    /// <summary>
    /// Extracts the timeout option (in milliseconds) from an options object.
    /// Returns -1 if not specified.
    /// </summary>
    internal static int GetTimeoutOption(object? options)
    {
        var val = GetOption(options, "timeout");
        if (val is double d && d > 0)
            return (int)d;
        return -1;
    }

    /// <summary>
    /// Builds the dynamic-import resolver for a vm importModuleDynamically option, to be
    /// installed on the sub-interpreter so an <c>import(x)</c> inside vm code routes through
    /// the user hook. Returns null when no hook is supplied or the
    /// USE_MAIN_CONTEXT_DEFAULT_LOADER sentinel is used (fall back to the default loader).
    /// The hook may return a module namespace object or a vm.Module (which is linked+
    /// evaluated and exposed via its namespace).
    /// </summary>
    internal static Func<string, object?>? BuildDynamicImportHook(object? importModuleDynamically, object? scriptOrModule, Interp? interp)
    {
        if (importModuleDynamically == null)
            return null;
        if (ReferenceEquals(importModuleDynamically, VmConstants.UseMainContextDefaultLoader))
            return null;

        return specifier =>
        {
            var attributes = new Dictionary<string, object?>();
            var result = RuntimeCallableDispatcher.Invoke(interp, importModuleDynamically, specifier, scriptOrModule, attributes);

            if (result is SharpTSPromise p && p.Task.IsCompletedSuccessfully)
                result = p.Task.Result;

            // If the hook returned a vm.Module, link (deps-free) + evaluate it and expose
            // its namespace; otherwise the hook returned a namespace object directly.
            var module = VmModuleRegistry.Get(result);
            if (module != null)
            {
                if (module.Status == "unlinked")
                    module.Link(interp, linker: null);
                if (module.Status != "evaluated")
                    module.Evaluate(interp);
                return new Dictionary<string, object?>(module.Exports);
            }
            return result;
        };
    }

    /// <summary>
    /// Captures the script-origin options (filename / line / column offsets) that flow
    /// into thrown error stacks. Mirrors Node's defaults.
    /// </summary>
    internal readonly record struct ScriptOrigin(string Filename, int LineOffset, int ColumnOffset)
    {
        public static ScriptOrigin From(object? options) => new(
            GetStringOption(options, "filename", "evalmachine.<anonymous>")!,
            GetIntOption(options, "lineOffset", 0),
            GetIntOption(options, "columnOffset", 0));

        public static readonly ScriptOrigin Default = new("evalmachine.<anonymous>", 0, 0);
    }

    /// <summary>
    /// Runs <paramref name="run"/> and, if a guest error escapes, prepends the script
    /// origin (filename:line:col) to its stack — matching Node, where vm errors are
    /// reported against the supplied filename/offsets rather than the host caller.
    /// </summary>
    internal static object? WithScriptOrigin(ScriptOrigin origin, Func<object?> run)
    {
        try
        {
            return run();
        }
        catch (Runtime.Exceptions.ThrowException te) when (te.Value is SharpTSError err)
        {
            // A guest error object propagated directly (e.g. the timeout error).
            // Rewrite its stack so the top frame points at the script origin
            // (filename:line:col), matching how Node reports vm errors. We touch only
            // .Stack (never .name/.message) and keep the same exception type/flow, so
            // interpreter and compiled callers stay in agreement.
            //
            // LIMITATION: a user `throw` or runtime fault is stringified by the
            // sub-interpreter's REPL boundary into a host Exception and reconstructed
            // by the outer interpreter (interp) / $Runtime.WrapException (compiled),
            // which mint a fresh stack — so the origin frame is not attached for those.
            // The supplied filename DOES flow into SyntaxError messages at parse time
            // (see ParseCode), which is the common, cross-mode-faithful case.
            PrependOriginFrame(err, origin);
            throw;
        }
    }

    private static void PrependOriginFrame(SharpTSError err, ScriptOrigin origin)
    {
        if (err.Stack != null && err.Stack.Contains(origin.Filename)) return;
        var frame = $"    at {origin.Filename}:{origin.LineOffset + 1}:{origin.ColumnOffset + 1}";
        var header = $"{err.Name}: {err.Message}";
        err.Stack = header + "\n" + frame;
    }

    /// <summary>
    /// Validates that a string is a valid JavaScript identifier.
    /// </summary>
    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_' && name[0] != '$') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_' && name[i] != '$') return false;
        }
        return true;
    }

    /// <summary>
    /// Parses a code string into statements. Throws on parse errors.
    /// Follows the REPL pattern: retries with appended ";" on parse failure.
    /// </summary>
    internal static List<Stmt> ParseCode(string code, string? filename = null)
    {
        var result = TryParse(code);

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            // Retry with appended semicolon (same as REPL)
            var retryResult = TryParse(code + ";");
            var retryErrors = retryResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (retryErrors.Count == 0)
                return retryResult.Statements;

            // Node reports vm syntax errors against the supplied filename.
            var prefix = filename != null ? $"{filename}: " : "";
            throw new Exception($"SyntaxError: {prefix}{errors[0].Message}");
        }

        return result.Statements;
    }

    private static ParseDiagnosticResult TryParse(string code)
    {
        var lexer = new Lexer(code);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }
}

/// <summary>
/// Constructor for vm.Script — pre-parses code for later execution.
/// </summary>
public sealed class VmScriptConstructor : ISharpTSCallable
{
    public int Arity() => 1;

    public object? Call(Interp interpreter, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string code)
            throw new Exception("vm.Script requires a code string");

        var options = args.Count > 1 ? args[1] : null;
        var origin = VmModuleInterpreter.ScriptOrigin.From(options);
        var produceCachedData = VmModuleInterpreter.GetBoolOption(options, "produceCachedData");
        var hasCachedDataInput = VmModuleInterpreter.GetOption(options, "cachedData") != null;
        var importModuleDynamically = VmModuleInterpreter.GetOption(options, "importModuleDynamically");

        // Pre-parse the code (with semicolon retry); syntax errors carry the filename.
        var statements = VmModuleInterpreter.ParseCode(code, origin.Filename);

        // Create Script as a dictionary — works in both interpreter (via SharpTSObject wrapper)
        // and compiled mode (dictionary is native object representation).
        // Methods use BuiltInMethod (ISharpTSCallable for interpreter, has "Call" method for compiled mode).
        var runInNewCtx = BuiltInMethod.CreateV2("runInNewContext", 0, 2,
            (interp, recv, methodArgs) =>
            {
                var contextObject = methodArgs.Length > 0 ? methodArgs[0].ToObject() : null;
                var timeout = VmModuleInterpreter.GetTimeoutOption(methodArgs.Length > 1 ? methodArgs[1].ToObject() : null);
                var hook = VmModuleInterpreter.BuildDynamicImportHook(importModuleDynamically, null, interp);
                return RuntimeValue.FromBoxed(VmModuleInterpreter.WithScriptOrigin(origin,
                    () => VmModuleInterpreter.ExecuteParsedInNewContext(statements, contextObject, interp, timeout, hook)));
            });

        var runInThisCtx = BuiltInMethod.CreateV2("runInThisContext", 0, 1,
            (interp, recv, methodArgs) =>
            {
                var timeout = VmModuleInterpreter.GetTimeoutOption(methodArgs.Length > 0 ? methodArgs[0].ToObject() : null);
                var hook = VmModuleInterpreter.BuildDynamicImportHook(importModuleDynamically, null, interp);
                return RuntimeValue.FromBoxed(VmModuleInterpreter.WithScriptOrigin(origin, () =>
                {
                    if (interp != null)
                    {
                        if (hook != null) interp.SetVmDynamicImportHook(hook);
                        try { return VmModuleInterpreter.ExecuteParsedInCurrentContext(statements, interp, timeout); }
                        finally { if (hook != null) interp.SetVmDynamicImportHook(null); }
                    }
                    // Compiled mode: no interpreter available, fall back to new context
                    return VmModuleInterpreter.ExecuteParsedInNewContext(statements, null, interp, timeout, hook);
                }));
            });

        var runInCtx = BuiltInMethod.CreateV2("runInContext", 1, 2,
            (interp, recv, methodArgs) =>
            {
                var context = methodArgs.Length > 0 ? methodArgs[0].ToObject() : null;
                if (!VmContext.IsContext(context))
                    throw new Exception("TypeError [ERR_INVALID_ARG_TYPE]: The \"contextifiedObject\" argument must be of type vm.Context.");
                var timeout = VmModuleInterpreter.GetTimeoutOption(methodArgs.Length > 1 ? methodArgs[1].ToObject() : null);
                var hook = VmModuleInterpreter.BuildDynamicImportHook(importModuleDynamically, null, interp);
                return RuntimeValue.FromBoxed(VmModuleInterpreter.WithScriptOrigin(origin,
                    () => VmModuleInterpreter.ExecuteParsedInNewContext(statements, context, interp, timeout, hook)));
            });

        // vm bytecode caching has no .NET equivalent (#1150 ceiling): createCachedData()
        // returns a deterministic marker Buffer derived from the source. It round-trips
        // (a passed-in cachedData is never rejected) but yields no real speedup.
        var createCachedData = BuiltInMethod.CreateV2("createCachedData", 0, 0,
            (interp, recv, methodArgs) =>
                RuntimeValue.FromObject(SharpTSBuffer.FromString(code, "utf8")));

        // Return as Dictionary<string, object?> — works in both modes:
        // Interpreter: EvaluateGetOnFallback handles IDictionary<string, object?>
        // Compiled: GetFieldsProperty handles Dictionary<string, object?>
        var script = new Dictionary<string, object?>
        {
            ["runInNewContext"] = runInNewCtx,
            ["runInThisContext"] = runInThisCtx,
            ["runInContext"] = runInCtx,
            ["createCachedData"] = createCachedData,
            ["sourceMapURL"] = null,
        };

        // produceCachedData: emit the marker Buffer up front and flag it produced.
        if (produceCachedData)
        {
            script["cachedData"] = SharpTSBuffer.FromString(code, "utf8");
            script["cachedDataProduced"] = true;
        }

        // cachedData input is accepted but never rejected (no real bytecode validation).
        if (hasCachedDataInput)
            script["cachedDataRejected"] = false;

        return script;
    }
}
