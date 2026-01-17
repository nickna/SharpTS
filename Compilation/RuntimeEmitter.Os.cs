using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// OS module methods for standalone assemblies.
/// Replaces SystemInfoHelper references with cross-platform .NET implementations.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitOsModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitOsFreemem(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits OsFreemem: returns available memory in bytes using GC.GetGCMemoryInfo().
    /// Forces a GC first to ensure memory info is populated with valid values.
    /// Signature: double OsFreemem()
    /// </summary>
    private void EmitOsFreemem(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "OsFreemem",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.OsFreemem = method;

        var il = method.GetILGenerator();

        // Force a GC to ensure GCMemoryInfo has valid data
        // Without this, MemoryLoadBytes would be 0 if no GC has occurred
        il.Emit(OpCodes.Ldc_I4_0); // generation = 0 (Gen0)
        il.Emit(OpCodes.Ldc_I4_0); // GCCollectionMode.Default = 0
        il.Emit(OpCodes.Ldc_I4_0); // blocking = false
        il.Emit(OpCodes.Call, _types.GC.GetMethod("Collect", [_types.Int32, typeof(GCCollectionMode), _types.Boolean])!);

        // var info = GC.GetGCMemoryInfo();
        var infoLocal = il.DeclareLocal(typeof(GCMemoryInfo));
        il.Emit(OpCodes.Call, _types.GC.GetMethod("GetGCMemoryInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, infoLocal);

        // return (double)(info.TotalAvailableMemoryBytes - info.MemoryLoadBytes);
        // MemoryLoadBytes is the total memory load when last GC occurred
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Call, typeof(GCMemoryInfo).GetProperty("TotalAvailableMemoryBytes")!.GetGetMethod()!);

        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Call, typeof(GCMemoryInfo).GetProperty("MemoryLoadBytes")!.GetGetMethod()!);

        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }
}
