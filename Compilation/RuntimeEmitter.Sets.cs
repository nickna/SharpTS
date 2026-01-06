using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitSetMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateSet(typeBuilder, runtime);
        EmitCreateSetFromArray(typeBuilder, runtime);
        EmitSetSize(typeBuilder, runtime);
        EmitSetAdd(typeBuilder, runtime);
        EmitSetHas(typeBuilder, runtime);
        EmitSetDelete(typeBuilder, runtime);
        EmitSetClear(typeBuilder, runtime);
        EmitSetKeys(typeBuilder, runtime);
        EmitSetValues(typeBuilder, runtime);
        EmitSetEntries(typeBuilder, runtime);
        EmitSetForEach(typeBuilder, runtime);
    }

    private static void EmitCreateSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateSet",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            Type.EmptyTypes
        );
        runtime.CreateSet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateSet")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateSetFromArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateSetFromArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.CreateSetFromArray = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateSetFromArray")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetSize",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(object)]
        );
        runtime.SetSize = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetSize")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.SetAdd = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetAdd")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetHas",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.SetHas = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetHas")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.SetDelete = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetDelete")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetClear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetClear",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object)]
        );
        runtime.SetClear = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetClear")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.SetKeys = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetKeys")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.SetValues = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetValues")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.SetEntries = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetEntries")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object), typeof(object)]
        );
        runtime.SetForEach = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("SetForEach")!);
        il.Emit(OpCodes.Ret);
    }
}
