using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

// Split out of RuntimeEmitter.CoreUtilities.cs (#1141). Emits the runtime
// operator helpers: relational (<, <=), typeof, instanceof, in, +, ==, ===.
public partial class RuntimeEmitter
{
    /// <summary>
    /// ToNumeric update helper used by ++/-- on object-typed storage. BigInt
    /// remains BigInt; all other values follow ToNumber and return a Number.
    /// </summary>
    private void EmitUpdateNumeric(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UpdateNumeric",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Boolean]);
        runtime.UpdateNumeric = method;
        var il = method.GetILGenerator();
        var numberPath = il.DefineLabel();
        var subtractBigInt = il.DefineLabel();
        var subtractNumber = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, numberPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "One")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, subtractBigInt);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Addition",
            _types.BigInteger, _types.BigInteger));
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(subtractBigInt);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Subtraction",
            _types.BigInteger, _types.BigInteger));
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(numberPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, subtractNumber);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(subtractNumber);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.JsLessThan(object x, object y) -&gt; bool</c>:
    /// ECMA-262 7.2.13 IsLessThan abstract algorithm (LeftFirst=true).
    /// If both operands are strings, lexicographic comparison.
    /// Otherwise both are coerced via ToNumber and numerically compared
    /// (NaN on either side yields false).
    /// </summary>
    private void EmitJsLessThan(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsLessThan",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.JsLessThan = method;

        var il = method.GetILGenerator();

        // Native integer loop counters are an internal optimization, but they
        // are still JavaScript Numbers. Normalize them before the runtime type
        // dispatch so mixed BigInt/Number comparisons take the same path as a
        // boxed double instead of falling through to ToNumber(BigInt).
        var leftCounterNormalized = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int64);
        il.Emit(OpCodes.Brfalse, leftCounterNormalized);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Int64);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Starg_S, 0);
        il.MarkLabel(leftCounterNormalized);

        var rightCounterNormalized = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int64);
        il.Emit(OpCodes.Brfalse, rightCounterNormalized);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int64);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Starg_S, 1);
        il.MarkLabel(rightCounterNormalized);

        // Mixed BigInt/Number relational comparison is permitted by
        // IsLessThan (unlike arithmetic mixing). Handle the boxed CLR shapes
        // before the ordinary ToNumber path, which correctly rejects BigInt.
        // Comparing against the truncated integer first preserves the result
        // around negative fractional Numbers without converting the BigInt to
        // an imprecise double.
        var normalComparison = il.DefineLabel();
        var leftBigIntNumber = il.DefineLabel();
        var rightBigIntNumber = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        var leftNotBigInt = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, leftNotBigInt);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, leftBigIntNumber);
        il.Emit(OpCodes.Br, normalComparison);
        il.MarkLabel(leftNotBigInt);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, normalComparison);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, rightBigIntNumber);
        il.Emit(OpCodes.Br, normalComparison);

        var bigIntegerCtorDouble = _types.GetConstructor(_types.BigInteger, _types.Double);
        var bigIntegerLess = _types.GetMethod(_types.BigInteger, "op_LessThan",
            _types.BigInteger, _types.BigInteger);
        var bigIntegerGreater = _types.GetMethod(_types.BigInteger, "op_GreaterThan",
            _types.BigInteger, _types.BigInteger);
        var mixedBigInt = il.DeclareLocal(_types.BigInteger);
        var mixedNumber = il.DeclareLocal(_types.Double);
        var truncatedBigInt = il.DeclareLocal(_types.BigInteger);

        // BigInt < Number
        il.MarkLabel(leftBigIntNumber);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Stloc, mixedBigInt);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, mixedNumber);
        var leftFinite = il.DefineLabel();
        var mixedReturnFalse = il.DefineLabel();
        var mixedReturnTrue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, mixedReturnFalse);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, mixedReturnTrue);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, leftFinite);
        il.Emit(OpCodes.Br, mixedReturnFalse);
        il.MarkLabel(leftFinite);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Newobj, bigIntegerCtorDouble);
        il.Emit(OpCodes.Stloc, truncatedBigInt);
        il.Emit(OpCodes.Ldloc, mixedBigInt);
        il.Emit(OpCodes.Ldloc, truncatedBigInt);
        il.Emit(OpCodes.Call, bigIntegerLess);
        il.Emit(OpCodes.Brtrue, mixedReturnTrue);
        il.Emit(OpCodes.Ldloc, mixedBigInt);
        il.Emit(OpCodes.Ldloc, truncatedBigInt);
        il.Emit(OpCodes.Call, bigIntegerGreater);
        il.Emit(OpCodes.Brtrue, mixedReturnFalse);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ret);

        // Number < BigInt
        il.MarkLabel(rightBigIntNumber);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Stloc, mixedBigInt);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, mixedNumber);
        var rightFinite = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, mixedReturnFalse);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, mixedReturnTrue);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, rightFinite);
        il.Emit(OpCodes.Br, mixedReturnFalse);
        il.MarkLabel(rightFinite);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Newobj, bigIntegerCtorDouble);
        il.Emit(OpCodes.Stloc, truncatedBigInt);
        il.Emit(OpCodes.Ldloc, truncatedBigInt);
        il.Emit(OpCodes.Ldloc, mixedBigInt);
        il.Emit(OpCodes.Call, bigIntegerLess);
        il.Emit(OpCodes.Brtrue, mixedReturnTrue);
        il.Emit(OpCodes.Ldloc, truncatedBigInt);
        il.Emit(OpCodes.Ldloc, mixedBigInt);
        il.Emit(OpCodes.Call, bigIntegerGreater);
        il.Emit(OpCodes.Brtrue, mixedReturnFalse);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Ldloc, mixedNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(mixedReturnTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(mixedReturnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(normalComparison);

        // If both args are strings, do lexicographic comparison (CompareOrdinal < 0).
        var notBothStrings = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notBothStrings);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notBothStrings);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        var compareOrdinal = _types.GetMethod(_types.String, "CompareOrdinal", _types.String, _types.String);
        il.Emit(OpCodes.Call, compareOrdinal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBothStrings);
        // Numeric path: a = ToNumber(arg0); b = ToNumber(arg1); a < b ? true : false (NaN → false).
        var aLocal = il.DeclareLocal(_types.Double);
        var bLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, aLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, bLocal);
        // NaN check: a == a, b == b
        var notNaN = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.MarkLabel(notNaN);
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.JsLessOrEqual(object x, object y) -&gt; bool</c>:
    /// ECMA-262 abstract relational comparison: x &lt;= y is "y &lt; x is false
    /// AND neither operand is NaN". Implemented as !JsLessThan(y, x) provided
    /// neither is NaN; we replicate the helper inline to avoid double ToNumber.
    /// </summary>
    private void EmitJsLessOrEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsLessOrEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.JsLessOrEqual = method;

        var il = method.GetILGenerator();

        // If both strings: CompareOrdinal <= 0
        var notBothStrings = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notBothStrings);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notBothStrings);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        var compareOrdinal = _types.GetMethod(_types.String, "CompareOrdinal", _types.String, _types.String);
        il.Emit(OpCodes.Call, compareOrdinal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBothStrings);
        var aLocal = il.DeclareLocal(_types.Double);
        var bLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, aLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, bLocal);
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);
        // a <= b → !(a > b) → !(b < a)
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.TypeOf = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var symbolLabel = il.DefineLabel();
        var functionLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // null => "object" (JS typeof null === "object")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // undefined => "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // Check for union types using $IUnionType marker interface
        // If value implements $IUnionType, unwrap via Value property and recurse
        var notUnionLabel = il.DefineLabel();
        var unionLocal = il.DeclareLocal(runtime.IUnionTypeInterface);

        // Check: if (value is $IUnionType union)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IUnionTypeInterface);
        il.Emit(OpCodes.Stloc, unionLocal);
        il.Emit(OpCodes.Ldloc, unionLocal);
        il.Emit(OpCodes.Brfalse, notUnionLabel);

        // Get underlying value via interface: union.Value
        il.Emit(OpCodes.Ldloc, unionLocal);
        il.Emit(OpCodes.Callvirt, runtime.IUnionTypeValueGetter);

        // return TypeOf(underlyingValue) - recursive call
        il.Emit(OpCodes.Call, method);  // Recursive call to self
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notUnionLabel);

        // bool => "boolean"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // double => "number"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // string => "string"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // TSSymbol => "symbol"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, symbolLabel);

        // TSFunction => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $BoundTSFunction => "function" (returned by Function.prototype.bind
        // and similar paths; without this, `typeof fn.bind(x) === "object"`).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $FunctionBindWrapper / $FunctionCallWrapper / $FunctionApplyWrapper
        // => "function". These wrap non-$TSFunction targets for late-bound
        // dispatch and need to be callable from JS land.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brtrue, functionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brtrue, functionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        if (_features.UsesPromise)
        {
            // Promise resolving functions are callable built-ins even though
            // their optimized CLR representation is not a $TSFunction/delegate.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
            il.Emit(OpCodes.Brtrue, functionLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
            il.Emit(OpCodes.Brtrue, functionLabel);
        }

        // Delegate => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Delegate);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $BoundArrayMethod / $BoundMapMethod / $BoundSetMethod / $BoundAnyFunction => "function"
        // These are callable wrappers returned by GetListProperty/GetMapProperty/GetSetProperty
        // for dynamic property access on arrays/maps/sets (duck typing across module boundaries)
        // and by `.bind` on non-$TSFunction targets.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brtrue, functionLabel);
        }

        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brtrue, functionLabel);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // System.Type => "function"
        // Compiled class references (e.g. `const f = Foo` where Foo is a class) are
        // emitted as Ldtoken + GetTypeFromHandle, which yields a System.Type. Node/JS
        // spec says classes are functions, so `typeof Foo === 'function'` must hold.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $CJSModule => "object" (falls through naturally, but explicit null-isinst checks
        // above might have short-circuited — this branch ensures consistent routing).
        // No early return needed; falls through to the generic "object" default at the end.

        // BigInteger => "bigint"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Proxy => check IsCallable: "function" if callable, "object" otherwise
        // Uses FullName comparison to avoid SharpTS.dll dependency
        var notProxyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "FullName").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, ProxyTypeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notProxyLabel);

        // It's a proxy - check IsCallable property getter via reflection
        EmitProxyMethodCall(il, () => il.Emit(OpCodes.Ldarg_0), "get_IsCallable", () =>
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
        });
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, functionLabel);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notProxyLabel);

        // Default => "object"
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "string");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(symbolLabel);
        il.Emit(OpCodes.Ldstr, "symbol");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(functionLabel);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldstr, "bigint");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitInstanceOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InstanceOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.InstanceOf = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // if (instance == null || classType == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // A plain Dictionary RHS is never a constructor — letting it reach the
        // IsAssignableFrom fallback matched EVERY dict-shaped value (object
        // literals, namespace singletons, module namespaces), so
        // `{} instanceof AbortSignal` was true (#246). The AbortSignal
        // namespace singleton brand-checks signal dicts via their
        // "_reasonSet" slot; any other dict RHS is false.
        var notDictRhsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictRhsLabel);

        if (runtime.AbortSignalNamespaceField != null)
        {
            var lhsDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldsfld, runtime.AbortSignalNamespaceField);
            il.Emit(OpCodes.Bne_Un, falseLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, lhsDictLocal);
            il.Emit(OpCodes.Ldloc, lhsDictLocal);
            il.Emit(OpCodes.Brfalse, falseLabel);
            il.Emit(OpCodes.Ldloc, lhsDictLocal);
            il.Emit(OpCodes.Ldstr, "_reasonSet");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Ret);
        }
        else
        {
            il.Emit(OpCodes.Br, falseLabel);
        }

        il.MarkLabel(notDictRhsLabel);

        // Per JS spec, `instance instanceof F` where F is a user function walks
        // instance's prototype chain looking for F.prototype. Compiled mode's
        // legacy InstanceOf used .NET IsAssignableFrom, which is type-system
        // semantics — wrong for $TSFunction callees (every $TSFunction has the
        // same .NET type, so the check was meaningless). With Stage 0b/0c
        // landed, F.prototype is a real $Object and `new F()` links newObj's
        // prototype to it; walking the chain via PDSGetPrototype now produces
        // the correct answer.
        var notTSFuncLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFuncLabel);

        // F.prototype = $Runtime.GetFunctionMethod(F, "prototype")
        var targetProtoLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Stloc, targetProtoLocal);

        // If F has no .prototype somehow (e.g., bound function), fall back to
        // false rather than walking — JS spec actually throws TypeError, but
        // returning false matches what the previous implementation produced.
        il.Emit(OpCodes.Ldloc, targetProtoLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Walk through [[GetPrototypeOf]] so Proxy traps participate and their
        // non-extensible-target invariant is enforced.
        // current = ObjectGetPrototypeOf(instance); while (current != null) {
        //   if (current === F.prototype) return true;
        //   current = PDSGetPrototype(current); }
        // return false
        var currentLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);

        var loopLabel = il.DefineLabel();
        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // current === F.prototype ?
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldloc, targetProtoLocal);
        il.Emit(OpCodes.Beq, trueLabel);

        // current = current.[[GetPrototypeOf]]
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(notTSFuncLabel);

        // Get type of instance and check IsAssignableFrom (legacy path for
        // .NET-typed class-reference instanceof checks).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Type);
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTypeLabel);

        // Stage 4z19 boxed-primitive detection: when classType is one of the
        // primitive types (Boolean/Double/String) and instance is a $Object
        // wrapper with __primitiveType matching, return true. Only applies
        // when the legacy IsAssignableFrom would say false (since .NET
        // System.Boolean/Double/String are sealed value types, IsAssignableFrom
        // for a $Object always returns false; checking the marker comes first
        // to short-circuit).
        var classTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Stloc, classTypeLocal);

        // `x instanceof Object` — the bare `Object` identifier resolves to the
        // System.Object Type token (see RuntimeEmitter.GlobalThis.cs), so apply
        // JS Object semantics rather than the IsAssignableFrom fallback below:
        // every .NET type is assignable to System.Object, so that fallback would
        // wrongly say true for boxed primitives (double/bool/string), BigInteger,
        // $TSSymbol, and the undefined sentinel. Per ECMA-262 OrdinaryHasInstance
        // a primitive O short-circuits to false — so a guest value is an Object
        // iff it is non-primitive (mirrors interp's IsJsObject, #342). Boxed
        // primitive wrappers (`new Number(5)`) are $Object instances, not boxed
        // doubles, so they correctly fall through to true.
        var notObjectClassLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, classTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Bne_Un, notObjectClassLabel);
        void PrimitiveToFalse(Type primType)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, primType);
            il.Emit(OpCodes.Brtrue, falseLabel);
        }
        PrimitiveToFalse(_types.Boolean);
        PrimitiveToFalse(_types.Double);
        PrimitiveToFalse(_types.String);
        PrimitiveToFalse(_types.BigInteger);
        PrimitiveToFalse(runtime.TSSymbolType);
        PrimitiveToFalse(runtime.UndefinedType);
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(notObjectClassLabel);

        // When classType is one of the primitive wrapper types (Number/String/
        // Boolean, which lower to the System.Double/String/Boolean Type tokens),
        // the decision is TERMINAL: per ECMA-262 OrdinaryHasInstance only a boxed
        // wrapper object (`new Number(5)`) is an instance — a bare primitive is
        // NOT. Branch to true iff `instance` carries the matching __primitiveType
        // marker, else to false. Falling through to the IsAssignableFrom fallback
        // below would wrongly match a bare boxed double/string/bool, since e.g.
        // IsAssignableFrom(double, double) is true (#375). Mirrors the terminal
        // `Object` branch above.
        void CheckBoxed(Type primType, string typeTag)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, classTypeLocal);
            il.Emit(OpCodes.Ldtoken, primType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, skip);
            // classType is the primitive wrapper type — true iff boxed marker matches.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, typeTag);
            il.Emit(OpCodes.Call, runtime.IsBoxedPrimitiveOfTypeMethod);
            il.Emit(OpCodes.Brtrue, trueLabel);
            il.Emit(OpCodes.Br, falseLabel);
            il.MarkLabel(skip);
        }
        CheckBoxed(_types.Boolean, "Boolean");
        CheckBoxed(_types.Double,  "Number");
        CheckBoxed(_types.String,  "String");
        CheckBoxed(_types.BigInteger, "BigInt");
        // Symbol lowers to the $TSSymbol Type token, so a bare $TSSymbol would
        // otherwise reach the IsAssignableFrom($TSSymbol, $TSSymbol) fallback and
        // wrongly match. Terminal like the wrappers above: true iff `instance` is
        // a boxed Symbol wrapper (`Object(sym)`), false for a bare symbol (#449).
        CheckBoxed(runtime.TSSymbolType, "Symbol");

        // `x instanceof Promise`: the Promise identifier resolves to
        // typeof(Task<object?>), but $Promise instances (and #242 Promise
        // subclasses, which derive from $Promise) wrap their task instead of
        // being one — accept them here; raw tasks fall through to the
        // IsAssignableFrom below.
        if (_features.UsesPromise)
        {
            var notTaskTargetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, classTypeLocal);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notTaskTargetLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
            il.Emit(OpCodes.Brtrue, trueLabel);
            il.MarkLabel(notTaskTargetLabel);
        }

        // Generic class target: `b instanceof Box` emits the OPEN generic
        // definition (Box`1) while instances carry constructed types
        // (Box<object>) — IsAssignableFrom never matches across that gap.
        // Walk the instance's base-type chain comparing generic definitions.
        var notGenericDefLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, classTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericTypeDefinition")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notGenericDefLabel);
        var walkTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, walkTypeLocal);
        var genericWalkLoop = il.DefineLabel();
        var genericWalkNext = il.DefineLabel();
        il.MarkLabel(genericWalkLoop);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, genericWalkNext);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ldloc, classTypeLocal);
        il.Emit(OpCodes.Beq, trueLabel);
        il.MarkLabel(genericWalkNext);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, walkTypeLocal);
        il.Emit(OpCodes.Br, genericWalkLoop);
        il.MarkLabel(notGenericDefLabel);

        // classType is Type, use it directly
        il.Emit(OpCodes.Ldloc, classTypeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTypeLabel);
        // classType is not Type, get its type
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits HasIn(object key, object obj) -> bool
    /// Implements the JavaScript 'in' operator: checks if a property exists in an object.
    /// Handles both symbol keys and string keys.
    /// </summary>
    private void EmitHasIn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HasIn",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.HasIn = method;
        runtime.ProxyOrdinaryHas = typeBuilder.DefineMethod(
            "ProxyOrdinaryHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var invalidRhsLabel = il.DefineLabel();
        var validRhsLabel = il.DefineLabel();

        // The RHS of `in` must be an Object. Null, undefined, and all other
        // primitives throw a guest TypeError rather than returning false.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, invalidRhsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, invalidRhsLabel);
        var rhsType = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Stloc, rhsType);
        il.Emit(OpCodes.Ldloc, rhsType);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, validRhsLabel);
        il.Emit(OpCodes.Ldloc, rhsType);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, invalidRhsLabel);
        il.MarkLabel(validRhsLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        // Note: HasIn signature is (key, obj) so obj is arg_1
        var notProxyLabel = il.DefineLabel();
        EmitProxyHasCheck(il, () => il.Emit(OpCodes.Ldarg_1), () => il.Emit(OpCodes.Ldarg_0), notProxyLabel, runtime);

        il.MarkLabel(notProxyLabel);

        // Check if key is a symbol
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // Ask [[GetOwnProperty]] before representation-specific fallbacks.
        // This covers intrinsic own properties such as boxed-string indices,
        // RegExp.lastIndex, and Function name/length.
        var noOrdinaryOwnDescriptorLabel = il.DefineLabel();
        var hasInDescriptorLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, hasInDescriptorLocal);
        il.Emit(OpCodes.Ldloc, hasInDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noOrdinaryOwnDescriptorLabel);
        il.Emit(OpCodes.Ldloc, hasInDescriptorLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noOrdinaryOwnDescriptorLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noOrdinaryOwnDescriptorLabel);

        // Use the shared HasProperty walk first for every ordinary property
        // key.  It covers own PDS-only accessors and prototype-chain entries
        // on dictionaries, $Object, arrays, and list-backed arguments.  The
        // type-specific code below remains as the fallback for emitted class
        // fields and CLR-reflected methods.
        var continueSpecializedHasIn = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, continueSpecializedHasIn);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(continueSpecializedHasIn);

        // String key path
        // Check if obj is $TSObject
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        // $TSObject - call HasProperty(string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        il.Emit(OpCodes.Ret);

        // Check if obj is Dictionary<string, object>
        il.MarkLabel(notTSObjectLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $Array (check BEFORE the plain List check — $Array inherits
        // List<object?>; the List branch below reads base.Count and misses
        // sparse holes, and returns true for a hole index where it should
        // return false).
        var tsArrayHasLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayHasLabel);

        // Check if obj is List<object> (array)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Other types (e.g., emitted class instances) — check via $IHasFields + reflection
        var classKeyStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, classKeyStrLocal);

        var classTrueLabel = il.DefineLabel();

        // Check $IHasFields interface: call HasProperty(key) for typed backing fields + _fields dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldloc, classKeyStrLocal);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsHasProperty);
        il.Emit(OpCodes.Brtrue, classTrueLabel);

        il.MarkLabel(notHasFieldsLabel);

        // Also check for methods via reflection (e.g., inherited methods)
        // Convert camelCase key to PascalCase for .NET method lookup
        var classPascalNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, classKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);
        il.Emit(OpCodes.Stloc, classPascalNameLocal);

        // obj.GetType().GetProperty(pascalName, Instance | Public | IgnoreCase)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldloc, classPascalNameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(System.Reflection.BindingFlags)));
        il.Emit(OpCodes.Brtrue, classTrueLabel);

        // obj.GetType().GetMethod(pascalName, Instance | Public | IgnoreCase)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldloc, classPascalNameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(System.Reflection.BindingFlags)));
        il.Emit(OpCodes.Brtrue, classTrueLabel);

        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(classTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Dictionary - use ContainsKey
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Ret);

        // List (array) - check "length" property or numeric index
        il.MarkLabel(listLabel);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var listKeyStrLocal = il.DeclareLocal(_types.String);

        // Convert key to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, listKeyStrLocal);

        // Check if key == "length" → return true
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listKeyStrLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notLengthLabel);
        // Try int.TryParse(key, out index) → if fails, return false
        il.Emit(OpCodes.Ldloc, listKeyStrLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, falseLabel);

        // index >= 0 && index < list.Count
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, falseLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, falseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // $Array — "length" is always present; numeric keys use TSArrayHasIndex
        // (which returns false for holes, unlike the List branch's index-in-
        // range check). Non-numeric named keys aren't stored on arrays, so
        // fall back to false.
        il.MarkLabel(tsArrayHasLabel);
        {
            var tsArrKeyStrLocal = il.DeclareLocal(_types.String);
            var tsArrIndexLocal = il.DeclareLocal(_types.Int64);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Stloc, tsArrKeyStrLocal);

            // Array exotic objects still have ordinary own properties in the
            // descriptor store.  An accessor over a hole (for example index 2
            // installed with Object.defineProperty) is present for `in` even
            // though dense storage reports no element at that index.
            var tsArrNoOwnDescriptor = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, tsArrKeyStrLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Brfalse, tsArrNoOwnDescriptor);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrNoOwnDescriptor);

            // if (key == "length") return true
            var tsArrNotLength = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsArrKeyStrLocal);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, tsArrNotLength);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsArrNotLength);
            // long.TryParse(key, out idx) — if fails, key isn't numeric → false.
            il.Emit(OpCodes.Ldloc, tsArrKeyStrLocal);
            il.Emit(OpCodes.Ldloca, tsArrIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "TryParse", _types.String, _types.Int64.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, falseLabel);

            // arr.HasIndex(idx) — handles sparse + hole semantics.
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, tsArrIndexLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
            il.Emit(OpCodes.Ret);
        }

        // Symbol key path. Check the own symbol dictionary, then continue up
        // [[Prototype]] so well-known symbol methods are inherited normally.
        il.MarkLabel(symbolKeyLabel);
        var symbolNotOwnLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brfalse, symbolNotOwnLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolNotOwnLabel);
        var symbolPrototypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, symbolPrototypeLocal);
        il.Emit(OpCodes.Ldloc, symbolPrototypeLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, symbolPrototypeLocal);
        il.Emit(OpCodes.Call, runtime.HasIn);
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidRhsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Right-hand side of 'in' is not an object");

        // SharpTSProxy's compiled callback uses the natural (target, key)
        // order, while the emitted `in` helper uses (key, target).
        var proxyOrdinaryHasIl = runtime.ProxyOrdinaryHas.GetILGenerator();
        proxyOrdinaryHasIl.Emit(OpCodes.Ldarg_1);
        proxyOrdinaryHasIl.Emit(OpCodes.Ldarg_0);
        proxyOrdinaryHasIl.Emit(OpCodes.Call, runtime.HasIn);
        proxyOrdinaryHasIl.Emit(OpCodes.Ret);
    }

    private void EmitAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Add",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.Add = method;

        var il = method.GetILGenerator();
        var stringConcatLabel = il.DefineLabel();
        var undefinedNanLabel = il.DefineLabel();

        // ECMA-262 §13.10.1 step 1-2: ToPrimitive both operands (default hint)
        // before the string-vs-numeric branch. UnwrapIfBoxed handles boxed
        // primitives and the observable valueOf/toString conversion for plain
        // objects (`new String("x") + "y"` → "xy" instead of
        // "[object Object]y").
        var leftLocal = il.DeclareLocal(_types.Object);
        var rightLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
        il.Emit(OpCodes.Stloc, leftLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
        il.Emit(OpCodes.Stloc, rightLocal);

        // if (left is string || right is string) string concat
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringConcatLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringConcatLabel);

        // ToNumeric preserves BigInt. Runtime-typed BigInt expressions (for
        // example a loop variable plus a BigInt literal in Test262) do not
        // reach the statically specialized emitter, so handle them here too.
        // Mixing BigInt and Number is a TypeError; two BigInts add exactly.
        var leftNotBigInt = il.DefineLabel();
        var addBigInts = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, leftNotBigInt);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, addBigInts);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot mix BigInt and other types");

        il.MarkLabel(leftNotBigInt);
        var neitherBigInt = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, neitherBigInt);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot mix BigInt and other types");

        il.MarkLabel(addBigInts);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Addition",
            _types.BigInteger, _types.BigInteger));
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(neitherBigInt);

        // Either operand $Undefined → NaN (ECMA-262 12.8.3: ToNumber(undefined) = NaN,
        // and any arithmetic with NaN yields NaN). Convert.ToDouble($Undefined) throws
        // because $Undefined isn't IConvertible; short-circuit here.
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedNanLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedNanLabel);

        // Numeric addition.  Use the language ToNumber operation, not CLR
        // Convert.ToDouble: Date/Array/plain-object operands require
        // ToPrimitive, Symbol must throw a guest TypeError, and undefined is
        // NaN rather than an InvalidCastException.
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(undefinedNanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // String concat - StringifyCoerce: JS-compatible conversion (null->"null",
        // bool->"true"/"false") that throws TypeError for Symbol operands (§7.1.17).
        il.MarkLabel(stringConcatLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.StringifyCoerce);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.StringifyCoerce);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Declares the Equals MethodBuilder shell. Body fills in via
    /// <see cref="EmitEquals"/>, which must run AFTER EmitToJsString so the
    /// Object-vs-String spec branch can reference <c>runtime.ToJsString</c>.
    /// </summary>
    internal void DeclareEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.Equals = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
    }

    private void EmitEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.Equals;

        var il = method.GetILGenerator();
        var checkRightNullish = il.DefineLabel();
        var notBothNullish = il.DefineLabel();
        var objectEqualsLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Retain the original operands until nullish and object-like
        // classification is complete. UnwrapIfBoxed performs observable
        // ToPrimitive work for plain objects, so calling it before these checks
        // would incorrectly invoke coercion hooks for object == null and for
        // object == object.
        var leftLocal = il.DeclareLocal(_types.Object);
        var rightLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, leftLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, rightLocal);

        // Local to track if left is nullish
        var leftNullish = il.DeclareLocal(_types.Boolean);
        var rightNullish = il.DeclareLocal(_types.Boolean);

        // Check if left is nullish (null or undefined)
        // leftNullish = (left == null || left is SharpTSUndefined)
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Brfalse_S, checkRightNullish); // left is null
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un); // true if left is SharpTSUndefined
        il.Emit(OpCodes.Stloc, leftNullish);
        il.Emit(OpCodes.Br_S, notBothNullish);

        il.MarkLabel(checkRightNullish);
        // Left is null - mark as nullish
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, leftNullish);

        il.MarkLabel(notBothNullish);

        // Check if right is nullish (null or undefined)
        // rightNullish = (right == null || right is SharpTSUndefined)
        var rightNotNull = il.DefineLabel();
        var afterRightCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Brtrue_S, rightNotNull);
        // Right is null - mark as nullish
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, rightNullish);
        il.Emit(OpCodes.Br_S, afterRightCheck);

        il.MarkLabel(rightNotNull);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un); // true if right is SharpTSUndefined
        il.Emit(OpCodes.Stloc, rightNullish);

        il.MarkLabel(afterRightCheck);

        // Nullish combinations are terminal and must not reach ToPrimitive.
        // If both are nullish, return true (null == undefined).
        var notBothNullishLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notBothNullishLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBothNullishLabel);

        // If only one is nullish, return false.
        var neitherNullishLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brfalse, neitherNullishLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(neitherNullishLabel);

        // Classify every non-nullish value through the runtime's JS typeof
        // implementation. Both "object" and "function" are object-like here,
        // covering $TSFunction, bound functions, delegates, class references,
        // and object-valued union wrappers without duplicating TypeOf's catalog.
        var leftObjectLike = il.DeclareLocal(_types.Boolean);
        var rightObjectLike = il.DeclareLocal(_types.Boolean);
        void EmitObjectLikeClassification(LocalBuilder operand, LocalBuilder result)
        {
            var typeOfLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldloc, operand);
            il.Emit(OpCodes.Call, runtime.TypeOf);
            il.Emit(OpCodes.Stloc, typeOfLocal);
            il.Emit(OpCodes.Ldloc, typeOfLocal);
            il.Emit(OpCodes.Ldstr, "object");
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Ldloc, typeOfLocal);
            il.Emit(OpCodes.Ldstr, "function");
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, result);
        }

        EmitObjectLikeClassification(leftLocal, leftObjectLike);
        EmitObjectLikeClassification(rightLocal, rightObjectLike);

        // IsLooselyEqual compares two objects by identity/equality without
        // ToPrimitive, even when either object is callable.
        il.Emit(OpCodes.Ldloc, leftObjectLike);
        il.Emit(OpCodes.Ldloc, rightObjectLike);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brtrue, objectEqualsLabel);

        // ECMA-262 §7.2.14 steps 11/12: only the object operand is converted
        // when exactly one side is object-like and the other is a non-nullish
        // primitive. Primitive/primitive comparisons skip this observable path.
        var checkRightObjectLikeLabel = il.DefineLabel();
        var operandsReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftObjectLike);
        il.Emit(OpCodes.Brfalse, checkRightObjectLikeLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
        il.Emit(OpCodes.Stloc, leftLocal);
        il.Emit(OpCodes.Br, operandsReadyLabel);

        il.MarkLabel(checkRightObjectLikeLabel);
        il.Emit(OpCodes.Ldloc, rightObjectLike);
        il.Emit(OpCodes.Brfalse, operandsReadyLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
        il.Emit(OpCodes.Stloc, rightLocal);
        il.MarkLabel(operandsReadyLabel);

        // ECMA-262 7.2.14 IsLooselyEqual: when one side is a Dictionary/$Object
        // and the other is a String, Number, or Boolean, the spec calls
        // ToPrimitive(object) then recursively compares. Two cases for the
        // primitive type matter:
        //   - String: compare as strings (ToJsString fires the same ToPrimitive
        //     valueOf/toString chain ToNumber would, but yields a string —
        //     `new String("one") == "one"` returns true because
        //     ToPrimitive(wrapper) is "one", then "one" === "one").
        //   - Number/Boolean: compare as numbers (ToNumber on both sides;
        //     ToNumber(boolean)=0/1 per spec; ToNumber on the object does
        //     ToPrimitive(hint number) then ToNumber).
        // Without the String split, `wrapper == "non-numeric"` was always
        // false because ToNumber("non-numeric")=NaN and NaN!==NaN.

        // If LEFT is Dict/$Object and RIGHT is double/string/bool → coerce LEFT.
        var notLeftCoercibleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var leftIsDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, leftIsDictLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notLeftCoercibleLabel);
        il.MarkLabel(leftIsDictLabel);
        // Right is String → ToJsString(LEFT) and string-compare.
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        var leftObjVsStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, leftObjVsStringLabel);
        // Right is double/bool → ToNumber both and Ceq.
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var leftObjVsNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, leftObjVsNumLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notLeftCoercibleLabel);
        il.MarkLabel(leftObjVsNumLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);
        // Object-vs-String: ToJsString(LEFT) and string-compare via op_Equality.
        il.MarkLabel(leftObjVsStringLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notLeftCoercibleLabel);

        // Symmetric: RIGHT is Dict/$Object and LEFT is primitive.
        var notRightCoercibleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var rightIsDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, rightIsDictLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notRightCoercibleLabel);
        il.MarkLabel(rightIsDictLabel);
        // Left is String → ToJsString(RIGHT) and string-compare.
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        var rightObjVsStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, rightObjVsStringLabel);
        // Left is double/bool → ToNumber both and Ceq.
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var rightObjVsNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, rightObjVsNumLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notRightCoercibleLabel);
        il.MarkLabel(rightObjVsNumLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);
        // String-vs-Object: ToJsString(RIGHT) and string-compare.
        il.MarkLabel(rightObjVsStringLabel);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notRightCoercibleLabel);

        // Primitive equality and object identity/equality share the existing
        // Object.Equals terminal path. Object/object branches arrive here with
        // the untouched original operands.
        il.MarkLabel(objectEqualsLabel);
        // CLR Double.Equals considers NaN equal to itself; JavaScript Number
        // equality does not. `ceq` has the required IEEE behavior.
        var notBothNumbers = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notBothNumbers);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notBothNumbers);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notBothNumbers);
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStrictEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 IsStrictlyEqual semantics: null/undefined are distinct values
        // (unlike loose ==). Used by Array.prototype.indexOf/lastIndexOf/includes,
        // which all forbid null/undefined unification per spec.
        var method = typeBuilder.DefineMethod(
            "StrictEquals",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.StrictEquals = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        var leftIsNull = il.DefineLabel();
        var leftNotUndef = il.DefineLabel();

        // If left is CLR null → match iff right is CLR null.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, leftIsNull);

        // If left is $Undefined → match iff right is $Undefined.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, leftNotUndef);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(leftNotUndef);
        // Left is non-null, non-undefined. If right is null or undefined → false.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);

        // ECMA-262 IsStrictlyEqual: NaN !== NaN. Object.Equals(NaN, NaN) is true
        // in .NET (Double.Equals special-cases NaN as equal to itself), so test
        // upfront via double.IsNaN. Pre-fix `[NaN].indexOf(NaN)` returned 0
        // instead of -1.
        var notDoubleSEqLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleSEqLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleSEqLabel);
        // Both are double — if either is NaN, return false.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.MarkLabel(notDoubleSEqLabel);

        // Both are concrete values — defer to Object.Equals (handles double,
        // string, reference equality for objects).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(leftIsNull);
        // Left is CLR null. Match iff right is also CLR null (NOT $Undefined).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
