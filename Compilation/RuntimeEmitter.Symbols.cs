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
        var symbolDictType = _types.DictionaryObjectObject;
        var symbolStorageType = _types.MakeGenericType(_types.ConditionalWeakTableOpen, _types.Object, symbolDictType);

        var method = typeBuilder.DefineMethod(
            "GetSymbolDict",
            MethodAttributes.Private | MethodAttributes.Static,
            symbolDictType,
            [_types.Object]
        );
        runtime.GetSymbolDictMethod = method;

        var il = method.GetILGenerator();

        // return _symbolStorage.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, symbolStorageField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, symbolStorageType.GetMethod("GetOrCreateValue", [_types.Object])!);
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
            _types.Boolean,
            [_types.Object]
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
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "$TSSymbol");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }
}
