using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Erased invocation policy bodies, emitted from exact supplied metadata.</summary>
internal static class ErasedSourceInvocationEmission
{
    internal const string ForeignMarkersMessage = "Generic methods carrying SharpTS source markers outside this guest's compiler-owned definitions are not supported in native guest output. Use --target dll or --target exe.";

    internal static void EmitLegacy(ILGenerator il, TypeProvider types, EmittedFunctionAttributesRuntime attributes)
    {
        var original = il.DefineLabel();
        var parameters = il.DeclareLocal(types.MakeArrayType(types.ParameterInfo));
        var genericParameters = il.DeclareLocal(types.MakeArrayType(types.Type));
        var arguments = il.DeclareLocal(types.MakeArrayType(types.Type));
        var constraints = il.DeclareLocal(types.MakeArrayType(types.Type));
        var argument = il.DeclareLocal(types.Type);
        var index = il.DeclareLocal(types.Int32);
        var containsGenericParameters = types.GetProperty(types.Type, "ContainsGenericParameters")!.GetGetMethod()!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.MethodInfo, "IsGenericMethodDefinition")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, original);
        // Positive source markers belong to this emitted output; host methods remain untouched.
        foreach (Type marker in new Type[] { attributes.FunctionNameType, attributes.FunctionLengthType, attributes.PadUndefinedType })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, marker);
            il.Emit(OpCodes.Call, types.GetMethod(types.Type, "GetTypeFromHandle", types.RuntimeTypeHandle));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, types.GetMethod(types.MethodInfo, "IsDefined", types.Type, types.Boolean));
            il.Emit(OpCodes.Brfalse, original);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.MethodInfo, "DeclaringType")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, containsGenericParameters);
        il.Emit(OpCodes.Brtrue, original);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.MethodInfo, "ReturnType")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, containsGenericParameters);
        il.Emit(OpCodes.Brtrue, original);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.MethodInfo, "GetParameters")!);
        il.Emit(OpCodes.Stloc, parameters);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var checkParameter = il.DefineLabel();
        var nextParameter = il.DefineLabel();
        il.Emit(OpCodes.Br, checkParameter);
        il.MarkLabel(nextParameter);
        il.Emit(OpCodes.Ldloc, parameters);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, containsGenericParameters);
        il.Emit(OpCodes.Brtrue, original);
        IncrementIndex();
        il.MarkLabel(checkParameter);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, parameters);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, nextParameter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.MethodInfo, "GetGenericArguments")!);
        il.Emit(OpCodes.Stloc, genericParameters);
        il.Emit(OpCodes.Ldloc, genericParameters);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, types.Type);
        il.Emit(OpCodes.Stloc, arguments);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var checkGenericParameter = il.DefineLabel();
        var nextGenericParameter = il.DefineLabel();
        var unconstrained = il.DefineLabel();
        var storeArgument = il.DefineLabel();
        il.Emit(OpCodes.Br, checkGenericParameter);
        il.MarkLabel(nextGenericParameter);
        il.Emit(OpCodes.Ldloc, genericParameters);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.Type, "GenericParameterAttributes")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, original);
        il.Emit(OpCodes.Ldloc, genericParameters);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.Type, "GetGenericParameterConstraints")!);
        il.Emit(OpCodes.Stloc, constraints);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, unconstrained);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, original);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argument);
        il.Emit(OpCodes.Ldloc, argument);
        il.Emit(OpCodes.Callvirt, containsGenericParameters);
        il.Emit(OpCodes.Brtrue, original);
        il.Emit(OpCodes.Br, storeArgument);
        il.MarkLabel(unconstrained);
        il.Emit(OpCodes.Ldtoken, types.Object);
        il.Emit(OpCodes.Call, types.GetMethod(types.Type, "GetTypeFromHandle", types.RuntimeTypeHandle));
        il.Emit(OpCodes.Stloc, argument);
        il.MarkLabel(storeArgument);
        il.Emit(OpCodes.Ldloc, arguments);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, argument);
        il.Emit(OpCodes.Stelem_Ref);
        IncrementIndex();
        il.MarkLabel(checkGenericParameter);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, arguments);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, nextGenericParameter);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, arguments);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.MethodInfo, "MakeGenericMethod", types.MakeArrayType(types.Type))!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(original);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        void IncrementIndex()
        {
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
        }
    }

    internal static void EmitNoOwnedGenerics(ILGenerator il, TypeProvider types,
        EmittedFunctionAttributesRuntime attributes, EmittedErrorRuntime errors)
    {
        var unchanged = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.MethodInfo, "IsGenericMethodDefinition")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, unchanged);
        foreach (Type marker in new Type[] { attributes.FunctionNameType, attributes.FunctionLengthType, attributes.PadUndefinedType })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, marker);
            il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, types.GetMethod(types.MethodInfo, "IsDefined", types.Type, types.Boolean));
            il.Emit(OpCodes.Brfalse, unchanged);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignMarkersMessage);
        il.MarkLabel(unchanged);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}
