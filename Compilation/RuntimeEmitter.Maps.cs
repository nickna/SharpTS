using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitMapMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateMap(typeBuilder, runtime);
        EmitCreateMapFromEntries(typeBuilder, runtime);
        EmitMapSize(typeBuilder, runtime);
        EmitMapGet(typeBuilder, runtime);
        EmitMapSet(typeBuilder, runtime);
        EmitMapHas(typeBuilder, runtime);
        EmitMapDelete(typeBuilder, runtime);
        EmitMapClear(typeBuilder, runtime);
        EmitMapKeys(typeBuilder, runtime);
        EmitMapValues(typeBuilder, runtime);
        EmitMapEntries(typeBuilder, runtime);
        EmitMapForEach(typeBuilder, runtime);
    }

    private void EmitCreateMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateMap = method;

        var il = method.GetILGenerator();
        // Call RuntimeTypes.CreateMap()
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateMap")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateMapFromEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateMapFromEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateMapFromEntries = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("CreateMapFromEntries")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapSize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.MapSize = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapSize")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.MapGet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapGet")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.MapSet = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapSet")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.MapHas = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapHas")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.MapDelete = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapDelete")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapClear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapClear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.MapClear = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapClear")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapKeys = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapKeys")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapValues = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapValues")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapEntries = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapEntries")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.MapForEach = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("MapForEach")!);
        il.Emit(OpCodes.Ret);
    }
}

