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
        EmitOsLoadavg(typeBuilder, runtime);
        EmitOsNetworkInterfaces(typeBuilder, runtime);
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

    /// <summary>
    /// Emits OsLoadavg: returns system load averages as List&lt;object?&gt;.
    /// Returns [0, 0, 0] on Windows per Node.js specification.
    /// For compiled standalone DLLs, returns zeros on all platforms for simplicity.
    /// Signature: List&lt;object?&gt; OsLoadavg()
    /// </summary>
    private void EmitOsLoadavg(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "OsLoadavg",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            Type.EmptyTypes
        );
        runtime.OsLoadavg = method;

        var il = method.GetILGenerator();

        // Create new List<object?>
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Add [0.0, 0.0, 0.0] to list
        // On Windows, this is correct per Node.js spec
        // On other platforms, full implementation would require complex process launching
        var addMethod = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);

        for (int i = 0; i < 3; i++)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, addMethod);
        }

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits OsNetworkInterfaces: returns network interface information as Dictionary&lt;string, object?&gt;.
    /// For compiled standalone DLLs, returns an empty dictionary (use interpreter for full functionality).
    /// Signature: Dictionary&lt;string, object?&gt; OsNetworkInterfaces()
    /// </summary>
    private void EmitOsNetworkInterfaces(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "OsNetworkInterfaces",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            Type.EmptyTypes
        );
        runtime.OsNetworkInterfaces = method;

        var il = method.GetILGenerator();

        // For compiled standalone DLLs, return an empty dictionary
        // The full implementation with network interface enumeration is complex
        // and users who need this should use the interpreter mode
        var dictCtor = _types.GetDefaultConstructor(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Ret);
    }
}
