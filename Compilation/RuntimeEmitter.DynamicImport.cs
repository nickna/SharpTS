using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits dynamic import support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits methods for dynamic import support.
    /// </summary>
    private void EmitDynamicImportMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitModuleRegistry(typeBuilder, runtime);
        EmitWrapTaskAsPromise(typeBuilder, runtime);
        EmitWrapVoidTaskAsObjectTask(typeBuilder, runtime);
        EmitDynamicImportModule(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the module registry field and registration methods.
    /// The registry maps module paths to factory functions that return module namespace objects.
    /// </summary>
    private void EmitModuleRegistry(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Field: private static Dictionary<string, Func<object?>>? _moduleRegistry;
        var dictType = typeof(Dictionary<string, Func<object?>>);
        var registryField = typeBuilder.DefineField(
            "_moduleRegistry",
            dictType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.ModuleRegistry = registryField;

        // Method: static void InitializeModuleRegistry()
        EmitInitializeModuleRegistry(typeBuilder, runtime, dictType, registryField);

        // Method: static void RegisterModule(string path, Func<object?> factory)
        EmitRegisterModule(typeBuilder, runtime, dictType, registryField);
    }

    /// <summary>
    /// Emits InitializeModuleRegistry: creates the registry dictionary if null.
    /// Signature: void InitializeModuleRegistry()
    /// </summary>
    private void EmitInitializeModuleRegistry(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        Type dictType,
        FieldBuilder registryField)
    {
        var method = typeBuilder.DefineMethod(
            "InitializeModuleRegistry",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.InitializeModuleRegistry = method;

        var il = method.GetILGenerator();
        var alreadyInitialized = il.DefineLabel();

        // if (_moduleRegistry != null) return;
        il.Emit(OpCodes.Ldsfld, registryField);
        il.Emit(OpCodes.Brtrue, alreadyInitialized);

        // _moduleRegistry = new Dictionary<string, Func<object?>>();
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stsfld, registryField);

        il.MarkLabel(alreadyInitialized);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits RegisterModule: adds a module factory to the registry.
    /// Signature: void RegisterModule(string path, Func&lt;object?&gt; factory)
    /// </summary>
    private void EmitRegisterModule(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        Type dictType,
        FieldBuilder registryField)
    {
        var method = typeBuilder.DefineMethod(
            "RegisterModule",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(string), typeof(Func<object?>)]
        );
        runtime.RegisterModule = method;

        var il = method.GetILGenerator();

        // _moduleRegistry[path] = factory;
        il.Emit(OpCodes.Ldsfld, registryField);
        il.Emit(OpCodes.Ldarg_0); // path
        il.Emit(OpCodes.Ldarg_1); // factory
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits WrapTaskAsPromise: wraps Task&lt;object?&gt; in $Promise.
    /// Signature: $Promise WrapTaskAsPromise(Task&lt;object?&gt; task)
    /// </summary>
    private void EmitWrapTaskAsPromise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapTaskAsPromise",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSPromiseType,
            [_types.TaskOfObject]
        );
        runtime.WrapTaskAsPromise = method;

        var il = method.GetILGenerator();

        // return new $Promise(task);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits WrapVoidTaskAsObjectTask: converts Task to Task&lt;object?&gt;.
    /// Signature: Task&lt;object?&gt; WrapVoidTaskAsObjectTask(Task task)
    /// </summary>
    private void EmitWrapVoidTaskAsObjectTask(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapVoidTaskAsObjectTask",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Task]
        );
        runtime.WrapVoidTaskAsObjectTask = method;

        var il = method.GetILGenerator();

        // Use Task.ContinueWith to convert void Task to Task<object?>
        // task.ContinueWith<object?>(t => null)
        // First we need to create a helper method that returns null

        // We'll use Task.FromResult(null) and await the input task first
        // Actually, the simplest approach is:
        // return task.ContinueWith(_ => (object?)null);

        // Get ContinueWith<TResult>(Func<Task, TResult>)
        var continueWithMethod = typeof(Task).GetMethod("ContinueWith", 1,
            [typeof(Func<,>).MakeGenericType(typeof(Task), Type.MakeGenericMethodParameter(0))])!
            .MakeGenericMethod(typeof(object));

        // First, emit the helper method for the Func
        var helperMethod = EmitVoidTaskContinuationHelper(typeBuilder);

        il.Emit(OpCodes.Ldarg_0); // task
        il.Emit(OpCodes.Ldnull); // target for delegate
        il.Emit(OpCodes.Ldftn, helperMethod);
        il.Emit(OpCodes.Newobj, typeof(Func<Task, object>).GetConstructors()[0]);
        il.Emit(OpCodes.Call, continueWithMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper method that takes a Task and returns null.
    /// Used as the continuation function for converting Task to Task&lt;object?&gt;.
    /// </summary>
    private MethodBuilder EmitVoidTaskContinuationHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "VoidTaskToNull",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Task]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits DynamicImportModule: loads a module dynamically at runtime.
    /// Signature: Task&lt;object?&gt; DynamicImportModule(string specifier, string currentModulePath)
    ///
    /// This implementation looks up the module in the pre-compiled registry.
    /// If found, returns a resolved task with the module namespace.
    /// If not found, returns a rejected task with an error message.
    /// </summary>
    private void EmitDynamicImportModule(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DynamicImportModule",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.String, _types.String]
        );
        runtime.DynamicImportModule = method;

        var il = method.GetILGenerator();

        // Types
        var dictType = typeof(Dictionary<string, Func<object?>>);
        var funcType = typeof(Func<object?>);
        var tcsType = _types.TaskCompletionSourceOfObject;
        var taskType = typeof(Task);

        // Locals
        var factoryLocal = il.DeclareLocal(funcType);
        var tcsLocal = il.DeclareLocal(tcsType);
        var exceptionLocal = il.DeclareLocal(_types.Exception);

        // Labels
        var notFoundLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // If registry is null, go to not found
        il.Emit(OpCodes.Ldsfld, runtime.ModuleRegistry);
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        // Func<object?> factory;
        // if (_moduleRegistry.TryGetValue(specifier, out factory))
        il.Emit(OpCodes.Ldsfld, runtime.ModuleRegistry);
        il.Emit(OpCodes.Ldarg_0); // specifier
        il.Emit(OpCodes.Ldloca, factoryLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        // Found - invoke factory and return Task.FromResult(factory())
        il.Emit(OpCodes.Ldloc, factoryLocal);
        il.Emit(OpCodes.Callvirt, funcType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Call, taskType.GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Br, returnLabel);

        // Not found - return rejected task
        il.MarkLabel(notFoundLabel);

        // var tcs = new TaskCompletionSource<object?>();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(tcsType));
        il.Emit(OpCodes.Stloc, tcsLocal);

        // Build error message: $"Dynamic import of '{specifier}' failed: module was not pre-compiled."
        il.Emit(OpCodes.Ldstr, "Dynamic import of '");
        il.Emit(OpCodes.Ldarg_0); // specifier
        il.Emit(OpCodes.Ldstr, "' failed: module was not pre-compiled. Ensure all dynamically imported modules are statically discoverable (use string literals).");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));

        // var ex = new Exception(message);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));

        // tcs.SetException(ex);
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "SetException", _types.Exception));

        // return tcs.Task;
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }
}
