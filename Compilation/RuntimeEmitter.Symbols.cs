using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: private static Dictionary&lt;object, object?&gt; GetSymbolDict(object obj)
    /// Returns the symbol dictionary for an object from the ConditionalWeakTable.
    /// </summary>
    private static void EmitGetSymbolDict(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder symbolStorageField)
    {
        var symbolDictType = typeof(Dictionary<object, object?>);
        var symbolStorageType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), symbolDictType);

        var method = typeBuilder.DefineMethod(
            "GetSymbolDict",
            MethodAttributes.Private | MethodAttributes.Static,
            symbolDictType,
            [typeof(object)]
        );
        runtime.GetSymbolDictMethod = method;

        var il = method.GetILGenerator();

        // return _symbolStorage.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, symbolStorageField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, symbolStorageType.GetMethod("GetOrCreateValue", [typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool IsSymbol(object obj)
    /// Returns true if the object is a TSSymbol.
    /// </summary>
    private static void EmitIsSymbol(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsSymbol",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsSymbolMethod = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (obj == null) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return obj.GetType().Name == "$TSSymbol";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "$TSSymbol");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }
}
