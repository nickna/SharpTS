using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitArrayPrototypeMethodInstaller(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodInfo setItem)
    {
        var method = typeBuilder.DefineMethod("_ArrayPrototypeInstallMethod",
            MethodAttributes.Private | MethodAttributes.Static, _types.Void,
            [_types.DictionaryStringObject, _types.String, _types.MethodInfo, _types.Int32]);
        var il = method.GetILGenerator();
        var wrapper = il.DeclareLocal(_types.Object);
        var descriptor = il.DeclareLocal(runtime.DescriptorStorage.DescriptorType);

        // Use the same declaration cache as ordinary property lookup, so the
        // dictionary entry and descriptor share the function's identity and
        // the JavaScript name/length supplied by this prototype slot.
        int parameterOffset = il.ILOffset;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_3);
        int factoryOffset = il.ILOffset;
        if (_nativeUnionParameters is { } conversion && conversion.MatchesOriginalFactory(runtime.FunctionConstruction.GetOrCreate))
            factoryOffset = conversion.EmitArrayInstallerFactory(il, method, parameterOffset);
        else il.Emit(OpCodes.Call, runtime.FunctionConstruction.GetOrCreate);
        il.Emit(OpCodes.Stloc, wrapper);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Callvirt, setItem);

        // Built-in data descriptors remain writable/configurable and become
        // non-enumerable, matching the per-slot installation used previously.
        il.Emit(OpCodes.Newobj, runtime.DescriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        _nativeUnionParameters?.RecordArrayInstaller(method, il, parameterOffset, factoryOffset);
        return method;
    }
}
