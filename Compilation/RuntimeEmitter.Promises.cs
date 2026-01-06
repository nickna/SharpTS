using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Promise static methods that delegate to RuntimeTypes.
    /// These methods return Task&lt;object?&gt; and are awaited by the compiled code.
    /// </summary>
    private static void EmitPromiseMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var taskType = typeof(Task<object?>);

        // Promise.resolve(value?) - simply wraps value in completed Task
        // IL equivalent: Task.FromResult<object?>(value)
        var resolve = typeBuilder.DefineMethod(
            "PromiseResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseResolve = resolve;
        {
            var il = resolve.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            // Call Task.FromResult<object?>(value)
            var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object));
            il.Emit(OpCodes.Call, fromResult);
            il.Emit(OpCodes.Ret);
        }

        // Promise.reject(reason) - creates a faulted Task
        // IL equivalent: Task.FromException<object?>(new Exception(reason?.ToString()))
        var reject = typeBuilder.DefineMethod(
            "PromiseReject",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseReject = reject;
        {
            var il = reject.GetILGenerator();
            // Create Exception from reason
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetMethod("ToString")!);
            var exceptionCtor = typeof(Exception).GetConstructor([typeof(string)])!;
            il.Emit(OpCodes.Newobj, exceptionCtor);
            // Call Task.FromException<object?>(exception)
            var fromException = typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!.MakeGenericMethod(typeof(object));
            il.Emit(OpCodes.Call, fromException);
            il.Emit(OpCodes.Ret);
        }

        // Promise.all(iterable) - complex async operation, emit helper that uses Task.WhenAll
        var all = typeBuilder.DefineMethod(
            "PromiseAll",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseAll = all;
        EmitPromiseAllBody(all.GetILGenerator(), runtime);

        // Promise.race(iterable) - complex async operation
        var race = typeBuilder.DefineMethod(
            "PromiseRace",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseRace = race;
        EmitPromiseRaceBody(race.GetILGenerator());

        // Promise.allSettled(iterable) - complex async operation
        var allSettled = typeBuilder.DefineMethod(
            "PromiseAllSettled",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseAllSettled = allSettled;
        EmitPromiseAllSettledBody(allSettled.GetILGenerator(), runtime);

        // Promise.any(iterable) - complex async operation
        var any = typeBuilder.DefineMethod(
            "PromiseAny",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [typeof(object)]
        );
        runtime.PromiseAny = any;
        EmitPromiseAnyBody(any.GetILGenerator());

        // Promise.prototype.then/catch/finally - stub methods (not fully supported in compiled mode)
        EmitPromiseInstanceStubs(typeBuilder, runtime, taskType);
    }

    /// <summary>
    /// Emits Promise.all body - since we can't easily emit proper async IL,
    /// we defer to the RuntimeTypes implementation which handles this correctly.
    /// The RuntimeTypes assembly will be available at runtime for this method.
    ///
    /// NOTE: This approach requires RuntimeTypes.PromiseAll to be available at runtime,
    /// which means compiled assemblies need SharpTS.dll to be present for Promise.all to work.
    /// For a fully standalone assembly, a more complex IL emission approach would be needed.
    /// </summary>
    private static void EmitPromiseAllBody(ILGenerator il, EmittedRuntime runtime)
    {
        // Just call RuntimeTypes.PromiseAll directly - this requires SharpTS.dll at runtime
        // but is the only way to get proper async behavior without emitting complex state machines
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("PromiseAll")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Promise.race body - defers to RuntimeTypes for proper async behavior.
    /// NOTE: Requires SharpTS.dll at runtime.
    /// </summary>
    private static void EmitPromiseRaceBody(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("PromiseRace")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Promise.allSettled body - defers to RuntimeTypes for proper async behavior.
    /// NOTE: Requires SharpTS.dll at runtime.
    /// </summary>
    private static void EmitPromiseAllSettledBody(ILGenerator il, EmittedRuntime runtime)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("PromiseAllSettled")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Promise.any body - defers to RuntimeTypes for proper async behavior.
    /// NOTE: Requires SharpTS.dll at runtime.
    /// </summary>
    private static void EmitPromiseAnyBody(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("PromiseAny")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits stub methods for Promise instance methods (then, catch, finally).
    /// These are not fully supported in compiled mode without more complex implementation.
    /// </summary>
    private static void EmitPromiseInstanceStubs(TypeBuilder typeBuilder, EmittedRuntime runtime, Type taskType)
    {
        // then - just returns the input task (no transformation)
        var then = typeBuilder.DefineMethod(
            "PromiseThen",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, typeof(object), typeof(object)]
        );
        runtime.PromiseThen = then;
        {
            var il = then.GetILGenerator();
            // Just return the input task - callbacks not implemented in compiled mode
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }

        // catch - just returns the input task
        var catchMethod = typeBuilder.DefineMethod(
            "PromiseCatch",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, typeof(object)]
        );
        runtime.PromiseCatch = catchMethod;
        {
            var il = catchMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }

        // finally - just returns the input task
        var finallyMethod = typeBuilder.DefineMethod(
            "PromiseFinally",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, typeof(object)]
        );
        runtime.PromiseFinally = finallyMethod;
        {
            var il = finallyMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }
    }
}
