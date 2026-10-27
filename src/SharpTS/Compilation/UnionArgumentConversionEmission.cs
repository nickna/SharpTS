using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Union conversion bodies emitted from supplied policy and error metadata.</summary>
internal static class UnionArgumentConversionEmission
{
    internal const string ForeignUnionMessage = "Implicit conversion to foreign Union_ value types is not supported in native guest output without compiler-owned unions. Use --target dll or --target exe.";

    internal static void EmitLegacy(ILGenerator il, TypeProvider types) => Emit(il, types, null);

    internal static void EmitNoOwnedUnions(ILGenerator il, TypeProvider types, EmittedErrorRuntime errors) => Emit(il, types, errors);

    private static void Emit(ILGenerator il, TypeProvider types, EmittedErrorRuntime? errors)
    {
        // Locals: 0=ParameterInfo[], 1=int count, 2=int i, 3=Type paramType, 4=Type argType, 5=MethodInfo implicitOp
        var paramsLocal = il.DeclareLocal(typeof(ParameterInfo[]));
        var countLocal = il.DeclareLocal(typeof(int));
        var iLocal = il.DeclareLocal(typeof(int));
        var paramTypeLocal = il.DeclareLocal(types.Type);
        var argTypeLocal = il.DeclareLocal(types.Type);
        var implicitOpLocal = il.DeclareLocal(types.MethodInfo);

        // Reuse immutable signature metadata captured at wrapper construction.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // count = Math.Min(args.Length, params.Length)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, countLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopConditionLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

        il.Emit(OpCodes.Br, loopConditionLabel);

        il.MarkLabel(loopBodyLabel);

        // paramType = params[i].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(ParameterInfo).GetProperty("ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paramTypeLocal);

        // if (!paramType.IsValueType) continue
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.Type, "IsValueType")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // if (!paramType.Name.StartsWith("Union_")) continue
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, types.GetProperty(types.Type, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "Union_");
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // if (args[i] == null) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // argType = args[i].GetType()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, types.GetMethod(types.Object, "GetType")!);
        il.Emit(OpCodes.Stloc, argTypeLocal);

        // if (argType == paramType) continue
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, continueLabel);

        if (errors is not null)
        {
            GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignUnionMessage);
        }
        else
        {
            // implicitOp = paramType.GetMethod("op_Implicit", new Type[] { argType })
            il.Emit(OpCodes.Ldloc, paramTypeLocal);
            il.Emit(OpCodes.Ldstr, "op_Implicit");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, types.Type);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, argTypeLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, types.GetMethod(types.Type, "GetMethod", [typeof(string), typeof(Type[])])!);
            il.Emit(OpCodes.Stloc, implicitOpLocal);

            // if (implicitOp == null) continue
            il.Emit(OpCodes.Ldloc, implicitOpLocal);
            il.Emit(OpCodes.Brfalse, continueLabel);

            // args[i] = implicitOp.Invoke(null, new object[] { args[i] })
            il.Emit(OpCodes.Ldarg_1);   // args array (for stelem later)
            il.Emit(OpCodes.Ldloc, iLocal);  // index (for stelem later)
            il.Emit(OpCodes.Ldloc, implicitOpLocal);
            il.Emit(OpCodes.Ldnull);     // target (null for static)
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stelem_Ref); // args[i] = result
        }

        il.MarkLabel(continueLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopBodyLabel);

        il.Emit(OpCodes.Ret);
    }
}
