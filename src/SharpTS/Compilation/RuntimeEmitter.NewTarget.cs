using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTSFunctionInvokeConstructed(TypeBuilder owner, EmittedFunctionValueRuntime functions,
        EmittedFunctionAttributesRuntime attributes, FieldInfo? currentArguments, FieldBuilder methodField,
        FieldBuilder targetField, FieldBuilder expectsThisField, FieldBuilder currentThisField,
        MethodBuilder adjustArguments, Action<ILGenerator, LocalBuilder> convertArguments,
        MethodInfo? nativeGenericResolver, MethodInfo? nativeCoreLookup)
    {
        var method = owner.DefineMethod("InvokeConstructed", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.ObjectArray, _types.Object]);
        functions.InvokeConstructed = method;
        var il = method.GetILGenerator();
        var markers = il.DeclareLocal(_types.ObjectArray);
        var core = il.DeclareLocal(_types.MethodInfo);
        var ordinary = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, methodField);
        il.Emit(OpCodes.Ldtoken, attributes.NewTargetCoreType); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(MemberInfo), "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Stloc, markers);
        il.Emit(OpCodes.Ldloc, markers); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Brfalse, ordinary);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, methodField);
        if (nativeCoreLookup is null)
            il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(MemberInfo), "DeclaringType")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, markers); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, attributes.NewTargetCoreType); il.Emit(OpCodes.Ldfld, attributes.NewTargetCoreName);
        if (nativeCoreLookup is null)
        {
            il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        }
        else il.Emit(OpCodes.Call, nativeCoreLookup);
        il.Emit(OpCodes.Stloc, core);
        var notGeneric = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, core);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsGenericMethodDefinition")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notGeneric);
        il.Emit(OpCodes.Ldloc, core);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, methodField);
        if (nativeGenericResolver is null)
        {
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.MethodInfo, "GetGenericArguments"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "MakeGenericMethod", _types.MakeArrayType(_types.Type)));
        }
        else
        {
            if (!nativeGenericResolver.IsStatic || nativeGenericResolver.IsGenericMethod ||
                nativeGenericResolver.ReturnType != _types.MethodInfo ||
                !nativeGenericResolver.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([_types.MethodInfo, _types.MethodInfo]))
                throw new InvalidOperationException("Native constructed generic resolution requires an exact static (core, entry) MethodInfo helper.");
            il.Emit(OpCodes.Call, nativeGenericResolver);
        }
        il.Emit(OpCodes.Stloc, core);
        il.MarkLabel(notGeneric);

        var effective = il.DeclareLocal(_types.ObjectArray);
        var noReceiver = il.DefineLabel(); var prepared = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, expectsThisField); il.Emit(OpCodes.Brfalse, noReceiver);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object); il.Emit(OpCodes.Stloc, effective);
        il.Emit(OpCodes.Ldloc, effective); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, effective); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", _types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32));
        il.Emit(OpCodes.Br, prepared);
        il.MarkLabel(noReceiver); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stloc, effective);
        il.MarkLabel(prepared);
        var adjusted = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, effective); il.Emit(OpCodes.Call, adjustArguments); il.Emit(OpCodes.Stloc, adjusted);
        convertArguments(il, adjusted);
        // Shape the source arguments before adding the semantic-only final slot.
        var complete = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldloc, adjusted); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object); il.Emit(OpCodes.Stloc, complete);
        il.Emit(OpCodes.Ldloc, adjusted); il.Emit(OpCodes.Ldloc, complete);
        il.Emit(OpCodes.Ldloc, adjusted); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", _types.ArrayType, _types.ArrayType, _types.Int32));
        il.Emit(OpCodes.Ldloc, complete); il.Emit(OpCodes.Ldloc, adjusted); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stelem_Ref);
        if (currentArguments != null) { il.Emit(OpCodes.Ldloc, effective); il.Emit(OpCodes.Stsfld, currentArguments); }
        var target = il.DeclareLocal(_types.Object);
        var instance = il.DefineLabel(); var haveTarget = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, core); il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsStatic")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, instance); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Br, haveTarget);
        il.MarkLabel(instance); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, targetField);
        il.MarkLabel(haveTarget); il.Emit(OpCodes.Stloc, target);
        var previousThis = il.DeclareLocal(_types.Object); var result = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, currentThisField); il.Emit(OpCodes.Stloc, previousThis);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stsfld, currentThisField);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, core); il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodInvoker, "Create", _types.MethodBase));
        il.Emit(OpCodes.Ldloc, target); il.Emit(OpCodes.Ldloc, complete);
        var span = _types.MakeGenericType(typeof(Span<>), _types.Object);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(span, _types.ObjectArray)!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInvoker, "Invoke", _types.Object, span)); il.Emit(OpCodes.Stloc, result);
        il.BeginFinallyBlock(); il.Emit(OpCodes.Ldloc, previousThis); il.Emit(OpCodes.Stsfld, currentThisField); il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinary);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, functions.InvokeWithThis); il.Emit(OpCodes.Ret);
    }

    private void EmitNewTargetCoreAttribute(ModuleBuilder module, EmittedFunctionAttributesRuntime attributes)
    {
        var type = module.DefineType("$NewTargetCore", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        var name = type.DefineField("Name", _types.String, FieldAttributes.Public | FieldAttributes.InitOnly);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.String]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, name); il.Emit(OpCodes.Ret);
        attributes.NewTargetCoreType = type; attributes.NewTargetCoreCtor = constructor; attributes.NewTargetCoreName = name;
        type.CreateType();
    }
}
