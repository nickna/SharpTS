using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Compiled-mode real async for child_process (#1012). Mirrors the interpreter
/// (Runtime/BuiltIns/Modules/Interpreter/ChildProcessModuleInterpreter.cs) and reuses
/// the fs #971 backgrounding shape: a process runs on Task.Run while an EventLoop.Ref()
/// keeps the loop alive, then the callback / lifecycle events fire and the loop is
/// Unref'd (with the same Task.Delay grace fs uses to keep fire-and-forget callbacks
/// from racing program exit).
///
/// The ChildProcess returned to guest code is a Dictionary&lt;string,object?&gt; (a compiled
/// "$Object"); the worker mutates that dict in place (pid/exitCode/killed/...) so the
/// guest's property reads observe live state. All IL is BCL-only so output stays standalone.
/// </summary>
public partial class RuntimeEmitter
{
    // $ChildProcessCtx — shared mutable state for one spawned/exec'd child.
    private TypeBuilder _childCtxType = null!;
    private ConstructorBuilder _childCtxCtor = null!;
    private FieldBuilder _childCtxProc = null!;        // Process (not yet started in dispatch)
    private FieldBuilder _childCtxEmitter = null!;     // object ($EventEmitter)
    private FieldBuilder _childCtxDict = null!;        // Dictionary<string,object?> (the ChildProcess)
    private FieldBuilder _childCtxCallback = null!;    // object (callback or null)
    private FieldBuilder _childCtxOptions = null!;     // object (options dict or null)
    private FieldBuilder _childCtxStdout = null!;      // object ($Readable)
    private FieldBuilder _childCtxStderr = null!;      // object ($Readable)
    private FieldBuilder _childCtxTimeout = null!;     // double (ms; <=0 = none)
    // Captured worker results, stored on the bg thread and replayed on the loop thread.
    private FieldBuilder _childCtxResStdout = null!;   // object (string)
    private FieldBuilder _childCtxResStderr = null!;   // object (string)
    private FieldBuilder _childCtxResCode = null!;     // int
    private FieldBuilder _childCtxResError = null!;    // object (error dict or null)
    private FieldBuilder _childCtxResKind = null!;     // int: 0 normal, 1 timeout, 2 exception
    private FieldBuilder _childCtxMaxBuffer = null!;   // int: exec maxBuffer cap (bytes); <0 unbounded
    private FieldBuilder _childCtxAsBuffer = null!;    // bool: encoding:'buffer' → raw $Buffer output
    private FieldBuilder _childCtxEncoding = null!;    // string: decode encoding (default "utf8")
    private MethodBuilder _childReadCappedBytes = null!;
    private MethodBuilder _childDecodeOutput = null!;
    private FieldBuilder _childCtxStdoutRedir = null!; // bool: stdout redirected (mode != inherit)
    private FieldBuilder _childCtxStderrRedir = null!; // bool: stderr redirected (mode != inherit)

    private MethodBuilder _childCtxRunCaptured = null!;
    private MethodBuilder _childCtxEmitCaptured = null!;
    private MethodBuilder _childCtxRunStreamed = null!;
    private MethodBuilder _childCtxEmitStreamClose = null!;
    private MethodBuilder _childCtxPumpStdout = null!;
    private MethodBuilder _childCtxPumpStderr = null!;
    private MethodBuilder _childCtxKill = null!;
    private MethodBuilder _childCtxSend = null!;
    private MethodBuilder _childCtxDisconnect = null!;
    private MethodBuilder _childCtxRef = null!;
    private MethodBuilder _childCtxStdinWrite = null!;
    private MethodBuilder _childCtxStdinEnd = null!;

    private MethodBuilder _childRunAsync = null!;
    private MethodBuilder _childConfigureSpawn = null!;
    private MethodBuilder _childStdioMode = null!;
    private MethodBuilder _childSpawnError = null!;
    private MethodBuilder _childCtxRunSpawnError = null!;

    // $ChildPush — a one-shot closure that pushes one chunk (or null = EOF) into a
    // $Readable on the event-loop thread, so all stream-buffer access stays single-threaded.
    private TypeBuilder _childPushType = null!;
    private ConstructorBuilder _childPushCtor = null!;
    private MethodBuilder _childPushRun = null!;
    private FieldBuilder _childPushStream = null!;
    private FieldBuilder _childPushChunk = null!;

    // BCL handles, resolved once.
    private MethodInfo _miProcStart = null!;
    private MethodInfo _miProcIdGet = null!;
    private MethodInfo _miProcStdoutGet = null!;
    private MethodInfo _miProcStderrGet = null!;
    private MethodInfo _miProcExitCodeGet = null!;
    private MethodInfo _miProcHasExitedGet = null!;
    private MethodInfo _miProcWaitForExit = null!;
    private MethodInfo _miProcWaitForExitMs = null!;
    private MethodInfo _miProcKillTree = null!;
    private MethodInfo _miExceptionMessageGet = null!;
    private MethodInfo _miDictSet = null!;
    private MethodInfo _miGetMethodFromHandle = null!;

    /// <summary>
    /// Builds the $ChildProcessCtx type + the ChildRunAsync backgrounding helpers.
    /// Called from EmitChildProcessMethods before the dispatch methods are emitted.
    /// </summary>
    private void EmitChildProcessAsyncInfra(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        _miProcStart = _types.GetMethod(_types.Process, "Start", Type.EmptyTypes)!;
        _miProcIdGet = _types.GetProperty(_types.Process, "Id")!.GetGetMethod()!;
        _miProcStdoutGet = _types.GetProperty(_types.Process, "StandardOutput")!.GetGetMethod()!;
        _miProcStderrGet = _types.GetProperty(_types.Process, "StandardError")!.GetGetMethod()!;
        _miProcExitCodeGet = _types.GetProperty(_types.Process, "ExitCode")!.GetGetMethod()!;
        _miProcHasExitedGet = _types.GetProperty(_types.Process, "HasExited")!.GetGetMethod()!;
        _miProcWaitForExit = _types.GetMethod(_types.Process, "WaitForExit", Type.EmptyTypes)!;
        _miProcWaitForExitMs = _types.GetMethod(_types.Process, "WaitForExit", [_types.Int32])!;
        _miProcKillTree = _types.GetMethod(_types.Process, "Kill", [_types.Boolean])!;
        _miExceptionMessageGet = _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!;
        _miDictSet = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!;
        _miGetMethodFromHandle = typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!;

        DefineChildPushType(runtime);
        DefineChildCtxType(runtime);
        EmitChildRunAsyncHelpers(runtimeType, runtime);
        EmitChildStdioMode(runtimeType, runtime);
        EmitChildReadCapped(runtimeType, runtime);
        EmitChildSpawnError(runtimeType, runtime);
        EmitConfigureSpawnStartInfo(runtimeType, runtime);
        EmitChildCtxMethods(runtime);
        _childCtxType.CreateType();
        _childPushType.CreateType();
    }

    /// <summary>
    /// static int ChildStdioMode(object options, int fd): 0 pipe, 1 inherit, 2 ignore — from the
    /// `stdio` option (string shorthand applied to all fds, or the array form). Mirrors the
    /// interpreter's ParseStdioModes; unknown values (fd/stream/'ipc') fall back to pipe.
    /// </summary>
    private void EmitChildStdioMode(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var m = runtimeType.DefineMethod("ChildStdioMode",
            MethodAttributes.Public | MethodAttributes.Static, _types.Int32, [_types.Object, _types.Int32]);
        _childStdioMode = m;
        var il = m.GetILGenerator();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var strLocal = il.DeclareLocal(_types.String);
        var pipe = il.DefineLabel();

        // dict = options as Dictionary; if null -> pipe
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, pipe);
        // if (!dict.TryGetValue("stdio", out tmp)) -> pipe
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "stdio");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, pipe);

        // if (tmp is string) strLocal = tmp; else if (tmp is List l) strLocal = l[fd] as string (when fd<count); else pipe
        var haveStr = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Brtrue, haveStr);
        // list form
        var notList = il.DefineLabel();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Brfalse, notList);
        // if (fd >= list.Count) pipe
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, pipe);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Br, haveStr);
        il.MarkLabel(notList);
        il.Emit(OpCodes.Br, pipe);

        il.MarkLabel(haveStr);
        // if (strLocal == "inherit") return 1; if (strLocal == "ignore") return 2; else 0
        var notInherit = il.DefineLabel();
        var strEq = _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!;
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldstr, "inherit");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, notInherit);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notInherit);
        var notIgnore = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldstr, "ignore");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, notIgnore);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notIgnore);
        il.MarkLabel(pipe);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// static void ConfigureSpawnStartInfo(ProcessStartInfo si, string command, object args, object options)
    /// Sets FileName/Arguments(/ArgumentList) honoring the `shell` option (true → default
    /// platform shell; string → that shell), mirroring the interpreter's ApplyShellCommand.
    /// </summary>
    private void EmitConfigureSpawnStartInfo(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var m = runtimeType.DefineMethod("ConfigureSpawnStartInfo",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void,
            [_types.ProcessStartInfo, _types.String, _types.Object, _types.Object]);
        _childConfigureSpawn = m;
        var il = m.GetILGenerator();

        var argsListLocal = il.DeclareLocal(_types.ListOfObject);
        var fullCmdLocal = il.DeclareLocal(_types.String);
        var useShellLocal = il.DeclareLocal(_types.Boolean);
        var shellPathLocal = il.DeclareLocal(_types.String);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var concat2 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!;
        var concat3 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!;
        var fnSet = _types.GetProperty(_types.ProcessStartInfo, "FileName")!.GetSetMethod()!;
        var argSet = _types.GetProperty(_types.ProcessStartInfo, "Arguments")!.GetSetMethod()!;

        // Redirect each fd unless its stdio mode is 'inherit' (1). options = arg3.
        void SetRedirect(string prop, int fd)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldc_I4, fd);
            il.Emit(OpCodes.Call, _childStdioMode);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ceq);           // mode == inherit
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);           // redirect = !(mode == inherit)
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ProcessStartInfo, prop)!.GetSetMethod()!);
        }
        SetRedirect("RedirectStandardInput", 0);
        SetRedirect("RedirectStandardOutput", 1);
        SetRedirect("RedirectStandardError", 2);

        // argsList = args as List
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, argsListLocal);

        // fullCmd = command; if (argsList != null && argsList.Count>0) fullCmd = command + " " + string.Join(" ", argsList.ToArray())
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, fullCmdLocal);
        var noJoin = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Brfalse, noJoin);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noJoin);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray")!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Join", [typeof(string), typeof(object[])])!);
        il.Emit(OpCodes.Call, concat3);
        il.Emit(OpCodes.Stloc, fullCmdLocal);
        il.MarkLabel(noJoin);

        // useShell = false; shellPath = null
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, useShellLocal);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, shellPathLocal);

        // if (options is Dict && dict.TryGetValue("shell", out tmp)) { ... }
        var afterShellOpt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, afterShellOpt);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "shell");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, afterShellOpt);

        // if (tmp is bool) useShell = (bool)tmp;
        var notBool = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBool);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stloc, useShellLocal);
        il.Emit(OpCodes.Br, afterShellOpt);
        il.MarkLabel(notBool);
        // else if (tmp is string s && s.Length>0) { useShell=true; shellPath=s; }
        var notStr = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notStr);          // (string s) on stack
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notStr);          // length 0 → leaves the string; handled below
        il.Emit(OpCodes.Stloc, shellPathLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, useShellLocal);
        il.Emit(OpCodes.Br, afterShellOpt);
        il.MarkLabel(notStr);
        il.Emit(OpCodes.Pop);                       // discard the Isinst result / empty string
        il.MarkLabel(afterShellOpt);

        // Branch on useShell
        var directLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, useShellLocal);
        il.Emit(OpCodes.Brfalse, directLabel);

        // Shell path: platform branch
        var notWindows = il.DefineLabel();
        var afterPlatform = il.DefineLabel();
        il.Emit(OpCodes.Call, _types.GetProperty(_types.OSPlatform, "Windows")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RuntimeInformation, "IsOSPlatform", [_types.OSPlatform])!);
        il.Emit(OpCodes.Brfalse, notWindows);
        // Windows: FileName = shellPath ?? "cmd.exe"; Arguments = "/d /s /c " + fullCmd
        il.Emit(OpCodes.Ldarg_0);
        EmitShellPathOrDefault(il, shellPathLocal, "cmd.exe");
        il.Emit(OpCodes.Callvirt, fnSet);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "/d /s /c ");
        il.Emit(OpCodes.Ldloc, fullCmdLocal);
        il.Emit(OpCodes.Call, concat2);
        il.Emit(OpCodes.Callvirt, argSet);
        il.Emit(OpCodes.Br, afterPlatform);
        // Unix: FileName = shellPath ?? "/bin/sh"; Arguments = "-c \"" + fullCmd.Replace("\"","\\\"") + "\""
        il.MarkLabel(notWindows);
        il.Emit(OpCodes.Ldarg_0);
        EmitShellPathOrDefault(il, shellPathLocal, "/bin/sh");
        il.Emit(OpCodes.Callvirt, fnSet);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "-c \"");
        il.Emit(OpCodes.Ldloc, fullCmdLocal);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, concat3);
        il.Emit(OpCodes.Callvirt, argSet);
        il.MarkLabel(afterPlatform);
        il.Emit(OpCodes.Br, endLabel);

        // Direct path: FileName = command; add args to ArgumentList
        il.MarkLabel(directLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, fnSet);

        // windowsVerbatimArguments: pass the args verbatim as a raw Arguments string (no .NET
        // quoting), as Node asks. options = arg3; args list = argsListLocal.
        var notVerbatim = il.DefineLabel();
        EmitOptionBool(il, 3, "windowsVerbatimArguments");
        il.Emit(OpCodes.Brfalse, notVerbatim);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Brfalse, notVerbatim);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray")!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Join", [typeof(string), typeof(object[])])!);
        il.Emit(OpCodes.Callvirt, argSet);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notVerbatim);

        var noArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Brfalse, noArgs);
        var argListLocal = il.DeclareLocal(typeof(System.Collections.ObjectModel.Collection<string>));
        var iLocal = il.DeclareLocal(_types.Int32);
        var aTmp = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ProcessStartInfo, "ArgumentList")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, argListLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);
        var loop = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, aTmp);
        il.Emit(OpCodes.Ldloc, argListLocal);
        var aNull = il.DefineLabel();
        var aAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, aTmp);
        il.Emit(OpCodes.Brfalse, aNull);
        il.Emit(OpCodes.Ldloc, aTmp);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Br, aAdd);
        il.MarkLabel(aNull);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(aAdd);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ObjectModel.Collection<string>).GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(loopEnd);
        il.MarkLabel(noArgs);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emit: (shellPathLocal != null && len>0) ? shellPathLocal : default.</summary>
    private void EmitShellPathOrDefault(ILGenerator il, LocalBuilder shellPathLocal, string def)
    {
        var useDefault = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, shellPathLocal);
        il.Emit(OpCodes.Brfalse, useDefault);
        il.Emit(OpCodes.Ldloc, shellPathLocal);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(useDefault);
        il.Emit(OpCodes.Ldstr, def);
        il.MarkLabel(done);
    }

    /// <summary>$ChildPush { object _stream; object _chunk; void Run() =&gt; (($Readable)_stream).Push(_chunk); }</summary>
    private void DefineChildPushType(EmittedRuntime runtime)
    {
        var mb = (ModuleBuilder)((TypeBuilder)runtime.TSEventEmitterType).Module;
        var t = EmitTypeDefinitions.DefineType(mb, "$ChildPush",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        _childPushType = t;
        _childPushStream = t.DefineField("_stream", _types.Object, FieldAttributes.Public);
        _childPushChunk = t.DefineField("_chunk", _types.Object, FieldAttributes.Public);

        var ctor = t.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object, _types.Object]);
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_1); cil.Emit(OpCodes.Stfld, _childPushStream);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_2); cil.Emit(OpCodes.Stfld, _childPushChunk);
        cil.Emit(OpCodes.Ret);
        _childPushCtor = ctor;

        _childPushRun = t.DefineMethod("Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        var il = _childPushRun.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childPushStream);
        il.Emit(OpCodes.Castclass, runtime.TSReadableType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childPushChunk);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    private void DefineChildCtxType(EmittedRuntime runtime)
    {
        var mb = (ModuleBuilder)((TypeBuilder)runtime.TSEventEmitterType).Module;
        var t = EmitTypeDefinitions.DefineType(mb,
            "$ChildProcessCtx",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        _childCtxType = t;

        _childCtxProc = t.DefineField("_proc", _types.Process, FieldAttributes.Public);
        _childCtxEmitter = t.DefineField("_emitter", _types.Object, FieldAttributes.Public);
        _childCtxDict = t.DefineField("_dict", _types.DictionaryStringObject, FieldAttributes.Public);
        _childCtxCallback = t.DefineField("_callback", _types.Object, FieldAttributes.Public);
        _childCtxOptions = t.DefineField("_options", _types.Object, FieldAttributes.Public);
        _childCtxStdout = t.DefineField("_stdout", _types.Object, FieldAttributes.Public);
        _childCtxStderr = t.DefineField("_stderr", _types.Object, FieldAttributes.Public);
        t.DefineField("_stdin", _types.Object, FieldAttributes.Public);
        _childCtxTimeout = t.DefineField("_timeout", _types.Double, FieldAttributes.Public);
        t.DefineField("_killSignal", _types.String, FieldAttributes.Public);
        _childCtxResStdout = t.DefineField("_resStdout", _types.Object, FieldAttributes.Public);
        _childCtxResStderr = t.DefineField("_resStderr", _types.Object, FieldAttributes.Public);
        _childCtxResCode = t.DefineField("_resCode", _types.Int32, FieldAttributes.Public);
        _childCtxResError = t.DefineField("_resError", _types.Object, FieldAttributes.Public);
        _childCtxResKind = t.DefineField("_resKind", _types.Int32, FieldAttributes.Public);
        _childCtxMaxBuffer = t.DefineField("_maxBuffer", _types.Int32, FieldAttributes.Public);
        _childCtxAsBuffer = t.DefineField("_asBuffer", _types.Boolean, FieldAttributes.Public);
        _childCtxEncoding = t.DefineField("_encoding", _types.String, FieldAttributes.Public);
        _childCtxStdoutRedir = t.DefineField("_stdoutRedir", _types.Boolean, FieldAttributes.Public);
        _childCtxStderrRedir = t.DefineField("_stderrRedir", _types.Boolean, FieldAttributes.Public);

        var ctor = t.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        cil.Emit(OpCodes.Ret);
        _childCtxCtor = ctor;

        // Declare the method builders now (bodies filled by EmitChildCtxMethods) so
        // ldtoken references resolve while wiring the dict in dispatch.
        _childCtxRunCaptured = t.DefineMethod("RunCaptured", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        _childCtxEmitCaptured = t.DefineMethod("EmitCaptured", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        _childCtxRunStreamed = t.DefineMethod("RunStreamed", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        _childCtxRunSpawnError = t.DefineMethod("RunSpawnError", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        _childCtxEmitStreamClose = t.DefineMethod("EmitStreamClose", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        _childCtxPumpStdout = t.DefineMethod("PumpStdout", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        _childCtxPumpStderr = t.DefineMethod("PumpStderr", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        _childCtxKill = t.DefineMethod("Kill", MethodAttributes.Public, _types.Object, [_types.Object]);
        _childCtxSend = t.DefineMethod("Send", MethodAttributes.Public, _types.Object, [_types.Object]);
        _childCtxDisconnect = t.DefineMethod("Disconnect", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        _childCtxRef = t.DefineMethod("RefSelf", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        _childCtxStdinWrite = t.DefineMethod("StdinWrite", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        _childCtxStdinEnd = t.DefineMethod("StdinEnd", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.Object, _types.Object]);
    }

    /// <summary>
    /// static void ChildRunAsync(Func&lt;object&gt; worker): EventLoop.Ref(); Task.Run(worker)
    /// then Unref (with Task.Delay grace). Mirrors FsRunAsync but self-contained so
    /// child_process never depends on UsesFs.
    /// </summary>
    private void EmitChildRunAsyncHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // static void ChildAsyncUnrefNow() => EventLoop.GetInstance().Unref();
        var now = runtimeType.DefineMethod("ChildAsyncUnrefNow",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, Type.EmptyTypes);
        {
            var il = now.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);
            il.Emit(OpCodes.Ret);
        }

        // static void ChildAsyncUnrefDrop(Task t) => EventLoop.GetInstance().Schedule(new Action(ChildAsyncUnrefNow));
        var drop = runtimeType.DefineMethod("ChildAsyncUnrefDrop",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [typeof(Task)]);
        {
            var il = drop.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, now);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
            il.Emit(OpCodes.Ret);
        }

        // static void ChildAsyncUnref(Task t) => Task.Delay(8).ContinueWith(ChildAsyncUnrefDrop);
        var unref = runtimeType.DefineMethod("ChildAsyncUnref",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [typeof(Task)]);
        {
            var il = unref.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 8);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("Delay", [_types.Int32])!);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, drop);
            il.Emit(OpCodes.Newobj, typeof(Action<Task>).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("ContinueWith", [typeof(Action<Task>)])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        // static void ChildRunAsync(Func<object> worker)
        var taskRunOpen = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(x => x.Name == "Run" && x.IsGenericMethodDefinition
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType.IsGenericType
                && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<>));
        var taskRun = EmitGenerics.MakeGenericMethod(taskRunOpen, _types.Object);

        var run = runtimeType.DefineMethod("ChildRunAsync",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [typeof(Func<object>)]);
        {
            var il = run.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopRef);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, taskRun);
            var tLocal = il.DeclareLocal(_types.TaskOfObject);
            il.Emit(OpCodes.Stloc, tLocal);

            il.Emit(OpCodes.Ldloc, tLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, unref);
            il.Emit(OpCodes.Newobj, typeof(Action<Task>).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
            il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("ContinueWith", [typeof(Action<Task>), typeof(TaskContinuationOptions)])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
        _childRunAsync = run;
    }

    // ---- Small IL helpers shared by the ctx method bodies ----

    /// <summary>Emit: ctx._emitter as $EventEmitter . Emit(name, new object[]{ arg }). Leaves nothing.</summary>
    private void EmitCtxEmit(ILGenerator il, EmittedRuntime runtime, string name, Action emitArg)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxEmitter);
        il.Emit(OpCodes.Castclass, runtime.TSEventEmitterType);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        emitArg();
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emit a fresh error object dict { message = msg, [code = code] } onto the stack.</summary>
    private void EmitNewErrorObject(ILGenerator il, Action emitMessage, Action? emitCode)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "message");
        emitMessage();
        il.Emit(OpCodes.Callvirt, _miDictSet);
        if (emitCode != null)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "code");
            emitCode();
            il.Emit(OpCodes.Callvirt, _miDictSet);
        }
        il.Emit(OpCodes.Ldloc, dictLocal);
    }

    /// <summary>Emit: runtime.InvokeValue(ctx._callback, args[]) when callback != null. Pops result.</summary>
    private void EmitInvokeCallback(ILGenerator il, EmittedRuntime runtime, Action emitArgsArray)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxCallback);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxCallback);
        emitArgsArray();
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skip);
    }

    private void EmitChildCtxMethods(EmittedRuntime runtime)
    {
        EmitCtxRunCaptured(runtime);
        EmitCtxEmitCaptured(runtime);
        EmitCtxRunStreamed(runtime);
        EmitCtxRunSpawnError(runtime);
        EmitCtxPumpStdout(runtime);
        EmitCtxPumpStderr(runtime);
        EmitCtxEmitStreamClose(runtime);
        EmitCtxKill(runtime);
        EmitCtxSend(runtime);
        EmitCtxDisconnect(runtime);
        EmitCtxRef(runtime);
        EmitCtxStdinWrite(runtime);
        EmitCtxStdinEnd(runtime);
    }

    /// <summary>
    /// exec/execFile worker (bg thread): start, capture stdout+stderr fully, wait
    /// (honoring timeout), record the outcome on ctx, then Schedule EmitCaptured on the
    /// event loop so the callback / lifecycle events fire on the loop thread AFTER the
    /// synchronous script has registered its listeners — matching the interpreter.
    /// </summary>
    private void EmitCtxRunCaptured(EmittedRuntime runtime)
    {
        var il = _childCtxRunCaptured.GetILGenerator();
        var codeLocal = il.DeclareLocal(_types.Int32);
        var afterTry = il.DefineLabel();

        // _resStdout = ""; _resStderr = ""; _resKind = 0;
        StoreCtxField(il, _childCtxResStdout, () => il.Emit(OpCodes.Ldstr, ""));
        StoreCtxField(il, _childCtxResStderr, () => il.Emit(OpCodes.Ldstr, ""));
        StoreCtxField(il, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_0));

        il.BeginExceptionBlock();

        // _proc.Start(); _dict["pid"] = (double)_proc.Id;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, _miProcStart);
        il.Emit(OpCodes.Pop);

        EmitDictSetFromCtx(il, "pid", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _childCtxProc);
            il.Emit(OpCodes.Callvirt, _miProcIdGet);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
        });

        // bool[] overflow = new bool[1];
        var overflowLocal = il.DeclareLocal(typeof(bool[]));
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Newarr, typeof(bool)); il.Emit(OpCodes.Stloc, overflowLocal);

        // _resStdout/_resStderr = ChildDecodeOutput(ChildReadCappedBytes(<pipe>.BaseStream,
        //   _maxBuffer, overflow), _asBuffer, _encoding) — Buffer or decoded string.
        var baseStreamGet = typeof(System.IO.StreamReader).GetProperty("BaseStream")!.GetGetMethod()!;
        void ReadDecoded(FieldBuilder resField, MethodInfo pipeGet)
        {
            StoreCtxField(il, resField, () =>
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxProc); il.Emit(OpCodes.Callvirt, pipeGet);
                il.Emit(OpCodes.Callvirt, baseStreamGet);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxMaxBuffer);
                il.Emit(OpCodes.Ldloc, overflowLocal);
                il.Emit(OpCodes.Call, _childReadCappedBytes);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxAsBuffer);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxEncoding);
                il.Emit(OpCodes.Call, _childDecodeOutput);
            });
        }
        ReadDecoded(_childCtxResStdout, _miProcStdoutGet);
        ReadDecoded(_childCtxResStderr, _miProcStderrGet);

        // if (overflow[0]) { try { _proc.Kill(true); } catch {}  _resError = maxBuffer error; }
        var noOverflow = il.DefineLabel();
        var afterKill = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, overflowLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_I1);
        il.Emit(OpCodes.Brfalse, noOverflow);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Callvirt, _miProcKillTree);
        il.Emit(OpCodes.Leave, afterKill);
        il.BeginCatchBlock(_types.Exception); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterKill);
        il.EndExceptionBlock();
        il.MarkLabel(afterKill);
        StoreCtxField(il, _childCtxResError, () =>
            EmitNewErrorObject(il,
                () => il.Emit(OpCodes.Ldstr, "stdout maxBuffer length exceeded"),
                () => il.Emit(OpCodes.Ldstr, "ERR_CHILD_PROCESS_STDIO_MAXBUFFER")));
        il.MarkLabel(noOverflow);

        // Timeout branch
        var noTimeoutWait = il.DefineLabel();
        var afterWait = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxTimeout);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble_Un, noTimeoutWait);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxTimeout);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _miProcWaitForExitMs);
        il.Emit(OpCodes.Brtrue, afterWait);

        // Timed out: kill tree, killed=true, _resKind=1, leave.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _miProcKillTree);
        EmitDictSetFromCtx(il, "killed", () => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Box, _types.Boolean); });
        StoreCtxField(il, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_1));
        il.Emit(OpCodes.Leave, afterTry);

        il.MarkLabel(noTimeoutWait);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, _miProcWaitForExit);
        il.MarkLabel(afterWait);

        // code = _proc.ExitCode; _resCode = code;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, _miProcExitCodeGet);
        il.Emit(OpCodes.Stloc, codeLocal);
        StoreCtxField(il, _childCtxResCode, () => il.Emit(OpCodes.Ldloc, codeLocal));

        // if (code != 0 && _resError == null) _resError = { message: "Command failed with exit code N", code: N }
        var zeroCode = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Brfalse, zeroCode);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError);
        il.Emit(OpCodes.Brtrue, zeroCode); // a maxBuffer (or other) error already set — keep it
        StoreCtxField(il, _childCtxResError, () =>
            EmitNewErrorObject(il,
                () =>
                {
                    il.Emit(OpCodes.Ldstr, "Command failed with exit code ");
                    il.Emit(OpCodes.Ldloca, codeLocal);
                    il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
                    il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
                },
                () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); }));
        il.MarkLabel(zeroCode);
        il.Emit(OpCodes.Leave, afterTry);

        // catch (Exception ex): _resKind = 2; _resError = { message: ex.Message }
        il.BeginCatchBlock(_types.Exception);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        StoreCtxField(il, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_2));
        StoreCtxField(il, _childCtxResError, () =>
            EmitNewErrorObject(il,
                () => { il.Emit(OpCodes.Ldloc, exLocal); il.Emit(OpCodes.Callvirt, _miExceptionMessageGet); },
                null));
        il.Emit(OpCodes.Leave, afterTry);
        il.EndExceptionBlock();

        il.MarkLabel(afterTry);
        // EventLoop.GetInstance().Schedule(new Action(this.EmitCaptured));
        EmitScheduleOnLoop(il, runtime, _childCtxEmitCaptured);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Replays the captured exec/execFile outcome on the event-loop thread: fires the
    /// (error|null, stdout, stderr) callback and emits close/exit (or error).
    /// </summary>
    private void EmitCtxEmitCaptured(EmittedRuntime runtime)
    {
        var il = _childCtxEmitCaptured.GetILGenerator();
        var codeLocal = il.DeclareLocal(_types.Int32);
        var ret = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxResCode);
        il.Emit(OpCodes.Stloc, codeLocal);

        // switch (_resKind)
        var kindExc = il.DefineLabel();
        var kindTimeout = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxResKind);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, kindExc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxResKind);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, kindTimeout);

        // kind 0 (normal): _dict["exitCode"] = code; cb(_resError, out, err); emit close/exit
        EmitDictSetFromCtx(il, "exitCode", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });
        EmitInvokeCallback(il, runtime, () => EmitArgs3(il,
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); },
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResStdout); },
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResStderr); }));
        EmitCtxEmit(il, runtime, "close", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });
        EmitCtxEmit(il, runtime, "exit", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });
        il.Emit(OpCodes.Br, ret);

        // kind 1 (timeout): _dict["exitCode"] = -1; emit error; cb(_resError', out, err)
        il.MarkLabel(kindTimeout);
        // build the timeout error here (matches interp message)
        StoreCtxField(il, _childCtxResError, () => EmitNewErrorObject(il, () => il.Emit(OpCodes.Ldstr, "Command timed out"), null));
        EmitDictSetFromCtx(il, "exitCode", () => { il.Emit(OpCodes.Ldc_R8, -1.0); il.Emit(OpCodes.Box, _types.Double); });
        EmitCtxEmit(il, runtime, "error", () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); });
        EmitInvokeCallback(il, runtime, () => EmitArgs3(il,
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); },
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResStdout); },
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResStderr); }));
        il.Emit(OpCodes.Br, ret);

        // kind 2 (exception): emit error; cb(_resError, "", "")
        il.MarkLabel(kindExc);
        EmitCtxEmit(il, runtime, "error", () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); });
        EmitInvokeCallback(il, runtime, () => EmitArgs3(il,
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); },
            () => il.Emit(OpCodes.Ldstr, ""),
            () => il.Emit(OpCodes.Ldstr, "")));

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emit: EventLoop.GetInstance().Schedule(new Action(this, ldftn method)).</summary>
    private void EmitScheduleOnLoop(ILGenerator il, EmittedRuntime runtime, MethodBuilder method)
    {
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, method);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
    }

    /// <summary>Builds new object[]{ a, b, c } on the stack.</summary>
    private void EmitArgs3(ILGenerator il, Action a, Action b, Action c)
    {
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); a(); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); b(); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); c(); il.Emit(OpCodes.Stelem_Ref);
    }

    /// <summary>Emit: _dict[key] = &lt;value&gt; (value produced by emitValue).</summary>
    private void EmitDictSetFromCtx(ILGenerator il, string key, Action emitValue)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxDict);
        il.Emit(OpCodes.Ldstr, key);
        emitValue();
        il.Emit(OpCodes.Callvirt, _miDictSet);
    }

    private void EmitCtxKill(EmittedRuntime runtime)
    {
        // object Kill(object signal):
        //   _dict["killed"] = true; _dict["signalCode"] = signal ?? "SIGTERM";
        //   try { if (!_proc.HasExited) _proc.Kill(true); } catch {}  return true;
        var il = _childCtxKill.GetILGenerator();
        EmitDictSetFromCtx(il, "killed", () => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Box, _types.Boolean); });
        // signalCode = (signal is string) ? signal : "SIGTERM"
        EmitDictSetFromCtx(il, "signalCode", () =>
        {
            var have = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "SIGTERM");
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(have);
            il.MarkLabel(done);
        });

        var afterKill = il.DefineLabel();
        il.BeginExceptionBlock();
        var skipKill = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Brfalse, skipKill);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, _miProcHasExitedGet);
        il.Emit(OpCodes.Brtrue, skipKill);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _miProcKillTree);
        il.MarkLabel(skipKill);
        il.Emit(OpCodes.Leave, afterKill);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterKill);
        il.EndExceptionBlock();
        il.MarkLabel(afterKill);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCtxSend(EmittedRuntime runtime)
    {
        // object Send(object message): no IPC channel for non-fork children -> return false.
        // Real IPC send is wired by the fork child (#1017).
        var il = _childCtxSend.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCtxDisconnect(EmittedRuntime runtime)
    {
        // object Disconnect(): no-op for non-fork children -> return null.
        var il = _childCtxDisconnect.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCtxRef(EmittedRuntime runtime)
    {
        // object RefSelf(): ref()/unref() both return the ChildProcess (the dict).
        var il = _childCtxRef.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxDict);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// spawn worker (bg thread): start, pump stdout/stderr on background tasks (each pushing
    /// chunks into its $Readable on the loop thread), wait, then Schedule EmitStreamClose.
    /// </summary>
    private void EmitCtxRunStreamed(EmittedRuntime runtime)
    {
        var il = _childCtxRunStreamed.GetILGenerator();
        var t1 = il.DeclareLocal(typeof(Task));
        var t2 = il.DeclareLocal(typeof(Task));
        var afterTry = il.DefineLabel();
        var actionCtor = typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!;
        var taskRunAction = typeof(Task).GetMethod("Run", [typeof(Action)])!;

        StoreCtxField(il, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_0));

        // Process was started synchronously in the dispatch (so stdin is usable immediately).
        il.BeginExceptionBlock();

        // Start a pump task only for a redirected fd (an 'inherit' fd is not redirected, so
        // reading it would throw). t1/t2 default to null and are awaited with a null-guard.
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, t1);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, t2);
        void StartPump(FieldBuilder redirField, MethodBuilder pump, LocalBuilder tLocal)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, redirField);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldftn, pump);
            il.Emit(OpCodes.Newobj, actionCtor);
            il.Emit(OpCodes.Call, taskRunAction);
            il.Emit(OpCodes.Stloc, tLocal);
            il.MarkLabel(skip);
        }
        StartPump(_childCtxStdoutRedir, _childCtxPumpStdout, t1);
        StartPump(_childCtxStderrRedir, _childCtxPumpStderr, t2);

        // _proc.WaitForExit(); t1?.Wait(); t2?.Wait();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, _miProcWaitForExit);
        void AwaitTask(LocalBuilder tLocal)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tLocal);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, tLocal);
            il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("Wait", Type.EmptyTypes)!);
            il.MarkLabel(skip);
        }
        AwaitTask(t1);
        AwaitTask(t2);

        // _resCode = _proc.ExitCode;
        StoreCtxField(il, _childCtxResCode, () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _childCtxProc);
            il.Emit(OpCodes.Callvirt, _miProcExitCodeGet);
        });
        il.Emit(OpCodes.Leave, afterTry);

        il.BeginCatchBlock(_types.Exception);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        StoreCtxField(il, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_2));
        StoreCtxField(il, _childCtxResError, () =>
            EmitNewErrorObject(il,
                () => { il.Emit(OpCodes.Ldloc, exLocal); il.Emit(OpCodes.Callvirt, _miExceptionMessageGet); },
                null));
        il.Emit(OpCodes.Leave, afterTry);
        il.EndExceptionBlock();

        il.MarkLabel(afterTry);
        EmitScheduleOnLoop(il, runtime, _childCtxEmitStreamClose);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Spawn-failure worker: schedule EmitStreamClose (which, with _resKind==2, emits 'error').</summary>
    private void EmitCtxRunSpawnError(EmittedRuntime runtime)
    {
        var il = _childCtxRunSpawnError.GetILGenerator();
        EmitScheduleOnLoop(il, runtime, _childCtxEmitStreamClose);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCtxPumpStdout(EmittedRuntime runtime) => EmitPumpBody(_childCtxPumpStdout, runtime, _miProcStdoutGet, _childCtxStdout);
    private void EmitCtxPumpStderr(EmittedRuntime runtime) => EmitPumpBody(_childCtxPumpStderr, runtime, _miProcStderrGet, _childCtxStderr);

    /// <summary>
    /// Read the redirected pipe in char chunks; for each chunk Schedule a $ChildPush onto
    /// the loop (data), and on EOF Schedule a $ChildPush(null) (end).
    /// </summary>
    private void EmitPumpBody(MethodBuilder method, EmittedRuntime runtime, MethodInfo readerGetter, FieldBuilder streamField)
    {
        var il = method.GetILGenerator();
        var readerLocal = il.DeclareLocal(_types.TextReader);
        var bufLocal = il.DeclareLocal(typeof(char[]));
        var nLocal = il.DeclareLocal(_types.Int32);
        var readMethod = _types.GetMethod(_types.TextReader, "Read", [typeof(char[]), _types.Int32, _types.Int32])!;
        var newStr = typeof(string).GetConstructor([typeof(char[]), _types.Int32, _types.Int32])!;
        var afterTry = il.DefineLabel();

        // reader = _proc.<getter>(); buf = new char[4096];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, readerGetter);
        il.Emit(OpCodes.Stloc, readerLocal);
        il.Emit(OpCodes.Ldc_I4, 4096);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Stloc, bufLocal);

        il.BeginExceptionBlock();
        var loop = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loop);
        // n = reader.Read(buf, 0, 4096)
        il.Emit(OpCodes.Ldloc, readerLocal);
        il.Emit(OpCodes.Ldloc, bufLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 4096);
        il.Emit(OpCodes.Callvirt, readMethod);
        il.Emit(OpCodes.Stloc, nLocal);
        // if (n <= 0) break;
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);
        // Schedule push(stream, new string(buf,0,n)) — but only when piped (stream != null);
        // for 'ignore' the field is null and we just drain.
        var skipData = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, streamField);
        il.Emit(OpCodes.Brfalse, skipData);
        EmitScheduleChildPush(il, runtime,
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, streamField); },
            () => { il.Emit(OpCodes.Ldloc, bufLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Newobj, newStr); });
        il.MarkLabel(skipData);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Leave, afterTry);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterTry);
        il.EndExceptionBlock();
        il.MarkLabel(afterTry);

        // Schedule push(stream, null) — EOF/end (only when piped).
        var skipEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, streamField);
        il.Emit(OpCodes.Brfalse, skipEnd);
        EmitScheduleChildPush(il, runtime,
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, streamField); },
            () => il.Emit(OpCodes.Ldnull));
        il.MarkLabel(skipEnd);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emit: EventLoop.GetInstance().Schedule(new Action(new $ChildPush(stream, chunk), Run)).</summary>
    private void EmitScheduleChildPush(ILGenerator il, EmittedRuntime runtime, Action emitStream, Action emitChunk)
    {
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        emitStream();
        emitChunk();
        il.Emit(OpCodes.Newobj, _childPushCtor);
        il.Emit(OpCodes.Ldftn, _childPushRun);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
    }

    /// <summary>Replays spawn close/exit (or error) on the loop thread, after all data/end pushes.</summary>
    private void EmitCtxEmitStreamClose(EmittedRuntime runtime)
    {
        var il = _childCtxEmitStreamClose.GetILGenerator();
        var codeLocal = il.DeclareLocal(_types.Int32);
        var ret = il.DefineLabel();

        // if (_resKind == 2) emit error; return
        var notExc = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxResKind);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, notExc);
        EmitCtxEmit(il, runtime, "error", () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _childCtxResError); });
        il.Emit(OpCodes.Br, ret);
        il.MarkLabel(notExc);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxResCode);
        il.Emit(OpCodes.Stloc, codeLocal);
        EmitDictSetFromCtx(il, "exitCode", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });
        EmitCtxEmit(il, runtime, "close", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });
        EmitCtxEmit(il, runtime, "exit", () => { il.Emit(OpCodes.Ldloc, codeLocal); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Box, _types.Double); });

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>stdin.write(chunk, enc?, cb?) — forward chunk to the child's StandardInput.</summary>
    private void EmitCtxStdinWrite(EmittedRuntime runtime)
    {
        var il = _childCtxStdinWrite.GetILGenerator();
        var afterWrite = il.DefineLabel();
        var swGet = _types.GetProperty(_types.Process, "StandardInput")!.GetGetMethod()!;
        var swWrite = typeof(System.IO.TextWriter).GetMethod("Write", [_types.String])!;
        var swFlush = typeof(System.IO.TextWriter).GetMethod("Flush", Type.EmptyTypes)!;

        // try { if (chunk != null) { var w = _proc.StandardInput; w.Write(chunk.ToString()); w.Flush(); } } catch {}
        il.BeginExceptionBlock();
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skip);
        var wLocal = il.DeclareLocal(typeof(System.IO.TextWriter));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, swGet);
        il.Emit(OpCodes.Stloc, wLocal);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Callvirt, swWrite);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Callvirt, swFlush);
        il.MarkLabel(skip);
        il.Emit(OpCodes.Leave, afterWrite);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterWrite);
        il.EndExceptionBlock();
        il.MarkLabel(afterWrite);

        // Invoke a write callback if present: prefer arg3 (cb), else arg2 (enc-as-cb).
        EmitInvokeWriteCb(il, runtime);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>stdin.end(chunk?, enc?, cb?) — optionally write, then close the child's StandardInput.</summary>
    private void EmitCtxStdinEnd(EmittedRuntime runtime)
    {
        var il = _childCtxStdinEnd.GetILGenerator();
        var afterEnd = il.DefineLabel();
        var swGet = _types.GetProperty(_types.Process, "StandardInput")!.GetGetMethod()!;
        var swWrite = typeof(System.IO.TextWriter).GetMethod("Write", [_types.String])!;
        var swClose = typeof(System.IO.TextWriter).GetMethod("Close", Type.EmptyTypes)!;

        il.BeginExceptionBlock();
        var wLocal = il.DeclareLocal(typeof(System.IO.TextWriter));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _childCtxProc);
        il.Emit(OpCodes.Callvirt, swGet);
        il.Emit(OpCodes.Stloc, wLocal);
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Callvirt, swWrite);
        il.MarkLabel(skip);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Callvirt, swClose);
        il.Emit(OpCodes.Leave, afterEnd);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterEnd);
        il.EndExceptionBlock();
        il.MarkLabel(afterEnd);

        EmitInvokeWriteCb(il, runtime);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>If arg3 (or arg2) is a $TSFunction, invoke it with [null].</summary>
    private void EmitInvokeWriteCb(ILGenerator il, EmittedRuntime runtime)
    {
        var done = il.DefineLabel();
        var tryArg2 = il.DefineLabel();
        // arg3 callable?
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, tryArg2);
        il.Emit(OpCodes.Ldarg_3);
        EmitInvokeNullArg(il, runtime);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(tryArg2);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_2);
        EmitInvokeNullArg(il, runtime);
        il.MarkLabel(done);
    }

    /// <summary>Stack: [callable]. Emits InvokeValue(callable, new object[]{ null }) and pops.</summary>
    private void EmitInvokeNullArg(ILGenerator il, EmittedRuntime runtime)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);
    }

    // ================= Dispatch-side helpers =================

    /// <summary>
    /// Apply options.cwd + options.env to a ProcessStartInfo. options is an object that
    /// may be a Dictionary or null. env replaces the inherited environment (Node semantics).
    /// </summary>
    private void EmitApplyChildOptions(ILGenerator il, LocalBuilder startInfoLocal, LocalBuilder optionsObjLocal)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var doneLabel = il.DefineLabel();
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;

        // dict = options as Dictionary; if null, done.
        il.Emit(OpCodes.Ldloc, optionsObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // cwd
        var noCwd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "cwd");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noCwd);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Brfalse, noCwd);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ProcessStartInfo, "WorkingDirectory")!.GetSetMethod()!);
        il.MarkLabel(noCwd);

        // env
        var noEnv = il.DefineLabel();
        var envDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var envEnumLocal = il.DeclareLocal(typeof(Dictionary<string, object?>.Enumerator));
        var envKvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object?>));
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "env");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noEnv);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, envDictLocal);
        il.Emit(OpCodes.Brfalse, noEnv);

        var envProp = _types.GetProperty(_types.ProcessStartInfo, "Environment")!.GetGetMethod()!;
        var iDictStringString = typeof(IDictionary<string, string?>);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, envProp);
        il.Emit(OpCodes.Callvirt, typeof(ICollection<KeyValuePair<string, string?>>).GetMethod("Clear")!);

        il.Emit(OpCodes.Ldloc, envDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, envEnumLocal);
        var envLoop = il.DefineLabel();
        var envLoopEnd = il.DefineLabel();
        il.Emit(OpCodes.Br, envLoopEnd);
        il.MarkLabel(envLoop);
        il.Emit(OpCodes.Ldloca, envEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, envKvpLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, envProp);
        il.Emit(OpCodes.Ldloca, envKvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object?>).GetProperty("Key")!.GetGetMethod()!);
        // value?.ToString() ?? ""
        il.Emit(OpCodes.Ldloca, envKvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object?>).GetProperty("Value")!.GetGetMethod()!);
        var valNull = il.DefineLabel();
        var valDone = il.DefineLabel();
        il.Emit(OpCodes.Stloc, tmpLocal);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Brfalse, valNull);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Br, valDone);
        il.MarkLabel(valNull);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(valDone);
        il.Emit(OpCodes.Callvirt, iDictStringString.GetMethod("set_Item", [_types.String, _types.String])!);
        il.MarkLabel(envLoopEnd);
        il.Emit(OpCodes.Ldloca, envEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brtrue, envLoop);
        il.MarkLabel(noEnv);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// timeoutLocal = (double)(options["timeout"]) if present and numeric, else 0.
    /// </summary>
    private void EmitParseTimeout(ILGenerator il, LocalBuilder optionsObjLocal, LocalBuilder timeoutLocal)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, timeoutLocal);

        il.Emit(OpCodes.Ldloc, optionsObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "timeout");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, timeoutLocal);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Pick the first of candidates that is a Dictionary (options) into outLocal, else null.
    /// </summary>
    private void EmitSelectOptions(ILGenerator il, LocalBuilder[] candidates, LocalBuilder outLocal)
    {
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, outLocal);
        foreach (var c in candidates)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Stloc, outLocal);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.MarkLabel(done);
    }

    /// <summary>
    /// Pick the first of candidates that is non-null and neither a Dictionary nor a List
    /// (i.e. a callback) into outLocal, else null. Scan candidates in priority order.
    /// </summary>
    private void EmitSelectCallback(ILGenerator il, LocalBuilder[] candidates, LocalBuilder outLocal)
    {
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, outLocal);
        foreach (var c in candidates)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Brfalse, next);            // null -> skip
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brtrue, next);             // dict -> skip
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Isinst, _types.ListOfObject);
            il.Emit(OpCodes.Brtrue, next);             // list -> skip
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Stloc, outLocal);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.MarkLabel(done);
    }

    /// <summary>
    /// Given a configured (not started) Process in processLocal, an options object and a
    /// callback object, build the $EventEmitter + $ChildProcessCtx + ChildProcess dict,
    /// launch the captured/streamed worker on the event loop, and leave the ChildProcess
    /// dict ($Object) on the stack. When streamed, also builds stdout/stderr/stdin streams.
    /// </summary>
    private void EmitBuildChildAndLaunch(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder processLocal, LocalBuilder optionsLocal, LocalBuilder callbackLocal, bool streamed)
    {
        var emitterLocal = il.DeclareLocal(runtime.TSEventEmitterType);
        var ctxLocal = il.DeclareLocal(_childCtxType);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var timeoutLocal = il.DeclareLocal(_types.Double);

        EmitParseTimeout(il, optionsLocal, timeoutLocal);

        // emitter = new $EventEmitter()
        il.Emit(OpCodes.Newobj, runtime.TSEventEmitterCtor);
        il.Emit(OpCodes.Stloc, emitterLocal);

        // ctx = new $ChildProcessCtx()
        il.Emit(OpCodes.Newobj, _childCtxCtor);
        il.Emit(OpCodes.Stloc, ctxLocal);
        StoreCtxField(il, ctxLocal, _childCtxProc, () => il.Emit(OpCodes.Ldloc, processLocal));
        StoreCtxField(il, ctxLocal, _childCtxEmitter, () => il.Emit(OpCodes.Ldloc, emitterLocal));
        StoreCtxField(il, ctxLocal, _childCtxCallback, () => il.Emit(OpCodes.Ldloc, callbackLocal));
        StoreCtxField(il, ctxLocal, _childCtxOptions, () => il.Emit(OpCodes.Ldloc, optionsLocal));
        StoreCtxField(il, ctxLocal, _childCtxTimeout, () => il.Emit(OpCodes.Ldloc, timeoutLocal));
        // maxBuffer (chars), default 1 MB.
        StoreCtxField(il, ctxLocal, _childCtxMaxBuffer, () => EmitParseMaxBuffer(il, optionsLocal));
        EmitStoreEncodingFields(il, ctxLocal, optionsLocal);

        // dict = new Dictionary<string,object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        // pid = 0.0, killed = false, connected = false, exitCode = null, signalCode = null
        EmitDictSet(il, dictLocal, "pid", () => { il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Box, _types.Double); });
        EmitDictSet(il, dictLocal, "killed", () => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Box, _types.Boolean); });
        EmitDictSet(il, dictLocal, "connected", () => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Box, _types.Boolean); });
        EmitDictSet(il, dictLocal, "exitCode", () => il.Emit(OpCodes.Ldnull));
        EmitDictSet(il, dictLocal, "signalCode", () => il.Emit(OpCodes.Ldnull));

        // on/once delegate to the emitter; kill/send/disconnect/ref/unref to ctx methods.
        EmitDictSet(il, dictLocal, "on", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, emitterLocal), runtime.TSEventEmitterOn));
        EmitDictSet(il, dictLocal, "once", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, emitterLocal), runtime.TSEventEmitterOnce));
        EmitDictSet(il, dictLocal, "addListener", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, emitterLocal), runtime.TSEventEmitterOn));
        EmitDictSet(il, dictLocal, "kill", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxKill));
        EmitDictSet(il, dictLocal, "send", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxSend));
        EmitDictSet(il, dictLocal, "disconnect", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxDisconnect));
        EmitDictSet(il, dictLocal, "ref", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxRef));
        EmitDictSet(il, dictLocal, "unref", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxRef));

        if (streamed)
            EmitBuildChildStreams(il, runtime, ctxLocal, dictLocal, optionsLocal);

        // ctx._dict = dict
        StoreCtxField(il, ctxLocal, _childCtxDict, () => il.Emit(OpCodes.Ldloc, dictLocal));

        var funcCtor = typeof(Func<object>).GetConstructor([_types.Object, typeof(IntPtr)])!;
        void Launch(MethodBuilder worker)
        {
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldftn, worker);
            il.Emit(OpCodes.Newobj, funcCtor);
            il.Emit(OpCodes.Call, _childRunAsync);
        }

        if (streamed)
        {
            // Start the process synchronously so child.stdin.write()/.end() on the same tick
            // reach a live StandardInput. A start failure (e.g. ENOENT) becomes an async 'error'.
            var afterLaunch = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc, processLocal);
            il.Emit(OpCodes.Callvirt, _miProcStart);
            il.Emit(OpCodes.Pop);
            EmitDictSet(il, dictLocal, "pid", () =>
            {
                il.Emit(OpCodes.Ldloc, processLocal);
                il.Emit(OpCodes.Callvirt, _miProcIdGet);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, _types.Double);
            });
            Launch(_childCtxRunStreamed);
            il.Emit(OpCodes.Leave, afterLaunch);
            il.BeginCatchBlock(_types.Exception);
            var exLocal = il.DeclareLocal(_types.Exception);
            il.Emit(OpCodes.Stloc, exLocal);
            StoreCtxField(il, ctxLocal, _childCtxResKind, () => il.Emit(OpCodes.Ldc_I4_2));
            StoreCtxField(il, ctxLocal, _childCtxResError, () =>
            {
                il.Emit(OpCodes.Ldloc, exLocal);
                il.Emit(OpCodes.Ldloc, processLocal);
                il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Process, "StartInfo")!.GetGetMethod()!);
                il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ProcessStartInfo, "FileName")!.GetGetMethod()!);
                il.Emit(OpCodes.Ldstr, "spawn");
                il.Emit(OpCodes.Call, _childSpawnError);
            });
            Launch(_childCtxRunSpawnError);
            il.Emit(OpCodes.Leave, afterLaunch);
            il.EndExceptionBlock();
            il.MarkLabel(afterLaunch);
        }
        else
        {
            Launch(_childCtxRunCaptured);
        }

        // return CreateObject(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
    }

    /// <summary>
    /// Build real $Readable stdout/stderr (pumped by RunStreamed) and a forwarding stdin
    /// object whose write/end push to the child's StandardInput. stdout/stderr are stored
    /// on ctx so the worker can push into them.
    /// </summary>
    private void EmitBuildChildStreams(ILGenerator il, EmittedRuntime runtime, LocalBuilder ctxLocal, LocalBuilder dictLocal, LocalBuilder optionsLocal)
    {
        // mode(fd): 0 pipe, 1 inherit, 2 ignore. Only 'pipe' fds get a real stream (others null,
        // matching Node). redir = mode != inherit (so 'ignore' is still read-and-drained).
        void Mode(LocalBuilder outLocal, int fd)
        {
            il.Emit(OpCodes.Ldloc, optionsLocal);
            il.Emit(OpCodes.Ldc_I4, fd);
            il.Emit(OpCodes.Call, _childStdioMode);
            il.Emit(OpCodes.Stloc, outLocal);
        }
        var outMode = il.DeclareLocal(_types.Int32);
        var errMode = il.DeclareLocal(_types.Int32);
        var inMode = il.DeclareLocal(_types.Int32);
        Mode(outMode, 1); Mode(errMode, 2); Mode(inMode, 0);

        // ctx redirected flags (mode != inherit)
        void StoreRedir(FieldBuilder f, LocalBuilder mode)
        {
            StoreCtxField(il, ctxLocal, f, () =>
            {
                il.Emit(OpCodes.Ldloc, mode); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq);
            });
        }
        StoreRedir(_childCtxStdoutRedir, outMode);
        StoreRedir(_childCtxStderrRedir, errMode);

        // A readable fd: if pipe, build $Readable + wire ctx + dict; else dict[key]=null.
        void BuildReadable(LocalBuilder mode, FieldBuilder ctxField, string key)
        {
            var pipe = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, mode);
            il.Emit(OpCodes.Brfalse, pipe); // 0 == pipe
            EmitDictSet(il, dictLocal, key, () => il.Emit(OpCodes.Ldnull));
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(pipe);
            var sLocal = il.DeclareLocal(runtime.TSReadableType);
            il.Emit(OpCodes.Newobj, runtime.TSReadableCtor);
            il.Emit(OpCodes.Stloc, sLocal);
            StoreCtxField(il, ctxLocal, ctxField, () => il.Emit(OpCodes.Ldloc, sLocal));
            EmitDictSet(il, dictLocal, key, () => il.Emit(OpCodes.Ldloc, sLocal));
            il.MarkLabel(done);
        }
        BuildReadable(outMode, _childCtxStdout, "stdout");
        BuildReadable(errMode, _childCtxStderr, "stderr");

        // stdin: if pipe, a forwarding { writable, write, end }; else null.
        var stdinPipe = il.DefineLabel();
        var stdinDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, inMode);
        il.Emit(OpCodes.Brfalse, stdinPipe);
        EmitDictSet(il, dictLocal, "stdin", () => il.Emit(OpCodes.Ldnull));
        il.Emit(OpCodes.Br, stdinDone);
        il.MarkLabel(stdinPipe);
        var stdinLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, stdinLocal);
        EmitDictSet(il, stdinLocal, "writable", () => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Box, _types.Boolean); });
        EmitDictSet(il, stdinLocal, "write", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxStdinWrite));
        EmitDictSet(il, stdinLocal, "end", () => EmitTSFunc(il, runtime, () => il.Emit(OpCodes.Ldloc, ctxLocal), _childCtxStdinEnd));
        EmitDictSet(il, dictLocal, "stdin", () => { il.Emit(OpCodes.Ldloc, stdinLocal); il.Emit(OpCodes.Call, runtime.CreateObject); });
        il.MarkLabel(stdinDone);
    }

    private void EmitDictSet(ILGenerator il, LocalBuilder dictLocal, string key, Action emitValue)
    {
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, key);
        emitValue();
        il.Emit(OpCodes.Callvirt, _miDictSet);
    }

    private void StoreCtxField(ILGenerator il, LocalBuilder ctxLocal, FieldBuilder field, Action emitValue)
    {
        il.Emit(OpCodes.Ldloc, ctxLocal);
        emitValue();
        il.Emit(OpCodes.Stfld, field);
    }

    /// <summary>this.field = &lt;value&gt; — for use inside ctx instance methods (this = arg0).</summary>
    private void StoreCtxField(ILGenerator il, FieldBuilder field, Action emitValue)
    {
        il.Emit(OpCodes.Ldarg_0);
        emitValue();
        il.Emit(OpCodes.Stfld, field);
    }

    /// <summary>
    /// Sets ctx._asBuffer + ctx._encoding from options["encoding"]: 'buffer' → asBuffer=true;
    /// any other name → that encoding; absent → 'utf8' (Node's exec default).
    /// </summary>
    private void EmitStoreEncodingFields(ILGenerator il, LocalBuilder ctxLocal, LocalBuilder optionsObjLocal)
    {
        var encStr = il.DeclareLocal(_types.String);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        var strEq = _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!;
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, encStr);
        il.Emit(OpCodes.Ldloc, optionsObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "encoding");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, encStr);
        il.MarkLabel(done);

        // _asBuffer = encStr == "buffer"
        StoreCtxField(il, ctxLocal, _childCtxAsBuffer, () =>
        {
            il.Emit(OpCodes.Ldloc, encStr); il.Emit(OpCodes.Ldstr, "buffer"); il.Emit(OpCodes.Call, strEq);
        });
        // _encoding = (encStr == null || encStr == "buffer") ? "utf8" : encStr
        StoreCtxField(il, ctxLocal, _childCtxEncoding, () =>
        {
            var useDefault = il.DefineLabel();
            var got = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, encStr); il.Emit(OpCodes.Brfalse, useDefault);
            il.Emit(OpCodes.Ldloc, encStr); il.Emit(OpCodes.Ldstr, "buffer"); il.Emit(OpCodes.Call, strEq); il.Emit(OpCodes.Brtrue, useDefault);
            il.Emit(OpCodes.Ldloc, encStr); il.Emit(OpCodes.Br, got);
            il.MarkLabel(useDefault); il.Emit(OpCodes.Ldstr, "utf8");
            il.MarkLabel(got);
        });
    }

    /// <summary>Leaves a bool on the stack: (arg[argIdx] as dict)?[key] is bool b ? b : false.</summary>
    private void EmitOptionBool(ILGenerator il, int argIdx, string key)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.Boolean);
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, dictLocal); il.Emit(OpCodes.Ldstr, key); il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal); il.Emit(OpCodes.Isinst, _types.Boolean); il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal); il.Emit(OpCodes.Unbox_Any, _types.Boolean); il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>Leaves an int on the stack: options["maxBuffer"] as int, default 1 MB.</summary>
    private void EmitParseMaxBuffer(ILGenerator il, LocalBuilder optionsObjLocal)
    {
        var resultLocal = il.DeclareLocal(_types.Int32);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4, 1048576);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, optionsObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "maxBuffer");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>
    /// static byte[] ChildReadCappedBytes(Stream stream, int maxBuffer, bool[] flag): reads up
    /// to maxBuffer bytes; on overflow sets flag[0]=true and stops. maxBuffer&lt;0 = unbounded.
    /// </summary>
    private void EmitChildReadCapped(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var streamT = typeof(System.IO.Stream);
        var memT = typeof(System.IO.MemoryStream);
        var m = runtimeType.DefineMethod("ChildReadCappedBytes",
            MethodAttributes.Public | MethodAttributes.Static, typeof(byte[]),
            [streamT, _types.Int32, typeof(bool[])]);
        _childReadCappedBytes = m;
        var il = m.GetILGenerator();
        var msLocal = il.DeclareLocal(memT);
        var bufLocal = il.DeclareLocal(typeof(byte[]));
        var nLocal = il.DeclareLocal(_types.Int32);
        var totalLocal = il.DeclareLocal(_types.Int32);
        var remLocal = il.DeclareLocal(_types.Int32);
        var readM = streamT.GetMethod("Read", [typeof(byte[]), _types.Int32, _types.Int32])!;
        var writeM = streamT.GetMethod("Write", [typeof(byte[]), _types.Int32, _types.Int32])!;

        il.Emit(OpCodes.Newobj, memT.GetConstructor(Type.EmptyTypes)!); il.Emit(OpCodes.Stloc, msLocal);
        il.Emit(OpCodes.Ldc_I4, 4096); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, bufLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, totalLocal);

        var loop = il.DefineLabel();
        var end = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, bufLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4, 4096);
        il.Emit(OpCodes.Callvirt, readM);
        il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ble, end);
        // if (maxBuffer >= 0 && total + n > maxBuffer) { rem = max(0, maxBuffer-total); ms.Write(buf,0,rem); flag[0]=true; break; }
        var noOverflow = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, noOverflow); // maxBuffer < 0 → unbounded
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ble, noOverflow);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, remLocal);
        var remOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, remOk);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, remLocal);
        il.MarkLabel(remOk);
        il.Emit(OpCodes.Ldloc, msLocal); il.Emit(OpCodes.Ldloc, bufLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, remLocal);
        il.Emit(OpCodes.Callvirt, writeM);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Br, end);
        il.MarkLabel(noOverflow);
        il.Emit(OpCodes.Ldloc, msLocal); il.Emit(OpCodes.Ldloc, bufLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Callvirt, writeM);
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, totalLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc, msLocal); il.Emit(OpCodes.Callvirt, memT.GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // static object ChildDecodeOutput(byte[] bytes, bool asBuffer, string encoding):
        //   var b = new $Buffer(bytes); return asBuffer ? b : b.ToEncodedString(encoding);
        var d = runtimeType.DefineMethod("ChildDecodeOutput",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object,
            [typeof(byte[]), _types.Boolean, _types.String]);
        _childDecodeOutput = d;
        var dil = d.GetILGenerator();
        var bLocal = dil.DeclareLocal(runtime.TSBufferType);
        dil.Emit(OpCodes.Ldarg_0); dil.Emit(OpCodes.Newobj, runtime.TSBufferCtor); dil.Emit(OpCodes.Stloc, bLocal);
        var decode = dil.DefineLabel();
        dil.Emit(OpCodes.Ldarg_1); dil.Emit(OpCodes.Brfalse, decode);
        dil.Emit(OpCodes.Ldloc, bLocal); dil.Emit(OpCodes.Ret);
        dil.MarkLabel(decode);
        dil.Emit(OpCodes.Ldloc, bLocal); dil.Emit(OpCodes.Ldarg_2); dil.Emit(OpCodes.Callvirt, runtime.TSBufferToString);
        dil.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// static object ChildSpawnError(object exObj, string command, string syscall): a Node-style
    /// error dict. A missing executable (Win32 error 2) → ENOENT with code/errno/syscall/path.
    /// </summary>
    private void EmitChildSpawnError(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var m = runtimeType.DefineMethod("ChildSpawnError",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object,
            [_types.Object, _types.String, _types.String]);
        _childSpawnError = m;
        var il = m.GetILGenerator();
        var w32 = typeof(System.ComponentModel.Win32Exception);
        var concat3 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!;
        var concat2 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!;
        var prefixLocal = il.DeclareLocal(_types.String);   // "syscall command"
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var wLocal = il.DeclareLocal(w32);
        var generic = il.DefineLabel();

        // w = exObj as Win32Exception; if (w == null || w.NativeErrorCode != 2) -> generic
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, w32);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, wLocal);
        il.Emit(OpCodes.Brfalse, generic);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Callvirt, w32.GetProperty("NativeErrorCode")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, generic);

        // prefix = syscall + " " + command
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldstr, " "); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, concat3);
        il.Emit(OpCodes.Stloc, prefixLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);
        void Set(string key, Action v) { il.Emit(OpCodes.Ldloc, dictLocal); il.Emit(OpCodes.Ldstr, key); v(); il.Emit(OpCodes.Callvirt, _miDictSet); }
        Set("message", () => { il.Emit(OpCodes.Ldloc, prefixLocal); il.Emit(OpCodes.Ldstr, " ENOENT"); il.Emit(OpCodes.Call, concat2); });
        Set("code", () => il.Emit(OpCodes.Ldstr, "ENOENT"));
        Set("errno", () =>
        {
            var unix = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Call, _types.GetProperty(_types.OSPlatform, "Windows")!.GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.RuntimeInformation, "IsOSPlatform", [_types.OSPlatform])!);
            il.Emit(OpCodes.Brfalse, unix);
            il.Emit(OpCodes.Ldc_R8, -4058.0); il.Emit(OpCodes.Br, done);
            il.MarkLabel(unix); il.Emit(OpCodes.Ldc_R8, -2.0);
            il.MarkLabel(done); il.Emit(OpCodes.Box, _types.Double);
        });
        Set("syscall", () => il.Emit(OpCodes.Ldloc, prefixLocal));
        Set("path", () => il.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        // generic: { message: ((Exception)exObj).Message }
        il.MarkLabel(generic);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal); il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _types.Exception); il.Emit(OpCodes.Callvirt, _miExceptionMessageGet);
        il.Emit(OpCodes.Callvirt, _miDictSet);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emit: new $TSFunction(target, methodof(method)).</summary>
    private void EmitTSFunc(ILGenerator il, EmittedRuntime runtime, Action emitTarget, MethodInfo method)
    {
        emitTarget();
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Call, _miGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
    }
}
