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
        var initializedReceiver = il.DeclareLocal(_types.Object);
        var initialized = il.DefineLabel();
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
        il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, initializedReceiver);
        il.BeginCatchBlock(typeof(TargetInvocationException));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException")!.GetGetMethod()!);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Br, initialized);
        il.MarkLabel(nothing); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, initializedReceiver);
        il.MarkLabel(initialized); il.Emit(OpCodes.Ldloc, initializedReceiver); il.Emit(OpCodes.Ret);

        il = definitions.ReadPrototypeProperty.GetILGenerator();
        var prototype = il.DeclareLocal(_types.Object);
        var descriptor = il.DeclareLocal(runtime.DescriptorStorage.DescriptorType);
        var getter = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, prototype);
        var loop = il.DefineLabel(); var next = il.DefineLabel(); var absent = il.DefineLabel(); var data = il.DefineLabel();
        il.MarkLabel(loop); il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, getter);
        il.Emit(OpCodes.Ldloc, getter); il.Emit(OpCodes.Brfalse, data);
        il.Emit(OpCodes.Ldloc, getter); il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType); il.Emit(OpCodes.Brtrue, absent);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, getter);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); il.Emit(OpCodes.Call, runtime.Invocation.Method); il.Emit(OpCodes.Ret);
        il.MarkLabel(data);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, absent);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!); il.Emit(OpCodes.Ret);
        il.MarkLabel(next);

        // Generated class prototypes keep assigned data in their dynamic store.
        // The public Fields compatibility view also synthesizes instance fields,
        // which do not belong to the prototype. Use its existing storage accessor.
        var dynamicStore = il.DeclareLocal(_types.MethodInfo);
        var fields = il.DeclareLocal(_types.DictionaryStringObject);
        var value = il.DeclareLocal(_types.Object);
        var ordinaryPrototype = il.DefineLabel();
        var nextPrototype = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "$EnsureFields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, dynamicStore);
        il.Emit(OpCodes.Ldloc, dynamicStore); il.Emit(OpCodes.Brfalse, ordinaryPrototype);
        il.Emit(OpCodes.Ldloc, dynamicStore); il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject); il.Emit(OpCodes.Stloc, fields);
        il.Emit(OpCodes.Ldloc, fields); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, nextPrototype);
        il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinaryPrototype);
        // Ordinary dictionary/object/proxy prototypes already implement their
        // receiver-aware descriptor lookup through Reflect.get.
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Reflect.Get); il.Emit(OpCodes.Ret);
        il.MarkLabel(nextPrototype);
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Call, runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototype); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(absent); il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Ret);

        il = definitions.ReadSuper.GetILGenerator();
        var instanceSuper = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Brfalse, instanceSuper);
        // The home constructor is lexical, while its internal prototype is
        // live. The separately supplied receiver remains the getter's this.
        var staticSuperBase = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, staticSuperBase);
        var hasStaticSuperBase = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, staticSuperBase); il.Emit(OpCodes.Brtrue, hasStaticSuperBase);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Cannot read properties of null super prototype");
        il.MarkLabel(hasStaticSuperBase);
        il.Emit(OpCodes.Ldloc, staticSuperBase); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, runtime.Reflect.Get); il.Emit(OpCodes.Ret);
        il.MarkLabel(instanceSuper);
        var superBase = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype"); il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Call, runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, superBase);
        var hasSuperBase = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, superBase); il.Emit(OpCodes.Brtrue, hasSuperBase);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Cannot read properties of null super prototype");
        il.MarkLabel(hasSuperBase);
        var symbolSuper = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Brfalse, symbolSuper);
        // String lookup retains the nominal prototype/instance-field boundary;
        // Symbol keys use the existing receiver-aware ordinary Get protocol.
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, superBase);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, definitions.ReadPrototypeProperty); il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolSuper);
        il.Emit(OpCodes.Ldloc, superBase); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, runtime.Reflect.Get); il.Emit(OpCodes.Ret);
    }
}
