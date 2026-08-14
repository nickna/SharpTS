using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

// Split out of RuntimeEmitter.Objects.Properties.cs (#1141): the delete-property emitters (sloppy + strict).
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits DeleteProperty(object obj, string name) -> bool
    /// Removes a property from an object and returns true if successful.
    /// Returns false for frozen/sealed objects or if the object doesn't support deletion.
    /// </summary>
    private void EmitDeleteProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCompactDictionaryOrder(typeBuilder, runtime);
        EmitDeletePropertyCore(typeBuilder, runtime, strict: false);
    }

    private void EmitCompactDictionaryOrder(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CompactDictionaryOrder",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.DictionaryStringObject]);
        runtime.CompactDictionaryOrder = method;
        var il = method.GetILGenerator();
        var keysLocal = il.DeclareLocal(_types.ListOfString);
        var valuesLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfString, [_types.IEnumerableOfString])!);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, valuesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var copyLoop = il.DefineLabel();
        var copyEnd = il.DefineLabel();
        il.MarkLabel(copyLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Count")!);
        il.Emit(OpCodes.Bge, copyEnd);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", [_types.String])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, copyLoop);
        il.MarkLabel(copyEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Clear", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        var restoreLoop = il.DefineLabel();
        var restoreEnd = il.DefineLabel();
        il.MarkLabel(restoreLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Count")!);
        il.Emit(OpCodes.Bge, restoreEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item")!);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, restoreLoop);
        il.MarkLabel(restoreEnd);
        il.Emit(OpCodes.Ret);
    }
    /// <summary>
    /// Emits DeleteProperty(object obj, string name) -> bool (non-strict) or
    /// DeletePropertyStrict(object obj, string name, bool strictMode) -> bool.
    /// On a failed delete (frozen/sealed object, non-configurable property)
    /// the non-strict variant returns false; the strict variant throws a
    /// TypeError when strictMode is set, else returns false.
    /// The strict variant's $TSFunction handler skips the frozen/sealed/PDS
    /// configurability checks — preserved as-is from before the #1131 merge
    /// (behavior-preserving refactor; see the epic notes for the drift list).
    /// </summary>
    private void EmitDeletePropertyCore(TypeBuilder typeBuilder, EmittedRuntime runtime, bool strict)
    {
        var method = typeBuilder.DefineMethod(
            strict ? "DeletePropertyStrict" : "DeleteProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            strict ? [_types.Object, _types.String, _types.Boolean] : [_types.Object, _types.String]
        );
        if (strict)
            runtime.DeletePropertyStrict = method;
        else
            runtime.DeleteProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // Emits the failed-delete path: strict mode (arg 2 set) throws
        // TypeError("Cannot delete property '<name>'<suffix>"), otherwise
        // (non-strict variant, or strictMode == false) returns false.
        void EmitDeleteFail(string suffix)
        {
            if (strict)
            {
                var sloppyLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Brfalse, sloppyLabel);
                EmitThrowTypeErrorWithName(il, runtime, "Cannot delete property '", suffix);
                il.MarkLabel(sloppyLabel);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        // null check - return true (deleting from null is allowed in JS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxyDeleteCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // Standard global bindings are synthesized rather than stored in the
        // mutable global dictionary. Preserve their deletion state in the
        // same per-object ledger used by configurable built-ins.
        var notGlobalObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalObjectLabel);

        // undefined, NaN, and Infinity are non-configurable.
        foreach (var nonConfigurableName in new[] { "undefined", "NaN", "Infinity" })
        {
            var nextName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, nonConfigurableName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality",
                _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, nextName);
            EmitDeleteFail(" from global object");
            il.MarkLabel(nextName);
        }

        var configurableGlobalLabel = il.DefineLabel();
        foreach (var configurableName in new[]
        {
            "globalThis", "parseInt", "parseFloat", "isNaN", "isFinite", "eval",
            "Array", "Date", "RegExp", "Map", "Set", "WeakMap", "WeakSet",
            "Promise", "Function", "Object", "Number", "String", "Boolean",
            "Symbol", "Error", "TypeError", "RangeError", "ReferenceError",
            "SyntaxError", "URIError", "EvalError", "AggregateError", "Math", "JSON"
        })
        {
            var nextName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, configurableName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality",
                _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, nextName);
            il.Emit(OpCodes.Br, configurableGlobalLabel);
            il.MarkLabel(nextName);
        }
        // Unknown globals retain the existing permissive delete behavior.
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(configurableGlobalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(notGlobalObjectLabel);

        // Check if $TSObject
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // $TSFunction — `delete fn.name` records in the per-instance set so
        // the synthetic name/length descriptors stop reporting (ECMA-262 §17
        // configurable). Pre-fix this fell through to trueLabel without
        // recording, so verifyProperty's isConfigurable failed.
        var tsFunctionDelLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionDelLabel);

        // $Array — `delete arr[i]` turns the slot into a hole. Must come
        // BEFORE the Dictionary check (not relevant here, just ordering)
        // and BEFORE the trueLabel fallthrough so actual deletions happen.
        // The strict variant delegates to the ordinary array deletion below
        // and converts a false result into the required TypeError.
        var tsArrayDelLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayDelLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // System.Type — `delete String.prototype` / `delete Number.MAX_VALUE`.
        // Shared by sloppy and strict deletion: EmitDeleteFail returns false
        // for the former and throws TypeError for the latter.
        {
            // `delete String.prototype` / `delete Number.MAX_VALUE`.
            // Per ECMA-262 §17 + §22.x: built-in constructor's "prototype" data
            // property is { writable:false, enumerable:false, configurable:false };
            // static constants likewise non-configurable. [[Delete]] returns false
            // on non-configurable. Test262 S15.5.3.1_A3 verifies. PDS check first
            // for user-installed override-descriptors with configurable=true.
            var notTypeForDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.Type);
            il.Emit(OpCodes.Brfalse, notTypeForDelLabel);
            var typeDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, typeDelDescLocal);
            var typeNoPdsDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, typeDelDescLocal);
            il.Emit(OpCodes.Brfalse, typeNoPdsDescLabel);
            il.Emit(OpCodes.Ldloc, typeDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var typeConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, typeConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeConfigurableLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            // Also mark in the per-Type deletion tracker so the static-names list
            // check in HasOwnPropertyHelper / gOPD doesn't resurrect this name.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeNoPdsDescLabel);
            // "prototype"/"name"/"length" are non-configurable on every built-in.
            var typeBuiltinNameTrueLabel = il.DefineLabel();
            void EmitTypeBuiltinNameCheck(string n)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            }
            EmitTypeBuiltinNameCheck("prototype");
            EmitTypeBuiltinNameCheck("name");
            EmitTypeBuiltinNameCheck("length");

            // Number Type-specific non-configurable constants. Reflection
            // probe below would miss these because JS names (UPPER_SNAKE_CASE)
            // differ from .NET names (PascalCase): MAX_VALUE → double.MaxValue
            // etc. Without this, `delete Number.MAX_VALUE` returned true
            // (Test262 S15.7.3.2_A3).
            var notNumberTypeForDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notNumberTypeForDelLabel);
            void EmitNumberConstNameCheck(string n)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            }
            EmitNumberConstNameCheck("MAX_VALUE");
            EmitNumberConstNameCheck("MIN_VALUE");
            EmitNumberConstNameCheck("NaN");
            EmitNumberConstNameCheck("POSITIVE_INFINITY");
            EmitNumberConstNameCheck("NEGATIVE_INFINITY");
            EmitNumberConstNameCheck("MAX_SAFE_INTEGER");
            EmitNumberConstNameCheck("MIN_SAFE_INTEGER");
            EmitNumberConstNameCheck("EPSILON");
            il.MarkLabel(notNumberTypeForDelLabel);
            // Object/Array/String constructor static method names: per ECMA-262
            // §17, every other data property has configurable:true. Mark the
            // deletion in the per-Type tracker so subsequent gOPD/hasOwn report
            // the property as absent, then return true. (prototype/name/length
            // and Number constants caught above are non-configurable.)
            var objTypeDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, objTypeDelLabel);
            void EmitObjectMethodDelCheck(string n)
            {
                var skipLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, skipLabel);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(skipLabel);
            }
            EmitObjectMethodDelCheck("assign"); EmitObjectMethodDelCheck("create");
            EmitObjectMethodDelCheck("defineProperties"); EmitObjectMethodDelCheck("defineProperty");
            EmitObjectMethodDelCheck("entries"); EmitObjectMethodDelCheck("freeze");
            EmitObjectMethodDelCheck("fromEntries"); EmitObjectMethodDelCheck("getOwnPropertyDescriptor");
            EmitObjectMethodDelCheck("getOwnPropertyDescriptors"); EmitObjectMethodDelCheck("getOwnPropertyNames");
            EmitObjectMethodDelCheck("getOwnPropertySymbols"); EmitObjectMethodDelCheck("getPrototypeOf");
            EmitObjectMethodDelCheck("groupBy"); EmitObjectMethodDelCheck("hasOwn"); EmitObjectMethodDelCheck("is");
            EmitObjectMethodDelCheck("isExtensible"); EmitObjectMethodDelCheck("isFrozen");
            EmitObjectMethodDelCheck("isSealed"); EmitObjectMethodDelCheck("keys");
            EmitObjectMethodDelCheck("preventExtensions"); EmitObjectMethodDelCheck("seal");
            EmitObjectMethodDelCheck("setPrototypeOf"); EmitObjectMethodDelCheck("values");
            il.MarkLabel(objTypeDelLabel);

            // Runtime-backed Promise/Error statics are configurable built-in
            // own properties, but are not discoverable through reflection on
            // their constructor Type tokens. Record deletion explicitly.
            var promiseTypeDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, promiseTypeDelLabel);
            EmitObjectMethodDelCheck("resolve"); EmitObjectMethodDelCheck("reject");
            EmitObjectMethodDelCheck("all"); EmitObjectMethodDelCheck("race");
            EmitObjectMethodDelCheck("allKeyed"); EmitObjectMethodDelCheck("allSettled");
            EmitObjectMethodDelCheck("allSettledKeyed"); EmitObjectMethodDelCheck("any");
            EmitObjectMethodDelCheck("withResolvers");
            il.MarkLabel(promiseTypeDelLabel);

            var errorTypeDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSErrorType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, errorTypeDelLabel);
            EmitObjectMethodDelCheck("isError");
            il.MarkLabel(errorTypeDelLabel);

            // Reflection: any static field/property on the Type → built-in own.
            const System.Reflection.BindingFlags typeDelStaticPub =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            var typeDelLocal = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Stloc, typeDelLocal);
            il.Emit(OpCodes.Ldloc, typeDelLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)typeDelStaticPub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(System.Reflection.BindingFlags)));
            il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            il.Emit(OpCodes.Ldloc, typeDelLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)typeDelStaticPub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(System.Reflection.BindingFlags)));
            il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            // Not a built-in own property — return true (delete-missing = success).
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeBuiltinNameTrueLabel);
            EmitDeleteFail("' of object");
            il.MarkLabel(notTypeForDelLabel);
        }

        // Remaining runtime-backed objects (notably Error instances) can own
        // properties represented solely in the descriptor store. Honor the
        // descriptor's configurability and remove it on successful deletion.
        // Previously these objects fell straight through to true without
        // changing observable state, so verifyProperty incorrectly classified
        // configurable Error cause/message slots as non-configurable.
        var fallbackDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, fallbackDescriptorLocal);
        il.Emit(OpCodes.Ldloc, fallbackDescriptorLocal);
        il.Emit(OpCodes.Brfalse, trueLabel);
        // Sealing/freezing makes every existing own property
        // non-configurable even though the stable stored descriptor retains
        // its original bit. Match gOPD's effective-configurability view.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        var fallbackNotSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, fallbackNotSealedLabel);
        EmitDeleteFail("' of a sealed object");
        il.MarkLabel(fallbackNotSealedLabel);
        il.Emit(OpCodes.Ldloc, fallbackDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        var fallbackConfigurableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, fallbackConfigurableLabel);
        EmitDeleteFail("' of object");
        il.MarkLabel(fallbackConfigurableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, trueLabel);

        // $TSFunction delete handler.
        il.MarkLabel(tsFunctionDelLabel);
        if (strict)
        {
            // Strict variant: record the deletion only — it intentionally skips
            // the frozen/sealed/PDS configurability checks the non-strict
            // handler performs (preserved pre-#1131 behavior).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Honor configurability:
            //   1. If frozen or sealed (via CWT), return false (silent no-op).
            //   2. If a PDS descriptor exists with configurable=false, return false
            //      without removing.
            //   3. Otherwise: clean up PDS, then mark as deleted in the per-instance
            //      tracker so the synthetic descriptor stops reporting and direct
            //      property lookups return undefined.
            // Frozen check.
            var tsFnDelTmp = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnDelTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnNotFrozenLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            // Sealed check.
            il.MarkLabel(tsFnNotFrozenLabel);
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnDelTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnNotSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnNotSealedLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            // PDS configurable check.
            il.MarkLabel(tsFnNotSealedLabel);
            var tsFnDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsFnDelDescLocal);
            var tsFnNoPdsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnDelDescLocal);
            il.Emit(OpCodes.Brfalse, tsFnNoPdsLabel);
            il.Emit(OpCodes.Ldloc, tsFnDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var tsFnDelConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsFnDelConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnDelConfigurableLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(tsFnNoPdsLabel);

            // Mark as deleted in per-instance tracker (covers synthetic
            // name/length and any other descriptor-less data entry).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // $Array delete handler. The strict entry point reuses the ordinary
        // array [[Delete]] implementation (which handles holes, integrity
        // levels, and descriptor configurability) and throws when it reports
        // failure. This is required by DeletePropertyOrThrow in mutators such
        // as copyWithin.
        il.MarkLabel(tsArrayDelLabel);
        if (strict)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.DeleteProperty);
            il.Emit(OpCodes.Brtrue, trueLabel);
            EmitDeleteFail("' of array");
        }
        else
        {
            var tsArrDelIndexLocal = il.DeclareLocal(_types.Int64);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsArrDelIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "TryParse", _types.String, _types.Int64.MakeByRefType()));
            var tsArrDelNonNumericLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsArrDelNonNumericLabel);

            // arr.DeleteAt(idx); return true;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, tsArrDelIndexLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayDeleteAt);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsArrDelNonNumericLabel);
            // Non-numeric key. PDS-installed named property: honor frozen +
            // descriptor.configurable (mirrors the Dict path's behavior).
            // Pre-fix returned true unconditionally, allowing `delete arr.foo`
            // to silently succeed even when `Object.freeze(arr)` made the
            // property non-configurable.
            var tsArrDelFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, tsArrDelFrozenLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelFrozenLabel);
            var tsArrDelSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsSealed);
            il.Emit(OpCodes.Brfalse, tsArrDelSealedLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelSealedLabel);
            // Check PDS descriptor configurable.
            var tsArrDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsArrDelDescLocal);
            var tsArrDelNoDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsArrDelDescLocal);
            il.Emit(OpCodes.Brfalse, tsArrDelNoDescLabel);
            il.Emit(OpCodes.Ldloc, tsArrDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var tsArrDelConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsArrDelConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelConfigurableLabel);
            // Configurable — PDS remove + return true.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(tsArrDelNoDescLabel);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // $TSObject - call the DeleteProperty / DeletePropertyStrict instance method
        il.MarkLabel(sharpTSObjectLabel);
        if (strict)
        {
            // Indexed/named writes on $Object may have both a live _fields entry
            // and descriptor metadata. DeletePropertyStrict must remove both;
            // otherwise the stale PDS entry remains observable as a null value.
            var tsObjectDeleteDesc = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var tsObjectDescriptorConfigurable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Ldloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Brfalse, tsObjectDescriptorConfigurable);
            il.Emit(OpCodes.Ldloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, tsObjectDescriptorConfigurable);
            EmitDeleteFail("' of object");
            il.MarkLabel(tsObjectDescriptorConfigurable);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        if (strict)
        {
            il.Emit(OpCodes.Ldarg_2); // strictMode
            il.Emit(OpCodes.Callvirt, runtime.TSObjectDeletePropertyStrict);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_1);
        }
        else
        {
            // The backing $Object dictionary and PDS metadata together form
            // one own property. Honor the descriptor's configurable bit, then
            // remove both representations. Previously the sloppy path removed
            // only _fields, leaving configurable PDS properties observable and
            // allowing non-configurable properties to appear deleted briefly.
            var tsObjectDeleteDesc = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var tsObjectDescriptorConfigurable = il.DefineLabel();
            il.Emit(OpCodes.Pop); // discard receiver/name loaded for the old direct call
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Ldloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Brfalse, tsObjectDescriptorConfigurable);
            il.Emit(OpCodes.Ldloc, tsObjectDeleteDesc);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, tsObjectDescriptorConfigurable);
            EmitDeleteFail("' of object");
            il.MarkLabel(tsObjectDescriptorConfigurable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSObjectType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_1);
        }
        il.Emit(OpCodes.Ret);

        // Dictionary - use Remove, honoring frozen/sealed and PDS configurability.
        il.MarkLabel(dictLabel);
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - fail (strict throws / sloppy returns false)
        EmitDeleteFail("' of a frozen object");

        // Check if sealed
        il.MarkLabel(notFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSealedLabel);

        // Sealed - fail (strict throws / sloppy returns false)
        EmitDeleteFail("' of a sealed object");

        // Not frozen/sealed — remove from BOTH the dict (default data entries)
        // AND the PDS descriptor store (Object.defineProperty installs). When
        // a PDS descriptor is present and non-configurable, delete fails per
        // ECMA-262 §10.1.10 without removing: strict throws TypeError
        // (§13.5.1.2), sloppy returns false.
        il.MarkLabel(notSealedLabel);
        var descLocalDel = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocalDel);
        var noPdsForDelLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descLocalDel);
        il.Emit(OpCodes.Brfalse, noPdsForDelLabel);
        // Descriptor present — check Configurable.
        il.Emit(OpCodes.Ldloc, descLocalDel);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        var configurableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, configurableLabel);
        // Non-configurable — fail without removing.
        EmitDeleteFail("' of object");
        il.MarkLabel(configurableLabel);
        // Configurable — remove PDS entry.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noPdsForDelLabel);

        // Always also try to remove from the dict (the property may be a
        // plain data entry without a PDS descriptor). Dictionary.Remove
        // returns false when the key isn't present, which is fine.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        // Dictionary reuses removed buckets on a later add, which does not
        // match ECMAScript's chronological string-key order. Rebuild after a
        // delete so a recreated property is appended after all surviving keys.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Call, runtime.CompactDictionaryOrder);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Return true (default for null and other types)
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
    private void EmitDeletePropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitDeletePropertyCore(typeBuilder, runtime, strict: true);
}
