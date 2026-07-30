using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the <c>$Process</c> class — the compiled counterpart of the live
/// process object (epic #1078). <c>$Process : $EventEmitter</c> implements
/// <c>$IHasFields</c>, so the bare <c>process</c> identifier, the module
/// facade's default export, and every dynamic property/method access all hit
/// the SAME singleton: events registered through any surface fire for all of
/// them, and <c>process.exitCode = …</c> / <c>process.title = …</c> work
/// through dynamic assignment.
///
/// Property/method dispatch is reflection-based (camelCase name →
/// PascalCase member via ToPascalCase), mirroring the generic emitted-class
/// dispatch path; the hot static-receiver form (<c>process.platform</c>)
/// still compiles to direct IL in <see cref="Emitters.ProcessStaticEmitter"/>.
///
/// Everything here is pure BCL — standalone DLLs keep working. The one
/// late-bound member is ppid (NtQueryInformationProcess/getppid live in
/// SharpTS.dll); it degrades to 0 without the runtime present, which is the
/// documented graceful-fallback contract for process (see CLAUDE.md).
/// </summary>
public partial class RuntimeEmitter
{
    // $Process type + members needed across emission steps
    private MethodBuilder _processGetInstance = null!;
    private FieldBuilder _processFieldsField = null!;

    // $Runtime-hosted process state
    private FieldBuilder _processThrowDeprecationField = null!;
    private FieldBuilder _processTraceDeprecationField = null!;
    private FieldBuilder _processNoDeprecationField = null!;
    private FieldBuilder _processSourceMapsEnabledField = null!;
    private FieldBuilder _processUmaskField = null!;
    private FieldBuilder _processTitleField = null!;
    private FieldBuilder _processHrtimeFnField = null!;
    private FieldBuilder _processMemoryUsageFnField = null!;
    private FieldBuilder _processReportField = null!;
    private FieldBuilder _processSignalRegistrationsField = null!;

    // Closure type for deferred (event-loop-scheduled) process event emission
    private ConstructorBuilder _processEmitClosureCtor = null!;

    private static readonly string[] _processTrappableSignals =
        ["SIGINT", "SIGTERM", "SIGHUP", "SIGQUIT", "SIGBREAK", "SIGWINCH"];

    /// <summary>
    /// Node signal name → conventional number (kill numeric form, 128+n exits).
    /// </summary>
    private static readonly (string Name, int Number)[] _processSignalNumbers =
    [
        ("SIGHUP", 1), ("SIGINT", 2), ("SIGQUIT", 3), ("SIGABRT", 6),
        ("SIGKILL", 9), ("SIGUSR1", 10), ("SIGUSR2", 12), ("SIGTERM", 15),
        ("SIGBREAK", 21), ("SIGWINCH", 28),
    ];

