using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits child_process module helper methods with full IL (no external dependencies).
    /// </summary>
    private void EmitChildProcessMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitChildProcessNoOp(typeBuilder);
        EmitChildProcessAsyncInfra(typeBuilder, runtime);
        EmitChildProcessExecSync(typeBuilder, runtime);
        EmitChildProcessSpawnSync(typeBuilder, runtime);
        EmitChildProcessExec(typeBuilder, runtime);
        EmitChildProcessSpawn(typeBuilder, runtime);
        EmitChildProcessExecFileSync(typeBuilder, runtime);
        EmitChildProcessExecFile(typeBuilder, runtime);
        EmitChildProcessFork(typeBuilder, runtime);
    }

    /// <summary>
    /// Declares an inputLocal, parses options[argIdx]["input"] into it, and sets
    /// RedirectStandardInput = (input != null) on startInfoLocal. Returns inputLocal. (#1021)
    /// </summary>
    private LocalBuilder EmitSyncInputRedirect(ILGenerator il, LocalBuilder startInfoLocal, int optionsArgIdx)
    {
        var inputLocal = il.DeclareLocal(_types.String);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tmpLocal = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, inputLocal);
        il.Emit(OpCodes.Ldarg, optionsArgIdx);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, dictLocal); il.Emit(OpCodes.Ldstr, "input"); il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, tmpLocal); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Stloc, inputLocal);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, inputLocal); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardInput")!.GetSetMethod()!);
        return inputLocal;
    }

    /// <summary>After Start: if input != null, write it to StandardInput and Close (EOF). (#1021)</summary>
    private void EmitSyncInputWrite(ILGenerator il, LocalBuilder processLocal, LocalBuilder inputLocal)
    {
        var noWrite = il.DefineLabel();
        var stdinGet = _types.Process.GetProperty("StandardInput")!.GetGetMethod()!;
        il.Emit(OpCodes.Ldloc, inputLocal); il.Emit(OpCodes.Brfalse, noWrite);
        il.Emit(OpCodes.Ldloc, processLocal); il.Emit(OpCodes.Callvirt, stdinGet);
        il.Emit(OpCodes.Ldloc, inputLocal); il.Emit(OpCodes.Callvirt, typeof(System.IO.TextWriter).GetMethod("Write", [_types.String])!);
        il.Emit(OpCodes.Ldloc, processLocal); il.Emit(OpCodes.Callvirt, stdinGet);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.TextWriter).GetMethod("Close", Type.EmptyTypes)!);
        il.MarkLabel(noWrite);
    }

    /// <summary>
    /// Emits: public static string ChildProcessExecSync(string command, object options)
    /// Executes a command synchronously and returns stdout.
    /// </summary>
    private void EmitChildProcessExecSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessExecSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object]);
        runtime.ChildProcessExecSync = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "execSync", method);

        var il = method.GetILGenerator();

        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);
        var processLocal = il.DeclareLocal(_types.Process);
        var stdoutLocal = il.DeclareLocal(_types.String);
        var stderrLocal = il.DeclareLocal(_types.String);
        var exitCodeLocal = il.DeclareLocal(_types.Int32);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var cwdLocal = il.DeclareLocal(_types.String);
        var tempObjLocal = il.DeclareLocal(_types.Object);

        // var startInfo = new ProcessStartInfo()
        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        // startInfo.UseShellExecute = false
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);

        // startInfo.RedirectStandardOutput = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);

        // startInfo.RedirectStandardError = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);

        // startInfo.CreateNoWindow = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);
        var __inputES = EmitSyncInputRedirect(il, startInfoLocal, 1);

        // Platform check: if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        var notWindowsLabel = il.DefineLabel();
        var afterPlatformLabel = il.DefineLabel();

        il.Emit(OpCodes.Call, _types.OSPlatform.GetProperty("Windows")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.RuntimeInformation.GetMethod("IsOSPlatform", [_types.OSPlatform])!);
        il.Emit(OpCodes.Brfalse, notWindowsLabel);

        // Windows: startInfo.FileName = "cmd.exe"
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "cmd.exe");
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("FileName")!.GetSetMethod()!);

        // startInfo.Arguments = "/c " + command
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "/c ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("Arguments")!.GetSetMethod()!);
        il.Emit(OpCodes.Br, afterPlatformLabel);

        // Unix/Linux
        il.MarkLabel(notWindowsLabel);

        // startInfo.FileName = "/bin/sh"
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "/bin/sh");
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("FileName")!.GetSetMethod()!);

        // startInfo.Arguments = "-c \"" + command.Replace("\"", "\\\"") + "\""
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "-c \"");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Replace", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("Arguments")!.GetSetMethod()!);

        il.MarkLabel(afterPlatformLabel);

        // Extract cwd from options if provided (options is Dictionary<string, object?>)
        var noCwdLabel = il.DefineLabel();
        var afterCwdLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.TryGetValue("cwd", out var cwdObj) && cwdObj != null)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "cwd");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        // startInfo.WorkingDirectory = cwdObj.ToString()
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("WorkingDirectory")!.GetSetMethod()!);

        il.MarkLabel(noCwdLabel);

        // Extract env from options if provided
        var noEnvLabel = il.DefineLabel();
        var envDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var envEnumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object?>.Enumerator));
        var envKvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object?>));

        // if dict is already loaded and dict.TryGetValue("env", out envObj) && envObj is Dictionary<string,object?>
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, noEnvLabel);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "env");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noEnvLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noEnvLabel);

        // envDict = (Dictionary<string,object?>)tempObj
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, envDictLocal);

        // startInfo.Environment.Clear() - to replace inherited env (Node.js behavior)
        var envProp = _types.ProcessStartInfo.GetProperty("Environment")!.GetGetMethod()!;
        var iDictStringString = typeof(IDictionary<string, string?>);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, envProp);
        il.Emit(OpCodes.Callvirt, typeof(ICollection<KeyValuePair<string, string?>>).GetMethod("Clear")!);

        // foreach (var kvp in envDict) { startInfo.Environment[kvp.Key] = kvp.Value?.ToString() ?? ""; }
        il.Emit(OpCodes.Ldloc, envDictLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, envEnumeratorLocal);

        var envLoopStart = il.DefineLabel();
        var envLoopEnd = il.DefineLabel();
        il.Emit(OpCodes.Br, envLoopEnd);

        il.MarkLabel(envLoopStart);
        // kvp = enumerator.Current
        il.Emit(OpCodes.Ldloca, envEnumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, envKvpLocal);

        // startInfo.Environment[kvp.Key] = kvp.Value?.ToString() ?? ""
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, envProp);

        // key
        il.Emit(OpCodes.Ldloca, envKvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object?>).GetProperty("Key")!.GetGetMethod()!);

        // value?.ToString() ?? ""
        il.Emit(OpCodes.Ldloca, envKvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object?>).GetProperty("Value")!.GetGetMethod()!);
        var envValNullLabel = il.DefineLabel();
        var envValDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Stloc, tempObjLocal);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, envValNullLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, envValDoneLabel);
        il.MarkLabel(envValNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(envValDoneLabel);

        // call IDictionary<string,string?>.set_Item(key, value)
        il.Emit(OpCodes.Callvirt, iDictStringString.GetMethod("set_Item", [_types.String, _types.String])!);

        il.MarkLabel(envLoopEnd);
        // if (enumerator.MoveNext()) goto loopStart
        il.Emit(OpCodes.Ldloca, envEnumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brtrue, envLoopStart);

        il.MarkLabel(noEnvLabel);

        // using var process = new Process { StartInfo = startInfo };
        // We'll handle the using/try-finally pattern manually
        var afterTryLabel = il.DefineLabel();
        var returnStdoutLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);

        // process.StartInfo = startInfo
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);

        // process.Start()
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("Start", Type.EmptyTypes)!);
        il.Emit(OpCodes.Pop);
        EmitSyncInputWrite(il, processLocal, __inputES);

        // stdout = process.StandardOutput.ReadToEnd()
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardOutput")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stdoutLocal);

        // stderr = process.StandardError.ReadToEnd()
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardError")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stderrLocal);

        // process.WaitForExit()
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("WaitForExit", Type.EmptyTypes)!);

        // exitCode = process.ExitCode
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("ExitCode")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, exitCodeLocal);

        il.Emit(OpCodes.Leave, afterTryLabel);

        // finally { process?.Dispose() }
        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(skipDisposeLabel);
        il.Emit(OpCodes.Endfinally);

        il.EndExceptionBlock();

        il.MarkLabel(afterTryLabel);

        // if (exitCode != 0) throw new Exception(...)
        var noErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, exitCodeLocal);
        il.Emit(OpCodes.Brfalse, noErrorLabel);

        // throw new Exception("Command failed with exit code " + exitCode + ": " + stderr)
        il.Emit(OpCodes.Ldstr, "Command failed with exit code ");
        il.Emit(OpCodes.Ldloca, exitCodeLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, stderrLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noErrorLabel);
        il.Emit(OpCodes.Ldloc, stdoutLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ChildProcessSpawnSync(string command, object args, object options)
    /// Spawns a process synchronously and returns result object.
    /// </summary>
    private void EmitChildProcessSpawnSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessSpawnSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessSpawnSync = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "spawnSync", method);

        var il = method.GetILGenerator();

        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);
        var processLocal = il.DeclareLocal(_types.Process);
        var stdoutLocal = il.DeclareLocal(_types.String);
        var stderrLocal = il.DeclareLocal(_types.String);
        var exitCodeLocal = il.DeclareLocal(_types.Int32);
        var argsListLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tempObjLocal = il.DeclareLocal(_types.Object);
        var iLocal = il.DeclareLocal(_types.Int32);
        var argListLocal = il.DeclareLocal(typeof(System.Collections.ObjectModel.Collection<string>));
        var errorMsgLocal = il.DeclareLocal(_types.String);

        // Initialize stdout, stderr, exitCode
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, stdoutLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, stderrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, exitCodeLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, errorMsgLocal);

        // var startInfo = new ProcessStartInfo(command)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        // startInfo.UseShellExecute = false
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);

        // startInfo.RedirectStandardOutput = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);

        // startInfo.RedirectStandardError = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);

        // startInfo.CreateNoWindow = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);

        // input = options["input"] as string; RedirectStandardInput = input != null. (#1021)
        var inputLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, inputLocal);
        var noInputOpt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Brfalse, noInputOpt);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "input");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noInputOpt);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, inputLocal);
        il.MarkLabel(noInputOpt);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un); // input != null
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardInput")!.GetSetMethod()!);

        // Extract args if provided (args is List<object?>)
        var noArgsLabel = il.DefineLabel();
        var afterArgsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, noArgsLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, argsListLocal);

        // Get ArgumentList
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("ArgumentList")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, argListLocal);

        // for (int i = 0; i < argsList.Count; i++) { argumentList.Add(argsList[i]?.ToString() ?? ""); }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var argsLoopStart = il.DefineLabel();
        var argsLoopEnd = il.DefineLabel();

        il.MarkLabel(argsLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, argsLoopEnd);

        // var arg = argsList[i]
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, tempObjLocal);

        // argumentList.Add(arg?.ToString() ?? "")
        il.Emit(OpCodes.Ldloc, argListLocal);
        var argNullLabel = il.DefineLabel();
        var argAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, argNullLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, argAddLabel);
        il.MarkLabel(argNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(argAddLabel);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ObjectModel.Collection<string>).GetMethod("Add", [_types.String])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, argsLoopStart);

        il.MarkLabel(argsLoopEnd);
        il.MarkLabel(noArgsLabel);

        // Extract cwd from options if provided
        var noCwdLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "cwd");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("WorkingDirectory")!.GetSetMethod()!);

        il.MarkLabel(noCwdLabel);

        // try { run process } catch (Exception ex) { errorMsg = ex.Message; exitCode = -1; }
        var afterProcessLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("Start", Type.EmptyTypes)!);
        il.Emit(OpCodes.Pop);

        // if (input != null) { process.StandardInput.Write(input); process.StandardInput.Close(); }  (#1021)
        var noInputWrite = il.DefineLabel();
        var stdinGet = _types.Process.GetProperty("StandardInput")!.GetGetMethod()!;
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Brfalse, noInputWrite);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, stdinGet);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.TextWriter).GetMethod("Write", [_types.String])!);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, stdinGet);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.TextWriter).GetMethod("Close", Type.EmptyTypes)!);
        il.MarkLabel(noInputWrite);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardOutput")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stdoutLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardError")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stderrLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("WaitForExit", Type.EmptyTypes)!);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("ExitCode")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, exitCodeLocal);

        // Dispose process
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);

        il.Emit(OpCodes.Leave, afterProcessLabel);

        // catch (Exception ex) { errorMsg = ex.Message; exitCode = -1; }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Callvirt, _types.Exception.GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, errorMsgLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, exitCodeLocal);
        il.Emit(OpCodes.Leave, afterProcessLabel);

        il.EndExceptionBlock();

        il.MarkLabel(afterProcessLabel);

        // Create result dictionary
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // result["stdout"] = stdout
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "stdout");
        il.Emit(OpCodes.Ldloc, stdoutLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["stderr"] = stderr
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "stderr");
        il.Emit(OpCodes.Ldloc, stderrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["status"] = (double)exitCode
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Ldloc, exitCodeLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["signal"] = null
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "signal");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // if (errorMsg != null) result["error"] = errorMsg
        var noErrorMsgLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, errorMsgLocal);
        il.Emit(OpCodes.Brfalse, noErrorMsgLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldloc, errorMsgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        il.MarkLabel(noErrorMsgLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    // Field for the no-op child process method
    private MethodBuilder _childProcessNoOp = null!;

    /// <summary>
    /// Emits a static no-op method that returns null. Used for kill/send/disconnect stubs.
    /// </summary>
    private void EmitChildProcessNoOp(TypeBuilder typeBuilder)
    {
        _childProcessNoOp = typeBuilder.DefineMethod(
            "ChildProcessNoOp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]);
        var il = _childProcessNoOp.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to create a ProcessStartInfo configured for exec (shell execution).
    /// Leaves the Process (not yet started) on the stack.
    /// </summary>
    private void EmitCreateExecProcess(ILGenerator il, LocalBuilder commandArg)
    {
        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);

        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        // UseShellExecute = false, RedirectStd* = true, CreateNoWindow = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardInput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);

        // Platform check
        var notWindowsLabel = il.DefineLabel();
        var afterPlatformLabel = il.DefineLabel();

        il.Emit(OpCodes.Call, _types.OSPlatform.GetProperty("Windows")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.RuntimeInformation.GetMethod("IsOSPlatform", [_types.OSPlatform])!);
        il.Emit(OpCodes.Brfalse, notWindowsLabel);

        // Windows: cmd.exe /c command
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "cmd.exe");
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("FileName")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "/c ");
        il.Emit(OpCodes.Ldloc, commandArg);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("Arguments")!.GetSetMethod()!);
        il.Emit(OpCodes.Br, afterPlatformLabel);

        // Unix: /bin/sh -c "command"
        il.MarkLabel(notWindowsLabel);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "/bin/sh");
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("FileName")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldstr, "-c \"");
        il.Emit(OpCodes.Ldloc, commandArg);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Replace", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("Arguments")!.GetSetMethod()!);

        il.MarkLabel(afterPlatformLabel);

        // new Process { StartInfo = startInfo }
        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, processLocal); // leave Process on stack
    }

    /// <summary>
    /// Emits: public static object ChildProcessExec(string command, object optionsOrCallback, object callback)
    /// Pure IL — creates Process, starts it, returns ChildProcess-like object.
    /// </summary>
    private void EmitChildProcessExec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessExec",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessExec = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "exec", method);

        var il = method.GetILGenerator();
        // exec(command, optionsOrCallback?, callback?)
        var cmdLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, cmdLocal);

        // a1 = arg1, a2 = arg2
        var a1 = il.DeclareLocal(_types.Object);
        var a2 = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stloc, a2);

        var optionsLocal = il.DeclareLocal(_types.Object);
        var callbackLocal = il.DeclareLocal(_types.Object);
        EmitSelectOptions(il, [a1, a2], optionsLocal);
        EmitSelectCallback(il, [a2, a1], callbackLocal);

        // Build the shell Process (not started) and apply cwd/env.
        EmitCreateExecProcess(il, cmdLocal); // leaves Process on stack
        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Stloc, processLocal);
        // Re-derive the start info from the process to apply options.
        var siLocal = il.DeclareLocal(_types.ProcessStartInfo);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, siLocal);
        EmitApplyChildOptions(il, siLocal, optionsLocal);

        EmitBuildChildAndLaunch(il, runtime, processLocal, optionsLocal, callbackLocal, streamed: false);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ChildProcessSpawn(string command, object args, object options)
    /// Pure IL — creates Process with direct command + args, returns ChildProcess with stdio.
    /// </summary>
    private void EmitChildProcessSpawn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessSpawn",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessSpawn = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "spawn", method);

        var il = method.GetILGenerator();

        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);

        // startInfo = new ProcessStartInfo()
        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardInput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);

        // options = arg1 (if dict, the no-args form spawn(cmd, opts)) or arg2.
        var a1 = il.DeclareLocal(_types.Object);
        var a2 = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stloc, a2);
        var optionsLocal = il.DeclareLocal(_types.Object);
        EmitSelectOptions(il, [a2, a1], optionsLocal);

        // Set FileName/Arguments(/ArgumentList), honoring options.shell.
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, _childConfigureSpawn);

        // new Process { StartInfo = startInfo }
        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);

        EmitApplyChildOptions(il, startInfoLocal, optionsLocal);

        // spawn has no callback; pass null callback, streamed worker.
        var nullCb = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, nullCb);
        EmitBuildChildAndLaunch(il, runtime, processLocal, optionsLocal, nullCb, streamed: true);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string ChildProcessExecFileSync(string file, object args, object options)
    /// Executes a file synchronously without a shell and returns stdout.
    /// Uses same pattern as SpawnSync but throws on non-zero exit code.
    /// </summary>
    private void EmitChildProcessExecFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessExecFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessExecFileSync = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "execFileSync", method);

        var il = method.GetILGenerator();

        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);
        var processLocal = il.DeclareLocal(_types.Process);
        var stdoutLocal = il.DeclareLocal(_types.String);
        var stderrLocal = il.DeclareLocal(_types.String);
        var exitCodeLocal = il.DeclareLocal(_types.Int32);
        var argsListLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var tempObjLocal = il.DeclareLocal(_types.Object);
        var iLocal = il.DeclareLocal(_types.Int32);
        var argListLocal = il.DeclareLocal(typeof(System.Collections.ObjectModel.Collection<string>));

        // Initialize
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, stdoutLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, stderrLocal);

        // var startInfo = new ProcessStartInfo(file)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        // startInfo.UseShellExecute = false
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);

        // startInfo.RedirectStandardOutput = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);

        // startInfo.RedirectStandardError = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);

        // startInfo.CreateNoWindow = true
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);
        var __inputEFS = EmitSyncInputRedirect(il, startInfoLocal, 2);

        // Extract args if provided (args is List<object?>)
        var noArgsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, noArgsLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, argsListLocal);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("ArgumentList")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, argListLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var argsLoopStart = il.DefineLabel();
        var argsLoopEnd = il.DefineLabel();

        il.MarkLabel(argsLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, argsLoopEnd);

        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, tempObjLocal);

        il.Emit(OpCodes.Ldloc, argListLocal);
        var argNullLabel = il.DefineLabel();
        var argAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, argNullLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, argAddLabel);
        il.MarkLabel(argNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(argAddLabel);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ObjectModel.Collection<string>).GetMethod("Add", [_types.String])!);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, argsLoopStart);

        il.MarkLabel(argsLoopEnd);
        il.MarkLabel(noArgsLabel);

        // Extract cwd from options
        var noCwdLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "cwd");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noCwdLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, noCwdLabel);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("WorkingDirectory")!.GetSetMethod()!);

        il.MarkLabel(noCwdLabel);

        // try { run process } finally { dispose }
        var afterTryLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("Start", Type.EmptyTypes)!);
        il.Emit(OpCodes.Pop);
        EmitSyncInputWrite(il, processLocal, __inputEFS);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardOutput")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stdoutLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StandardError")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.TextReader.GetMethod("ReadToEnd")!);
        il.Emit(OpCodes.Stloc, stderrLocal);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetMethod("WaitForExit", Type.EmptyTypes)!);

        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("ExitCode")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, exitCodeLocal);

        il.Emit(OpCodes.Leave, afterTryLabel);

        // finally { process?.Dispose() }
        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(skipDisposeLabel);
        il.Emit(OpCodes.Endfinally);

        il.EndExceptionBlock();

        il.MarkLabel(afterTryLabel);

        // if (exitCode != 0) throw
        var noErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, exitCodeLocal);
        il.Emit(OpCodes.Brfalse, noErrorLabel);

        il.Emit(OpCodes.Ldstr, "Command failed with exit code ");
        il.Emit(OpCodes.Ldloca, exitCodeLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, stderrLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noErrorLabel);
        il.Emit(OpCodes.Ldloc, stdoutLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ChildProcessExecFile(string file, object args, object options, object callback)
    /// Pure IL — creates Process with direct file + args, returns ChildProcess-like object.
    /// </summary>
    private void EmitChildProcessExecFile(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessExecFile",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object, _types.Object]);
        runtime.ChildProcessExecFile = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "execFile", method);

        var il = method.GetILGenerator();

        // execFile(file, args?, options?, callback?)
        var startInfoLocal = il.DeclareLocal(_types.ProcessStartInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ProcessStartInfo.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Stloc, startInfoLocal);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("UseShellExecute")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardOutput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardError")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("RedirectStandardInput")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("CreateNoWindow")!.GetSetMethod()!);

        // Add args from arg1 if it's a List (proper per-arg, not space-join).
        var noArgsLabel = il.DefineLabel();
        var argsListLocal = il.DeclareLocal(_types.ListOfObject);
        var argListLocal = il.DeclareLocal(typeof(System.Collections.ObjectModel.Collection<string>));
        var iLocal = il.DeclareLocal(_types.Int32);
        var argTmpLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, argsListLocal);
        il.Emit(OpCodes.Brfalse, noArgsLabel);

        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.ProcessStartInfo.GetProperty("ArgumentList")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, argListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var argsLoop = il.DefineLabel();
        var argsLoopEnd = il.DefineLabel();
        il.MarkLabel(argsLoop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, argsLoopEnd);
        il.Emit(OpCodes.Ldloc, argsListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, argTmpLocal);
        il.Emit(OpCodes.Ldloc, argListLocal);
        var argNull = il.DefineLabel();
        var argAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argTmpLocal);
        il.Emit(OpCodes.Brfalse, argNull);
        il.Emit(OpCodes.Ldloc, argTmpLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, argAdd);
        il.MarkLabel(argNull);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(argAdd);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ObjectModel.Collection<string>).GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, argsLoop);
        il.MarkLabel(argsLoopEnd);
        il.MarkLabel(noArgsLabel);

        // Disambiguate options/callback among arg1/arg2/arg3.
        var a1 = il.DeclareLocal(_types.Object);
        var a2 = il.DeclareLocal(_types.Object);
        var a3 = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stloc, a2);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stloc, a3);
        var optionsLocal = il.DeclareLocal(_types.Object);
        var callbackLocal = il.DeclareLocal(_types.Object);
        EmitSelectOptions(il, [a1, a2, a3], optionsLocal);
        EmitSelectCallback(il, [a3, a2, a1], callbackLocal);

        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Newobj, _types.Process.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, processLocal);
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Ldloc, startInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.Process.GetProperty("StartInfo")!.GetSetMethod()!);

        EmitApplyChildOptions(il, startInfoLocal, optionsLocal);
        EmitBuildChildAndLaunch(il, runtime, processLocal, optionsLocal, callbackLocal, streamed: false);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ChildProcessFork(string modulePath, object args, object options)
    /// fork runs a child .ts module through the SharpTS interpreter — a compiled standalone
    /// binary has no in-process compiler, so it co-locates SharpTS.dll (RequireSharpTSRuntime,
    /// recorded at the fork call site) and bridges by reflection to
    /// ChildProcessModuleInterpreter.ForkForCompiledLoop, passing the compiled $EventLoop's
    /// Ref/Unref/Schedule so IPC + lifecycle events marshal onto the compiled loop (#1017).
    /// Mirrors $Runtime.CreateWorker. With --standalone (SharpTS.dll absent) it throws.
    /// </summary>
    private void EmitChildProcessFork(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessFork",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessFork = method;
        runtime.RegisterBuiltInModuleMethod("child_process", "fork", method);

        var il = method.GetILGenerator();
        var typeLocal = il.DeclareLocal(_types.Type);
        var loopLocal = il.DeclareLocal(runtime.EventLoopType);
        var refLocal = il.DeclareLocal(typeof(Action));
        var unrefLocal = il.DeclareLocal(typeof(Action));
        var scheduleLocal = il.DeclareLocal(typeof(Action<Action>));
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var actionCtor = typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!;
        var actionOfActionCtor = typeof(Action<Action>).GetConstructor([_types.Object, typeof(IntPtr)])!;

        // Type t = Type.GetType("SharpTS.Runtime.BuiltIns.Modules.Interpreter.ChildProcessModuleInterpreter, SharpTS");
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.BuiltIns.Modules.Interpreter.ChildProcessModuleInterpreter, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, typeLocal);

        var typeOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, typeOk);
        il.Emit(OpCodes.Ldstr, "child_process.fork requires the SharpTS runtime (SharpTS.dll) to be present. " +
                               "Compile without --standalone so it is co-located with the output.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(typeOk);

        // loop = $EventLoop.GetInstance();
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Stloc, loopLocal);
        // ref/unref/schedule delegates bound to the loop instance
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopRef);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, refLocal);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopUnref);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, unrefLocal);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopSchedule);
        il.Emit(OpCodes.Newobj, actionOfActionCtor);
        il.Emit(OpCodes.Stloc, scheduleLocal);

        // object[] args = { modulePath, argsObj, optionsObj, ref, unref, schedule };
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        void SetArg(int i, OpCode ld) { il.Emit(OpCodes.Ldloc, argsLocal); il.Emit(OpCodes.Ldc_I4, i); il.Emit(ld); il.Emit(OpCodes.Stelem_Ref); }
        SetArg(0, OpCodes.Ldarg_0);
        SetArg(1, OpCodes.Ldarg_1);
        SetArg(2, OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, argsLocal); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Ldloc, refLocal); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Ldloc, unrefLocal); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal); il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Ldloc, scheduleLocal); il.Emit(OpCodes.Stelem_Ref);

        // return t.GetMethod("ForkForCompiledLoop").Invoke(null, args);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "ForkForCompiledLoop");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }
}
