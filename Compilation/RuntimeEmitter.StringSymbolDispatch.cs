using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Implements the shared GetMethod(object, wellKnownSymbol) portion of the
    /// String match/search/replace/split protocols. Primitive candidates must
    /// not consult their prototype symbol properties, while object candidates
    /// invoke an existing method with the candidate as <c>this</c>.
    /// </summary>
    private void EmitStringTryInvokeSymbolMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringTryInvokeSymbolMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, runtime.TSSymbolType, _types.ObjectArray, _types.Boolean.MakeByRefType()]);
        runtime.StringTryInvokeSymbolMethod = method;

        var il = method.GetILGenerator();
        var noMethodLabel = il.DefineLabel();
        var objectCandidateLabel = il.DefineLabel();
        var callableLabel = il.DefineLabel();
        var methodLocal = il.DeclareLocal(_types.Object);
        var typeOfLocal = il.DeclareLocal(_types.String);

        // invoked = false
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I1);

        // GetMethod is only observable for Objects. This deliberately excludes
        // Boolean/Number/String/Symbol/BigInt primitives.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, noMethodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noMethodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Stloc, typeOfLocal);
        il.Emit(OpCodes.Ldloc, typeOfLocal);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, objectCandidateLabel);
        il.Emit(OpCodes.Ldloc, typeOfLocal);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, noMethodLabel);

        il.MarkLabel(objectCandidateLabel);

        // Native RegExp objects keep using the mature compiled fast path when
        // they inherit the standard protocol method. Only an own override is
        // dispatched here. Ordinary objects still use full prototype lookup.
        if (_features.UsesRegExp)
        {
            var notNativeRegExpLabel = il.DefineLabel();
            var ownRegExpSymbolLabel = il.DefineLabel();
            var ownSymbolsLocal = il.DeclareLocal(_types.DictionaryObjectObject);
            var ownSymbolValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notNativeRegExpLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, ownSymbolsLocal);
            il.Emit(OpCodes.Ldloc, ownSymbolsLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, ownSymbolValueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brtrue, ownRegExpSymbolLabel);
            il.Emit(OpCodes.Br, noMethodLabel);
            il.MarkLabel(ownRegExpSymbolLabel);
            il.MarkLabel(notNativeRegExpLabel);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, methodLocal);

        // undefined and null both mean that the built-in fallback continues.
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, noMethodLabel);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, callableLabel);

        il.MarkLabel(noMethodLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callableLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
    }
}
