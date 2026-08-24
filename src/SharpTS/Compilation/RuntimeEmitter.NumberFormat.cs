using System;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Concatenates a proven integer loop counter with a string without first
    /// allocating the counter's decimal representation. A bounded thread-local
    /// buffer supplies the formatting span; <see cref="string.Concat(ReadOnlySpan{char}, ReadOnlySpan{char})"/>
    /// then creates only the final JavaScript string.
    /// </summary>
    private void EmitConcatStringInt64Method(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatStringInt64",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int64, _types.Boolean]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        runtime.ConcatStringInt64 = method;

        var bufferField = typeBuilder.DefineField(
            "_concatInt64Buffer",
            typeof(char[]),
            FieldAttributes.Private | FieldAttributes.Static);
        bufferField.SetCustomAttribute(
            typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!,
            CustomAttributeEncoder.EmptyBlob);

        var il = method.GetILGenerator();
        var bufferLocal = il.DeclareLocal(typeof(char[]));
        var spanLocal = il.DeclareLocal(typeof(Span<char>));
        var formatLocal = il.DeclareLocal(typeof(ReadOnlySpan<char>));
        var numberSpanLocal = il.DeclareLocal(typeof(ReadOnlySpan<char>));
        var charsWrittenLocal = il.DeclareLocal(_types.Int32);
        var bufferReady = il.DefineLabel();
        var textFirst = il.DefineLabel();

        il.Emit(OpCodes.Ldsfld, bufferField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, bufferReady);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, bufferField);
        il.MarkLabel(bufferReady);
        il.Emit(OpCodes.Stloc, bufferLocal);

        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Call, typeof(Span<char>).GetMethod(
            "op_Implicit", [typeof(char[])])!);
        il.Emit(OpCodes.Stloc, spanLocal);
        il.Emit(OpCodes.Ldloca, formatLocal);
        il.Emit(OpCodes.Initobj, typeof(ReadOnlySpan<char>));
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Ldloc, spanLocal);
        il.Emit(OpCodes.Ldloca, charsWrittenLocal);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo)
            .GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(long).GetMethod(
            "TryFormat",
            [
                typeof(Span<char>),
                typeof(int).MakeByRefType(),
                typeof(ReadOnlySpan<char>),
                typeof(IFormatProvider)
            ])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, charsWrittenLocal);
        il.Emit(OpCodes.Newobj, typeof(ReadOnlySpan<char>).GetConstructor(
            [typeof(char[]), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, numberSpanLocal);

        MethodInfo asSpan = typeof(MemoryExtensions).GetMethod(
            "AsSpan", [typeof(string)])!;
        MethodInfo concat = typeof(string).GetMethod(
            "Concat", [typeof(ReadOnlySpan<char>), typeof(ReadOnlySpan<char>)])!;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, textFirst);
        il.Emit(OpCodes.Ldloc, numberSpanLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, asSpan);
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(textFirst);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, asSpan);
        il.Emit(OpCodes.Ldloc, numberSpanLocal);
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the body of <c>$Runtime.FormatNumber(double) -> string</c>, a byte-for-byte
    /// port of <see cref="RuntimeTypes.FormatNumber(double)"/> (ECMA-262 7.1.12.1
    /// Number::toString base 10): shortest round-trip digits via <c>double.ToString("R")</c>
    /// repositioned per the spec's plain-vs-exponential thresholds. The MethodBuilder is
    /// forward-declared in DefineRuntimeClassPhase1 so Stringify can call it.
    ///
    /// Keep in sync with RuntimeTypes.FormatNumber — both implement the same algorithm so
    /// interpreted and compiled output match.
    /// </summary>
    private void EmitFormatNumberMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = (MethodBuilder)runtime.FormatNumber;
        var il = method.GetILGenerator();

        // BCL tokens (all standalone-safe — System.* only).
        var ci = typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!;
        var dblIsNaN = _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!;
        var dblIsPosInf = _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!;
        var dblIsNegInf = _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!;
        var mathFloor = _types.GetMethod(_types.Math, "Floor", [_types.Double])!;
        var mathAbsD = _types.GetMethod(_types.Math, "Abs", [_types.Double])!;
        var mathAbsI = _types.GetMethod(_types.Math, "Abs", [_types.Int32])!;
        var longToString = _types.GetMethod(_types.Int64, "ToString", [typeof(IFormatProvider)])!;
        var intToString = _types.GetMethod(_types.Int32, "ToString", [typeof(IFormatProvider)])!;
        var intParse = _types.GetMethod(_types.Int32, "Parse", [_types.String, typeof(IFormatProvider)])!;
        var dblToStr = _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!;
        var strIndexOf = _types.GetMethod(_types.String, "IndexOf", [typeof(char)])!;
        var strSub1 = _types.GetMethod(_types.String, "Substring", [_types.Int32])!;
        var strSub2 = _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!;
        var strRemove = _types.GetMethod(_types.String, "Remove", [_types.Int32, _types.Int32])!;
        var strTrimEnd = _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!;
        var strLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var strChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var strConcat3 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!;
        var strConcat4 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String, _types.String])!;
        var strConcatArr = _types.GetMethod(_types.String, "Concat", [typeof(string[])])!;
        var newStr = _types.GetConstructor(_types.String, [typeof(char), _types.Int32])!;

        // locals
        var signL = il.DeclareLocal(_types.String);
        var rL = il.DeclareLocal(_types.String);
        var digitsL = il.DeclareLocal(_types.String);
        var nL = il.DeclareLocal(_types.Int32);
        var eIdxL = il.DeclareLocal(_types.Int32);
        var dotL = il.DeclareLocal(_types.Int32);
        var mantL = il.DeclareLocal(_types.String);
        var expL = il.DeclareLocal(_types.Int32);
        var leadL = il.DeclareLocal(_types.Int32);
        var kL = il.DeclareLocal(_types.Int32);
        var eL = il.DeclareLocal(_types.Int32);
        var mantOutL = il.DeclareLocal(_types.String);
        var esignL = il.DeclareLocal(_types.String);
        var absEL = il.DeclareLocal(_types.Int32);
        var longL = il.DeclareLocal(_types.Int64);
        var absL = il.DeclareLocal(_types.Double);

        // if (double.IsNaN(d)) return "NaN";
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, dblIsNaN); il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldstr, "NaN"); il.Emit(OpCodes.Ret);
        il.MarkLabel(notNaN);
        // if (double.IsPositiveInfinity(d)) return "Infinity";
        var notPI = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, dblIsPosInf); il.Emit(OpCodes.Brfalse, notPI);
        il.Emit(OpCodes.Ldstr, "Infinity"); il.Emit(OpCodes.Ret);
        il.MarkLabel(notPI);
        // if (double.IsNegativeInfinity(d)) return "-Infinity";
        var notNI = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, dblIsNegInf); il.Emit(OpCodes.Brfalse, notNI);
        il.Emit(OpCodes.Ldstr, "-Infinity"); il.Emit(OpCodes.Ret);
        il.MarkLabel(notNI);

        // if (d == Math.Floor(d) && Math.Abs(d) < 2^53) return ((long)d).ToString(Invariant);
        var notIntFast = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, mathFloor);
        il.Emit(OpCodes.Bne_Un, notIntFast);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, mathAbsD);
        il.Emit(OpCodes.Ldc_R8, 9007199254740992.0);
        il.Emit(OpCodes.Bge, notIntFast);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Stloc, longL);
        il.Emit(OpCodes.Ldloca, longL); il.Emit(OpCodes.Call, ci); il.Emit(OpCodes.Call, longToString);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notIntFast);

        // sign = d < 0 ? "-" : "";
        var signPos = il.DefineLabel(); var signDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Bge, signPos);
        il.Emit(OpCodes.Ldstr, "-"); il.Emit(OpCodes.Stloc, signL); il.Emit(OpCodes.Br, signDone);
        il.MarkLabel(signPos); il.Emit(OpCodes.Ldstr, ""); il.Emit(OpCodes.Stloc, signL);
        il.MarkLabel(signDone);

        // r = Math.Abs(d).ToString("R", Invariant);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, mathAbsD); il.Emit(OpCodes.Stloc, absL);
        il.Emit(OpCodes.Ldloca, absL); il.Emit(OpCodes.Ldstr, "R"); il.Emit(OpCodes.Call, ci);
        il.Emit(OpCodes.Call, dblToStr); il.Emit(OpCodes.Stloc, rL);

        // eIdx = r.IndexOf('E');
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Ldc_I4, (int)'E'); il.Emit(OpCodes.Callvirt, strIndexOf);
        il.Emit(OpCodes.Stloc, eIdxL);

        var noE = il.DefineLabel(); var afterParse = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, eIdxL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, noE);

        // --- E branch ---
        // mant = r.Substring(0, eIdx);
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, eIdxL);
        il.Emit(OpCodes.Callvirt, strSub2); il.Emit(OpCodes.Stloc, mantL);
        // exp = int.Parse(r.Substring(eIdx + 1), Invariant);
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Ldloc, eIdxL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, strSub1); il.Emit(OpCodes.Call, ci); il.Emit(OpCodes.Call, intParse);
        il.Emit(OpCodes.Stloc, expL);
        // dot = mant.IndexOf('.');
        il.Emit(OpCodes.Ldloc, mantL); il.Emit(OpCodes.Ldc_I4, (int)'.'); il.Emit(OpCodes.Callvirt, strIndexOf);
        il.Emit(OpCodes.Stloc, dotL);
        var eDotElse = il.DefineLabel(); var eDotDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, eDotElse);
        // digits = mant; n = mant.Length + exp;
        il.Emit(OpCodes.Ldloc, mantL); il.Emit(OpCodes.Stloc, digitsL);
        il.Emit(OpCodes.Ldloc, mantL); il.Emit(OpCodes.Callvirt, strLen); il.Emit(OpCodes.Ldloc, expL);
        il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, nL); il.Emit(OpCodes.Br, eDotDone);
        il.MarkLabel(eDotElse);
        // digits = mant.Remove(dot, 1); n = dot + exp;
        il.Emit(OpCodes.Ldloc, mantL); il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, strRemove); il.Emit(OpCodes.Stloc, digitsL);
        il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Ldloc, expL); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, nL);
        il.MarkLabel(eDotDone);
        il.Emit(OpCodes.Br, afterParse);

        // --- no-E branch ---
        il.MarkLabel(noE);
        // dot = r.IndexOf('.');
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Ldc_I4, (int)'.'); il.Emit(OpCodes.Callvirt, strIndexOf);
        il.Emit(OpCodes.Stloc, dotL);
        var nDotElse = il.DefineLabel(); var nDotDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, nDotElse);
        // digits = r; n = r.Length;
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Stloc, digitsL);
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Callvirt, strLen); il.Emit(OpCodes.Stloc, nL);
        il.Emit(OpCodes.Br, nDotDone);
        il.MarkLabel(nDotElse);
        // digits = r.Remove(dot, 1); n = dot;
        il.Emit(OpCodes.Ldloc, rL); il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, strRemove); il.Emit(OpCodes.Stloc, digitsL);
        il.Emit(OpCodes.Ldloc, dotL); il.Emit(OpCodes.Stloc, nL);
        il.MarkLabel(nDotDone);

        il.MarkLabel(afterParse);

        // lead = 0; while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; n--; }
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, leadL);
        var loopStart = il.DefineLabel(); var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, leadL);
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Callvirt, strLen); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldloc, leadL); il.Emit(OpCodes.Callvirt, strChars);
        il.Emit(OpCodes.Ldc_I4, (int)'0'); il.Emit(OpCodes.Bne_Un, loopEnd);
        il.Emit(OpCodes.Ldloc, leadL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, leadL);
        il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, nL);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // digits = digits.Substring(lead).TrimEnd('0');
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldloc, leadL); il.Emit(OpCodes.Callvirt, strSub1);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4, (int)'0'); il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, strTrimEnd); il.Emit(OpCodes.Stloc, digitsL);

        // if (digits.Length == 0) digits = "0";
        var digitsNotEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Callvirt, strLen); il.Emit(OpCodes.Brtrue, digitsNotEmpty);
        il.Emit(OpCodes.Ldstr, "0"); il.Emit(OpCodes.Stloc, digitsL);
        il.MarkLabel(digitsNotEmpty);

        // k = digits.Length;
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Callvirt, strLen); il.Emit(OpCodes.Stloc, kL);

        // if (k <= n && n <= 21) return sign + digits + new string('0', n - k);
        var notB1 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, kL); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Bgt, notB1);
        il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldc_I4, 21); il.Emit(OpCodes.Bgt, notB1);
        il.Emit(OpCodes.Ldloc, signL); il.Emit(OpCodes.Ldloc, digitsL);
        il.Emit(OpCodes.Ldc_I4, (int)'0'); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldloc, kL); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newobj, newStr); il.Emit(OpCodes.Call, strConcat3); il.Emit(OpCodes.Ret);
        il.MarkLabel(notB1);

        // if (0 < n && n <= 21) return sign + digits.Substring(0, n) + "." + digits.Substring(n);
        var notB2 = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Bge, notB2);
        il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldc_I4, 21); il.Emit(OpCodes.Bgt, notB2);
        il.Emit(OpCodes.Ldloc, signL);
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Callvirt, strSub2);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Callvirt, strSub1);
        il.Emit(OpCodes.Call, strConcat4); il.Emit(OpCodes.Ret);
        il.MarkLabel(notB2);

        // if (-6 < n && n <= 0) return sign + "0." + new string('0', -n) + digits;
        var notB3 = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4, -6); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Bge, notB3);
        il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bgt, notB3);
        il.Emit(OpCodes.Ldloc, signL);
        il.Emit(OpCodes.Ldstr, "0.");
        il.Emit(OpCodes.Ldc_I4, (int)'0'); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newobj, newStr);
        il.Emit(OpCodes.Ldloc, digitsL);
        il.Emit(OpCodes.Call, strConcat4); il.Emit(OpCodes.Ret);
        il.MarkLabel(notB3);

        // mantOut = k == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1);
        var kNot1 = il.DefineLabel(); var mantOutDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, kL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, kNot1);
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Stloc, mantOutL); il.Emit(OpCodes.Br, mantOutDone);
        il.MarkLabel(kNot1);
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Callvirt, strSub2);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ldloc, digitsL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Callvirt, strSub1);
        il.Emit(OpCodes.Call, strConcat3); il.Emit(OpCodes.Stloc, mantOutL);
        il.MarkLabel(mantOutDone);

        // e = n - 1; esign = e >= 0 ? "+" : "-";
        il.Emit(OpCodes.Ldloc, nL); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, eL);
        var eNeg = il.DefineLabel(); var eSignDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, eL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, eNeg);
        il.Emit(OpCodes.Ldstr, "+"); il.Emit(OpCodes.Stloc, esignL); il.Emit(OpCodes.Br, eSignDone);
        il.MarkLabel(eNeg); il.Emit(OpCodes.Ldstr, "-"); il.Emit(OpCodes.Stloc, esignL);
        il.MarkLabel(eSignDone);

        // return sign + mantOut + "e" + esign + Math.Abs(e).ToString(Invariant);
        il.Emit(OpCodes.Ldloc, eL); il.Emit(OpCodes.Call, mathAbsI); il.Emit(OpCodes.Stloc, absEL);
        il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Newarr, _types.String);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, signL); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldloc, mantOutL); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldstr, "e"); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Ldloc, esignL); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloca, absEL); il.Emit(OpCodes.Call, ci); il.Emit(OpCodes.Call, intToString);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, strConcatArr); il.Emit(OpCodes.Ret);
    }
}
