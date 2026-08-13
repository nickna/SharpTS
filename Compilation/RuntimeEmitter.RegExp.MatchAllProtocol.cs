using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Fills the phase-1 declaration of RegExp.prototype[@@matchAll]. Emitting
    /// this body late makes dynamic SpeciesConstructor and ordinary property
    /// operations available to the public $RegExp wrapper.
    /// </summary>
    private void EmitRegExpSymbolMatchAllProtocol(EmittedRuntime runtime)
    {
        var il = runtime.RegExpSymbolMatchAllProtocol.GetILGenerator();
        var s = il.DeclareLocal(_types.String);
        var constructor = il.DeclareLocal(_types.Object);
        var species = il.DeclareLocal(_types.Object);
        var flags = il.DeclareLocal(_types.String);
        var matcher = il.DeclareLocal(_types.Object);
        var args = il.DeclareLocal(_types.ObjectArray);
        var lastIndexNumber = il.DeclareLocal(_types.Double);
        var lastIndex = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, s);

        // C = SpeciesConstructor(rx, %RegExp%).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, constructor);
        var defaultSpecies = il.DefineLabel();
        var haveConstructor = il.DefineLabel();
        var haveSpecies = il.DefineLabel();
        var speciesReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, haveConstructor);
        il.Emit(OpCodes.Br, defaultSpecies);
        il.MarkLabel(haveConstructor);
        EmitRejectPrimitive(il, runtime, constructor,
            "RegExp constructor property must be an object");
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolSpecies);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, species);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Brfalse, defaultSpecies);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, haveSpecies);
        il.MarkLabel(defaultSpecies);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, species);
        il.Emit(OpCodes.Br, speciesReady);
        il.MarkLabel(haveSpecies);
        il.MarkLabel(speciesReady);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, flags);

        // Construct the matcher. RegExpFromArgs is the intrinsic default and
        // observes regexp-like source access; custom species use JS Construct.
        var constructCustom = il.DefineLabel();
        var matcherReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Brtrue, constructCustom);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Call, runtime.RegExpFromArgs);
        il.Emit(OpCodes.Stloc, matcher);
        il.Emit(OpCodes.Br, matcherReady);
        il.MarkLabel(constructCustom);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, args);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Call, runtime.ConstructDynamicValue);
        il.Emit(OpCodes.Stloc, matcher);
        il.MarkLabel(matcherReady);

        // ToLength(Get(rx, "lastIndex")); abrupt valueOf/toPrimitive results
        // escape before the iterator is created.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, lastIndexNumber);
        var zeroLastIndex = il.DefineLabel();
        var maxLastIndex = il.DefineLabel();
        var lastIndexReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lastIndexNumber);
        il.Emit(OpCodes.Ldloc, lastIndexNumber);
        il.Emit(OpCodes.Bne_Un, zeroLastIndex);
        il.Emit(OpCodes.Ldloc, lastIndexNumber);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble, zeroLastIndex);
        il.Emit(OpCodes.Ldloc, lastIndexNumber);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Bgt, maxLastIndex);
        il.Emit(OpCodes.Ldloc, lastIndexNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Truncate", _types.Double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lastIndex);
        il.Emit(OpCodes.Br, lastIndexReady);
        il.MarkLabel(maxLastIndex);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, lastIndex);
        il.Emit(OpCodes.Br, lastIndexReady);
        il.MarkLabel(zeroLastIndex);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lastIndex);
        il.MarkLabel(lastIndexReady);

        il.Emit(OpCodes.Ldloc, matcher);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldloc, lastIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // The String#matchAll materializer already produces spec-shaped match
        // arrays and a stateful iterator. Its intrinsic matcher branch avoids
        // re-entering this protocol.
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Ldloc, matcher);
        il.Emit(OpCodes.Call, runtime.StringMatchAllRegExp);
        il.Emit(OpCodes.Ret);
    }
}
