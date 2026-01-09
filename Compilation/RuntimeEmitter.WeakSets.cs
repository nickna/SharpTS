using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakSetMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateWeakSet(typeBuilder, runtime);
        EmitWeakSetAdd(typeBuilder, runtime);
        EmitWeakSetHas(typeBuilder, runtime);
        EmitWeakSetDelete(typeBuilder, runtime);
    }

    private void EmitCreateWeakSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateWeakSet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateWeakSet")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetAdd = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakSetAdd")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetHas = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakSetHas")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetDelete = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakSetDelete")!);
        il.Emit(OpCodes.Ret);
    }
}

