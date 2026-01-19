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
        EmitChildProcessExecSync(typeBuilder, runtime);
        EmitChildProcessSpawnSync(typeBuilder, runtime);
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
}
