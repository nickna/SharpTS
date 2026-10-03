using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct StringSymbolDispatchInputs(
        Type SymbolType, Type UndefinedType, MethodInfo TypeOf, Type? RegExpType,
        MethodInfo GetSymbolStorage, MethodInfo GetIndex, MethodInfo InvokeMethod);

    /// <summary>
    /// Implements the shared GetMethod(object, wellKnownSymbol) portion of the
    /// String match/search/replace/split protocols. Every non-nullish candidate
    /// consults its symbol properties, including inherited primitive prototype
    /// properties, and invokes an existing method with the original candidate
    /// as <c>this</c>.
    /// </summary>
    private void EmitStringTryInvokeSymbolMethod(
        TypeBuilder typeBuilder, EmittedStringRuntime strings, StringSymbolDispatchInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "StringTryInvokeSymbolMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, inputs.SymbolType, _types.ObjectArray, _types.Boolean.MakeByRefType(), _types.Boolean.MakeByRefType()]);
        strings.TryInvokeSymbolMethod = method;

        var il = method.GetILGenerator();
        var noMethodLabel = il.DefineLabel();
        var callableLabel = il.DefineLabel();
        var methodLocal = il.DeclareLocal(_types.Object);

        // invoked = false
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I1);
        // hasOwnNativeSymbol = false. Callers use this to distinguish an
        // inherited native RegExp protocol from an own nullish override.
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I1);

        // GetMethod performs ordinary [[Get]] for every non-nullish candidate.
        // GetIndex resolves primitive prototypes while retaining the original
        // receiver for accessor and method invocation.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, noMethodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, noMethodLabel);
        // Record whether a native RegExp supplied an own symbol property.
        // Regardless of that result, continue through ordinary GetIndex below:
        // it resolves the current RegExp.prototype descriptor before falling
        // back to the intrinsic. This makes prototype replacements observable
        // to String.prototype.match/search/replace/split as required by GetMethod.
        if (inputs.RegExpType is not null)
        {
            var notNativeRegExpLabel = il.DefineLabel();
            var afterOwnRegExpSymbolLabel = il.DefineLabel();
            var ownSymbolsLocal = il.DeclareLocal(_types.DictionaryObjectObject);
            var ownSymbolValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, inputs.RegExpType);
            il.Emit(OpCodes.Brfalse, notNativeRegExpLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, inputs.GetSymbolStorage);
            il.Emit(OpCodes.Stloc, ownSymbolsLocal);
            il.Emit(OpCodes.Ldloc, ownSymbolsLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, ownSymbolValueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brfalse, afterOwnRegExpSymbolLabel);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stind_I1);
            il.MarkLabel(afterOwnRegExpSymbolLabel);
            il.MarkLabel(notNativeRegExpLabel);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.GetIndex);
        il.Emit(OpCodes.Stloc, methodLocal);

        // undefined and null both mean that the built-in fallback continues.
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, noMethodLabel);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
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
        il.Emit(OpCodes.Call, inputs.InvokeMethod);
        il.Emit(OpCodes.Ret);
    }
}
