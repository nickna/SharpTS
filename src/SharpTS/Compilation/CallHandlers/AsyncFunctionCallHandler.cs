using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles calls to async functions by resolving them from AsyncMethods registry
/// and synchronously awaiting the result.
/// </summary>
public class AsyncFunctionCallHandler : ICallHandler
{
    public int Priority => 80;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable asyncVar)
            return false;

        var ctx = emitter.Context;
        var types = ctx.Types;
        if (ctx.AsyncMethods?.TryGetValue(asyncVar.Name.Lexeme, out var asyncMethod) != true || asyncMethod == null)
            return false;

        var il = emitter.IL;
        var asyncMethodParams = asyncMethod.GetParameters();
        var paramCount = asyncMethodParams.Length;

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            emitter.EmitExpression(call.Arguments[i]);
            if (i < asyncMethodParams.Length)
                emitter.EmitConversionForParameter(call.Arguments[i], asyncMethodParams[i].ParameterType);
            else
                emitter.EmitBoxIfNeeded(call.Arguments[i]);
        }

        for (int i = call.Arguments.Count; i < paramCount; i++)
            emitter.EmitOmittedArgument(asyncMethodParams[i].ParameterType);

        il.Emit(OpCodes.Call, asyncMethod);

        // Synchronously wait: task.GetAwaiter().GetResult()
        var returnType = asyncMethod.ReturnType;
        if (returnType.FullName == "System.Threading.Tasks.Task" || (!returnType.IsGenericType && typeof(System.Threading.Tasks.Task).IsAssignableFrom(returnType)))
        {
            var getAwaiter = types.GetMethod(returnType, "GetAwaiter");
            var awaiterType = getAwaiter.ReturnType;
            var getResult = types.GetMethod(awaiterType, "GetResult");

            var taskLocal = il.DeclareLocal(returnType);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloca, taskLocal);
            il.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = il.DeclareLocal(awaiterType);
            il.Emit(OpCodes.Stloc, awaiterLocal);
            il.Emit(OpCodes.Ldloca, awaiterLocal);
            il.Emit(OpCodes.Call, getResult);

            il.Emit(OpCodes.Ldnull);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1")
        {
            var getAwaiter = types.GetMethod(returnType, "GetAwaiter");
            var awaiterType = getAwaiter.ReturnType;
            var getResult = types.GetMethod(awaiterType, "GetResult");

            var taskLocal = il.DeclareLocal(returnType);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloca, taskLocal);
            il.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = il.DeclareLocal(awaiterType);
            il.Emit(OpCodes.Stloc, awaiterLocal);
            il.Emit(OpCodes.Ldloca, awaiterLocal);
            il.Emit(OpCodes.Call, getResult);

            var resultType = returnType.GetGenericArguments()[0];
            if (resultType.IsValueType)
                il.Emit(OpCodes.Box, resultType);
        }

        emitter.SetStackUnknown();
        return true;
    }
}
