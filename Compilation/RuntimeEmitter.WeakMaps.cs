using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitWeakMapMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateWeakMap(typeBuilder, runtime);
        EmitWeakMapGet(typeBuilder, runtime);
        EmitWeakMapSet(typeBuilder, runtime);
        EmitWeakMapHas(typeBuilder, runtime);
        EmitWeakMapDelete(typeBuilder, runtime);
    }

    private static void EmitCreateWeakMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateWeakMap = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateWeakMap")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWeakMapGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapGet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakMapGet")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWeakMapSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.WeakMapSet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakMapSet")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWeakMapHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapHas = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakMapHas")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWeakMapDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapDelete = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("WeakMapDelete")!);
        il.Emit(OpCodes.Ret);
    }
}
