using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

// Split out of RuntimeEmitter.CoreUtilities.cs (#1141). Emits the runtime
// operator helpers: relational (<, <=), typeof, instanceof, in, +, ==, ===.
public partial class RuntimeEmitter
{
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

        // Walk: current = PDSGetPrototype(instance); while (current != null) {
        //   if (current === F.prototype) return true;
        //   current = PDSGetPrototype(current); }
        // return false
        var currentLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, currentLocal);

        var loopLabel = il.DefineLabel();
        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // current === F.prototype ?
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldloc, targetProtoLocal);
        il.Emit(OpCodes.Beq, trueLabel);

        // current = PDSGetPrototype(current)
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
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
        var notTaskTargetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, classTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Bne_Un, notTaskTargetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.MarkLabel(notTaskTargetLabel);

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

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();

        // if (obj == null) return false
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        // Note: HasIn signature is (key, obj) so obj is arg_1
        var notProxyLabel = il.DefineLabel();
        EmitProxyHasCheck(il, () => il.Emit(OpCodes.Ldarg_1), () => il.Emit(OpCodes.Ldarg_0), notProxyLabel, runtime);

        il.MarkLabel(notProxyLabel);

        // Check if key is a symbol
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

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

        // Symbol key path
        il.MarkLabel(symbolKeyLabel);
        // Get symbol dict and check if key exists
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
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
        // before the string-vs-numeric branch. UnwrapIfBoxed handles the boxed-
        // primitive case (`new String("x") + "y"` → "xy" instead of
        // "[object Object]y"); plain $Object operands pass through unchanged
        // and continue to the existing Stringify path which calls .ToString().
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

        // Either operand $Undefined → NaN (ECMA-262 12.8.3: ToNumber(undefined) = NaN,
        // and any arithmetic with NaN yields NaN). Convert.ToDouble($Undefined) throws
        // because $Undefined isn't IConvertible; short-circuit here.
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedNanLabel);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedNanLabel);

        // Numeric addition
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
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
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var checkRightNullish = il.DefineLabel();
        var notBothNullish = il.DefineLabel();
        var objectEqualsLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // ECMA-262 §7.2.14 step 11/12: when one operand is an Object and the
        // other a primitive, IsLooselyEqual delegates to ToPrimitive on the
        // Object then re-runs. For boxed-primitive wrappers the spec'd
        // OrdinaryToPrimitive lands at __primitiveValue via valueOf, so unwrap
        // upfront and let the existing primitive-vs-primitive logic do the
        // rest. Plain $Object operands without a __primitiveType marker pass
        // through unchanged (UnwrapIfBoxed is a no-op there) and continue to
        // the existing Dict/$Object-vs-primitive ToNumber path below, which
        // handles `Number.prototype == 0` and similar.
        var leftLocal = il.DeclareLocal(_types.Object);
        var rightLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
        il.Emit(OpCodes.Stloc, leftLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.UnwrapIfBoxedMethod);
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

        // If both are nullish, return true (null == undefined)
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // If only one is nullish, return false
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brtrue, falseLabel);

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

        // Neither is nullish - use object.Equals
        il.Emit(OpCodes.Ldloc, leftLocal);
        il.Emit(OpCodes.Ldloc, rightLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

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
