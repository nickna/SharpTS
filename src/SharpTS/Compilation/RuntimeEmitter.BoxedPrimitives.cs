using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>$Runtime.NewBoxedPrimitive(string typeTag, object value) -&gt; $Object</c>:
    /// builds a fresh <c>$Object</c> wrapping a primitive (boolean/number/string)
    /// for the <c>new Boolean(x) / new Number(x) / new String(x)</c> ECMA-262
    /// boxed-primitive protocol. The wrapper has:
    /// <list type="bullet">
    /// <item>Marker field <c>__primitiveType</c> holding the JS type name.</item>
    /// <item>Marker field <c>__primitiveValue</c> holding the underlying primitive.</item>
    /// <item>PDS prototype link to the matching prototype singleton.</item>
    /// </list>
    /// Plus methods like <c>valueOf</c> are available via the prototype chain.
    /// Used by <c>TryEmitBuiltInConstructor</c> for Boolean/Number/String.
    /// </summary>
    private void EmitNewBoxedPrimitive(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NewBoxedPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]);
        runtime.NewBoxedPrimitiveMethod = method;

        var il = method.GetILGenerator();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var objLocal = il.DeclareLocal(runtime.TSObjectType);
        var typeTagLocal = il.DeclareLocal(_types.String);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        // Save type tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, typeTagLocal);

        // dict = new Dictionary<string,object>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["__primitiveType"] = typeTag
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Ldloc, typeTagLocal);
        il.Emit(OpCodes.Callvirt, setItem);

        // dict["__primitiveValue"] = value
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, setItem);

        // String exotic object: per ECMA-262 §10.4.3, a String wrapper has a
        // [[Length]] internal slot mirroring the primitive's length, plus
        // own indexed properties for each character. We materialize them
        // into the dict so dynamic GetProperty surfaces them naturally —
        // `(new String("hi")).length === 2` and `s[0] === "h"`. Skipped for
        // Number/Boolean wrappers (no equivalent slots).
        var skipStringLayoutLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeTagLocal);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipStringLayoutLabel);
        // Only string values get the layout — defensive against caller passing
        // a non-string for the "String" tag (shouldn't happen in practice).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipStringLayoutLabel);

        var strLocal = il.DeclareLocal(_types.String);
        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // dict["length"] = (double)len
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, setItem);

        // for (i = 0; i < len; i++) dict[i.ToString()] = str[i].ToString()
        var idxLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, dictLocal);
        // key = idx.ToString()
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        // value = str[idx].ToString() (single-char string)
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        il.MarkLabel(skipStringLayoutLabel);

        // obj = new $Object(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Stloc, objLocal);

        // String exotic own properties have fixed descriptors: length is
        // non-writable/non-enumerable/non-configurable, while character
        // indices are non-writable/enumerable/non-configurable. The dict
        // above supplies fast values; PDS supplies the observable attributes
        // and prevents assignment/deletion from mutating those values.
        var skipStringDescriptors = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeTagLocal);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipStringDescriptors);

        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);

        var descriptorLoop = il.DefineLabel();
        var descriptorLoopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.MarkLabel(descriptorLoop);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, descriptorLoopEnd);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, descriptorLoop);
        il.MarkLabel(descriptorLoopEnd);
        il.MarkLabel(skipStringDescriptors);

        // Set prototype based on typeTag (Boolean/Number/String/BigInt/Symbol
        // → matching singleton).
        // Populate is called to ensure the prototype singleton has the right
        // methods (e.g. toString stub) before we link.
        void LinkProto(string tag, FieldBuilder protoField, MethodBuilder populate)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, typeTagLocal);
            il.Emit(OpCodes.Ldstr, tag);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Call, populate);
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldsfld, protoField);
            il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
            il.MarkLabel(skip);
        }
        LinkProto("Boolean", runtime.BooleanPrototypeField, runtime.BooleanPrototypePopulateMethod);
        LinkProto("Number",  runtime.NumberPrototypeField,  runtime.NumberPrototypePopulateMethod);
        LinkProto("String",  runtime.StringPrototypeField,  runtime.StringPrototypePopulateMethod);
        if (_features.UsesBigInt)
            LinkProto("BigInt", runtime.BigIntPrototypeField, runtime.BigIntPrototypePopulateMethod);
        LinkProto("Symbol", runtime.SymbolPrototypeField, runtime.SymbolPrototypePopulateMethod);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.NormalizeForeignEvalValue(object value) -&gt; object</c>.
    /// Boxed-primitive wrappers (<c>__primitiveType</c>/<c>__primitiveValue</c>) are
    /// recognized across the rest of the compiled runtime by an <c>Isinst $Object</c>
    /// type check (see <see cref="EmitNewBoxedPrimitive"/>, ToNumber, ToJsString, the
    /// <c>==</c> coercion). A wrapper produced OUTSIDE the emitted runtime — most
    /// notably an interpreter <c>SharpTSObject</c> returned across the <c>eval()</c>
    /// boundary for <c>eval("new Number")</c> — is a different CLR type, so that check
    /// misses and the wrapper neither coerces (<c>== 0</c>) nor dispatches
    /// <c>valueOf</c>. Re-wrap such a foreign Number/Boolean/String wrapper as a native
    /// <c>$Object</c> via <see cref="EmitNewBoxedPrimitive"/> so all downstream handling
    /// works uniformly. The interpreter's <c>SharpTSUndefined</c> singleton is likewise
    /// translated to the emitted runtime's undefined singleton; leaving it foreign would
    /// make ToBoolean treat an eval declaration completion as a truthy host object.
    /// Everything else — null, primitives, already-native
    /// <c>$Object</c>/Dictionary objects, and non-wrapper foreign objects — passes
    /// through unchanged, keeping this off the hot path for compiled-origin values.
    /// (Test262 language/expressions/new/S11.2.2_A1.1 / A1.2.)
    /// </summary>
    private void EmitNormalizeForeignEvalValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NormalizeForeignEvalValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.NormalizeForeignEvalValueMethod = method;

        var il = method.GetILGenerator();
        var passthrough = il.DefineLabel();
        var convert = il.DefineLabel();
        var ptLocal = il.DeclareLocal(_types.String);
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        // null → passthrough.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, passthrough);

        // The eval bridge runs the source in SharpTS.dll's interpreter. Its
        // SharpTSUndefined type is intentionally not referenced by the output
        // assembly, so recognize it by stable full name and substitute the
        // emitted runtime's own undefined singleton.
        var notForeignUndefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "FullName")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSUndefined");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, notForeignUndefined);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notForeignUndefined);

        // Already a native $Object (the common compiled-origin case) → passthrough.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, passthrough);

        // Plain Dictionary object literal → passthrough (not a boxed wrapper).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, passthrough);

        // pt = GetProperty(value, "__primitiveType") as string  (works on foreign
        // objects via the general property dispatch; null when absent/non-string).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, ptLocal);
        il.Emit(OpCodes.Ldloc, ptLocal);
        il.Emit(OpCodes.Brfalse, passthrough);

        // Only the three coercible wrapper tags — leave Symbol/other markers alone.
        il.Emit(OpCodes.Ldloc, ptLocal);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, convert);
        il.Emit(OpCodes.Ldloc, ptLocal);
        il.Emit(OpCodes.Ldstr, "Boolean");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, convert);
        il.Emit(OpCodes.Ldloc, ptLocal);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, passthrough);

        il.MarkLabel(convert);
        // return NewBoxedPrimitive(pt, GetProperty(value, "__primitiveValue"))
        il.Emit(OpCodes.Ldloc, ptLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(passthrough);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.ToObject(object value) -&gt; object</c>: ECMA-262
    /// 7.1.18 ToObject coercion. <c>null</c>/<c>undefined</c> → empty
    /// <c>$Object</c>; <c>bool</c>/<c>double</c> → boxed wrapper via
    /// <c>NewBoxedPrimitive</c>; everything else (including <c>string</c>)
    /// passes through unchanged. Used by <c>new Object(v)</c> in compiled mode.
    /// String is intentionally not boxed — see Stage 4z19 carve-out.
    /// </summary>
    private void EmitToObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Body fill: method signature forward-declared by DefineRuntimeClassPhase1.
        var method = runtime.ToObjectMethod;
        var il = method.GetILGenerator();

        // null → empty $Object
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNullLabel);

        // undefined → empty $Object
        var notUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notUndefLabel);

        // bool → NewBoxedPrimitive("Boolean", arg)
        var notBoolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        il.Emit(OpCodes.Ldstr, "Boolean");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolLabel);

        // double → NewBoxedPrimitive("Number", arg)
        var notNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notNumLabel);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumLabel);

        // BigInt → NewBoxedPrimitive("BigInt", arg)
        var notBigIntLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        il.Emit(OpCodes.Ldstr, "BigInt");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBigIntLabel);

        // Symbol → NewBoxedPrimitive("Symbol", arg). ECMA-262 §7.1.18 step 4:
        // ToObject on a Symbol returns a fresh wrapper whose [[SymbolData]]
        // holds the original primitive. test262's `Object(sym) !== sym`
        // identity check verifies the wrapper is distinct.
        if (runtime.TSSymbolType != null)
        {
            var notSymLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
            il.Emit(OpCodes.Brfalse, notSymLabel);
            il.Emit(OpCodes.Ldstr, "Symbol");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSymLabel);
        }

        // String → NewBoxedPrimitive("String", arg). ECMA-262 §7.1.18 step 5:
        // ToObject on a string returns a String exotic object with the
        // primitive in [[StringData]]. Required for `Object.assign("a")` +
        // `Object("a")` to return a wrapper whose `valueOf() === "a"`. Note:
        // many internal call sites pass already-objectified values (e.g.
        // GetProperty receivers) and we don't double-wrap because the early
        // `Isinst _types.String` check only matches raw .NET strings, not
        // \$Object wrappers (which carry __primitiveValue internally).
        var notStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStrLabel);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewBoxedPrimitiveMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrLabel);

        // Otherwise (dict, array, $Object, etc.) — return as-is.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void DefineIsBoxedPrimitiveOfTypeShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.IsBoxedPrimitiveOfTypeMethod = typeBuilder.DefineMethod(
            "IsBoxedPrimitiveOfType",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]);
    }

    /// <summary>
    /// Emits <c>$Runtime.UnwrapStringReceiver(object) -&gt; string</c>: coerces
    /// a String-method receiver to its underlying string. Fast-paths actual
    /// strings; unwraps Stage-4z19 boxed primitives (<c>$Object</c> with
    /// <c>__primitiveType="String"</c>) to their <c>__primitiveValue</c>;
    /// otherwise falls back to <c>ToJsString</c> (which handles bool/double/etc.
    /// per the JS spec ToString protocol).
    /// </summary>
    private void EmitUnwrapStringReceiver(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UnwrapStringReceiver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.UnwrapStringReceiverMethod = method;

        var il = method.GetILGenerator();

        // string fast path
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringLabel);

        // $Object wrapper unwrap: __primitiveValue (string only)
        var notWrapperLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notWrapperLabel);
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocal);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notWrapperLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notWrapperLabel);

        // Fallback: spec ToString. Note ToJsString does NOT throw on
        // null/undefined (returns "null" / "undefined") — direct call sites
        // through StringEmitter only fire on receivers the type checker
        // proved non-nullish, so this matches expectations. Borrowed-method
        // dispatch flows through CoercePrimitiveArgs.RequireObjectCoercibleThis
        // which is the spec gate.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.IsBoxedPrimitiveOfType(object obj, string typeTag) -&gt; bool</c>:
    /// returns true iff <paramref name="obj"/> is a <c>$Object</c> whose
    /// <c>__primitiveType</c> field equals <paramref name="typeTag"/>. Used by
    /// the <c>instanceof</c> emitter to recognize boxed Boolean/Number/String.
    /// </summary>
    private void EmitIsBoxedPrimitiveOfType(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.IsBoxedPrimitiveOfTypeMethod;
        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();

        // null/undefined → false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Must be $Object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Use HasOwnPropertyHelper-style lookup via TSObject.HasProperty +
        // GetProperty for "__primitiveType". Read via the public getter.
        var typeValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, typeValueLocal);

        // Compare with typeTag string.
        il.Emit(OpCodes.Ldloc, typeValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, typeValueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.UnwrapIfBoxed(object obj) -&gt; object</c>. In addition
    /// to the boxed-primitive fast path, this performs the observable first
    /// step of default-hint OrdinaryToPrimitive for dictionary-backed objects:
    /// an own callable <c>valueOf</c> is invoked before string conversion. This
    /// Addition applies this to both operands. Abstract equality applies it
    /// only to an object operand paired with a non-nullish primitive; nullish
    /// and object-to-object comparisons must bypass this observable conversion.
    /// </summary>
    /// <summary>
    /// Defines the <c>UnwrapIfBoxed</c> MethodBuilder shell (no body). Emitted
    /// early — before <c>Add</c>/<c>Stringify</c>/<c>Equals</c>, which reference
    /// its token — while the body (<see cref="EmitUnwrapIfBoxedBody"/>) is filled
    /// later, once <c>GetProperty</c>/<c>InvokeMethodValue</c>/<c>HasOwnPropertyHelper</c>
    /// (which it calls for the #574 own-conversion dispatch) have been emitted.
    /// </summary>
    private void DeclareUnwrapIfBoxed(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.UnwrapIfBoxedMethod = typeBuilder.DefineMethod(
            "UnwrapIfBoxed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
    }

    private void EmitUnwrapIfBoxedBody(EmittedRuntime runtime)
    {
        var method = (MethodBuilder)runtime.UnwrapIfBoxedMethod;
        var il = method.GetILGenerator();
        var passThruLabel = il.DefineLabel();

        // null / undefined / non-object-backed values → pass through.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, passThruLabel);
        var objectLikeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, objectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, passThruLabel);
        il.MarkLabel(objectLikeLabel);

        var isBoxedLocal = il.DeclareLocal(_types.Boolean);
        var primitiveValueLocal = il.DeclareLocal(_types.Object);

        // A $Object with __primitiveType uses its internal primitive value when
        // it has no own valueOf override. Plain dictionary objects continue to
        // the observable own-valueOf/default-string conversion below.
        var afterMarkerProbeLabel = il.DefineLabel();
        var typeMarkerLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterMarkerProbeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, typeMarkerLocal);
        il.Emit(OpCodes.Ldloc, typeMarkerLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, afterMarkerProbeLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isBoxedLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, primitiveValueLocal);
        il.MarkLabel(afterMarkerProbeLabel);

        // #574: ECMA-262 7.1.1 ToPrimitive (default/number hint, used by == and +):
        // OrdinaryToPrimitive tries valueOf first. An OWN valueOf override wins;
        // otherwise the *inherited* valueOf — which for a boxed wrapper just yields
        // its [[PrimitiveValue]] — already returns a primitive, so toString is never
        // consulted. Hence: own valueOf (if any) → else __primitiveValue. An
        // inherited prototype valueOf is NOT own (HasOwnPropertyHelper does not walk
        // the prototype), so an un-overridden wrapper falls through to the slot read
        // below, preserving prior behavior.
        var emptyArgs = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgs);

        var noCallableValueOf = il.DefineLabel();
        var useStringFallback = il.DefineLabel();
        // OrdinaryToPrimitive uses ordinary [[Get]], so an inherited custom
        // valueOf must be observed just like an own method. A missing or
        // non-callable method falls through to the boxed-slot fast path (when
        // applicable), then toString.
        var fnLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "valueOf");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, noCallableValueOf);
        // res = InvokeMethodValue(arg, fn, emptyArgs) — a throwing override
        // propagates naturally.
        var resLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Ldloc, emptyArgs);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, resLocal);
        // An object result is not a primitive → fall back to the slot.
        il.Emit(OpCodes.Ldloc, resLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, useStringFallback);
        il.Emit(OpCodes.Ldloc, resLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, useStringFallback);
        // Primitive (incl. null/undefined/string/number/bool) → return it.
        il.Emit(OpCodes.Ldloc, resLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noCallableValueOf);

        // No own override on a boxed primitive: its inherited valueOf returns
        // [[PrimitiveValue]], so use the internal slot directly.
        il.Emit(OpCodes.Ldloc, isBoxedLocal);
        il.Emit(OpCodes.Brfalse, useStringFallback);
        il.Emit(OpCodes.Ldloc, primitiveValueLocal);
        il.Emit(OpCodes.Ret);

        // Plain object, non-callable own valueOf, or object-valued valueOf:
        // continue at toString. ToJsString implements the remaining string-hint
        // method lookup and primitive-result validation.
        il.MarkLabel(useStringFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(passThruLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}
