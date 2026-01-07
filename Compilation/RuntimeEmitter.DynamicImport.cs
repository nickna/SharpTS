using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Emits dynamic import support methods into the generated assembly.
/// </summary>
public static partial class RuntimeEmitter
{
    /// <summary>
    /// Emits methods for dynamic import support.
    /// </summary>
    private static void EmitDynamicImportMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitWrapTaskAsPromise(typeBuilder, runtime);
        EmitDynamicImportModule(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits WrapTaskAsPromise: wraps Task&lt;object?&gt; in SharpTSPromise.
    /// Signature: SharpTSPromise WrapTaskAsPromise(Task&lt;object?&gt; task)
    /// </summary>
    private static void EmitWrapTaskAsPromise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapTaskAsPromise",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SharpTSPromise),
            [_types.TaskOfObject]
        );
        runtime.WrapTaskAsPromise = method;

        var il = method.GetILGenerator();

        // return new SharpTSPromise(task);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(SharpTSPromise).GetConstructor([typeof(Task<object?>)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DynamicImportModule: loads a module dynamically at runtime.
    /// Signature: Task&lt;object?&gt; DynamicImportModule(string specifier, string currentModulePath)
    ///
    /// Note: In compiled mode, dynamic imports with computed paths are not fully supported.
    /// This implementation returns a rejected task since the module was not pre-compiled.
    /// For string literal paths, the compiler should optimize to use the pre-compiled module directly.
    /// </summary>
    private static void EmitDynamicImportModule(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DynamicImportModule",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.String, _types.String]
        );
        runtime.DynamicImportModule = method;

        var il = method.GetILGenerator();

        // Locals
        var tcsType = _types.TaskCompletionSourceOfObject;
        var tcsLocal = il.DeclareLocal(tcsType);
        var exLocal = il.DeclareLocal(_types.Exception);

        // var tcs = new TaskCompletionSource<object?>();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(tcsType));
        il.Emit(OpCodes.Stloc, tcsLocal);

        // Build error message: $"Dynamic import of '{specifier}' is not supported in compiled mode."
        il.Emit(OpCodes.Ldstr, "Dynamic import of '");
        il.Emit(OpCodes.Ldarg_0); // specifier
        il.Emit(OpCodes.Ldstr, "' is not supported in compiled mode. Use static imports or ensure all modules are pre-compiled.");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));

        // var ex = new Exception(message);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, exLocal);

        // tcs.SetException(ex);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "SetException", _types.Exception));

        // return tcs.Task;
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }
}
