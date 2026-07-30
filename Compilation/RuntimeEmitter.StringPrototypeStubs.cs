using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits stub <c>$Runtime.StringTo*</c> / <c>StringTrim*</c> helpers used
    /// only for <c>$TSFunction</c> wrapping in <see cref="EmitStringPrototypePopulate"/>.
    /// The hot path for <c>"abc".toUpperCase()</c> is inline-emitted by
    /// <c>StringEmitter</c>, so these stubs aren't called from compiled
    /// user code — they exist purely so <c>String.prototype.toUpperCase</c>
    /// can be referenced as a value (typeof === "function",
    /// isConstructor === false) without polluting the inline dispatch path.
    /// Each stub takes <c>object</c> (the string this-arg) and returns the
    /// corresponding .NET String operation result.
    /// </summary>
    private void EmitStringPrototypeStubs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Concrete trim/toUpperCase/toLowerCase helpers: enforce ECMA-262
        // RequireObjectCoercible (throw TypeError on undefined/null) and
        // coerce via JS-spec ToJsString (so .call(false) → "false" not
        // .NET "False").
        runtime.StringToUpperCase = EmitStringStringStub(typeBuilder, runtime, "StringToUpperCase", "ToUpper", strictReceiver: true);
        runtime.StringToLowerCase = EmitStringStringStub(typeBuilder, runtime, "StringToLowerCase", "ToLower", strictReceiver: true);
        // JsTrim(string, int mode) for inline call sites that already have a
        // string on the stack. mode: 0=both, 1=start, 2=end. Define BEFORE
        // StringTrim* stubs since those forward to it.
        runtime.JsTrimInline = EmitJsTrimInline(typeBuilder, runtime);
        // Trim variants need ECMA-262 whitespace set, which differs from .NET's
        // char.IsWhiteSpace by including ﻿ (ZWNBSP). Use a custom helper.
        runtime.StringTrim = EmitJsTrimHelper(typeBuilder, runtime, "StringTrim", trimMode: 0, strictReceiver: true);
        runtime.StringTrimStart = EmitJsTrimHelper(typeBuilder, runtime, "StringTrimStart", trimMode: 1, strictReceiver: true);
        runtime.StringTrimEnd = EmitJsTrimHelper(typeBuilder, runtime, "StringTrimEnd", trimMode: 2, strictReceiver: true);

        // Generic stub for methods without specific helpers — used only for
        // typeof + isConstructor probes via $TSFunction wrappers, AND wired
        // into Object.prototype.toString / Array.prototype.toString. Stays
        // tolerant of null/undefined receivers (returns empty string) since
        // those wirings legitimately call with non-string receivers.
        runtime.StringPrototypeGenericStub = EmitStringStringStub(typeBuilder, runtime, "_StringPrototypeStub", "ToString", strictReceiver: false);

        // Strict variant for methods whose first spec step is RequireObjectCoercible
        // (match/matchAll/search/etc.) — these throw TypeError on null/undefined
        // receivers per ECMA-262 22.1.3.* step 1. Used for borrowed-method
        // patterns (`String.prototype.match.call(null, /./)`) where the inline
        // dispatch path doesn't fire.
        _ = EmitStringStringStub(typeBuilder, runtime, "_StringPrototypeStrictStub", "ToString", strictReceiver: true);

        // ECMA-262 22.1.3.27 String.prototype.toString === valueOf === thisStringValue.
        // Returns the underlying string for both primitive strings and Stage-4z19
        // boxed wrappers; throws TypeError on non-string-like receivers (per spec).
        runtime.StringProtoToStringHelper = EmitStringProtoToStringHelper(typeBuilder, runtime);

        // ECMA-262 19.1.3.6 Object.prototype.toString — returns "[object X]"
        // brand based on receiver type. Wired into the Object.prototype slot
        // for borrowed-method patterns. Mirrors the syntactic
        // `Object.prototype.toString.call(...)` pattern matcher in
        // ILEmitter.Calls.cs.
        runtime.ObjectProtoToStringHelper = EmitObjectProtoToStringHelper(typeBuilder, runtime);
        runtime.ObjectProtoValueOfHelper = EmitObjectProtoValueOfHelper(typeBuilder, runtime);
        runtime.ObjectProtoToLocaleStringHelper = EmitObjectProtoToLocaleStringHelper(typeBuilder, runtime);

        // ECMA-262 23.1.3.32 Array.prototype.toString — returns the join of
        // the array elements with no separator (defaults to ","). Previously
        // wired to the generic ToString stub which returned Convert.ToString
        // (System.Object[] for compiled List<object>) — broke any Test262
        // test that did `arr.toString()` directly.
        runtime.ArrayProtoToStringHelper = EmitArrayProtoToStringHelper(typeBuilder, runtime);
    }

    private MethodBuilder EmitArrayProtoToStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayProtoToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        method.DefineParameter(1, ParameterAttributes.None, "__this");

        var il = method.GetILGenerator();

        var emptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, emptyLabel);

        // Pass `this` through ArrayLikeMaterialize so $Array, List<object>,
        // object[] (compiled arguments), and any-like receivers all reduce
        // to a List<object>. Then ArrayJoin with undefined separator (spec
        // default ","). Mirrors ECMA-262 23.1.3.32 step 2.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ArrayLikeMaterialize);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.ArrayJoin);
        il.Emit(OpCodes.Ret);

        // null/undefined receiver → empty string. Defensive; the materializer
        // would throw, but tolerance matches the generic-stub legacy.
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitObjectProtoToLocaleStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 §20.1.3.5: Object.prototype.toLocaleString does ? Invoke(O,
        // "toString"). Step 1 in ToObject(this) throws on null/undefined. Pre-
        // fix this delegated to ObjectProtoToString which has its own
        // "[object Null]" / "[object Undefined]" paths (correct for toString
        // but wrong for toLocaleString). Wrap with the null/undef throw, then
        // fall through to toString for everything else.
        var method = typeBuilder.DefineMethod(
            "ObjectProtoToLocaleString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        var il = method.GetILGenerator();
        var passThroughLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, passThroughLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(passThroughLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(notUndefLabel);
        // Delegate to ObjectProtoToString for the actual conversion.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectProtoToStringHelper);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitObjectProtoValueOfHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 §20.1.3.7: returns ! ToObject(this). For our purposes we
        // pass the receiver through unchanged — primitives stay primitive
        // (`(5).valueOf() === 5` etc.), and objects stay objects, which the
        // materializer's ToPrimitive treats as "valueOf returned non-primitive"
        // so the toString fallback fires per spec for plain objects.
        // null/undefined receiver throws TypeError per the ToObject(this) step.
        var method = typeBuilder.DefineMethod(
            "ObjectProtoValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        var il = method.GetILGenerator();
        var passThroughLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, passThroughLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(passThroughLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(notUndefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitObjectProtoToStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectProtoToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        // Name first parameter "__this" so $TSFunction.InvokeWithThis (called
        // when the helper is borrowed via `obj.toString = Object.prototype.toString`)
        // prepends the receiver as arg0 instead of treating the user-supplied
        // arg list as the actual JS arguments.
        method.DefineParameter(1, ParameterAttributes.None, "__this");

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        void EmitTag(string tag)
        {
            il.Emit(OpCodes.Ldstr, tag);
            il.Emit(OpCodes.Br, endLabel);
        }

        // null
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        EmitTag("[object Null]");
        il.MarkLabel(notNullLabel);

        // undefined
        var notUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        EmitTag("[object Undefined]");
        il.MarkLabel(notUndefLabel);

        // Math singleton
        var notMathLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, notMathLabel);
        EmitTag("[object Math]");
        il.MarkLabel(notMathLabel);

        // Stage 4z19 boxed primitive wrappers — $TSObject with __primitiveType
        // marker. Read the marker via direct dict TryGetValue (avoid recursing
        // through GetProperty which would walk the prototype chain). Tag using
        // the primitive type so `(new Number()).toString.call(obj) === "[object Number]"`.
        var notBoxedTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notBoxedTSObjectLabel);
        var boxedTypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Ldloca, boxedTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notBoxedTSObjectLabel);
        // Compare with "Number" / "String" / "Boolean" markers and emit the
        // matching tag. String marker isn't actually used since `new String(x)`
        // stays primitive, but include for completeness.
        var notNumberMarkerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, boxedTypeLocal);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brfalse, notNumberMarkerLabel);
        EmitTag("[object Number]");
        il.MarkLabel(notNumberMarkerLabel);
        var notBoolMarkerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, boxedTypeLocal);
        il.Emit(OpCodes.Ldstr, "Boolean");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brfalse, notBoolMarkerLabel);
        EmitTag("[object Boolean]");
        il.MarkLabel(notBoolMarkerLabel);
        var notStringMarkerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, boxedTypeLocal);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brfalse, notStringMarkerLabel);
        EmitTag("[object String]");
        il.MarkLabel(notStringMarkerLabel);
        il.MarkLabel(notBoxedTSObjectLabel);

        // JSON singleton
        var notJsonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Bne_Un, notJsonLabel);
        EmitTag("[object JSON]");
        il.MarkLabel(notJsonLabel);

        // Number/String/Boolean/Array prototype singletons
        var notNumberProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Bne_Un, notNumberProtoLabel);
        EmitTag("[object Number]");
        il.MarkLabel(notNumberProtoLabel);

        var notStringProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Bne_Un, notStringProtoLabel);
        EmitTag("[object String]");
        il.MarkLabel(notStringProtoLabel);

        var notBoolProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Bne_Un, notBoolProtoLabel);
        EmitTag("[object Boolean]");
        il.MarkLabel(notBoolProtoLabel);

        var notArrayProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Bne_Un, notArrayProtoLabel);
        EmitTag("[object Array]");
        il.MarkLabel(notArrayProtoLabel);

        // $Arguments : List<object> — sloppy arguments object marker. Must come
        // before the List<object> check since $Arguments inherits from it and
        // would otherwise tag "[object Array]".
        var notArgumentsTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArgumentsTypeLabel);
        EmitTag("[object Arguments]");
        il.MarkLabel(notArgumentsTypeLabel);

        // object[]
        var notArgsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Brfalse, notArgsLabel);
        EmitTag("[object Arguments]");
        il.MarkLabel(notArgsLabel);

        // List<object>
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);
        EmitTag("[object Array]");
        il.MarkLabel(notListLabel);

        // $Array
        var notTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);
        EmitTag("[object Array]");
        il.MarkLabel(notTSArrayLabel);

        // string
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        EmitTag("[object String]");
        il.MarkLabel(notStringLabel);

        // bool
        var notBoolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        EmitTag("[object Boolean]");
        il.MarkLabel(notBoolLabel);

        // double
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        EmitTag("[object Number]");
        il.MarkLabel(notDoubleLabel);

        // Function classification — every value the emitted TypeOf reports as
        // "function" must brand "[object Function]" (ECMA-262 §20.1.3.6 step 7
        // IsCallable). lodash's baseGetTag/isFunction classifies by this tag;
        // mismatches send built-in constructors down the host-constructor path
        // (#314). Mirrors the TypeOf ladder in RuntimeEmitter.CoreUtilities.cs.
        void EmitFunctionBranch(Type? callableType)
        {
            if (callableType == null) return;
            var notMatch = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, callableType);
            il.Emit(OpCodes.Brfalse, notMatch);
            EmitTag("[object Function]");
            il.MarkLabel(notMatch);
        }

        EmitFunctionBranch(runtime.TSFunctionType);
        EmitFunctionBranch(runtime.BoundTSFunctionType);
        EmitFunctionBranch(runtime.FunctionBindWrapperType);
        EmitFunctionBranch(runtime.FunctionCallWrapperType);
        EmitFunctionBranch(runtime.FunctionApplyWrapperType);
        EmitFunctionBranch(_types.Delegate);
        EmitFunctionBranch(runtime.BoundArrayMethodType);
        if (_features.UsesMap)
            EmitFunctionBranch(runtime.BoundMapMethodType);
        if (_features.UsesSet)
            EmitFunctionBranch(runtime.BoundSetMethodType);
        EmitFunctionBranch(runtime.BoundAnyFunctionType);
        // System.Type — built-in constructors and class references held as
        // values are Type tokens (`var O = Object`); typeof says "function",
        // so the toString tag must agree (#314).
        EmitFunctionBranch(_types.Type);

        // $Date — ECMA-262 §21.4.4.42 brand check via [[DateValue]] slot.
        if (runtime.TSDateType != null)
        {
            var notTSDateLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brfalse, notTSDateLabel);
            EmitTag("[object Date]");
            il.MarkLabel(notTSDateLabel);
        }

        // $RegExp — §22.2.6.13 brand check via [[RegExpMatcher]] slot.
        if (runtime.TSRegExpType != null)
        {
            var notTSRegExpLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notTSRegExpLabel);
            EmitTag("[object RegExp]");
            il.MarkLabel(notTSRegExpLabel);
        }

        // $Error — §20.5.3.4 brand check via [[ErrorData]] slot.
        if (runtime.TSErrorType != null)
        {
            var notTSErrorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSErrorType);
            il.Emit(OpCodes.Brfalse, notTSErrorLabel);
            EmitTag("[object Error]");
            il.MarkLabel(notTSErrorLabel);
        }

        // Task<object> or $TSPromise — branded via Promise.prototype[@@toStringTag].
        // ECMA-262 §19.1.3.6 reads @@toStringTag off the receiver; for a
        // Promise the chain walk lands on Promise.prototype which we now
        // populate with "Promise". Emit a direct brand check here so
        // `Object.prototype.toString.call(promise) === "[object Promise]"`
        // without depending on prototype-chain walks at runtime.
        if (runtime.TSPromiseType != null)
        {
            var notTSPromiseLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
            il.Emit(OpCodes.Brfalse, notTSPromiseLabel);
            EmitTag("[object Promise]");
            il.MarkLabel(notTSPromiseLabel);
        }
        var notTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        EmitTag("[object Promise]");
        il.MarkLabel(notTaskLabel);

        // Default
        il.Emit(OpCodes.Ldstr, "[object Object]");

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits ECMA-262 22.1.3.27 String.prototype.toString (and valueOf — same
    /// thisStringValue extraction). Reads the wrapper's <c>__primitiveValue</c>
    /// directly via TSObject's fields dict to avoid recursing through GetProperty
    /// (which would walk the prototype chain back to this very helper).
    /// </summary>
    private MethodBuilder EmitStringProtoToStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringProtoToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");

        var il = method.GetILGenerator();

        // string fast path — return as-is.
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringLabel);

        // String.prototype singleton itself: [[StringData]] is "" per spec.
        var notStringPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Bne_Un, notStringPrototypeLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringPrototypeLabel);

        // $TSObject wrapper — read __primitiveValue from the field dict directly.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldloca, primValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObjectLabel);

        // Other receivers — TypeError per spec. Borrowed-method calls of the
        // form `String.prototype.toString.call(42)` rely on this throw.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.toString requires that 'this' be a String");

        return method;
    }

    private MethodBuilder EmitStringStringStub(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, string netName, bool strictReceiver)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);

        var il = method.GetILGenerator();

        if (strictReceiver)
        {
            // ECMA-262 RequireObjectCoercible: throws TypeError on undefined/null.
            // Apply here so `String.prototype.trim.call(undefined)` and similar
            // borrowed-method patterns surface the spec-defined TypeError.
            var notNullLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, notNullLabel);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
            il.MarkLabel(notNullLabel);

            var notUndefLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brfalse, notUndefLabel);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
            il.MarkLabel(notUndefLabel);

            // Coerce via $Runtime.ToJsString — JS-spec ToString protocol so
            // booleans surface as "true"/"false" (not .NET "True"/"False"),
            // numbers via JS formatting, objects via valueOf/toString.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.ToJsString);
        }
        else
        {
            // Generic-stub path used as the fallback wiring for various
            // prototype.toString slots. Stay tolerant of null/undefined and
            // avoid ToJsString — the latter walks the prototype chain looking
            // for "toString" which itself is wired to this stub, causing
            // infinite recursion. Convert.ToString gives the receiver's .NET
            // ToString without dispatching back through the prototype.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToString", _types.Object));
        }

        var netMethod = _types.GetMethodNoParams(_types.String, netName);
        if (netMethod == null)
            throw new System.InvalidOperationException($"String.{netName} not found");
        il.Emit(OpCodes.Callvirt, netMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits <c>$Runtime.JsTrim*(object) -&gt; string</c> using the ECMA-262
    /// 7.2.10 IsWhiteSpace + IsLineTerminator predicate (which adds U+FEFF
    /// vs .NET's <c>char.IsWhiteSpace</c>).
    /// </summary>
    private MethodBuilder EmitJsTrimHelper(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, int trimMode, bool strictReceiver)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);

        var il = method.GetILGenerator();

        if (strictReceiver)
        {
            var notNullLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, notNullLabel);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
            il.MarkLabel(notNullLabel);

            var notUndefLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brfalse, notUndefLabel);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
            il.MarkLabel(notUndefLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.ToJsString);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToString", _types.Object));
        }

        // Now stack has the string. Inline the trim algorithm.
        il.Emit(OpCodes.Ldc_I4, trimMode);
        il.Emit(OpCodes.Call, runtime.JsTrimInline);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits <c>$Runtime.JsTrimInline(string s, int mode) -&gt; string</c>:
    /// JS-spec trim that adds U+FEFF (ZWNBSP) to the .NET whitespace set.
    /// mode: 0=both, 1=start only, 2=end only.
    /// </summary>
    private MethodBuilder EmitJsTrimInline(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsTrimInline",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32]);

        var il = method.GetILGenerator();

        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);

        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (s == null) return s
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // len = s.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // start = 0; end = len - 1
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, endLocal);

        // mode != 2 → trim start
        var skipStart = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, skipStart);

        var startLoop = il.DefineLabel();
        var startLoopEnd = il.DefineLabel();
        il.MarkLabel(startLoop);
        // start < len?
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, startLoopEnd);
        // c = s[start]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        // is JS whitespace?
        EmitJsWhitespaceCheck(il);
        il.Emit(OpCodes.Brfalse, startLoopEnd);
        // start++
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, startLoop);
        il.MarkLabel(startLoopEnd);
        il.MarkLabel(skipStart);

        // mode != 1 → trim end
        var skipEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, skipEnd);

        var endLoop = il.DefineLabel();
        var endLoopEnd = il.DefineLabel();
        il.MarkLabel(endLoop);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Blt, endLoopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        EmitJsWhitespaceCheck(il);
        il.Emit(OpCodes.Brfalse, endLoopEnd);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endLoop);
        il.MarkLabel(endLoopEnd);
        il.MarkLabel(skipEnd);

        // result = s.Substring(start, end - start + 1)
        // if end < start, return ""
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Bge, notEmpty);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Stack: char → int (1 if whitespace, 0 otherwise). char.IsWhiteSpace || c == '﻿'.
    /// </summary>
    private void EmitJsWhitespaceCheck(ILGenerator il)
    {
        // Stack: char. Dup; check IsWhiteSpace; if false, check == ﻿.
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsWhiteSpace", _types.Char));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Stack still has the char (we duped). Compare to ﻿.
        il.Emit(OpCodes.Ldc_I4, 0xFEFF);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(trueLabel);
        // Pop the duped char and push 1.
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(doneLabel);
    }
}
