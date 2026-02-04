using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: private static Dictionary&lt;object, object?&gt; GetSymbolDict(object obj)
    /// Returns the symbol dictionary for an object from the ConditionalWeakTable.
    /// </summary>
    private void EmitGetSymbolDict(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder symbolStorageField)
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
    private void EmitIsSymbol(TypeBuilder typeBuilder, EmittedRuntime runtime)
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

    /// <summary>
    /// Emits: public static void DisposeResource(object resource, object disposeSymbol)
    /// Disposes a resource using Symbol.dispose if available.
    /// </summary>
    private void EmitDisposeResource(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DisposeResource",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.DisposeResource = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();
        var noDisposeLabel = il.DefineLabel();
        var hasDisposeLabel = il.DefineLabel();
        var invokeLabel = il.DefineLabel();

        var disposeMethodLocal = il.DeclareLocal(_types.Object); // local 0: dispose method
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject); // local 1: symbol dict

        // if (resource == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // var symbolDict = GetSymbolDict(resource);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);

        // if (!symbolDict.TryGetValue(disposeSymbol, out disposeMethod)) return;
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldarg_1); // disposeSymbol
        il.Emit(OpCodes.Ldloca, disposeMethodLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, noDisposeLabel);

        // if (disposeMethod == null) return;
        il.Emit(OpCodes.Ldloc, disposeMethodLocal);
        il.Emit(OpCodes.Brfalse, noDisposeLabel);

        // Invoke the dispose method with resource as the context
        // Use InvokeMethodValue(thisObj, method, args) which handles various function types
        il.Emit(OpCodes.Ldarg_0); // resource (for 'this' context)
        il.Emit(OpCodes.Ldloc, disposeMethodLocal);
        il.Emit(OpCodes.Ldc_I4_0); // no additional args
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop); // Discard return value
        il.Emit(OpCodes.Br, doneLabel);

        // No dispose method - check for .NET IDisposable
        il.MarkLabel(noDisposeLabel);
        var notDisposableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(IDisposable));
        il.Emit(OpCodes.Brfalse, notDisposableLabel);

        // Call IDisposable.Dispose()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Callvirt, _types.DisposableDispose);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notDisposableLabel);
        // No disposal method - silently return (TypeScript allows this)

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }
}

