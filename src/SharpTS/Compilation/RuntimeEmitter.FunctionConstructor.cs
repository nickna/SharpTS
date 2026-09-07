using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitFunctionConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // These are ordinary sloppy functions, with the same receiver binding
        // as guest functions. Keep them off $Runtime, whose methods are branded
        // as non-constructible built-ins by IsConstructor/GetFunctionMethod.
        var emptyBody = typeBuilder.DefineMethod("EmptyFunctionBody",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        emptyBody.DefineParameter(1, ParameterAttributes.None, "__this");
        var emptyIL = emptyBody.GetILGenerator();
        emptyIL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        emptyIL.Emit(OpCodes.Ret);

        var returnThisBody = typeBuilder.DefineMethod("ReturnThisFunctionBody",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        returnThisBody.DefineParameter(1, ParameterAttributes.None, "__this");
        var bodyIL = returnThisBody.GetILGenerator();
        var useGlobal = bodyIL.DefineLabel();
        bodyIL.Emit(OpCodes.Ldarg_0);
        bodyIL.Emit(OpCodes.Brfalse, useGlobal);
        bodyIL.Emit(OpCodes.Ldarg_0);
        bodyIL.Emit(OpCodes.Isinst, runtime.UndefinedType);
        bodyIL.Emit(OpCodes.Brtrue, useGlobal);
        bodyIL.Emit(OpCodes.Ldarg_0);
        bodyIL.Emit(OpCodes.Ret);
        bodyIL.MarkLabel(useGlobal);
        bodyIL.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        bodyIL.Emit(OpCodes.Ret);

        var method = typeBuilder.DefineMethod("ConstructFunction",
            MethodAttributes.Public | MethodAttributes.Static, typeBuilder, [_types.ObjectArray]);
        runtime.FunctionConstructor = method;
        var il = method.GetILGenerator();
        var body = il.DeclareLocal(_types.String);
        var empty = il.DefineLabel();
        var unsupported = il.DefineLabel();
        var create = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, empty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, unsupported);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, body);
        il.Emit(OpCodes.Ldloc, body);
        il.Emit(OpCodes.Brfalse, unsupported);
        il.Emit(OpCodes.Ldloc, body);
        il.Emit(OpCodes.Ldstr, FunctionConstructorContract.ReturnThisPattern);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.NonBacktracking);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Regex), "IsMatch", _types.String, _types.String, typeof(RegexOptions)));
        il.Emit(OpCodes.Brfalse, unsupported);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldtoken, returnThisBody);
        il.Emit(OpCodes.Br, create);

        il.MarkLabel(unsupported);
        il.Emit(OpCodes.Ldstr, FunctionConstructorContract.UnsupportedSourceMessage);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String)!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(empty);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldtoken, emptyBody);
        il.MarkLabel(create);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(MethodBase), "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Ldstr, "anonymous");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Ret);
    }
}
