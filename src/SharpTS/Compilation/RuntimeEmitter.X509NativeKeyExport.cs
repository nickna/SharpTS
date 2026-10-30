using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitNativeX509KeyExportMember(ILGenerator il, TypeBuilder certificate, EmittedRuntime runtime)
    {
        // This is the actual declaration emitted by EmitTSKeyObjectClass, not a
        // name-based capability inferred from an arbitrary key's runtime Type.
        var export = runtime.RequireCrypto().KeyObjectExport;
        if (export.DeclaringType is not TypeBuilder owner || !ReferenceEquals(owner.Module, certificate.Module) ||
            !ReferenceEquals(export.Module, certificate.Module) || !owner.IsCreated() ||
            export.Name != "export" || !export.IsPublic || export.IsStatic || export.IsGenericMethod ||
            export.ReturnType != _types.Object ||
            !export.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.Object]))
            throw new InvalidOperationException("Native X509 key export requires the completed same-output KeyObject declaration.");

        var refused = il.DefineLabel();
        var admitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, refused);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type]));
        il.Emit(OpCodes.Brtrue, admitted);
        il.MarkLabel(refused);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Native X509 verification requires this guest's own KeyObject. Use --target dll or --target exe for other CLR key carriers.");
        il.MarkLabel(admitted);
        il.Emit(OpCodes.Ldtoken, export);
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(MethodBase), "GetMethodFromHandle",
            [typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)]));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
    }
}
