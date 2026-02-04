using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'os' module.
/// All methods emit direct calls to .NET BCL where possible.
/// </summary>
public sealed class OsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "os";

    private static readonly string[] _exportedMembers =
    [
        "platform", "arch", "hostname", "homedir", "tmpdir",
        "type", "release", "cpus", "totalmem", "freemem",
        "EOL", "userInfo", "loadavg", "networkInterfaces"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
            "platform" => EmitPlatform(emitter),
            "arch" => EmitArch(emitter),
            "hostname" => EmitHostname(emitter),
            "homedir" => EmitHomedir(emitter),
            "tmpdir" => EmitTmpdir(emitter),
            "type" => EmitType(emitter),
            "release" => EmitRelease(emitter),
            "cpus" => EmitCpus(emitter),
            "totalmem" => EmitTotalmem(emitter),
            "freemem" => EmitFreemem(emitter),
            "userInfo" => EmitUserInfo(emitter),
            "loadavg" => EmitLoadavg(emitter),
            "networkInterfaces" => EmitNetworkInterfaces(emitter),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return propertyName switch
        {
            "EOL" => EmitEOL(emitter),
            _ => false
        };
    }

    private static bool EmitPlatform(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit runtime check for platform
        // RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win32" :
        //     RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "darwin"
        var linuxLabel = il.DefineLabel();
        var darwinLabel = il.DefineLabel();
        var done = il.DefineLabel();

        // Check Windows
        il.Emit(OpCodes.Call, typeof(OSPlatform).GetProperty("Windows")!.GetMethod!);
        il.Emit(OpCodes.Call, ctx.Types.RuntimeInformationIsOSPlatform);
        il.Emit(OpCodes.Brfalse, linuxLabel);
        il.Emit(OpCodes.Ldstr, "win32");
        il.Emit(OpCodes.Br, done);

        // Check Linux
        il.MarkLabel(linuxLabel);
        il.Emit(OpCodes.Call, typeof(OSPlatform).GetProperty("Linux")!.GetMethod!);
        il.Emit(OpCodes.Call, ctx.Types.RuntimeInformationIsOSPlatform);
        il.Emit(OpCodes.Brfalse, darwinLabel);
        il.Emit(OpCodes.Ldstr, "linux");
        il.Emit(OpCodes.Br, done);

        // Default to darwin
        il.MarkLabel(darwinLabel);
        il.Emit(OpCodes.Ldstr, "darwin");

        il.MarkLabel(done);
        return true;
    }

    private static bool EmitArch(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // RuntimeInformation.ProcessArchitecture mapped to Node.js names
        var armLabel = il.DefineLabel();
        var arm64Label = il.DefineLabel();
        var x86Label = il.DefineLabel();
        var unknownLabel = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Call, typeof(RuntimeInformation).GetProperty("ProcessArchitecture")!.GetMethod!);
        var archLocal = il.DeclareLocal(typeof(Architecture));
        il.Emit(OpCodes.Stloc, archLocal);

        // Check X64
        il.Emit(OpCodes.Ldloc, archLocal);
        il.Emit(OpCodes.Ldc_I4, (int)Architecture.X64);
        il.Emit(OpCodes.Bne_Un, armLabel);
        il.Emit(OpCodes.Ldstr, "x64");
        il.Emit(OpCodes.Br, done);

        // Check Arm
        il.MarkLabel(armLabel);
        il.Emit(OpCodes.Ldloc, archLocal);
        il.Emit(OpCodes.Ldc_I4, (int)Architecture.Arm);
        il.Emit(OpCodes.Bne_Un, arm64Label);
        il.Emit(OpCodes.Ldstr, "arm");
        il.Emit(OpCodes.Br, done);

        // Check Arm64
        il.MarkLabel(arm64Label);
        il.Emit(OpCodes.Ldloc, archLocal);
        il.Emit(OpCodes.Ldc_I4, (int)Architecture.Arm64);
        il.Emit(OpCodes.Bne_Un, x86Label);
        il.Emit(OpCodes.Ldstr, "arm64");
        il.Emit(OpCodes.Br, done);

        // Check X86
        il.MarkLabel(x86Label);
        il.Emit(OpCodes.Ldloc, archLocal);
        il.Emit(OpCodes.Ldc_I4, (int)Architecture.X86);
        il.Emit(OpCodes.Bne_Un, unknownLabel);
        il.Emit(OpCodes.Ldstr, "ia32");
        il.Emit(OpCodes.Br, done);

        // Unknown architecture - default to "unknown"
        il.MarkLabel(unknownLabel);
        il.Emit(OpCodes.Ldstr, "unknown");

        il.MarkLabel(done);
        return true;
    }

    private static bool EmitHostname(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Call, typeof(Environment).GetProperty("MachineName")!.GetMethod!);
        return true;
    }

    private static bool EmitHomedir(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.UserProfile);
        il.Emit(OpCodes.Call, typeof(Environment).GetMethod("GetFolderPath", [typeof(Environment.SpecialFolder)])!);
        return true;
    }

    private static bool EmitTmpdir(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetTempPath"));
        return true;
    }

    private static bool EmitType(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Similar to platform but returns OS type names
        var linuxLabel = il.DefineLabel();
        var darwinLabel = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Call, typeof(OSPlatform).GetProperty("Windows")!.GetMethod!);
        il.Emit(OpCodes.Call, ctx.Types.RuntimeInformationIsOSPlatform);
        il.Emit(OpCodes.Brfalse, linuxLabel);
        il.Emit(OpCodes.Ldstr, "Windows_NT");
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(linuxLabel);
        il.Emit(OpCodes.Call, typeof(OSPlatform).GetProperty("Linux")!.GetMethod!);
        il.Emit(OpCodes.Call, ctx.Types.RuntimeInformationIsOSPlatform);
        il.Emit(OpCodes.Brfalse, darwinLabel);
        il.Emit(OpCodes.Ldstr, "Linux");
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(darwinLabel);
        il.Emit(OpCodes.Ldstr, "Darwin");

        il.MarkLabel(done);
        return true;
    }

    private static bool EmitRelease(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Environment.OSVersion.VersionString
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty("OSVersion")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(OperatingSystem).GetProperty("VersionString")!.GetMethod!);
        return true;
    }

    private static bool EmitCpus(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Create a List<object> with ProcessorCount entries
        // Each entry is a simple object { model: "cpu", speed: 0 }
        il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(ctx.Types.ListOfObject));
        var listLocal = il.DeclareLocal(ctx.Types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Get processor count
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty("ProcessorCount")!.GetMethod!);
        var countLocal = il.DeclareLocal(ctx.Types.Int32);
        il.Emit(OpCodes.Stloc, countLocal);

        // Loop to add CPU entries
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var indexLocal = il.DeclareLocal(ctx.Types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create a simple CPU info object
        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "model");
        il.Emit(OpCodes.Ldstr, "cpu");
        il.Emit(OpCodes.Call, addMethod);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "speed");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        il.Emit(OpCodes.Call, addMethod);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.ListOfObject, "Add", ctx.Types.Object));

        // Increment index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
        return true;
    }

    private static bool EmitTotalmem(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
        // Must store struct in local and use ldloca for property access on value types
        il.Emit(OpCodes.Call, typeof(GC).GetMethod("GetGCMemoryInfo", Type.EmptyTypes)!);
        var infoLocal = il.DeclareLocal(typeof(GCMemoryInfo));
        il.Emit(OpCodes.Stloc, infoLocal);
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Call, typeof(GCMemoryInfo).GetProperty("TotalAvailableMemoryBytes")!.GetMethod!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitFreemem(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call emitted OsFreemem helper for standalone assemblies
        il.Emit(OpCodes.Call, ctx.Runtime!.OsFreemem);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitUserInfo(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // username
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "username");
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty("UserName")!.GetMethod!);
        il.Emit(OpCodes.Call, addMethod);

        // uid (-1 on Windows)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "uid");
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        il.Emit(OpCodes.Call, addMethod);

        // gid (-1 on Windows)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "gid");
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        il.Emit(OpCodes.Call, addMethod);

        // shell
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "shell");
        il.Emit(OpCodes.Ldnull); // null on Windows
        il.Emit(OpCodes.Call, addMethod);

        // homedir
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "homedir");
        il.Emit(OpCodes.Ldc_I4, (int)Environment.SpecialFolder.UserProfile);
        il.Emit(OpCodes.Call, typeof(Environment).GetMethod("GetFolderPath", [typeof(Environment.SpecialFolder)])!);
        il.Emit(OpCodes.Call, addMethod);

        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }

    private static bool EmitEOL(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Call, typeof(Environment).GetProperty("NewLine")!.GetMethod!);
        return true;
    }

    private static bool EmitLoadavg(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call the emitted OsLoadavg runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.OsLoadavg);
        return true;
    }

    private static bool EmitNetworkInterfaces(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call the emitted OsNetworkInterfaces runtime helper and wrap in TSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.OsNetworkInterfaces);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }
}
