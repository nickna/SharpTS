using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Fills the phase-1 declaration of RegExp.prototype[@@split]. The body is
    /// emitted late because SpeciesConstructor must route through the shared
    /// dynamic-construction protocol.
    /// </summary>
    private void EmitRegExpSymbolSplitProtocol(EmittedRuntime runtime)
    {
        var method = runtime.RegExpSymbolSplitProtocol;
        var il = method.GetILGenerator();

        var s = il.DeclareLocal(_types.String);
        var constructor = il.DeclareLocal(_types.Object);
        var species = il.DeclareLocal(_types.Object);
        var flags = il.DeclareLocal(_types.String);
        var newFlags = il.DeclareLocal(_types.String);
        var fullUnicode = il.DeclareLocal(_types.Boolean);
        var splitter = il.DeclareLocal(_types.Object);
        var args = il.DeclareLocal(_types.ObjectArray);
        var limitNumber = il.DeclareLocal(_types.Double);
        var result = il.DeclareLocal(_types.ListOfObject);
        var execResult = il.DeclareLocal(_types.Object);
        var p = il.DeclareLocal(_types.Int32);
        var q = il.DeclareLocal(_types.Int32);
        var e = il.DeclareLocal(_types.Int32);
        var captureLength = il.DeclareLocal(_types.Int32);
        var captureIndex = il.DeclareLocal(_types.Int32);
        var number = il.DeclareLocal(_types.Double);

        var stringLength = _types.GetProperty(_types.String, "Length").GetGetMethod()!;
        var stringContains = _types.GetMethod(_types.String, "Contains", _types.String);
        var stringConcat = _types.GetMethod(_types.String, "Concat", _types.String, _types.String);
        var substringRange = _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32);
        var substringTail = _types.GetMethod(_types.String, "Substring", _types.Int32);
        var listCtor = _types.GetDefaultConstructor(_types.ListOfObject);
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var listCount = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var isNaN = _types.GetMethod(_types.Double, "IsNaN", _types.Double);
        var isInfinity = _types.GetMethod(_types.Double, "IsInfinity", _types.Double);
        var truncate = _types.GetMethod(typeof(Math), "Truncate", _types.Double);
        var floor = _types.GetMethod(typeof(Math), "Floor", _types.Double);

        // S = ToString(string). The public wrapper already performed the
        // RequireObject check for rx.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, s);

        // C = SpeciesConstructor(rx, %RegExp%). Undefined constructor/species
        // is represented by the default branch; GetIndex is required here so
        // symbol-keyed accessors and their abrupt completions are observable.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, constructor);

        var defaultSpecies = il.DefineLabel();
        var invalidConstructor = il.DefineLabel();
        var haveConstructor = il.DefineLabel();
        var haveSpecies = il.DefineLabel();
        var afterSpecies = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Brfalse, invalidConstructor);
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, haveConstructor);
        il.Emit(OpCodes.Br, defaultSpecies);

        il.MarkLabel(invalidConstructor);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp constructor property must be an object");

        il.MarkLabel(haveConstructor);
        EmitRejectPrimitive(il, runtime, constructor, "RegExp constructor property must be an object");
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolSpecies);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, species);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Brfalse, defaultSpecies);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, haveSpecies);
        il.Emit(OpCodes.Br, defaultSpecies);

        il.MarkLabel(defaultSpecies);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, species);
        il.Emit(OpCodes.Br, afterSpecies);
        il.MarkLabel(haveSpecies);
        il.MarkLabel(afterSpecies);

        // flags = ToString(Get(rx, "flags")); newFlags includes sticky.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, flags);

        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldstr, "u");
        il.Emit(OpCodes.Callvirt, stringContains);
        var unicodeTrue = il.DefineLabel();
        var unicodeReady = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, unicodeTrue);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldstr, "v");
        il.Emit(OpCodes.Callvirt, stringContains);
        il.Emit(OpCodes.Br, unicodeReady);
        il.MarkLabel(unicodeTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.MarkLabel(unicodeReady);
        il.Emit(OpCodes.Stloc, fullUnicode);

        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldstr, "y");
        il.Emit(OpCodes.Callvirt, stringContains);
        var addSticky = il.DefineLabel();
        var flagsReady = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, addSticky);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Stloc, newFlags);
        il.Emit(OpCodes.Br, flagsReady);
        il.MarkLabel(addSticky);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldstr, "y");
        il.Emit(OpCodes.Call, stringConcat);
        il.Emit(OpCodes.Stloc, newFlags);
        il.MarkLabel(flagsReady);

        // splitter = Construct(C, « rx, newFlags »). The intrinsic default is
        // built directly; an overridden @@species uses the common JS new path.
        var constructCustom = il.DefineLabel();
        var splitterReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Brtrue, constructCustom);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        var defaultPlainReceiver = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, defaultPlainReceiver);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        il.Emit(OpCodes.Ldloc, newFlags);
        il.Emit(OpCodes.Newobj, runtime.TSRegExpCtorPatternFlags);
        il.Emit(OpCodes.Stloc, splitter);
        il.Emit(OpCodes.Br, splitterReady);

        // Match the interpreter's current intrinsic-default representation
        // for an ordinary receiver: reuse rx and let RegExpExec validate its
        // exec property. (A real $RegExp above still gets a fresh sticky
        // splitter.) This keeps existing parity until %RegExp% construction
        // is represented uniformly in both engines.
        il.MarkLabel(defaultPlainReceiver);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, splitter);
        il.Emit(OpCodes.Br, splitterReady);

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
        il.Emit(OpCodes.Ldloc, newFlags);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Ldloc, args);
        il.Emit(OpCodes.Call, runtime.ConstructDynamicValue);
        il.Emit(OpCodes.Stloc, splitter);
        il.MarkLabel(splitterReady);

        // lim = ToUint32(limit), preserving the full 0..2^32-1 range in a
        // double because the output itself can never exceed Int32 length.
        var coerceLimit = il.DefineLabel();
        var limitReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, coerceLimit);
        il.Emit(OpCodes.Ldc_R8, 4294967295.0);
        il.Emit(OpCodes.Stloc, limitNumber);
        il.Emit(OpCodes.Br, limitReady);
        il.MarkLabel(coerceLimit);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var limitNotUndefined = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, limitNotUndefined);
        il.Emit(OpCodes.Ldc_R8, 4294967295.0);
        il.Emit(OpCodes.Stloc, limitNumber);
        il.Emit(OpCodes.Br, limitReady);
        il.MarkLabel(limitNotUndefined);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, number);
        var zeroLimit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, isNaN);
        il.Emit(OpCodes.Brtrue, zeroLimit);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, isInfinity);
        il.Emit(OpCodes.Brtrue, zeroLimit);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, zeroLimit);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, truncate);
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, limitNumber);
        il.Emit(OpCodes.Ldloc, limitNumber);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var limitReadyPositive = il.DefineLabel();
        il.Emit(OpCodes.Bge, limitReadyPositive);
        il.Emit(OpCodes.Ldloc, limitNumber);
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, limitNumber);
        il.Emit(OpCodes.Br, limitReadyPositive);
        il.MarkLabel(zeroLimit);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, limitNumber);
        il.MarkLabel(limitReadyPositive);
        il.MarkLabel(limitReady);

        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stloc, result);

        var nonZeroLimit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, limitNumber);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, nonZeroLimit);
        EmitReturnArray(il, runtime, result);
        il.MarkLabel(nonZeroLimit);

        // Empty input: return [] when the splitter matches empty, otherwise [""].
        var nonEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Brtrue, nonEmpty);
        EmitSetLastIndex(il, runtime, splitter, 0);
        il.Emit(OpCodes.Ldloc, splitter);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Call, runtime.RegExpExec);
        il.Emit(OpCodes.Stloc, execResult);
        var emptyMatched = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Brtrue, emptyMatched);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Callvirt, listAdd);
        il.MarkLabel(emptyMatched);
        EmitReturnArray(il, runtime, result);
        il.MarkLabel(nonEmpty);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, p);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, q);

        var loopTest = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var noMatch = il.DefineLabel();
        var advance = il.DefineLabel();
        var nextMatch = il.DefineLabel();
        var captureLoop = il.DefineLabel();
        var capturesDone = il.DefineLabel();
        il.Emit(OpCodes.Br, loopTest);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldloc, splitter);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldloc, q);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ldloc, splitter);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Call, runtime.RegExpExec);
        il.Emit(OpCodes.Stloc, execResult);
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Brfalse, noMatch);

        il.Emit(OpCodes.Ldloc, splitter);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        EmitToLengthInt(il, runtime, number, e, isNaN, floor);
        il.Emit(OpCodes.Ldloc, e);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Callvirt, stringLength);
        var eClamped = il.DefineLabel();
        il.Emit(OpCodes.Ble, eClamped);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Stloc, e);
        il.MarkLabel(eClamped);
        il.Emit(OpCodes.Ldloc, e);
        il.Emit(OpCodes.Ldloc, p);
        il.Emit(OpCodes.Beq, advance);

        // A.push(S.substring(p, q)).
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Ldloc, p);
        il.Emit(OpCodes.Ldloc, q);
        il.Emit(OpCodes.Ldloc, p);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, substringRange);
        il.Emit(OpCodes.Callvirt, listAdd);
        EmitReturnIfLimitReached(il, runtime, result, limitNumber, listCount);

        // captureLength = ToLength(Get(z, "length")); append captures 1..n-1.
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        EmitToLengthInt(il, runtime, number, captureLength, isNaN, floor);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, captureIndex);
        il.Emit(OpCodes.Br, captureLoop);
        il.MarkLabel(nextMatch);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Callvirt, listAdd);
        EmitReturnIfLimitReached(il, runtime, result, limitNumber, listCount);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, captureIndex);
        il.MarkLabel(captureLoop);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldloc, captureLength);
        il.Emit(OpCodes.Blt, nextMatch);
        il.MarkLabel(capturesDone);
        il.Emit(OpCodes.Ldloc, e);
        il.Emit(OpCodes.Stloc, p);
        il.Emit(OpCodes.Ldloc, e);
        il.Emit(OpCodes.Stloc, q);
        il.Emit(OpCodes.Br, loopTest);

        il.MarkLabel(noMatch);
        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Ldloc, q);
        il.Emit(OpCodes.Ldloc, fullUnicode);
        il.Emit(OpCodes.Call, runtime.TSRegExpAdvanceStringIndexSpec);
        il.Emit(OpCodes.Stloc, q);

        il.MarkLabel(loopTest);
        il.Emit(OpCodes.Ldloc, q);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Blt, loopBody);

        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Ldloc, p);
        il.Emit(OpCodes.Callvirt, substringTail);
        il.Emit(OpCodes.Callvirt, listAdd);
        EmitReturnArray(il, runtime, result);
    }

    private void EmitRejectPrimitive(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder value,
        string message)
    {
        var ok = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, value);
        var nonNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, nonNull);
        GuestErrorEmitter.ThrowTypeError(il, runtime, message);
        il.MarkLabel(nonNull);

        foreach (var type in new[]
                 {
                     _types.String, _types.Double, _types.Boolean,
                     _types.BigInteger, runtime.TSSymbolType
                 })
        {
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Isinst, type);
            var next = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, next);
            GuestErrorEmitter.ThrowTypeError(il, runtime, message);
            il.MarkLabel(next);
        }
        il.MarkLabel(ok);
    }

    private void EmitToLengthInt(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder number,
        LocalBuilder destination,
        System.Reflection.MethodInfo isNaN,
        System.Reflection.MethodInfo floor)
    {
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, number);
        var zero = il.DefineLabel();
        var max = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, isNaN);
        il.Emit(OpCodes.Brtrue, zero);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble, zero);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Bge, max);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, floor);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(zero);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(max);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, destination);
        il.MarkLabel(done);
    }

    private void EmitSetLastIndex(ILGenerator il, EmittedRuntime runtime, LocalBuilder splitter, int value)
    {
        il.Emit(OpCodes.Ldloc, splitter);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldc_R8, (double)value);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);
    }

    private void EmitReturnIfLimitReached(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder result,
        LocalBuilder limit,
        System.Reflection.MethodInfo listCount)
    {
        var keepGoing = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, limit);
        il.Emit(OpCodes.Blt, keepGoing);
        EmitReturnArray(il, runtime, result);
        il.MarkLabel(keepGoing);
    }

    private static void EmitReturnArray(ILGenerator il, EmittedRuntime runtime, LocalBuilder result)
    {
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