    /// <summary>
    /// Emits the $Process infrastructure. Called at the end of
    /// EmitProcessMethods so the per-value $Runtime helpers it delegates to
    /// already exist. Order inside: reserve cyclic signatures → value helpers →
    /// emit-closure type → $Process type → fill the late helper bodies.
    /// </summary>
    private void EmitProcessObjectInfrastructure(TypeBuilder runtimeTb, EmittedRuntime runtime)
    {
        var moduleBuilder = (ModuleBuilder)runtimeTb.Module;

        // ---- Reserve signatures with cycles ($Process ⇄ late helpers) ----
        // (GetProcessObject is reserved in DefineRuntimeClassPhase1 — the
        // globalThis value-form path needs its signature before this runs.)
        runtime.ProcessExit = runtimeTb.DefineMethod(
            "ProcessExit", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.ProcessKill = runtimeTb.DefineMethod(
            "ProcessKill", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.ProcessEmitWarning = runtimeTb.DefineMethod(
            "ProcessEmitWarning", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object, _types.Object]);
        runtime.ProcessRunLifecycle = runtimeTb.DefineMethod(
            "ProcessRunLifecycle", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Type.EmptyTypes);
        runtime.ProcessRegisterSignal = runtimeTb.DefineMethod(
            "ProcessRegisterSignal", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [_types.String]);
        runtime.ProcessDispatchSignal = runtimeTb.DefineMethod(
            "ProcessDispatchSignal", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [_types.String]);

        // ---- State fields on $Runtime ----
        _processThrowDeprecationField = runtimeTb.DefineField("_processThrowDeprecation", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
        _processTraceDeprecationField = runtimeTb.DefineField("_processTraceDeprecation", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
        _processNoDeprecationField = runtimeTb.DefineField("_processNoDeprecation", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
        _processSourceMapsEnabledField = runtimeTb.DefineField("_processSourceMapsEnabled", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
        _processUmaskField = runtimeTb.DefineField("_processUmask", _types.Int32, FieldAttributes.Private | FieldAttributes.Static);
        _processTitleField = runtimeTb.DefineField("_processTitle", _types.String, FieldAttributes.Private | FieldAttributes.Static);
        _processHrtimeFnField = runtimeTb.DefineField("_processHrtimeFn", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        _processMemoryUsageFnField = runtimeTb.DefineField("_processMemoryUsageFn", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        _processReportField = runtimeTb.DefineField("_processReport", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        _processSignalRegistrationsField = runtimeTb.DefineField("_processSignalRegistrations",
            _types.DictionaryStringObject, FieldAttributes.Private | FieldAttributes.Static);

        // ---- Value helpers (no $Process dependency) ----
        EmitProcessGetPpid(runtimeTb, runtime);
        EmitProcessTitleHelpers(runtimeTb, runtime);
        EmitProcessJsonInfoHelpers(runtimeTb, runtime);
        EmitProcessGetAllowedFlags(runtimeTb, runtime);
        EmitProcessCpuUsage(runtimeTb, runtime);
        EmitProcessResourceUsage(runtimeTb, runtime);
        EmitProcessAvailableMemory(runtimeTb, runtime);
        EmitProcessGetActiveResourcesInfo(runtimeTb, runtime);
        EmitProcessHrtimeBigint(runtimeTb, runtime);
        EmitProcessMemoryRss(runtimeTb, runtime);
        EmitProcessFunctionWithMemberGetters(runtimeTb, runtime);
        EmitProcessUmask(runtimeTb, runtime);
        EmitProcessReportHelpers(runtimeTb, runtime);

        // ---- Deferred-emit closure + the $Process type itself ----
        EmitProcessEmitClosureType(moduleBuilder, runtime);
        EmitProcessType(moduleBuilder, runtime);

        // ---- Late helper bodies (reference $Process) ----
        EmitGetProcessObjectBody(runtime);
        EmitProcessExitBody(runtime);
        EmitProcessKillBody(runtime);
        EmitProcessEmitWarningBody(runtime);
        EmitProcessRunLifecycleBody(runtime);
        EmitProcessSignalMachinery(runtimeTb, runtime);
    }

    // =====================================================================
    // Value helpers
    // =====================================================================

    /// <summary>
    /// ProcessGetPpid() → object (boxed double). Late-binds to
    /// ProcessBuiltIns.GetParentPid when SharpTS.dll is present; 0 otherwise
    /// (graceful-fallback member — see class remarks).
    /// </summary>
    private void EmitProcessGetPpid(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessGetPpid",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetPpid = method;

        var il = method.GetILGenerator();
        var fallback = il.DefineLabel();

        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.BuiltIns.ProcessBuiltIns, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "GetParentPid");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        var miLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Stloc, miLocal);
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        // int (boxed) → double (boxed)
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessGetTitle() → object(string) and ProcessSetTitle(object) → void.
    /// An assigned title wins; otherwise the process name. Setting best-effort
    /// syncs Console.Title on Windows.
    /// </summary>
    private void EmitProcessTitleHelpers(TypeBuilder tb, EmittedRuntime runtime)
    {
        var getter = tb.DefineMethod("ProcessGetTitle",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetTitle = getter;
        {
            var il = getter.GetILGenerator();
            var useProcessName = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _processTitleField);
            il.Emit(OpCodes.Brfalse, useProcessName);
            il.Emit(OpCodes.Ldsfld, _processTitleField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(useProcessName);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "ProcessName"));
            il.Emit(OpCodes.Ret);
        }

        var setter = tb.DefineMethod("ProcessSetTitle",
            MethodAttributes.Public | MethodAttributes.Static, typeof(void), [_types.Object]);
        runtime.ProcessSetTitle = setter;
        {
            var il = setter.GetILGenerator();
            var titleLocal = il.DeclareLocal(_types.String);
            var skipNull = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, skipNull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skipNull);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Stloc, titleLocal);
            il.Emit(OpCodes.Ldloc, titleLocal);
            il.Emit(OpCodes.Stsfld, _processTitleField);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // try { Console.Title = title } catch { } — no console attached
                il.BeginExceptionBlock();
                il.Emit(OpCodes.Ldloc, titleLocal);
                il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Title").SetMethod!);
                il.BeginCatchBlock(_types.Exception);
                il.Emit(OpCodes.Pop);
                il.EndExceptionBlock();
            }
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits the static info-object getters (versions/release/features/config)
    /// as dictionary-building IL from compile-time-baked values, cached in
    /// static fields. (No JsonParse dependency — the JSON helpers are gated on
    /// UsesJSON, but $Process is unconditional.) Compile-time platform ==
    /// runtime platform is an established emitter assumption
    /// (EmitPlatformString does the same).
    /// </summary>
    private void EmitProcessJsonInfoHelpers(TypeBuilder tb, EmittedRuntime runtime)
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        string sharptsVersion = typeof(RuntimeEmitter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        runtime.ProcessGetVersions = EmitDictConstantGetter(tb, "ProcessGetVersions",
        [
            ("node", Runtime.BuiltIns.ProcessBuiltIns.NodeVersion),
            ("sharpts", sharptsVersion),
            ("dotnet", Environment.Version.ToString()),
        ]);
        runtime.ProcessGetRelease = EmitDictConstantGetter(tb, "ProcessGetRelease",
        [
            ("name", "node"), ("sourceUrl", ""), ("headersUrl", ""),
        ]);
        runtime.ProcessGetFeatures = EmitDictConstantGetter(tb, "ProcessGetFeatures",
        [
            ("inspector", false), ("debug", false), ("uv", true), ("ipv6", true),
            ("tls", true), ("tls_alpn", true), ("tls_sni", true), ("tls_ocsp", false),
            ("cached_builtins", true), ("typescript", "strip"),
        ]);
        runtime.ProcessGetConfig = EmitDictConstantGetter(tb, "ProcessGetConfig",
        [
            ("target_defaults", (object)Array.Empty<(string, object)>()),
            ("variables", (object)new (string, object)[]
            {
                ("host_arch", arch), ("target_arch", arch), ("node_module_version", 0.0),
            }),
        ]);

        // execArgv: cached empty List<object?> (SharpTS accepts no runtime flags).
        var execArgvCache = tb.DefineField("_cacheProcessExecArgv", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var execArgv = tb.DefineMethod("ProcessGetExecArgv",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetExecArgv = execArgv;
        {
            var il = execArgv.GetILGenerator();
            var create = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, execArgvCache);
            il.Emit(OpCodes.Brfalse, create);
            il.Emit(OpCodes.Ldsfld, execArgvCache);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(create);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, execArgvCache);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits IL that builds a Dictionary&lt;string, object?&gt; from baked
    /// entries into <paramref name="il"/>, leaving it on the stack. Entry
    /// values may be string / bool / double, or a nested
    /// <c>(string, object)[]</c> which recurses into a nested dictionary.
    /// </summary>
    private void EmitInlineDictionary(ILGenerator il, (string Key, object Value)[] entries)
    {
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item");
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);
        foreach (var (key, value) in entries)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, key);
            switch (value)
            {
                case string s:
                    il.Emit(OpCodes.Ldstr, s);
                    break;
                case bool b:
                    il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Box, _types.Boolean);
                    break;
                case double d:
                    il.Emit(OpCodes.Ldc_R8, d);
                    il.Emit(OpCodes.Box, _types.Double);
                    break;
                case (string, object)[] nested:
                    EmitInlineDictionary(il, nested);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported baked dictionary value for '{key}'");
            }
            il.Emit(OpCodes.Callvirt, setItem);
        }
        il.Emit(OpCodes.Ldloc, dictLocal);
    }

    /// <summary>
    /// Emits a static getter building a baked dictionary on first call, cached
    /// in a static field.
    /// </summary>
    private MethodBuilder EmitDictConstantGetter(TypeBuilder tb, string name, (string Key, object Value)[] entries)
    {
        var cache = tb.DefineField("_cache" + name, _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var method = tb.DefineMethod(name,
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);

        var il = method.GetILGenerator();
        var create = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cache);
        il.Emit(OpCodes.Brfalse, create);
        il.Emit(OpCodes.Ldsfld, cache);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        EmitInlineDictionary(il, entries);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, cache);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// ProcessGetAllowedFlags() → object: an empty HashSet&lt;object?&gt;
    /// (the compiled Set shape) — SharpTS honors no NODE_OPTIONS flags, so
    /// allowedNodeEnvironmentFlags.has(x) is correctly false for everything.
    /// </summary>
    private void EmitProcessGetAllowedFlags(TypeBuilder tb, EmittedRuntime runtime)
    {
        var cache = tb.DefineField("_cacheAllowedFlags", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var method = tb.DefineMethod("ProcessGetAllowedFlags",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetAllowedFlags = method;

        var il = method.GetILGenerator();
        var create = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cache);
        il.Emit(OpCodes.Brfalse, create);
        il.Emit(OpCodes.Ldsfld, cache);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.HashSetOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, cache);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessCpuUsage(object prev) → Dictionary { user, system } µs.
    /// </summary>
    private void EmitProcessCpuUsage(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessCpuUsage",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        runtime.ProcessCpuUsage = method;

        var il = method.GetILGenerator();
        var procLocal = il.DeclareLocal(_types.Process);
        var userLocal = il.DeclareLocal(_types.Double);
        var systemLocal = il.DeclareLocal(_types.Double);
        var spanLocal = il.DeclareLocal(typeof(TimeSpan));
        var prevDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
        il.Emit(OpCodes.Stloc, procLocal);

        // user = UserProcessorTime.Ticks / 10.0
        il.Emit(OpCodes.Ldloc, procLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "UserProcessorTime"));
        il.Emit(OpCodes.Stloc, spanLocal);
        il.Emit(OpCodes.Ldloca, spanLocal);
        il.Emit(OpCodes.Call, typeof(TimeSpan).GetProperty("Ticks")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, userLocal);

        // system = PrivilegedProcessorTime.Ticks / 10.0
        il.Emit(OpCodes.Ldloc, procLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "PrivilegedProcessorTime"));
        il.Emit(OpCodes.Stloc, spanLocal);
        il.Emit(OpCodes.Ldloca, spanLocal);
        il.Emit(OpCodes.Call, typeof(TimeSpan).GetProperty("Ticks")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, systemLocal);

        // prev delta: if (prev is Dictionary) subtract its user/system
        var noPrev = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, prevDictLocal);
        il.Emit(OpCodes.Ldloc, prevDictLocal);
        il.Emit(OpCodes.Brfalse, noPrev);

        var noPrevUser = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, prevDictLocal);
        il.Emit(OpCodes.Ldstr, "user");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, noPrevUser);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, noPrevUser);
        il.Emit(OpCodes.Ldloc, userLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, userLocal);
        il.MarkLabel(noPrevUser);

        var noPrevSystem = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, prevDictLocal);
        il.Emit(OpCodes.Ldstr, "system");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, noPrevSystem);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, noPrevSystem);
        il.Emit(OpCodes.Ldloc, systemLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, systemLocal);
        il.MarkLabel(noPrevSystem);

        il.MarkLabel(noPrev);

        // return new Dictionary { ["user"] = max(0,user), ["system"] = max(0,system) }
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "user");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ldloc, userLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "system");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ldloc, systemLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessResourceUsage() → Dictionary in the Node shape; .NET-derivable
    /// values populated, libuv counters 0.
    /// </summary>
    private void EmitProcessResourceUsage(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessResourceUsage",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessResourceUsage = method;

        var il = method.GetILGenerator();
        var procLocal = il.DeclareLocal(_types.Process);
        var spanLocal = il.DeclareLocal(typeof(TimeSpan));
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item");

        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
        il.Emit(OpCodes.Stloc, procLocal);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        void EmitCpuEntry(string key, string property)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldloc, procLocal);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, property));
            il.Emit(OpCodes.Stloc, spanLocal);
            il.Emit(OpCodes.Ldloca, spanLocal);
            il.Emit(OpCodes.Call, typeof(TimeSpan).GetProperty("Ticks")!.GetGetMethod()!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ldc_R8, 10.0);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, setItem);
        }

        EmitCpuEntry("userCPUTime", "UserProcessorTime");
        EmitCpuEntry("systemCPUTime", "PrivilegedProcessorTime");

        // maxRSS (kilobytes)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "maxRSS");
        il.Emit(OpCodes.Ldloc, procLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "PeakWorkingSet64"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1024.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, setItem);

        foreach (var key in new[]
        {
            "sharedMemorySize", "unsharedDataSize", "unsharedStackSize",
            "minorPageFault", "majorPageFault", "swappedOut", "fsRead", "fsWrite",
            "ipcSent", "ipcReceived", "signalsCount",
            "voluntaryContextSwitches", "involuntaryContextSwitches",
        })
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, setItem);
        }

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessAvailableMemory() → object(double): GC's total available minus load.
    /// </summary>
    private void EmitProcessAvailableMemory(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessAvailableMemory",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessAvailableMemory = method;

        var il = method.GetILGenerator();
        var infoLocal = il.DeclareLocal(typeof(GCMemoryInfo));
        il.Emit(OpCodes.Call, typeof(GC).GetMethod("GetGCMemoryInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, infoLocal);
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Call, typeof(GCMemoryInfo).GetProperty("TotalAvailableMemoryBytes")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Call, typeof(GCMemoryInfo).GetProperty("MemoryLoadBytes")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessGetActiveResourcesInfo() → List with one "Timeout" entry per
    /// active event-loop handle (approximation — same as the interpreter).
    /// </summary>
    private void EmitProcessGetActiveResourcesInfo(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessGetActiveResourcesInfo",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetActiveResourcesInfoM = method;

        var il = method.GetILGenerator();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var countLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, listLocal);

        // count = $EventLoop.GetInstance().HasPendingWork() ? approximate handles : 0
        // The active-handle count itself is private; use HasPendingWork to decide
        // between 0 and 1 entries per pending state. Approximation documented.
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopHasPendingWork);
        var noneLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noneLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, countLocal);
        var haveCount = il.DefineLabel();
        il.Emit(OpCodes.Br, haveCount);
        il.MarkLabel(noneLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, countLocal);
        il.MarkLabel(haveCount);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldstr, "Timeout");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessHrtimeBigint() → object(BigInteger): monotonic nanoseconds since
    /// process start (same baseline as uptime/hrtime).
    /// </summary>
    private void EmitProcessHrtimeBigint(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessHrtimeBigint",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessHrtimeBigint = method;

        var il = method.GetILGenerator();
        // nanos = (GetTimestamp() - baseline) * (1e9 / Frequency) as double → long
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Stopwatch, "GetTimestamp"));
        il.Emit(OpCodes.Ldsfld, runtime.ProcessUptimeBaselineField);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1_000_000_000.0);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldsfld, _types.GetField(_types.Stopwatch, "Frequency"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.BigInteger, _types.Int64));
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>ProcessMemoryRss() → object(double): the working set.</summary>
    private void EmitProcessMemoryRss(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessMemoryRss",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessMemoryRss = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "WorkingSet64"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ProcessGetHrtimeFn/ProcessGetMemoryUsageFn — cached $TSFunction
    /// values carrying their Node function-members (hrtime.bigint,
    /// memoryUsage.rss) attached via the dynamic SetProperty path (the same
    /// storage `fn.x = …` uses).
    /// </summary>
    private void EmitProcessFunctionWithMemberGetters(TypeBuilder tb, EmittedRuntime runtime)
    {
        runtime.ProcessGetHrtimeFn = EmitFunctionWithMemberGetter(tb, runtime,
            "ProcessGetHrtimeFn", _processHrtimeFnField,
            runtime.ProcessHrtime, "bigint", runtime.ProcessHrtimeBigint);
        runtime.ProcessGetMemoryUsageFn = EmitFunctionWithMemberGetter(tb, runtime,
            "ProcessGetMemoryUsageFn", _processMemoryUsageFnField,
            runtime.ProcessMemoryUsage, "rss", runtime.ProcessMemoryRss);
    }

    private MethodBuilder EmitFunctionWithMemberGetter(
        TypeBuilder tb, EmittedRuntime runtime, string name, FieldBuilder cacheField,
        MethodBuilder mainImpl, string memberName, MethodBuilder memberImpl)
    {
        var method = tb.DefineMethod(name,
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);

        var il = method.GetILGenerator();
        var create = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Brfalse, create);
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(create);
        var fnLocal = il.DeclareLocal(_types.Object);

        // fn = new $TSFunction(null, mainImpl)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldtoken, mainImpl);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Stloc, fnLocal);

        // SetProperty(fn, memberName, new $TSFunction(null, memberImpl))
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Ldstr, memberName);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldtoken, memberImpl);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Stsfld, cacheField);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// ProcessUmask(object mask) → object(double): stored-value semantics
    /// (default 0o22). Get with no/undefined arg; set returns the previous.
    /// </summary>
    private void EmitProcessUmask(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("ProcessUmask",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        runtime.ProcessUmask = method;

        var il = method.GetILGenerator();
        var prevLocal = il.DeclareLocal(_types.Int32);
        var initDone = il.DefineLabel();

        // Lazy default 0o22 (0 means "uninitialized" is wrong; use a sentinel
        // via a second bool? Simpler: initialize in cctor-less style — treat 0
        // as valid and set the default at first read when never written.)
        // The field is initialized to 0o22 the first time either path runs.
        var initField = tb.DefineField("_processUmaskInit", _types.Boolean, FieldAttributes.Private | FieldAttributes.Static);
        il.Emit(OpCodes.Ldsfld, initField);
        il.Emit(OpCodes.Brtrue, initDone);
        il.Emit(OpCodes.Ldc_I4, 0x12); // 0o22
        il.Emit(OpCodes.Stsfld, _processUmaskField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initField);
        il.MarkLabel(initDone);

        il.Emit(OpCodes.Ldsfld, _processUmaskField);
        il.Emit(OpCodes.Stloc, prevLocal);

        // set path: number arg
        var getOnly = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, getOnly);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stsfld, _processUmaskField);
        il.MarkLabel(getOnly);

        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the process.report support: ProcessBuildReport (dictionary with
    /// the BCL-derivable sections), getReport/writeReport impls, and
    /// ProcessGetReport (the cached report config object carrying both fns).
    /// </summary>
    private void EmitProcessReportHelpers(TypeBuilder tb, EmittedRuntime runtime)
    {
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item");

        // ---- ProcessBuildReport() → Dictionary ----
        var build = tb.DefineMethod("ProcessBuildReport",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "ia32",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown"
            };
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win32"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "unknown";

            var il = build.GetILGenerator();
            var rootLocal = il.DeclareLocal(_types.DictionaryStringObject);
            var headerLocal = il.DeclareLocal(_types.DictionaryStringObject);

            // Static skeleton (baked): header constants + empty sections.
            EmitInlineDictionary(il,
            [
                ("header", (object)new (string, object)[]
                {
                    ("reportVersion", 3.0),
                    ("event", "JavaScript API"),
                    ("trigger", "GetReport"),
                    ("nodejsVersion", "v" + Runtime.BuiltIns.ProcessBuiltIns.NodeVersion),
                    ("wordSize", 64.0),
                    ("arch", arch),
                    ("platform", platform),
                }),
                ("javascriptStack", (object)new (string, object)[] { ("message", "") }),
                ("javascriptHeap", (object)Array.Empty<(string, object)>()),
                ("userLimits", (object)Array.Empty<(string, object)>()),
            ]);
            il.Emit(OpCodes.Stloc, rootLocal);

            // Empty list-valued sections (nativeStack/libuv/workers/sharedObjects
            // + javascriptStack.stack) — ceilings in compiled mode.
            foreach (var listKey in new[] { "nativeStack", "libuv", "workers", "sharedObjects" })
            {
                il.Emit(OpCodes.Ldloc, rootLocal);
                il.Emit(OpCodes.Ldstr, listKey);
                il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
                il.Emit(OpCodes.Callvirt, setItem);
            }
            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ldstr, "javascriptStack");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item"));
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "stack");
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Callvirt, setItem);

            // header = (Dictionary)root["header"]
            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ldstr, "header");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item"));
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, headerLocal);

            // header dynamic fields
            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "processId");
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessId"));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "threadId");
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "CurrentManagedThreadId"));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "cwd");
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Directory, "GetCurrentDirectory"));
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "dumpEventTime");
            il.Emit(OpCodes.Call, typeof(DateTime).GetProperty("Now")!.GetGetMethod()!);
            var nowLocal = il.DeclareLocal(typeof(DateTime));
            il.Emit(OpCodes.Stloc, nowLocal);
            il.Emit(OpCodes.Ldloca, nowLocal);
            il.Emit(OpCodes.Ldstr, "yyyy-MM-dd HH:mm:ss");
            il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToString", [_types.String])!);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "host");
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "MachineName"));
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, headerLocal);
            il.Emit(OpCodes.Ldstr, "osName");
            il.Emit(OpCodes.Call, typeof(RuntimeInformation).GetProperty("OSDescription")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, setItem);

            // resourceUsage + environmentVariables from live helpers
            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ldstr, "resourceUsage");
            il.Emit(OpCodes.Call, runtime.ProcessResourceUsage);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ldstr, "environmentVariables");
            il.Emit(OpCodes.Call, runtime.ProcessGetEnv);
            il.Emit(OpCodes.Callvirt, setItem);

            // javascriptHeap live values
            var heapLocal = il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ldstr, "javascriptHeap");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item"));
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, heapLocal);

            il.Emit(OpCodes.Ldloc, heapLocal);
            il.Emit(OpCodes.Ldstr, "usedMemory");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.GC, "GetTotalMemory", _types.Boolean));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Ret);
        }

        // ---- ProcessWriteReportImpl(object filename, object err) → object(string) ----
        var write = tb.DefineMethod("ProcessWriteReportImpl",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object, _types.Object]);
        {
            var il = write.GetILGenerator();
            var nameLocal = il.DeclareLocal(_types.String);

            var haveName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Stloc, nameLocal);
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Brtrue, haveName);

            // default: report.<yyyyMMdd.HHmmss>.<pid>.001.json
            var nowLocal = il.DeclareLocal(typeof(DateTime));
            il.Emit(OpCodes.Ldstr, "report.");
            il.Emit(OpCodes.Call, typeof(DateTime).GetProperty("Now")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, nowLocal);
            il.Emit(OpCodes.Ldloca, nowLocal);
            il.Emit(OpCodes.Ldstr, "yyyyMMdd.HHmmss");
            il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToString", [_types.String])!);
            il.Emit(OpCodes.Ldstr, ".");
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessId"));
            var pidLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Stloc, pidLocal);
            il.Emit(OpCodes.Ldloca, pidLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Ldstr, ".001.json");
            // Concat(string, string, string, string) then Concat(string, string)
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String, _types.String));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
            il.Emit(OpCodes.Stloc, nameLocal);

            il.MarkLabel(haveName);
            // File.WriteAllText(name, stringify(BuildReport())). JsonStringify is
            // gated on UsesJSON; fall back to the unconditional display Stringify
            // when the program never touches JSON (best-effort report content).
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Call, build);
            if (_features.UsesJSON)
            {
                // JsonStringify returns object (JSON.stringify may yield
                // undefined for other inputs); a dictionary always yields a
                // string — narrow for WriteAllText.
                il.Emit(OpCodes.Call, runtime.JsonStringify);
                il.Emit(OpCodes.Castclass, _types.String);
            }
            else
            {
                il.Emit(OpCodes.Call, runtime.Stringify);
            }
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "WriteAllText", _types.String, _types.String));
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ret);
        }

    // ---- ProcessGetReport() → cached Dictionary (config + fns) ----
        var get = tb.DefineMethod("ProcessGetReport",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        runtime.ProcessGetReport = get;
        {
            var il = get.GetILGenerator();
            var create = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _processReportField);
            il.Emit(OpCodes.Brfalse, create);
            il.Emit(OpCodes.Ldsfld, _processReportField);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(create);
            var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
            EmitInlineDictionary(il,
            [
                ("directory", ""), ("filename", ""), ("compact", false),
                ("signal", "SIGUSR2"), ("reportOnFatalError", false),
                ("reportOnSignal", false), ("reportOnUncaughtException", false),
                ("excludeNetwork", false),
            ]);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, dictLocal);

            // getReport / writeReport as $TSFunction wrappers
            void AddFn(string name, MethodBuilder impl)
            {
                il.Emit(OpCodes.Ldloc, dictLocal);
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldtoken, impl);
                il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
                il.Emit(OpCodes.Castclass, _types.MethodInfo);
                il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
                il.Emit(OpCodes.Callvirt, setItem);
            }

            // getReport([err]) — err is accepted and ignored (stack sections are
            // ceilings in compiled mode).
            var getReportImpl = tb.DefineMethod("ProcessGetReportImpl",
                MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
            {
                var gil = getReportImpl.GetILGenerator();
                gil.Emit(OpCodes.Call, build);
                gil.Emit(OpCodes.Ret);
            }

            AddFn("getReport", getReportImpl);
            AddFn("writeReport", write);

            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Stsfld, _processReportField);
            il.Emit(OpCodes.Ldsfld, _processReportField);
            il.Emit(OpCodes.Ret);
        }
    }

    // =====================================================================
    // $ProcessEmitClosure — deferred process-event emission on the event loop
    // =====================================================================

    private void EmitProcessEmitClosureType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$ProcessEmitClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var eventField = tb.DefineField("_event", _types.String, FieldAttributes.Private);
        var argField = tb.DefineField("_arg", _types.Object, FieldAttributes.Private);

        var ctor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [_types.String, _types.Object]);
        _processEmitClosureCtor = ctor;
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, eventField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, argField);
            il.Emit(OpCodes.Ret);
        }

        var invoke = tb.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        runtime.ProcessEmitClosureInvoke = invoke;
        {
            var il = invoke.GetILGenerator();
            // ((EventEmitter)GetProcessObject()).Emit(_event, [_arg]) — discard result
            il.Emit(OpCodes.Call, runtime.GetProcessObject);
            il.Emit(OpCodes.Castclass, runtime.TSEventEmitterType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, eventField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, argField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        tb.CreateType();
    }

    // =====================================================================
    // The $Process type
    // =====================================================================

    private void EmitProcessType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$Process",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType);
        tb.AddInterfaceImplementation(runtime.IHasFieldsInterface);

        _processFieldsField = tb.DefineField("_fields", _types.DictionaryStringObject, FieldAttributes.Private);
        var instanceField = tb.DefineField("_instance", tb, FieldAttributes.Private | FieldAttributes.Static);

        // ctor: base(); _fields = new()
        var ctor = tb.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Stfld, _processFieldsField);
            il.Emit(OpCodes.Ret);
        }

        // static GetInstance()
        var getInstance = tb.DefineMethod("GetInstance",
            MethodAttributes.Public | MethodAttributes.Static, tb, Type.EmptyTypes);
        _processGetInstance = getInstance;
        {
            var il = getInstance.GetILGenerator();
            var create = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Brfalse, create);
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(create);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stsfld, instanceField);
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ret);
        }

        EmitProcessHasFieldsImplementation(tb, runtime);
        EmitProcessInstanceProperties(tb, runtime);
        EmitProcessInstanceMethods(tb, runtime);
        EmitProcessOnListenerAdded(tb, runtime);

        // ToString parity with the interpreter's SharpTSProcess.
        var toString = tb.DefineMethod("ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        {
            var il = toString.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "[object process]");
            il.Emit(OpCodes.Ret);
        }

        tb.CreateType();
    }

    /// <summary>
    /// $IHasFields implementation: expando dictionary first, then PascalCase
    /// reflection over the $Process surface (properties, then methods wrapped
    /// as $TSFunction). Mirrors the generic emitted-class dispatch.
    /// </summary>
    private void EmitProcessHasFieldsImplementation(TypeBuilder tb, EmittedRuntime runtime)
    {
        const BindingFlags lookupFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

        // Fields property
        var fieldsGetter = tb.DefineMethod("get_Fields",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.DictionaryStringObject, Type.EmptyTypes);
        {
            var il = fieldsGetter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _processFieldsField);
            il.Emit(OpCodes.Ret);
        }
        var fieldsProp = tb.DefineProperty("Fields", PropertyAttributes.None, _types.DictionaryStringObject, null);
        fieldsProp.SetGetMethod(fieldsGetter);
        tb.DefineMethodOverride(fieldsGetter, runtime.IHasFieldsFieldsGetter);

        // GetProperty(string) — expando → property → method wrapper → null
        var getProp = tb.DefineMethod("GetProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object, [_types.String]);
        {
            var il = getProp.GetILGenerator();
            var valueLocal = il.DeclareLocal(_types.Object);
            var pascalLocal = il.DeclareLocal(_types.String);
            var piLocal = il.DeclareLocal(typeof(PropertyInfo));
            var miLocal = il.DeclareLocal(_types.MethodInfo);

            // expando
            var notExpando = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _processFieldsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
            il.Emit(OpCodes.Brfalse, notExpando);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notExpando);

            // pascal = ToPascalCase(name)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToPascalCase);
            il.Emit(OpCodes.Stloc, pascalLocal);

            // property?
            var noProperty = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldloc, pascalLocal);
            il.Emit(OpCodes.Ldc_I4, (int)lookupFlags);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Stloc, piLocal);
            il.Emit(OpCodes.Ldloc, piLocal);
            il.Emit(OpCodes.Brfalse, noProperty);
            il.Emit(OpCodes.Ldloc, piLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetMethod("GetValue", [_types.Object])!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noProperty);

            // method? → $TSFunction(this, mi)
            var noMethod = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldloc, pascalLocal);
            il.Emit(OpCodes.Ldc_I4, (int)lookupFlags);
            il.Emit(OpCodes.Call, runtime.SafeGetMethod);
            il.Emit(OpCodes.Stloc, miLocal);
            il.Emit(OpCodes.Ldloc, miLocal);
            il.Emit(OpCodes.Brfalse, noMethod);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, miLocal);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noMethod);

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        tb.DefineMethodOverride(getProp, runtime.IHasFieldsGetProperty);

        // SetProperty(string, object) — writable property → setter; else expando
        var setProp = tb.DefineMethod("SetProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(void), [_types.String, _types.Object]);
        {
            var il = setProp.GetILGenerator();
            var piLocal = il.DeclareLocal(typeof(PropertyInfo));

            var expando = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToPascalCase);
            il.Emit(OpCodes.Ldc_I4, (int)lookupFlags);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Stloc, piLocal);
            il.Emit(OpCodes.Ldloc, piLocal);
            il.Emit(OpCodes.Brfalse, expando);
            il.Emit(OpCodes.Ldloc, piLocal);
            il.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetProperty("CanWrite")!.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, expando);
            il.Emit(OpCodes.Ldloc, piLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetMethod("SetValue", [_types.Object, _types.Object])!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(expando);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _processFieldsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            il.Emit(OpCodes.Ret);
        }
        tb.DefineMethodOverride(setProp, runtime.IHasFieldsSetProperty);

        // HasProperty(string)
        var hasProp = tb.DefineMethod("HasProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Boolean, [_types.String]);
        {
            var il = hasProp.GetILGenerator();
            var trueLabel = il.DefineLabel();
            var pascalLocal = il.DeclareLocal(_types.String);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _processFieldsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brtrue, trueLabel);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToPascalCase);
            il.Emit(OpCodes.Stloc, pascalLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldloc, pascalLocal);
            il.Emit(OpCodes.Ldc_I4, (int)lookupFlags);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Brtrue, trueLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldloc, pascalLocal);
            il.Emit(OpCodes.Ldc_I4, (int)lookupFlags);
            il.Emit(OpCodes.Call, runtime.SafeGetMethod);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(trueLabel);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
        tb.DefineMethodOverride(hasProp, runtime.IHasFieldsHasProperty);
    }

    /// <summary>
    /// Defines the PascalCase instance properties of $Process (all typed
    /// object; getters delegate to $Runtime helpers). Dynamic reads resolve
    /// them through GetProperty's reflection path.
    /// </summary>
    private void EmitProcessInstanceProperties(TypeBuilder tb, EmittedRuntime runtime)
    {
        void Define(string name, Action<ILGenerator> emitGet, Action<ILGenerator>? emitSet = null)
        {
            var getter = tb.DefineMethod("get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                _types.Object, Type.EmptyTypes);
            var gil = getter.GetILGenerator();
            emitGet(gil);
            gil.Emit(OpCodes.Ret);

            var prop = tb.DefineProperty(name, PropertyAttributes.None, _types.Object, null);
            prop.SetGetMethod(getter);

            if (emitSet != null)
            {
                var setter = tb.DefineMethod("set_" + name,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    typeof(void), [_types.Object]);
                var sil = setter.GetILGenerator();
                emitSet(sil);
                sil.Emit(OpCodes.Ret);
                prop.SetSetMethod(setter);
            }
        }

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win32"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "unknown";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };

        Define("Platform", il => il.Emit(OpCodes.Ldstr, platform));
        Define("Arch", il => il.Emit(OpCodes.Ldstr, arch));
        Define("Pid", il =>
        {
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessId"));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
        });
        Define("Ppid", il => il.Emit(OpCodes.Call, runtime.ProcessGetPpid));
        Define("Version", il => il.Emit(OpCodes.Ldstr, "v" + Runtime.BuiltIns.ProcessBuiltIns.NodeVersion));
        Define("Versions", il => il.Emit(OpCodes.Call, runtime.ProcessGetVersions));
        Define("Env", il => il.Emit(OpCodes.Call, runtime.ProcessGetEnv));
        Define("Argv", il => il.Emit(OpCodes.Call, runtime.ProcessGetArgv));
        Define("Argv0", il =>
        {
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetCommandLineArgs"));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
        });
        Define("ExecPath", il =>
        {
            var haveIt = il.DefineLabel();
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessPath"));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, haveIt);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetCommandLineArgs"));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(haveIt);
        });
        Define("ExecArgv", il => il.Emit(OpCodes.Call, runtime.ProcessGetExecArgv));
        Define("ExitCode",
            il =>
            {
                il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ExitCode"));
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, _types.Double);
            },
            il =>
            {
                // null/undefined → 0; double → (int)
                var isDouble = il.DefineLabel();
                var store = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, _types.Double);
                il.Emit(OpCodes.Brtrue, isDouble);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Br, store);
                il.MarkLabel(isDouble);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Unbox_Any, _types.Double);
                il.Emit(OpCodes.Conv_I4);
                il.MarkLabel(store);
                il.Emit(OpCodes.Call, _types.GetProperty(_types.Environment, "ExitCode").SetMethod!);
            });
        Define("Title",
            il => il.Emit(OpCodes.Call, runtime.ProcessGetTitle),
            il =>
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.ProcessSetTitle);
            });
        Define("Config", il => il.Emit(OpCodes.Call, runtime.ProcessGetConfig));
        Define("Release", il => il.Emit(OpCodes.Call, runtime.ProcessGetRelease));
        Define("Features", il => il.Emit(OpCodes.Call, runtime.ProcessGetFeatures));
        Define("DebugPort", il =>
        {
            il.Emit(OpCodes.Ldc_R8, 9229.0);
            il.Emit(OpCodes.Box, _types.Double);
        });
        Define("AllowedNodeEnvironmentFlags", il => il.Emit(OpCodes.Call, runtime.ProcessGetAllowedFlags));
        Define("Stdin", il =>
        {
            if (runtime.GetStdin is null) il.Emit(OpCodes.Ldnull);
            else il.Emit(OpCodes.Call, runtime.GetStdin);
        });
        Define("Stdout", il =>
        {
            if (runtime.GetStdout is null) il.Emit(OpCodes.Ldnull);
            else il.Emit(OpCodes.Call, runtime.GetStdout);
        });
        Define("Stderr", il =>
        {
            if (runtime.GetStderr is null) il.Emit(OpCodes.Ldnull);
            else il.Emit(OpCodes.Call, runtime.GetStderr);
        });
        Define("Report", il => il.Emit(OpCodes.Call, runtime.ProcessGetReport));
        Define("Connected", il =>
        {
            // Compiled fork children run interpreted (see child_process #1017) —
            // a compiled binary never has an in-process IPC channel.
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, _types.Boolean);
        });
        Define("Hrtime", il => il.Emit(OpCodes.Call, runtime.ProcessGetHrtimeFn));
        Define("MemoryUsage", il => il.Emit(OpCodes.Call, runtime.ProcessGetMemoryUsageFn));
        Define("NextTick", il => il.Emit(OpCodes.Call, runtime.ProcessGetNextTick));

        void DefineFlag(string name, FieldBuilder field)
        {
            Define(name,
                il =>
                {
                    il.Emit(OpCodes.Ldsfld, field);
                    il.Emit(OpCodes.Box, _types.Boolean);
                },
                il =>
                {
                    var isTrue = il.DefineLabel();
                    var store = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Isinst, _types.Boolean);
                    il.Emit(OpCodes.Brtrue, isTrue);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Br, store);
                    il.MarkLabel(isTrue);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Unbox_Any, _types.Boolean);
                    il.MarkLabel(store);
                    il.Emit(OpCodes.Stsfld, field);
                });
        }
        DefineFlag("ThrowDeprecation", _processThrowDeprecationField);
        DefineFlag("TraceDeprecation", _processTraceDeprecationField);
        DefineFlag("NoDeprecation", _processNoDeprecationField);
        DefineFlag("SourceMapsEnabled", _processSourceMapsEnabledField);
    }

    /// <summary>
    /// Defines the PascalCase instance methods of $Process (thin wrappers over
    /// $Runtime statics). Dynamic invocation resolves them via GetProperty's
    /// method-wrapper path or the generic reflection dispatch.
    /// </summary>
    private void EmitProcessInstanceMethods(TypeBuilder tb, EmittedRuntime runtime)
    {
        MethodBuilder Define(string name, Type[] parameters, Action<ILGenerator> body)
        {
            var method = tb.DefineMethod(name, MethodAttributes.Public, _types.Object, parameters);
            var il = method.GetILGenerator();
            body(il);
            il.Emit(OpCodes.Ret);
            return method;
        }

        Define("Cwd", Type.EmptyTypes, il =>
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Directory, "GetCurrentDirectory")));

        Define("Chdir", [_types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "SetCurrentDirectory", _types.String));
            il.Emit(OpCodes.Ldnull);
        });

        Define("Exit", [_types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ProcessExit);
        });

        Define("Uptime", Type.EmptyTypes, il =>
        {
            il.Emit(OpCodes.Call, runtime.ProcessUptime);
            il.Emit(OpCodes.Box, _types.Double);
        });

        Define("Kill", [_types.Object, _types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.ProcessKill);
        });

        Define("Abort", Type.EmptyTypes, il =>
        {
            il.Emit(OpCodes.Ldstr, "process.abort() called");
            il.Emit(OpCodes.Call, typeof(Environment).GetMethod("FailFast", [_types.String])!);
            il.Emit(OpCodes.Ldnull);
        });

        Define("Umask", [_types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ProcessUmask);
        });

        Define("CpuUsage", [_types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ProcessCpuUsage);
        });

        Define("ResourceUsage", Type.EmptyTypes, il =>
            il.Emit(OpCodes.Call, runtime.ProcessResourceUsage));

        Define("AvailableMemory", Type.EmptyTypes, il =>
            il.Emit(OpCodes.Call, runtime.ProcessAvailableMemory));

        Define("ConstrainedMemory", Type.EmptyTypes, il =>
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
        });

        Define("GetActiveResourcesInfo", Type.EmptyTypes, il =>
            il.Emit(OpCodes.Call, runtime.ProcessGetActiveResourcesInfoM));

        Define("EmitWarning", [_types.Object, _types.Object, _types.Object, _types.Object], il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Call, runtime.ProcessEmitWarning);
        });

        Define("SetSourceMapsEnabled", [_types.Object], il =>
        {
            var isTrue = il.DefineLabel();
            var store = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.Boolean);
            il.Emit(OpCodes.Brtrue, isTrue);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Br, store);
            il.MarkLabel(isTrue);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.MarkLabel(store);
            il.Emit(OpCodes.Stsfld, _processSourceMapsEnabledField);
            il.Emit(OpCodes.Ldnull);
        });
    }

    /// <summary>
    /// Overrides $EventEmitter.OnListenerAdded: process.on('SIGINT', …) lazily
    /// installs the OS signal handler (#1081).
    /// </summary>
    private void EmitProcessOnListenerAdded(TypeBuilder tb, EmittedRuntime runtime)
    {
        var method = tb.DefineMethod("OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void), [_types.String]);

        var il = method.GetILGenerator();
        var register = il.DefineLabel();
        var done = il.DefineLabel();

        foreach (var signal in _processTrappableSignals)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, signal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, register);
        }
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(register);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ProcessRegisterSignal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        tb.DefineMethodOverride(method, runtime.TSEventEmitterOnListenerAdded);
    }

    // =====================================================================
    // Late helper bodies (reference $Process)
    // =====================================================================

    private void EmitGetProcessObjectBody(EmittedRuntime runtime)
    {
        var il = ((MethodBuilder)runtime.GetProcessObject).GetILGenerator();
        il.Emit(OpCodes.Call, _processGetInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessExit(object code): publish exitCode, emit 'exit' synchronously on
    /// the $Process singleton, terminate. (Fixes the historical compiled
    /// divergence where process.exit() never fired 'exit' listeners.)
    /// </summary>
    private void EmitProcessExitBody(EmittedRuntime runtime)
    {
        var il = ((MethodBuilder)runtime.ProcessExit).GetILGenerator();
        var codeLocal = il.DeclareLocal(_types.Int32);

        // code = arg is double ? (int)arg : Environment.ExitCode
        var isDouble = il.DefineLabel();
        var haveCode = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, isDouble);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ExitCode"));
        il.Emit(OpCodes.Br, haveCode);
        il.MarkLabel(isDouble);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.MarkLabel(haveCode);
        il.Emit(OpCodes.Stloc, codeLocal);

        // Environment.ExitCode = code (so 'exit' listeners read the final value)
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Environment, "ExitCode").SetMethod!);

        // GetInstance().Emit("exit", [code]) — swallow listener errors like Node
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Call, _processGetInstance);
        il.Emit(OpCodes.Ldstr, "exit");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Environment, "Exit", _types.Int32));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessKill(object pid, object signal) — see the interpreter's
    /// ProcessBuiltIns.Signals for semantics (signal 0 existence check,
    /// self-signal in-process dispatch, Process.Kill for termination signals).
    /// </summary>
    private void EmitProcessKillBody(EmittedRuntime runtime)
    {
        var il = ((MethodBuilder)runtime.ProcessKill).GetILGenerator();
        var pidLocal = il.DeclareLocal(_types.Int32);
        var signalLocal = il.DeclareLocal(_types.String);
        var signalNumLocal = il.DeclareLocal(_types.Int32);
        var targetLocal = il.DeclareLocal(_types.Process);

        void ThrowGuestError(string message, string code)
        {
            il.Emit(OpCodes.Ldstr, message);
            il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, code);
            il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeSetter);
            il.Emit(OpCodes.Call, runtime.CreateException);
            il.Emit(OpCodes.Throw);
        }

        // pid must be a number
        var pidOk = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, pidOk);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "The \"pid\" argument must be of type number.");
        il.MarkLabel(pidOk);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, pidLocal);

        // EnsureExists inline: Process.GetProcessById(pid) → target (throws ESRCH)
        void EmitEnsureExists()
        {
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc, pidLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Process, "GetProcessById", _types.Int32));
            il.Emit(OpCodes.Stloc, targetLocal);
            il.BeginCatchBlock(typeof(ArgumentException));
            il.Emit(OpCodes.Pop);
            ThrowGuestError("kill ESRCH", "ESRCH");
            il.EndExceptionBlock();
        }

        // signal 0 → existence check
        var notZero = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notZero);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bne_Un, notZero);
        EmitEnsureExists();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notZero);

        // Resolve signal name: default SIGTERM; string as-is; number mapped
        var resolved = il.DefineLabel();
        var checkString = il.DefineLabel();
        il.Emit(OpCodes.Ldstr, "SIGTERM");
        il.Emit(OpCodes.Stloc, signalLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, resolved); // null/undefined → default
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, signalLocal);
        il.Emit(OpCodes.Br, resolved);
        il.MarkLabel(checkString);
        // numeric form
        var badSignal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, badSignal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, signalNumLocal);
        foreach (var (name, number) in _processSignalNumbers)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, signalNumLocal);
            il.Emit(OpCodes.Ldc_I4, number);
            il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Stloc, signalLocal);
            il.Emit(OpCodes.Br, resolved);
            il.MarkLabel(next);
        }
        il.MarkLabel(badSignal);
        ThrowGuestError("Unknown signal", "ERR_UNKNOWN_SIGNAL");

        il.MarkLabel(resolved);

        // Validate the name and pick its number
        var validated = il.DefineLabel();
        foreach (var (name, number) in _processSignalNumbers)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, signalLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldc_I4, number);
            il.Emit(OpCodes.Stloc, signalNumLocal);
            il.Emit(OpCodes.Br, validated);
            il.MarkLabel(next);
        }
        ThrowGuestError("Unknown signal", "ERR_UNKNOWN_SIGNAL");
        il.MarkLabel(validated);

        // Self?
        var notSelf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pidLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessId"));
        il.Emit(OpCodes.Bne_Un, notSelf);

        // listeners? → dispatch in-process
        var noListeners = il.DefineLabel();
        il.Emit(OpCodes.Call, _processGetInstance);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterListenerCount);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble_Un, noListeners);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.ProcessDispatchSignal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noListeners);
        // default action for termination signals: exit(128+n); others ignored
        var selfIgnored = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, signalNumLocal);
        il.Emit(OpCodes.Ldc_I4, 28); // SIGWINCH — ignore-by-default
        il.Emit(OpCodes.Beq, selfIgnored);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Ldloc, signalNumLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Environment, "Exit", _types.Int32));
        il.MarkLabel(selfIgnored);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSelf);
        EmitEnsureExists();
        // Termination signals → Kill(); others are accepted no-ops
        var crossDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, signalNumLocal);
        il.Emit(OpCodes.Ldc_I4, 28);
        il.Emit(OpCodes.Beq, crossDone);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Process, "Kill"));
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        ThrowGuestError("kill EPERM", "EPERM");
        il.EndExceptionBlock();
        il.MarkLabel(crossDone);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessEmitWarning(warning, typeOrOptions, code, ctor): builds the
    /// warning object, honors the deprecation flags, prints the Node default
    /// line to stderr, and emits 'warning' asynchronously on the event loop.
    /// </summary>
    private void EmitProcessEmitWarningBody(EmittedRuntime runtime)
    {
        var il = ((MethodBuilder)runtime.ProcessEmitWarning).GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item");

        var typeLocal = il.DeclareLocal(_types.String);    // warning name
        var messageLocal = il.DeclareLocal(_types.String);
        var codeLocal = il.DeclareLocal(_types.String);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var optionsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);

        // type = "Warning"; code = null
        il.Emit(OpCodes.Ldstr, "Warning");
        il.Emit(OpCodes.Stloc, typeLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, codeLocal);

        // options: string → type; dictionary → {type, code}
        var optionsDone = il.DefineLabel();
        var tryDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, tryDict);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);
        // code from arg2 when string form
        var noCodeArg = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noCodeArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, codeLocal);
        il.MarkLabel(noCodeArg);
        il.Emit(OpCodes.Br, optionsDone);

        il.MarkLabel(tryDict);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, optionsDone);
        // type
        var noOptType = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, noOptType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noOptType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);
        il.MarkLabel(noOptType);
        // code
        var noOptCode = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, noOptCode);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noOptCode);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, codeLocal);
        il.MarkLabel(noOptCode);
        il.MarkLabel(optionsDone);

        // message = warning?.ToString() ?? ""
        var haveMessage = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, haveMessage);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, messageLocal);
        var messageDone = il.DefineLabel();
        il.Emit(OpCodes.Br, messageDone);
        il.MarkLabel(haveMessage);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, messageLocal);
        il.MarkLabel(messageDone);

        // DeprecationWarning: noDeprecation → suppress; throwDeprecation → throw
        var notDeprecation = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "DeprecationWarning");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notDeprecation);
        var notSuppressed = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _processNoDeprecationField);
        il.Emit(OpCodes.Brfalse, notSuppressed);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSuppressed);
        var notThrow = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _processThrowDeprecationField);
        il.Emit(OpCodes.Brfalse, notThrow);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notThrow);
        il.MarkLabel(notDeprecation);

        // Build the warning object { name, message, stack, code? }
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, setItem);
        var noCode = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Brfalse, noCode);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Callvirt, setItem);
        il.MarkLabel(noCode);

        // stderr default line: "(node:<pid>) [code] <name>: <message>"
        var pidLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "Error"));
        il.Emit(OpCodes.Ldstr, "(node:");
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessId"));
        il.Emit(OpCodes.Stloc, pidLocal);
        il.Emit(OpCodes.Ldloca, pidLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Ldstr, ") ");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        // + optional "[code] "
        var codeAppended = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Brfalse, codeAppended);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Ldstr, "] ");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.MarkLabel(codeAppended);
        // + "name: message"
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));

        // Emit 'warning' on the next loop turn
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldstr, "warning");
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, _processEmitClosureCtor);
        il.Emit(OpCodes.Ldftn, runtime.ProcessEmitClosureInvoke);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ProcessRunLifecycle(): fires 'beforeExit' at loop drain (re-running the
    /// loop while listeners schedule new work), then 'exit'. Called by the
    /// entry point right after the first $EventLoop.Run() returns.
    /// </summary>
    private void EmitProcessRunLifecycleBody(EmittedRuntime runtime)
    {
        var il = ((MethodBuilder)runtime.ProcessRunLifecycle).GetILGenerator();

        void EmitProcessEvent(string eventName)
        {
            il.Emit(OpCodes.Call, _processGetInstance);
            il.Emit(OpCodes.Ldstr, eventName);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ExitCode"));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        }

        var loopTop = il.DefineLabel();
        var done = il.DefineLabel();

        il.MarkLabel(loopTop);
        EmitProcessEvent("beforeExit");
        il.Emit(OpCodes.Brfalse, done); // no listeners → exit phase

        // listeners ran; if they scheduled work → run the loop again and re-fire
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopHasPendingWork);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRun);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(done);
        EmitProcessEvent("exit");
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Signal machinery: registration (PosixSignalRegistration, deduped, kept
    /// alive in a static dictionary) and dispatch (event-loop-scheduled emit).
    /// SIGBREAK maps to the CTRL_BREAK-backed PosixSignal on Windows.
    /// </summary>
    private void EmitProcessSignalMachinery(TypeBuilder tb, EmittedRuntime runtime)
    {
        // ---- static handler: void ProcessSignalHandler(PosixSignalContext) ----
        var handler = tb.DefineMethod("ProcessSignalHandler",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(System.Runtime.InteropServices.PosixSignalContext)]);
        {
            var il = handler.GetILGenerator();
            // ctx.Cancel = true
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, typeof(PosixSignalContext).GetProperty("Cancel")!.GetSetMethod()!);

            // map ctx.Signal → Node name (compile-time platform bake for SIGBREAK)
            var signalLocal = il.DeclareLocal(typeof(PosixSignal));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(PosixSignalContext).GetProperty("Signal")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, signalLocal);

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var map = new List<(PosixSignal Signal, string Name)>
            {
                (PosixSignal.SIGINT, "SIGINT"),
                (PosixSignal.SIGTERM, "SIGTERM"),
                (PosixSignal.SIGHUP, "SIGHUP"),
                (PosixSignal.SIGQUIT, isWindows ? "SIGBREAK" : "SIGQUIT"),
            };
            // SIGWINCH is [UnsupportedOSPlatform("windows")] and can never fire there,
            // so it is baked out of the emitted dispatch map on Windows (same
            // compile-time platform bake as SIGBREAK above).
            if (!OperatingSystem.IsWindows())
                map.Add((PosixSignal.SIGWINCH, "SIGWINCH"));
            var end = il.DefineLabel();
            foreach (var (signal, name) in map)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, signalLocal);
                il.Emit(OpCodes.Ldc_I4, (int)signal);
                il.Emit(OpCodes.Bne_Un, next);
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, runtime.ProcessDispatchSignal);
                il.Emit(OpCodes.Br, end);
                il.MarkLabel(next);
            }
            il.MarkLabel(end);
            il.Emit(OpCodes.Ret);
        }

        // ---- ProcessDispatchSignal(string name): schedule emit on the loop ----
        {
            var il = ((MethodBuilder)runtime.ProcessDispatchSignal).GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0); // arg to the listener is the signal name
            il.Emit(OpCodes.Newobj, _processEmitClosureCtor);
            il.Emit(OpCodes.Ldftn, runtime.ProcessEmitClosureInvoke);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
            il.Emit(OpCodes.Ret);
        }

        // ---- ProcessRegisterSignal(string name) ----
        {
            var il = ((MethodBuilder)runtime.ProcessRegisterSignal).GetILGenerator();
            var regsLocal = il.DeclareLocal(_types.DictionaryStringObject);

            // lazy dictionary
            var haveDict = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _processSignalRegistrationsField);
            il.Emit(OpCodes.Brtrue, haveDict);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Stsfld, _processSignalRegistrationsField);
            il.MarkLabel(haveDict);
            il.Emit(OpCodes.Ldsfld, _processSignalRegistrationsField);
            il.Emit(OpCodes.Stloc, regsLocal);

            // dedupe
            var notRegistered = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, regsLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brfalse, notRegistered);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notRegistered);

            // map name → PosixSignal (SIGBREAK rides the CTRL_BREAK-backed SIGQUIT)
            var signalLocal = il.DeclareLocal(_types.Int32);
            var map = new List<(string Name, PosixSignal Signal)>
            {
                ("SIGINT", PosixSignal.SIGINT),
                ("SIGTERM", PosixSignal.SIGTERM),
                ("SIGHUP", PosixSignal.SIGHUP),
                ("SIGQUIT", PosixSignal.SIGQUIT),
                ("SIGBREAK", PosixSignal.SIGQUIT),
            };
            // SIGWINCH is [UnsupportedOSPlatform("windows")]: registering it there
            // would just throw (swallowed below), so bake it out of the emitted map
            // on Windows — process.on('SIGWINCH') stays the same silent no-op.
            if (!OperatingSystem.IsWindows())
                map.Add(("SIGWINCH", PosixSignal.SIGWINCH));
            var haveSignal = il.DefineLabel();
            foreach (var (name, signal) in map)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldc_I4, (int)signal);
                il.Emit(OpCodes.Stloc, signalLocal);
                il.Emit(OpCodes.Br, haveSignal);
                il.MarkLabel(next);
            }
            il.Emit(OpCodes.Ret); // unknown name — nothing to register
            il.MarkLabel(haveSignal);

            // try { regs[name] = PosixSignalRegistration.Create(sig, handler) } catch { }
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc, regsLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, signalLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, handler);
            il.Emit(OpCodes.Newobj, typeof(Action<PosixSignalContext>).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, typeof(PosixSignalRegistration).GetMethod("Create",
                [typeof(PosixSignal), typeof(Action<PosixSignalContext>)])!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            il.BeginCatchBlock(_types.Exception);
            il.Emit(OpCodes.Pop);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);
        }
    }
}
