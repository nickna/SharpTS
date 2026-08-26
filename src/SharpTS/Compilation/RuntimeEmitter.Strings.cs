using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitStringCharAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringCharAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        // First param naming as "__this" happens in StringPrototypePopulate.Wire
        // so all wired helpers get the same treatment uniformly.
        runtime.StringCharAt = method;

        var il = method.GetILGenerator();

        // index = (int)$Runtime.ToNumber(args[0]). ToNumber coerces bool/string/etc.
        // per ECMA-262 ToInteger; raw Unbox_Any Double would throw on non-double
        // args (e.g. when this method is borrowed via
        // `obj.charAt = String.prototype.charAt; obj.charAt(false)`).
        var indexLocal = il.DeclareLocal(_types.Int32);
        // Default to 0 if args is empty.
        var hasArgLabel = il.DefineLabel();
        var afterIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, afterIndexLabel);
        il.MarkLabel(hasArgLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(afterIndexLabel);

        var returnEmpty = il.DefineLabel();
        var validIndex = il.DefineLabel();

        // if (index < 0) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnEmpty);

        // if (index >= str.Length) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnEmpty);

        // Return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSubstring(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSubstring",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.ObjectArray]
        );
        runtime.StringSubstring = method;

        var il = method.GetILGenerator();

        // String prototype methods are generic: RequireObjectCoercible(this),
        // then ToString(this). Keeping the receiver object-typed also prevents
        // reflection dispatch from applying CLR string conversion first.
        var substringReceiverOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, substringReceiverOkLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var substringReceiverCoerceLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, substringReceiverCoerceLabel);
        il.MarkLabel(substringReceiverOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.substring called on null or undefined");
        il.MarkLabel(substringReceiverCoerceLabel);
        var substringStringLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, substringStringLocal);

        // ECMA-262 22.1.3.22: ToIntegerOrInfinity on each arg (NaN → 0,
        // ±Infinity → ±IntMax). Without this, Conv_I4 on NaN/Infinity is
        // undefined behavior — e.g. `s.substring(NaN, Infinity)` returns ""
        // instead of the full string per spec.
        var startLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        var substringHasStartLabel = il.DefineLabel();
        var substringStartReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, substringHasStartLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, substringStartReadyLabel);
        il.MarkLabel(substringHasStartLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(substringStartReadyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // end = args.Length > 1 && args[1] != undefined ? ToIntegerOrInfinity(args[1], 0) : str.Length
        var endLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        var hasEnd = il.DefineLabel();
        var endIsUndefined = il.DefineLabel();
        var defaultEnd = il.DefineLabel();
        var afterEnd = il.DefineLabel();
        il.Emit(OpCodes.Bgt, hasEnd);
        il.MarkLabel(defaultEnd);
        il.Emit(OpCodes.Ldloc, substringStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Br, afterEnd);
        il.MarkLabel(hasEnd);
        // args.Length already distinguishes an omitted end argument. CLR null
        // is the compiled representation of an explicitly supplied JavaScript
        // null and must therefore flow through ToIntegerOrInfinity as +0.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, defaultEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.MarkLabel(afterEnd);
        il.Emit(OpCodes.Stloc, endLocal);

        // Clamp start to [0, len] and end to [0, len]
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, substringStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // Clamp end: if negative (e.g. ToIntegerOrInfinity(-Inf) = int.MinValue), use 0.
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, substringStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);

        // ECMA-262: from = min(start, end); to = max(start, end). Swap if start > end.
        var fromLocal = il.DeclareLocal(_types.Int32);
        var toLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, fromLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, toLocal);

        // return str.Substring(from, to - from)
        il.Emit(OpCodes.Ldloc, substringStringLocal);
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits StringSubstr: str.substr(start, length). JS Annex B (deprecated but widely used).
    /// Negative start counts from end; length clamped. Returns substring of specified length.
    /// Needed by yaml's lexer (pushCount uses `buffer.substr(pos, n)` to emit single-char tokens).
    /// </summary>
    private void EmitStringSubstr(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSubstr",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringSubstr = method;

        var il = method.GetILGenerator();
        var lenLocal = il.DeclareLocal(_types.Int32);
        var startLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // int len = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "get_Length"));
        il.Emit(OpCodes.Stloc, lenLocal);

        // int start = ToIntegerOrInfinity(args[0]); if (start < 0) start = max(0, len + start)
        // Use ToIntegerOrInfinity instead of raw Unbox_Any Double — borrowed
        // patterns may pass bool/string/object args that need ECMA-262 coercion.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, startLocal);

        var nonNegStart = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, nonNegStart);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(nonNegStart);

        // Clamp start to [0, len]
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // int length = args.Length >= 2 ? (int)(double)args[1] : len - start
        var hasLength = il.DefineLabel();
        var afterLength = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasLength);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Br, afterLength);
        il.MarkLabel(hasLength);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.MarkLabel(afterLength);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // If length <= 0, return ""
        var validLength = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, validLength);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        // Clamp length to [0, len - start]
        il.MarkLabel(validLength);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, lengthLocal);

        // return str.Substring(start, length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Object]
        );
        runtime.StringIndexOf = method;

        var il = method.GetILGenerator();

        // return (double)str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits StringIndexOfFrom: str.indexOf(search, fromIndex). JS-spec: fromIndex is clamped
    /// to [0, length]; out-of-range returns -1. Needed by yaml's lexer (buffer.indexOf('\n', pos)).
    /// </summary>
    private void EmitStringIndexOfFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIndexOfFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Object, _types.Object]
        );
        runtime.StringIndexOfFrom = method;

        var il = method.GetILGenerator();
        var idxLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var notFoundLabel = il.DefineLabel();
        var clampMaxLabel = il.DefineLabel();
        var indexOfSearchLocal = il.DeclareLocal(_types.String);

        // int len = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "get_Length"));
        il.Emit(OpCodes.Stloc, lenLocal);

        // Search-string coercion precedes position coercion (§22.1.3.8).
        // Both are observable and may throw.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, indexOfSearchLocal);

        // ToIntegerOrInfinity performs observable object coercion and maps the
        // infinities to sentinel int extrema suitable for the clamps below.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (idx < 0) idx = 0
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, clampMaxLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (idx > len) return -1 (past end)
        il.MarkLabel(clampMaxLabel);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bgt, notFoundLabel);

        // return (double)str.IndexOf(search, idx)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexOfSearchLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String, _types.Int32));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits allocation-free overloads for the fixed-arity primitive string
    /// intrinsics. Their signatures deliberately contain no <see cref="object"/>
    /// or <c>object[]</c> slots, so a statically proven call stays in typed IL.
    /// </summary>
    private void EmitPrimitiveStringIntrinsics(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        MethodBuilder toInteger = EmitPrimitiveStringToInteger(typeBuilder);
        MethodImplAttributes inlineAndOptimize =
            MethodImplAttributes.AggressiveInlining |
            MethodImplAttributes.AggressiveOptimization;
        var lengthGetter = _types.GetProperty(_types.String, "Length").GetGetMethod()!;
        var substring = _types.GetMethod(
            _types.String, "Substring", _types.Int32, _types.Int32);
        var indexOfOrdinal = _types.GetMethod(
            _types.String, "IndexOf",
            [_types.String, _types.Int32, typeof(StringComparison)])!;
        var min = _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32);
        var max = _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32);

        var indexOf = typeBuilder.DefineMethod(
            "StringIndexOfPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.String, _types.Double]);
        indexOf.SetImplementationFlags(inlineAndOptimize);
        runtime.StringIndexOfPrimitive = indexOf;
        {
            var il = indexOf.GetILGenerator();
            var start = il.DeclareLocal(_types.Int32);

            // Clamp ToIntegerOrInfinity(position) to [0, string.length]. In
            // particular, an empty needle at a position beyond the end must
            // return length rather than -1.
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, toInteger);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, lengthGetter);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, start);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, start);
            il.Emit(OpCodes.Ldc_I4, (int)StringComparison.Ordinal);
            il.Emit(OpCodes.Callvirt, indexOfOrdinal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ret);
        }

        var includes = typeBuilder.DefineMethod(
            "StringIncludesPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String, _types.Double]);
        includes.SetImplementationFlags(inlineAndOptimize);
        runtime.StringIncludesPrimitive = includes;
        {
            var il = includes.GetILGenerator();
            var found = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, indexOf);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Bge, found);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(found);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        var slice = typeBuilder.DefineMethod(
            "StringSlicePrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Double, _types.Double, _types.Boolean]);
        slice.SetImplementationFlags(inlineAndOptimize);
        runtime.StringSlicePrimitive = slice;
        {
            var il = slice.GetILGenerator();
            var length = il.DeclareLocal(_types.Int32);
            var from = il.DeclareLocal(_types.Int32);
            var to = il.DeclareLocal(_types.Int32);
            var nonNegativeStart = il.DefineLabel();
            var startReady = il.DefineLabel();
            var hasEnd = il.DefineLabel();
            var nonNegativeEnd = il.DefineLabel();
            var endReady = il.DefineLabel();
            var returnSubstring = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, lengthGetter);
            il.Emit(OpCodes.Stloc, length);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, toInteger);
            il.Emit(OpCodes.Stloc, from);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bge, nonNegativeStart);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Stloc, from);
            il.Emit(OpCodes.Br, startReady);
            il.MarkLabel(nonNegativeStart);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, from);
            il.MarkLabel(startReady);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brtrue, hasEnd);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Stloc, to);
            il.Emit(OpCodes.Br, endReady);
            il.MarkLabel(hasEnd);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, toInteger);
            il.Emit(OpCodes.Stloc, to);
            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bge, nonNegativeEnd);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Stloc, to);
            il.Emit(OpCodes.Br, endReady);
            il.MarkLabel(nonNegativeEnd);
            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, to);
            il.MarkLabel(endReady);

            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Bgt, returnSubstring);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(returnSubstring);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, substring);
            il.Emit(OpCodes.Ret);
        }

        var substringPrimitive = typeBuilder.DefineMethod(
            "StringSubstringPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Double, _types.Double, _types.Boolean]);
        substringPrimitive.SetImplementationFlags(inlineAndOptimize);
        runtime.StringSubstringPrimitive = substringPrimitive;
        {
            var il = substringPrimitive.GetILGenerator();
            var length = il.DeclareLocal(_types.Int32);
            var start = il.DeclareLocal(_types.Int32);
            var end = il.DeclareLocal(_types.Int32);
            var from = il.DeclareLocal(_types.Int32);
            var to = il.DeclareLocal(_types.Int32);
            var hasEnd = il.DefineLabel();
            var endReady = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, lengthGetter);
            il.Emit(OpCodes.Stloc, length);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, toInteger);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, start);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brtrue, hasEnd);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Stloc, end);
            il.Emit(OpCodes.Br, endReady);
            il.MarkLabel(hasEnd);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, toInteger);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, end);
            il.MarkLabel(endReady);

            il.Emit(OpCodes.Ldloc, start);
            il.Emit(OpCodes.Ldloc, end);
            il.Emit(OpCodes.Call, min);
            il.Emit(OpCodes.Stloc, from);
            il.Emit(OpCodes.Ldloc, start);
            il.Emit(OpCodes.Ldloc, end);
            il.Emit(OpCodes.Call, max);
            il.Emit(OpCodes.Stloc, to);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Ldloc, to);
            il.Emit(OpCodes.Ldloc, from);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, substring);
            il.Emit(OpCodes.Ret);
        }
    }

    private MethodBuilder EmitPrimitiveStringToInteger(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "StringToIntegerOrInfinityPrimitive",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.Double]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

        var il = method.GetILGenerator();
        var notNaN = il.DefineLabel();
        var belowMaximum = il.DefineLabel();
        var aboveMinimum = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNaN);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Blt, belowMaximum);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(belowMaximum);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, (double)int.MinValue);
        il.Emit(OpCodes.Bgt, aboveMinimum);
        il.Emit(OpCodes.Ldc_I4, int.MinValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(aboveMinimum);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitStringReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String]
        );
        runtime.StringReplace = method;

        var il = method.GetILGenerator();

        // JavaScript replace only replaces first occurrence
        // var index = str.IndexOf(search)
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String));
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) return str
        var found = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, found);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(found);
        // return str.Substring(0, index) + replacement + str.Substring(index + search.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));

        il.Emit(OpCodes.Ldarg_2); // replacement

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitStringSearchHelper(typeBuilder, runtime, "StringIncludes", "Contains", "includes",
            m => runtime.StringIncludes = m);
    }

    private void EmitStringStartsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitStringSearchHelper(typeBuilder, runtime, "StringStartsWith", "StartsWith", "startsWith",
            m => runtime.StringStartsWith = m);
    }

    private void EmitStringEndsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitStringSearchHelper(typeBuilder, runtime, "StringEndsWith", "EndsWith", "endsWith",
            m => runtime.StringEndsWith = m);
    }

    /// <summary>
    /// Emits a string search helper (Contains/StartsWith/EndsWith) that
    /// throws TypeError if the search argument is a RegExp per ECMA-262
    /// §22.1.3.{7,20,6} step 4. Without the check the prototype-dispatch
    /// path silently casts RegExp to string via Castclass, throwing an
    /// InvalidCastException instead of a spec TypeError.
    /// </summary>
    private void EmitStringSearchHelper(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string methodName, string clrMethodName, string jsMethodName,
        Action<MethodBuilder> assign)
    {
        // Signature: (string self, object searchString, object position) → bool.
        // The 3rd param is required so the call site can always push the
        // position (or null/undefined if not supplied). undefined → 0
        // (or len for endsWith); Symbol throws via ToIntegerOrInfinity.
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.Object, _types.Object]
        );
        assign(method);

        var il = method.GetILGenerator();
        // IsRegExp first observes searchString[Symbol.match]. An explicit
        // non-nullish value controls the answer (and its getter may throw);
        // only when absent do native RegExp objects fall back to their brand.
        // This check must precede ToString(searchString).
        if (runtime.TSRegExpType != null)
        {
            var rejectRegExpLabel = il.DefineLabel();
            var brandCheckLabel = il.DefineLabel();
            var notRegExpLabel = il.DefineLabel();
            var matchValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldsfld, runtime.SymbolMatch);
            il.Emit(OpCodes.Call, runtime.GetIndex);
            il.Emit(OpCodes.Stloc, matchValueLocal);
            il.Emit(OpCodes.Ldloc, matchValueLocal);
            il.Emit(OpCodes.Brfalse, brandCheckLabel);
            il.Emit(OpCodes.Ldloc, matchValueLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, brandCheckLabel);
            il.Emit(OpCodes.Ldloc, matchValueLocal);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brtrue, rejectRegExpLabel);
            il.Emit(OpCodes.Br, notRegExpLabel);
            il.MarkLabel(brandCheckLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpLabel);
            il.MarkLabel(rejectRegExpLabel);
            il.Emit(OpCodes.Ldstr, "First argument to String.prototype." + jsMethodName + " must not be a regular expression");
            GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
            il.MarkLabel(notRegExpLabel);
        }

        // Coerce searchString via ToJsString (handles non-string, throws on Symbol).
        var searchStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchStrLocal);

        // Coerce position via ToIntegerOrInfinity (throws TypeError on Symbol
        // and preserves ±Infinity for the clamps below). null /
        // undefined → 0 (startsWith/includes) or len (endsWith — caller treats
        // undef as "use length"). The helper materializes 0 here; endsWith
        // semantics are handled inside the per-method branch below.
        var posLocal = il.DeclareLocal(_types.Int32);
        var posUndefLabel = il.DefineLabel();
        var posDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, posUndefLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, posUndefLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, posLocal);
        il.Emit(OpCodes.Br, posDoneLabel);
        il.MarkLabel(posUndefLabel);
        il.Emit(OpCodes.Ldc_I4_M1);  // sentinel for "position not supplied"
        il.Emit(OpCodes.Stloc, posLocal);
        il.MarkLabel(posDoneLabel);

        // len = arg0.Length; searchLen = searchStr.Length.
        var lenLocal = il.DeclareLocal(_types.Int32);
        var searchLenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldloc, searchStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, searchLenLocal);

        if (clrMethodName == "EndsWith")
        {
            // endsWith: endPos = (pos == -1) ? len : clamp(pos, 0, len).
            //           start = endPos - searchLen. If start < 0 → false.
            //           else: arg0.IndexOf(searchStr, start) == start.
            var startLocal = il.DeclareLocal(_types.Int32);
            var bailFalseLabel = il.DefineLabel();
            var posSupplied = il.DefineLabel();
            var endComputed = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, posLocal);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Bne_Un, posSupplied);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Br, endComputed);
            il.MarkLabel(posSupplied);
            il.Emit(OpCodes.Ldloc, posLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
            il.MarkLabel(endComputed);
            il.Emit(OpCodes.Ldloc, searchLenLocal);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, startLocal);
            // start < 0 → false.
            il.Emit(OpCodes.Ldloc, startLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, bailFalseLabel);
            // arg0.IndexOf(searchStr, start) == start.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, searchStrLocal);
            il.Emit(OpCodes.Ldloc, startLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String, _types.Int32));
            il.Emit(OpCodes.Ldloc, startLocal);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(bailFalseLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // startsWith / includes: start = clamp(max(pos, 0), 0, len). When
            // pos was undef (-1 sentinel), default to 0.
            var startLocal = il.DeclareLocal(_types.Int32);
            var posSupplied = il.DefineLabel();
            var startComputed = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, posLocal);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Bne_Un, posSupplied);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Br, startComputed);
            il.MarkLabel(posSupplied);
            il.Emit(OpCodes.Ldloc, posLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
            il.MarkLabel(startComputed);
            il.Emit(OpCodes.Stloc, startLocal);

            if (clrMethodName == "StartsWith")
            {
                // start + searchLen > len → false. Else: arg0.IndexOf(searchStr, start) == start.
                var bailFalseLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, startLocal);
                il.Emit(OpCodes.Ldloc, searchLenLocal);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldloc, lenLocal);
                il.Emit(OpCodes.Bgt, bailFalseLabel);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, searchStrLocal);
                il.Emit(OpCodes.Ldloc, startLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String, _types.Int32));
                il.Emit(OpCodes.Ldloc, startLocal);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(bailFalseLabel);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
            }
            else  // Contains (includes)
            {
                // arg0.IndexOf(searchStr, start) >= 0.
                var trueLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, searchStrLocal);
                il.Emit(OpCodes.Ldloc, startLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String, _types.Int32));
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Bge, trueLabel);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(trueLabel);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
            }
        }
    }

    private void EmitStringSlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSlice(object receiver, object[] args) -> string
        // Handles negative indices and optional end parameter.
        // argCount derived from args.Length so the helper is borrowable via
        // \$TSFunction reflection without a metadata mismatch — the explicit
        // int argCount param previously confused the borrowed-pattern dispatch.
        var method = typeBuilder.DefineMethod(
            "StringSlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.ObjectArray]
        );
        runtime.StringSlice = method;

        var il = method.GetILGenerator();
        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var sliceReceiverThrowLabel = il.DefineLabel();
        var sliceReceiverReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, sliceReceiverThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, sliceReceiverReadyLabel);
        il.MarkLabel(sliceReceiverThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.slice called on null or undefined");
        il.MarkLabel(sliceReceiverReadyLabel);
        var sliceStringLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, sliceStringLocal);

        // lengthLocal = str.Length
        il.Emit(OpCodes.Ldloc, sliceStringLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // start = ToIntegerOrInfinity(args[0], 0). Handles NaN/±Infinity per spec
        // (NaN→0, +Inf→intMax, -Inf→intMin). Conv_I4 alone is undefined behavior
        // for those values.
        var sliceHasStartLabel = il.DefineLabel();
        var sliceStartReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, sliceHasStartLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, sliceStartReadyLabel);
        il.MarkLabel(sliceHasStartLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(sliceStartReadyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, startLocal);

        // end = args.Length > 1 && args[1] != null/undefined ? ToIntegerOrInfinity(args[1], 0) : length
        var noEndArg = il.DefineLabel();
        var endArgDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noEndArg);
        // Missing/undefined is represented as null or $Undefined by the
        // borrowed-method argument packer.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, noEndArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noEndArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endArgDone);
        il.MarkLabel(noEndArg);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endArgDone);

        // Handle negative start: if (start < 0) start = max(0, length + start)
        var startNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);
        // start is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotNegative);

        // Handle negative end: if (end < 0) end = max(0, length + end)
        var endNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNegative);
        // end is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotNegative);

        // Clamp start to length: start = min(start, length)
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // Clamp end to length: end = min(end, length)
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);

        // if (end <= start) return ""
        var returnSubstring = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, returnSubstring);
        // return ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        // return str.Substring(start, end - start)
        il.MarkLabel(returnSubstring);
        il.Emit(OpCodes.Ldloc, sliceStringLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringRepeat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringRepeat(string str, object count) -> string. count is `object`
        // so a Symbol or other primitive coerces through ToNumber here, which
        // throws TypeError on Symbol per ECMA-262 §22.1.3.16 step 4. Pre-fix
        // the signature took `double` directly, so the caller's Convert.ToDouble
        // raised InvalidCastException instead of TypeError.
        var method = typeBuilder.DefineMethod(
            "StringRepeat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object]
        );
        runtime.StringRepeat = method;

        var il = method.GetILGenerator();
        var countDoubleLocal = il.DeclareLocal(_types.Double);
        var countLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // Coerce via ToNumber (throws on Symbol / BigInt / object-with-throwing-
        // valueOf). undefined → NaN → 0 (per ToIntegerOrInfinity step 2).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, countDoubleLocal);

        // ECMA-262 21.1.3.13: validate count first.
        //   if count is NaN → ToIntegerOrInfinity returns 0 → return "" (no throw)
        //   if count < 0 (incl. -∞) → throw RangeError
        //   if count is +∞ → throw RangeError
        var nonNegLabel = il.DefineLabel();
        var throwRangeLabel = il.DefineLabel();
        // NaN check (NaN != NaN)
        il.Emit(OpCodes.Ldloc, countDoubleLocal);
        il.Emit(OpCodes.Ldloc, countDoubleLocal);
        il.Emit(OpCodes.Bne_Un, nonNegLabel); // NaN: skip throw, fall through to Conv_I4 path (will yield 0)
        // < 0 check (catches finite negatives + -Infinity)
        il.Emit(OpCodes.Ldloc, countDoubleLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt, throwRangeLabel);
        // +Infinity check
        il.Emit(OpCodes.Ldloc, countDoubleLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, throwRangeLabel);
        il.Emit(OpCodes.Br, nonNegLabel);

        il.MarkLabel(throwRangeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Invalid count value");

        il.MarkLabel(nonNegLabel);

        // count = (int)countDouble (NaN → garbage, but emptyLabel below catches via count<=0)
        il.Emit(OpCodes.Ldloc, countDoubleLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, countLocal);

        // if (count <= 0 || str.Length == 0) return ""
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // result = ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < count; i++) result += str
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringPadStart(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadStart(string str, object[] args) -> string. argCount derived
        // from args.Length internally so the helper is borrowable via \$TSFunction.
        var method = typeBuilder.DefineMethod(
            "StringPadStart",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringPadStart = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(_types.Int32);
        var padStringLocal = il.DeclareLocal(_types.String);
        var padLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)$Runtime.ToNumber(args[0]) — coerce non-double per ECMA-262.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = args.Length > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        // padString = (args[1] is undefined) ? " " : ? ToString(args[1])
        // ECMA-262 §22.1.3.{12,13} step 5: only UNDEFINED takes the " " default;
        // null and other primitives go through ToString (so `padStart(10, null)`
        // → "null" pad, not " ").
        var padArgLocal = il.DeclareLocal(_types.Object);
        var padArgIsUndefLabel = il.DefineLabel();
        var padArgDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, padArgLocal);
        il.Emit(OpCodes.Ldloc, padArgLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, padArgIsUndefLabel);
        il.Emit(OpCodes.Ldloc, padArgLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, padArgDoneLabel);
        il.MarkLabel(padArgIsUndefLabel);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(padArgDoneLabel);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and prepend
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringPadEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadEnd(string str, object[] args) -> string. argCount derived
        // from args.Length internally so the helper is borrowable via \$TSFunction.
        var method = typeBuilder.DefineMethod(
            "StringPadEnd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringPadEnd = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(_types.Int32);
        var padStringLocal = il.DeclareLocal(_types.String);
        var padLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)$Runtime.ToNumber(args[0]) — coerce non-double per ECMA-262.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = args.Length > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        // padString = (args[1] is undefined) ? " " : ? ToString(args[1])
        // Per padStart — only undefined triggers the " " default.
        var padArgLocalEnd = il.DeclareLocal(_types.Object);
        var padArgIsUndefLabelEnd = il.DefineLabel();
        var padArgDoneLabelEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, padArgLocalEnd);
        il.Emit(OpCodes.Ldloc, padArgLocalEnd);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, padArgIsUndefLabelEnd);
        il.Emit(OpCodes.Ldloc, padArgLocalEnd);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, padArgDoneLabelEnd);
        il.MarkLabel(padArgIsUndefLabelEnd);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(padArgDoneLabelEnd);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and append
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringCharCodeAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringCharCodeAt(string str, double index) -> double
        var method = typeBuilder.DefineMethod(
            "StringCharCodeAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Double]
        );
        // Statically typed string sites call this helper for every character in
        // lexer/interpreter-style loops. Keep JS's NaN bounds semantics here,
        // but let RyuJIT fold the tiny helper into the caller so Length and
        // get_Chars can optimize with the surrounding loop.
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        runtime.StringCharCodeAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var nanLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0 || index >= str.Length) return NaN
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nanLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, nanLabel);

        // return (double)str[index]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringConcat(string str, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringConcat = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // result = str
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < args.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // result += ToString(args[i]). ToJsString performs the observable
        // @@toPrimitive/toString protocol for objects and preserves the JS
        // spellings of null/undefined/booleans.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringLastIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringLastIndexOf(string str, string search) -> double
        var method = typeBuilder.DefineMethod(
            "StringLastIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.String]
        );
        runtime.StringLastIndexOf = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "LastIndexOf", _types.String));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringReplaceAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringReplaceAll(string str, string search, string replacement) -> string
        // ECMA-262 22.1.3.20 GetSubstitution requires `$$` → `$`, `$&` →
        // matched, ``$` ``→ pre-match, `$'` → post-match. .NET's Regex.Replace
        // honours the same syntax for these symbols (and additionally
        // `$_`/`${name}`, but those aren't reachable from a literal-string
        // search). Routing through Regex.Replace(Regex.Escape(search), …)
        // gives us spec-compliant substitution without re-implementing the
        // scanner.
        var method = typeBuilder.DefineMethod(
            "StringReplaceAll",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String]
        );
        runtime.StringReplaceAll = method;

        var il = method.GetILGenerator();
        var emptySearchLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (search.Length == 0) handle below — spec says insert replacement
        // at every position, including ends. Defer to Regex (it handles empty
        // patterns too: matches every position-of-zero-width).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptySearchLabel);

        // return Regex.Replace(str, Regex.Escape(search), replacement)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Escape", [_types.String])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(emptySearchLabel);
        // ECMA-262 22.1.3.20: empty search inserts replacement at every
        // position 0..len (one between each char + start + end). .NET's
        // Regex.Replace with empty pattern only inserts at position 0.
        // Build manually: replacement + str[0] + replacement + str[1] + ... + str[len-1] + replacement.
        // Result length: len + (len + 1) * replacement.Length, but we use
        // StringBuilder for simplicity.
        var sbLocalE = il.DeclareLocal(_types.StringBuilder);
        var iLocalE = il.DeclareLocal(_types.Int32);
        var loopStartE = il.DefineLabel();
        var loopEndE = il.DefineLabel();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocalE);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocalE);

        il.MarkLabel(loopStartE);
        il.Emit(OpCodes.Ldloc, iLocalE);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bgt, loopEndE);
        // sb.Append(replacement)
        il.Emit(OpCodes.Ldloc, sbLocalE);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        // if (i < length) sb.Append(str[i])
        var skipChar = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocalE);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, skipChar);
        il.Emit(OpCodes.Ldloc, sbLocalE);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocalE);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipChar);
        il.Emit(OpCodes.Ldloc, iLocalE);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocalE);
        il.Emit(OpCodes.Br, loopStartE);

        il.MarkLabel(loopEndE);
        il.Emit(OpCodes.Ldloc, sbLocalE);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringAt(string str, double index) -> object (string or null)
        var method = typeBuilder.DefineMethod(
            "StringAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Double]
        );
        runtime.StringAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // length = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) index = length + index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegative = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegative);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(notNegative);

        // if (index < 0 || index >= length) return null
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nullLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, nullLabel);

        // return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        // Box the char and call ToString on it
        il.Emit(OpCodes.Box, _types.Char);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", _types.EmptyTypes));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        // ECMA-262 22.1.3.1 String.prototype.at: out-of-range → undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringFromCharCode(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringFromCharCode(object[] args) -> string
        // Creates a string from the specified sequence of UTF-16 code units
        var method = typeBuilder.DefineMethod(
            "StringFromCharCode",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );
        runtime.StringFromCharCode = method;

        var il = method.GetILGenerator();
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var charsLocal = il.DeclareLocal(_types.CharArray);
        var iLocal = il.DeclareLocal(_types.Int32);
        var emptyLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // if (args == null || args.Length == 0) return ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // chars = new char[length]
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Stloc, charsLocal);

        // for (i = 0; i < length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // chars[i] = (char)(ToUint16(args[i]) & 0xFFFF)
        // ECMA-262 ToUint16: NaN/Infinity → 0; otherwise (int)d & 0xFFFF.
        // Conv_I4 on NaN/Infinity is undefined in CLR — branch on IsFinite first.
        var dLocal = il.DeclareLocal(_types.Double);
        var notFiniteLabel = il.DefineLabel();
        var afterCoerceLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, charsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, dLocal);

        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", _types.Double));
        il.Emit(OpCodes.Brfalse, notFiniteLabel);
        // finite: ECMA-262 ToUint16 — truncate towards zero, then mod 2^16.
        // Use long (Conv_I8) so 4294967294 doesn't overflow int. -1 stays as -1
        // and `& 0xFFFF` produces 65535 per spec.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I8, 0xFFFFL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, afterCoerceLabel);
        // NaN / ±Infinity → 0
        il.MarkLabel(notFiniteLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.MarkLabel(afterCoerceLabel);
        il.Emit(OpCodes.Conv_U2);  // Convert to char
        il.Emit(OpCodes.Stelem_I2);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return new string(chars)
        il.Emit(OpCodes.Ldloc, charsLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.CharArray]));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringCodePointAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringCodePointAt(string str, object index) -> object (double or undefined)
        var method = typeBuilder.DefineMethod(
            "StringCodePointAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]
        );
        runtime.StringCodePointAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var nullLabel = il.DefineLabel();
        var noSurrogate = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // index = ToIntegerOrInfinity(indexArg). Keeping the argument boxed
        // preserves object/array ToPrimitive semantics (e.g. [1] -> "1" -> 1).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0 || index >= str.Length) return null
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nullLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, nullLabel);

        // char c = str[index]
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);

        // Check for surrogate pair: Char.IsHighSurrogate(c) && index+1 < str.Length && Char.IsLowSurrogate(str[index+1])
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsHighSurrogate", _types.Char));
        il.Emit(OpCodes.Brfalse, noSurrogate);

        // index + 1 < str.Length
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, noSurrogate);

        // Char.IsLowSurrogate(str[index+1])
        var lowSurrogateLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, lowSurrogateLocal);
        il.Emit(OpCodes.Ldloc, lowSurrogateLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
        il.Emit(OpCodes.Brfalse, noSurrogate);

        // return (double)Char.ConvertToUtf32(c, lowSurrogate)
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldloc, lowSurrogateLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ConvertToUtf32", _types.Char, _types.Char));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, doneLabel);

        // noSurrogate: return (double)c
        il.MarkLabel(noSurrogate);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        // ECMA-262 22.1.3.3 step 7: out-of-range index → return undefined.
        // Pre-fix used null which fails `=== undefined` strict equality
        // checks in Test262 (which uses assert.sameValue).
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringWellFormedMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // The $TSFunction wrapper coerces the borrowed receiver to string before
        // entering these helpers, preserving RequireObjectCoercible/ToString
        // ordering and observable user coercion.
        var isWellFormed = typeBuilder.DefineMethod(
            "StringIsWellFormed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String]);
        runtime.StringIsWellFormed = isWellFormed;
        {
            var il = isWellFormed.GetILGenerator();
            var index = il.DeclareLocal(_types.Int32);
            var codeUnit = il.DeclareLocal(_types.Char);
            var loop = il.DefineLabel();
            var checkLow = il.DefineLabel();
            var advanceOne = il.DefineLabel();
            var invalid = il.DefineLabel();
            var valid = il.DefineLabel();

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, index);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Bge, valid);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
            il.Emit(OpCodes.Stloc, codeUnit);
            il.Emit(OpCodes.Ldloc, codeUnit);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsHighSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, checkLow);

            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Bge, invalid);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, loop);

            il.MarkLabel(checkLow);
            il.Emit(OpCodes.Ldloc, codeUnit);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
            il.Emit(OpCodes.Brtrue, invalid);
            il.MarkLabel(advanceOne);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, loop);

            il.MarkLabel(invalid);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(valid);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        var toWellFormed = typeBuilder.DefineMethod(
            "StringToWellFormed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]);
        runtime.StringToWellFormed = toWellFormed;
        {
            var il = toWellFormed.GetILGenerator();
            var chars = il.DeclareLocal(_types.CharArray);
            var index = il.DeclareLocal(_types.Int32);
            var codeUnit = il.DeclareLocal(_types.Char);
            var loop = il.DefineLabel();
            var checkLow = il.DefineLabel();
            var advanceOne = il.DefineLabel();
            var replace = il.DefineLabel();
            var done = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "ToCharArray"));
            il.Emit(OpCodes.Stloc, chars);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, index);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, done);

            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldelem_U2);
            il.Emit(OpCodes.Stloc, codeUnit);
            il.Emit(OpCodes.Ldloc, codeUnit);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsHighSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, checkLow);

            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, replace);
            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U2);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, replace);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, loop);

            il.MarkLabel(checkLow);
            il.Emit(OpCodes.Ldloc, codeUnit);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
            il.Emit(OpCodes.Brtrue, replace);
            il.MarkLabel(advanceOne);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, loop);

            il.MarkLabel(replace);
            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4, 0xFFFD);
            il.Emit(OpCodes.Stelem_I2);
            il.Emit(OpCodes.Br, advanceOne);

            il.MarkLabel(done);
            il.Emit(OpCodes.Ldloc, chars);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.CharArray]));
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitStringFromCodePoint(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringFromCodePoint(object[] args) -> string
        // Creates a string from Unicode code points, handling supplementary characters via surrogate pairs
        var method = typeBuilder.DefineMethod(
            "StringFromCodePoint",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );
        runtime.StringFromCodePoint = method;

        var il = method.GetILGenerator();
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var codePointLocal = il.DeclareLocal(_types.Int32);
        var emptyLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // if (args == null || args.Length == 0) return ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Build in amortized linear time. The generated RegExp conformance
        // corpus calls fromCodePoint with 10,000 arguments per chunk; repeated
        // String.Concat here made each chunk quadratic and pushed otherwise
        // trivial compiled tests beyond the worker timeout.
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // codePoint = ToNumber(args[i]). Validate per ECMA-262 22.1.2.2:
        //   - reject NaN (ToNumber on undefined / non-numeric strings)
        //   - reject non-integers (3.14 → throw, even though Conv_I4 would yield 3)
        //   - reject < 0 or > 0x10FFFF (Unicode max)
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, dLocal);

        var validLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        // NaN check (NaN compares unequal to itself)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Bne_Un, throwLabel);
        // Math.Floor(d) != d → non-integer → throw
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Bne_Un, throwLabel);
        // d < 0 → throw
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt, throwLabel);
        // d > 0x10FFFF → throw
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, (double)0x10FFFF);
        il.Emit(OpCodes.Bgt, throwLabel);
        // Valid: Conv_I4
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, codePointLocal);
        il.Emit(OpCodes.Br, validLabel);

        il.MarkLabel(throwLabel);
        // Include the offending value for parity with the interpreter and
        // tsc/V8 (e.g. "Invalid code point -1"). dLocal holds the ToNumber
        // result; route it through ToJsString (ECMA ToString → JS number
        // formatting) so NaN/fractional/out-of-range values print correctly.
        // This sits only on the cold error path, so the box has no hot cost.
        il.Emit(OpCodes.Ldstr, "Invalid code point ");
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSRangeErrorCtor);

        il.MarkLabel(validLabel);

        // result.Append(<UTF16-encoded codePoint>)
        // ECMA-262 §11.1.3: char.ConvertFromUtf32 rejects lone surrogates
        // (0xD800–0xDFFF), but fromCodePoint must emit them as single UTF-16
        // code units. Code points <= 0xFFFF (incl. lone surrogates) become one
        // char; supplementary code points (> 0xFFFF, never a surrogate) go
        // through ConvertFromUtf32.
        il.Emit(OpCodes.Ldloc, resultLocal);
        var supplementaryLabel = il.DefineLabel();
        var segmentReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, codePointLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFFFF);
        il.Emit(OpCodes.Bgt, supplementaryLabel);
        // cp <= 0xFFFF → char.ToString((char)cp)
        il.Emit(OpCodes.Ldloc, codePointLocal);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        il.Emit(OpCodes.Br, segmentReadyLabel);
        // cp > 0xFFFF → char.ConvertFromUtf32(cp)
        il.MarkLabel(supplementaryLabel);
        il.Emit(OpCodes.Ldloc, codePointLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ConvertFromUtf32", _types.Int32));
        il.MarkLabel(segmentReadyLabel);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringIterator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIterator",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        runtime.StringIterator = method;

        var il = method.GetILGenerator();
        var receiverOkLabel = il.DefineLabel();
        var strLocal = il.DeclareLocal(_types.String);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var charLocal = il.DeclareLocal(_types.Char);
        var segmentLocal = il.DeclareLocal(_types.String);
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var singleCodeUnitLabel = il.DefineLabel();
        var segmentReadyLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, receiverOkLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, singleCodeUnitLabel);
        il.MarkLabel(receiverOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype[Symbol.iterator] called on null or undefined");

        // Reuse the label after the receiver guard as the normal entry point.
        il.MarkLabel(singleCodeUnitLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
        il.Emit(OpCodes.Bge, loopEndLabel);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);

        // A valid surrogate pair is yielded as one two-code-unit string.
        var useSingleCodeUnitLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsHighSurrogate", _types.Char));
        il.Emit(OpCodes.Brfalse, useSingleCodeUnitLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
        il.Emit(OpCodes.Bge, useSingleCodeUnitLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
        il.Emit(OpCodes.Brfalse, useSingleCodeUnitLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, segmentLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, segmentReadyLabel);

        il.MarkLabel(useSingleCodeUnitLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        il.Emit(OpCodes.Stloc, segmentLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(segmentReadyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, segmentLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringNormalize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringNormalize(string str, int argCount, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringNormalize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32, _types.ObjectArray]
        );
        runtime.StringNormalize = method;

        var il = method.GetILGenerator();
        var formLocal = il.DeclareLocal(_types.String);
        var nfcLabel = il.DefineLabel();
        var nfdLabel = il.DefineLabel();
        var nfkcLabel = il.DefineLabel();
        var nfkdLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var normFormLocal = il.DeclareLocal(_types.Int32); // NormalizationForm enum value

        // if (argCount == 0) form = "NFC"
        var hasArgLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); // argCount
        il.Emit(OpCodes.Brtrue, hasArgLabel);
        il.Emit(OpCodes.Ldstr, "NFC");
        il.Emit(OpCodes.Stloc, formLocal);
        var afterFormLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterFormLabel);

        // Explicit undefined has the same default as an omitted argument.
        il.MarkLabel(hasArgLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nfcLabel);

        // Otherwise form = ToString(args[0]). This preserves user coercion and
        // guest TypeError propagation for Symbol values.
        il.Emit(OpCodes.Ldarg_2); // args
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, formLocal);

        il.MarkLabel(afterFormLabel);

        // Switch on form string
        il.Emit(OpCodes.Ldloc, formLocal);
        il.Emit(OpCodes.Ldstr, "NFC");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nfcLabel);

        il.Emit(OpCodes.Ldloc, formLocal);
        il.Emit(OpCodes.Ldstr, "NFD");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nfdLabel);

        il.Emit(OpCodes.Ldloc, formLocal);
        il.Emit(OpCodes.Ldstr, "NFKC");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nfkcLabel);

        il.Emit(OpCodes.Ldloc, formLocal);
        il.Emit(OpCodes.Ldstr, "NFKD");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nfkdLabel);

        // Invalid form - throw
        il.Emit(OpCodes.Br, throwLabel);

        // NFC = FormC = 1
        il.MarkLabel(nfcLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, normFormLocal);
        il.Emit(OpCodes.Br, callLabel);

        // NFD = FormD = 2
        il.MarkLabel(nfdLabel);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, normFormLocal);
        il.Emit(OpCodes.Br, callLabel);

        // NFKC = FormKC = 5
        il.MarkLabel(nfkcLabel);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Stloc, normFormLocal);
        il.Emit(OpCodes.Br, callLabel);

        // NFKD = FormKD = 6
        il.MarkLabel(nfkdLabel);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Stloc, normFormLocal);
        il.Emit(OpCodes.Br, callLabel);

        // Call str.Normalize(normForm)
        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Ldloc, normFormLocal);
        var normalizeMethod = _types.GetMethod(_types.String, "Normalize", [typeof(System.Text.NormalizationForm)])!;
        il.Emit(OpCodes.Callvirt, normalizeMethod);
        il.Emit(OpCodes.Ret);

        // Throw RangeError
        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "The normalization form should be one of NFC, NFD, NFKC, NFKD.");
    }

    private void EmitStringLocaleCompare(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringLocaleCompare(string str, string that) -> double
        var method = typeBuilder.DefineMethod(
            "StringLocaleCompare",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.String]
        );
        runtime.StringLocaleCompare = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Int32);
        var negLabel = il.DefineLabel();
        var posLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // ECMA-402 locale comparison treats canonically equivalent Unicode
        // sequences as equal. Normalize both operands before delegating to the
        // host culture comparison so composed/decomposed forms collate alike.
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Normalize"));
        il.Emit(OpCodes.Ldarg_1); // that
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Normalize"));
        il.Emit(OpCodes.Ldc_I4_1); // StringComparison.CurrentCulture = 1
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Compare", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (result < 0) return -1.0
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, negLabel);

        // if (result > 0) return 1.0
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, posLabel);

        // return 0.0
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(negLabel);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(posLabel);
        il.Emit(OpCodes.Ldc_R8, 1.0);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }
}

