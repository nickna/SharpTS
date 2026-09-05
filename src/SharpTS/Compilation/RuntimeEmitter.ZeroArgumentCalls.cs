using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitInvokeMethodValue0(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("InvokeMethodValue0",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.InvokeMethodValue0 = method;
        var il = method.GetILGenerator();
        EmitStackGuard(il, runtime);
        var fallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(fallback);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        EmitEmptyArguments(il);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSFunctionInvokeWithThis0(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder expectsThis, FieldBuilder parameterCount, FieldBuilder capturesArguments,
        FieldBuilder needsConversion, FieldBuilder invoker, FieldBuilder methodInfo, FieldBuilder target)
    {
        var method = typeBuilder.DefineMethod("InvokeWithThis0", MethodAttributes.Public,
            _types.Object, [_types.Object]);
        runtime.TSFunctionInvokeWithThis0 = method;
        var il = method.GetILGenerator();
        var fallback = il.DefineLabel();
        // Metadata is cached when the function is constructed. Only a single
        // synthetic object __this parameter can bypass argument adjustment.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, expectsThis);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, parameterCount);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, fallback);
        foreach (var field in new[] { capturesArguments, needsConversion })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Brtrue, fallback);
        }
        // A null receiver has special bound-target handling for static built-in
        // helpers in InvokeWithThis. Keep that behavior on the existing path.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, fallback);
        var instanceTarget = il.DefineLabel();
        var invoke = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, invoker);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, methodInfo);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsStatic")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, instanceTarget);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, invoke);
        il.MarkLabel(instanceTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, target);
        il.MarkLabel(invoke);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInvoker, "Invoke", _types.Object, _types.Object));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        EmitEmptyArguments(il);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);
    }

    private void EmitEmptyArguments(ILGenerator il) => il.Emit(OpCodes.Call,
        EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
}
