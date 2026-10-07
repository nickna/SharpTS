using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitRuntimeParentBodies(EmittedRuntime runtime)
    {
        var definitions = runtime.ClassDefinitions;
        var il = definitions.ValidateParent.GetILGenerator();
        var supported = il.DefineLabel();
        var parentType = il.DeclareLocal(_types.Type);
        var plainType = il.DefineLabel(); var inspectType = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, definitions.Type); il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, plainType); il.Emit(OpCodes.Ldfld, definitions.Template); il.Emit(OpCodes.Br, inspectType);
        il.MarkLabel(plainType); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, _types.Type);
        il.MarkLabel(inspectType); il.Emit(OpCodes.Stloc, parentType);
        var unsupported = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, parentType); il.Emit(OpCodes.Brfalse, unsupported);
        il.Emit(OpCodes.Ldloc, parentType); il.Emit(OpCodes.Ldstr, "$InitializeReceiver");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Brtrue, supported);
        il.MarkLabel(unsupported);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Runtime superclass must be an ordinary user class constructor");
        il.MarkLabel(supported); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);

        il = definitions.GetParent.GetILGenerator();
        var staticOwner = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, definitions.Type); il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, staticOwner); il.Emit(OpCodes.Ldfld, definitions.Parent); il.Emit(OpCodes.Ret);
        il.MarkLabel(staticOwner); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType")!.GetGetMethod()!); il.Emit(OpCodes.Ret);

        il = definitions.ReadArgument.GetILGenerator();
        var missing = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, missing); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Ret);
        il.MarkLabel(missing); il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Ret);

        il = definitions.InitializeReceiver.GetILGenerator();
        var nothing = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, nothing);
        var template = il.DeclareLocal(_types.Type);
        var useType = il.DefineLabel(); var haveType = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, definitions.Type); il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, useType); il.Emit(OpCodes.Ldfld, definitions.Template); il.Emit(OpCodes.Br, haveType);
        il.MarkLabel(useType); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _types.Type);
        il.MarkLabel(haveType); il.Emit(OpCodes.Stloc, template);
        var adapter = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, template); il.Emit(OpCodes.Ldstr, "$InitializeReceiver");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String)); il.Emit(OpCodes.Stloc, adapter);
        var haveAdapter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, adapter); il.Emit(OpCodes.Brtrue, haveAdapter);
        il.Emit(OpCodes.Ldloc, template); il.Emit(OpCodes.Ldtoken, _types.Object); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Beq, nothing);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Runtime superclass initialization requires an ordinary user class constructor");
        il.MarkLabel(haveAdapter);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, adapter); il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray)); il.Emit(OpCodes.Pop);
        il.BeginCatchBlock(typeof(TargetInvocationException));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException")!.GetGetMethod()!);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock(); il.MarkLabel(nothing); il.Emit(OpCodes.Ret);

        il = definitions.ReadPrototypeProperty.GetILGenerator();
        var prototype = il.DeclareLocal(_types.Object); var descriptor = il.DeclareLocal(runtime.DescriptorStorage.DescriptorType);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, prototype);
        var loop = il.DefineLabel(); var next = il.DefineLabel(); var absent = il.DefineLabel(); var data = il.DefineLabel();
        il.MarkLabel(loop); il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, data);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); il.Emit(OpCodes.Call, runtime.Invocation.Method); il.Emit(OpCodes.Ret);
        il.MarkLabel(data); il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!); il.Emit(OpCodes.Ret);
        il.MarkLabel(next); il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Call, runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototype); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(absent); il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Ret);

        il = definitions.ReadSuper.GetILGenerator();
        var instanceSuper = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Brfalse, instanceSuper);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, definitions.GetParent); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property); il.Emit(OpCodes.Ret);
        il.MarkLabel(instanceSuper);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, definitions.GetParent);
        il.Emit(OpCodes.Ldstr, "prototype"); il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, definitions.ReadPrototypeProperty); il.Emit(OpCodes.Ret);
    }
}
