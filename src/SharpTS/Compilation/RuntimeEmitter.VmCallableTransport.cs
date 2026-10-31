using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Invoke)
        EmitVmCallableTransport(TypeBuilder owner, EmittedRuntime runtime)
    {
        var vm = runtime.RequireVm();
        var callable = EmitTypeDefinitions.DefineType((ModuleBuilder)owner.Module, "$VmCallableTransport",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        var source = callable.DefineField("Source", _types.Object, FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = callable.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object]);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, source);
        il.Emit(OpCodes.Ret);
        var invoke = callable.DefineMethod("Invoke", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.ObjectArray]);
        invoke.DefineParameter(1, ParameterAttributes.None, "__this");
        invoke.DefineParameter(2, ParameterAttributes.None, "arguments").SetCustomAttribute(
            typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!, CustomAttributeEncoder.EmptyBlob);
        il = invoke.GetILGenerator();
        // The Core endpoint owns the actual callable/receiver/argument role and
        // re-enters its retained evaluator lease. This is not a CLR fallback.
        EmitVmBoundaryBody(il, vm, () => EmitVmCoreInvoke(il, "InvokeVmCallable",
            body => { body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldfld, source); },
            body => { body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Call, vm.RestoreValue); },
            body => body.Emit(OpCodes.Ldarg_2)), enterScope: false);
        callable.CreateType();
        return (callable, ctor, invoke);
    }

    private void EmitVmMappedValueReaders(EmittedRuntime runtime, FieldBuilder reverse)
    {
        var vm = runtime.RequireVm();
        var il = vm.IsMappedValue.GetILGenerator();
        var source = il.DeclareLocal(_types.Object);
        var absent = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldsfld, reverse);
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldsfld, reverse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, source);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTableObjectObject, "TryGetValue",
            _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, absent);
        // Ownerless facade Promises are caller-realm transport, not foreign
        // source values. Only the actual owner's receipt selects this protocol.
        EmitVmCoreInvoke(il, "IsVmValue", body => body.Emit(OpCodes.Ldloc, source));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(absent);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il = vm.ReadMappedProperty.GetILGenerator();
        EmitVmBoundaryBody(il, vm, () => EmitVmCoreInvoke(il, "ReadVmValueProperty",
            body => { body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, vm.RestoreValue); },
            body => body.Emit(OpCodes.Ldarg_1)), enterScope: false);
        il = vm.MappedPrototype.GetILGenerator();
        EmitVmBoundaryBody(il, vm, () => EmitVmCoreInvoke(il, "GetVmValuePrototype",
            body => { body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, vm.RestoreValue); }), enterScope: false);
    }
}
