using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Async function and Promise emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits a call to an async function.
    /// The async method returns Task&lt;object?&gt; which we synchronously await.
    /// </summary>
    private void EmitAsyncFunctionCall(MethodInfo asyncMethod, List<Expr> arguments)
    {
        var paramCount = asyncMethod.GetParameters().Length;

        // Emit provided arguments
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EmitBoxIfNeeded(arg);
        }

        // Pad with nulls for missing arguments (for default parameters)
        for (int i = arguments.Count; i < paramCount; i++)
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Call the async method (returns Task<object?> or Task)
        IL.Emit(OpCodes.Call, asyncMethod);

        // Synchronously wait for the result: task.GetAwaiter().GetResult()
        Type returnType = asyncMethod.ReturnType;

        if (returnType == _ctx.Types.Task || returnType.FullName == "System.Threading.Tasks.Task")
        {
            // Task (no return value) - just wait for completion
            var getAwaiter = _ctx.Types.GetMethod(_ctx.Types.Task, "GetAwaiter");
            var awaiterType = _ctx.Types.TaskAwaiter;
            var getResult = _ctx.Types.GetMethod(awaiterType, "GetResult");

            // Store task in local to call methods on it
            var taskLocal = IL.DeclareLocal(_ctx.Types.Task);
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            // void async functions return null
            IL.Emit(OpCodes.Ldnull);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1")
        {
            // Task<T> - wait and get result
            var getAwaiter = returnType.GetMethod("GetAwaiter")!;
            var awaiterType = getAwaiter.ReturnType;
            var getResult = awaiterType.GetMethod("GetResult")!;

            // Store task in local to call methods on it
            var taskLocal = IL.DeclareLocal(returnType);
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            // Box if necessary (result might be value type)
            Type resultType = returnType.GetGenericArguments()[0];
            if (resultType.IsValueType)
            {
                IL.Emit(OpCodes.Box, resultType);
            }
        }
        else
        {
            // Non-task return type (shouldn't happen for async methods)
            // Just leave the result on the stack
        }
    }

    private void EmitPromiseStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "resolve":
                // Promise.resolve(value?)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseResolve);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "reject":
                // Promise.reject(reason)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseReject);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "all":
                // Promise.all(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAll);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "race":
                // Promise.race(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseRace);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "allSettled":
                // Promise.allSettled(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAllSettled);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "any":
                // Promise.any(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAny);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            default:
                IL.Emit(OpCodes.Ldnull);
                return;
        }
    }

    /// <summary>
    /// Emits code to await a Task<object?> and get its result.
    /// </summary>
    private void EmitAwaitTask()
    {
        // Store task in local
        var taskOfObject = _ctx.Types.TaskOfObject;
        var taskLocal = IL.DeclareLocal(taskOfObject);
        IL.Emit(OpCodes.Stloc, taskLocal);
        IL.Emit(OpCodes.Ldloca, taskLocal);

        // Call GetAwaiter()
        var getAwaiter = _ctx.Types.GetMethod(taskOfObject, "GetAwaiter");
        IL.Emit(OpCodes.Call, getAwaiter);

        // Store awaiter and call GetResult()
        var awaiterType = _ctx.Types.TaskAwaiterOfObject;
        var awaiterLocal = IL.DeclareLocal(awaiterType);
        IL.Emit(OpCodes.Stloc, awaiterLocal);
        IL.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResult = _ctx.Types.GetMethod(awaiterType, "GetResult");
        IL.Emit(OpCodes.Call, getResult);
    }
}
