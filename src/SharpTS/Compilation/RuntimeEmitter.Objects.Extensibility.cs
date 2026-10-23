using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ObjectIntegrityInputs(EmittedArrayStorageRuntime ArrayStorage,
        EmittedDescriptorStorageRuntime DescriptorStorage, EmittedObjectStorageRuntime ObjectStorage,
        MethodBuilder GetOwnPropertyDescriptor, Type IHasFieldsInterface);

    private readonly record struct GeneratorIntegrityInputs(Type FunctionType, MethodInfo GetMethodInfo,
        MethodInfo Kind, MethodInfo EnsureOwnPrototype);

    /// <summary>
    /// Emits Object.freeze(obj) - freezes an object to prevent property changes.
    /// Uses PropertyDescriptorStore to track frozen objects for compiled code.
    /// Signature: object ObjectFreeze(object obj)
    /// </summary>
    private void EmitObjectFreeze(TypeBuilder typeBuilder, EmittedObjectStateRuntime objectState, ObjectIntegrityInputs inputs,
        GeneratorIntegrityInputs generators)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectFreeze",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        objectState.Freeze = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // If obj is null, just return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnLabel);

        EmitClassMetadataIntegrity(il, inputs, freeze: true);

        // A generator already owns its prototype slot, even when no read has
        // materialized the descriptor. Make that slot visible before freezing.
        var afterGenerator = il.DefineLabel();
        var generatorFunction = il.DeclareLocal(generators.FunctionType);
        var kickoff = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, generators.FunctionType);
        il.Emit(OpCodes.Stloc, generatorFunction);
        il.Emit(OpCodes.Ldloc, generatorFunction); il.Emit(OpCodes.Brfalse, afterGenerator);
        il.Emit(OpCodes.Ldloc, generatorFunction); il.Emit(OpCodes.Callvirt, generators.GetMethodInfo);
        il.Emit(OpCodes.Stloc, kickoff);
        il.Emit(OpCodes.Ldloc, kickoff); il.Emit(OpCodes.Call, generators.Kind);
        il.Emit(OpCodes.Brfalse, afterGenerator);
        il.Emit(OpCodes.Ldloc, generatorFunction); il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, afterGenerator);
        il.Emit(OpCodes.Ldloc, generatorFunction); il.Emit(OpCodes.Ldloc, kickoff);
        il.Emit(OpCodes.Call, generators.EnsureOwnPrototype); il.Emit(OpCodes.Pop);
        il.MarkLabel(afterGenerator);

        // Stage E.2 M2: also call $Array.Freeze() to set the internal _isFrozen
        // bit. SetStrict / SetLong / the frozen-check branches in SetIndex all
        // prefer the internal flag; the legacy FrozenObjectsField weak table is
        // updated below for backward compatibility with other dispatch paths
        // that predate the $Array encapsulation.
        var notTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Callvirt, inputs.ArrayStorage.Freeze);
        il.MarkLabel(notTSArrayLabel);

        // Mirror for $Object: set its internal _isFrozen/_isSealed/_isNonExtensible
        // bits so TSObjectSetProperty's instance-method gate honors freezing.
        // `new fn()` (constructor invocation on a $TSFunction) creates a $Object,
        // and that's the common path for tests that freeze user objects.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Callvirt, inputs.ObjectStorage.Freeze);
        il.MarkLabel(notTSObjectLabel);

        // Call $PropertyDescriptorStore.Freeze(obj) - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.Freeze);

        // Also add to legacy frozen objects table for backward compatibility
        il.Emit(OpCodes.Ldsfld, objectState.FrozenObjects);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);  // true
        il.Emit(OpCodes.Box, _types.Boolean);
        var addOrUpdateMethod = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>)
            .GetMethod("AddOrUpdate");
        if (addOrUpdateMethod != null)
        {
            il.Emit(OpCodes.Callvirt, addOrUpdateMethod);
        }
        else
        {
            var setItem = _types.TryGetMethod(_types.ConditionalWeakTable, "set_Item")
                ?? _types.TryGetProperty(_types.ConditionalWeakTable, "Item")?.GetSetMethod();
            if (setItem != null)
            {
                il.Emit(OpCodes.Callvirt, setItem);
            }
            else
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
            }
        }

        // Also add to sealed objects table (frozen implies sealed)
        il.Emit(OpCodes.Ldsfld, objectState.SealedObjects);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        if (addOrUpdateMethod != null)
        {
            il.Emit(OpCodes.Callvirt, addOrUpdateMethod);
        }
        else
        {
            var setItem = _types.TryGetMethod(_types.ConditionalWeakTable, "set_Item")
                ?? _types.TryGetProperty(_types.ConditionalWeakTable, "Item")?.GetSetMethod();
            if (setItem != null)
            {
                il.Emit(OpCodes.Callvirt, setItem);
            }
            else
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
            }
        }

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.seal(obj) - seals an object to prevent property addition/removal.
    /// Uses PropertyDescriptorStore to track sealed objects for compiled code.
    /// Signature: object ObjectSeal(object obj)
    /// </summary>
    private void EmitObjectSeal(TypeBuilder typeBuilder, EmittedObjectStateRuntime objectState, ObjectIntegrityInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectSeal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        objectState.Seal = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // If obj is null, just return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnLabel);

        EmitClassMetadataIntegrity(il, inputs, freeze: false);

        // Mirror for $Object: set its _isSealed/_isNonExtensible bits so
        // TSObjectSetProperty's instance-method gate honors sealing.
        var notTSObjectSealLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Brfalse, notTSObjectSealLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Callvirt, inputs.ObjectStorage.Seal);
        il.MarkLabel(notTSObjectSealLabel);

        // number[] unboxing: mark a $Array non-extensible so the unboxed PushDouble fast path refuses to
        // append (Object.seal otherwise only registers the array in the external collections the boxed
        // ArrayPush consults, which PushDouble can't reach).
        var notTSArraySealLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Brfalse, notTSArraySealLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Callvirt, inputs.ArrayStorage.MarkNonExtensible);
        il.MarkLabel(notTSArraySealLabel);

        // Call $PropertyDescriptorStore.Seal(obj) - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.Seal);

        // Also add to legacy sealed objects table for backward compatibility
        il.Emit(OpCodes.Ldsfld, objectState.SealedObjects);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        var addOrUpdateMethod = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>)
            .GetMethod("AddOrUpdate");
        if (addOrUpdateMethod != null)
        {
            il.Emit(OpCodes.Callvirt, addOrUpdateMethod);
        }
        else
        {
            var setItem = _types.TryGetMethod(_types.ConditionalWeakTable, "set_Item")
                ?? _types.TryGetProperty(_types.ConditionalWeakTable, "Item")?.GetSetMethod();
            if (setItem != null)
            {
                il.Emit(OpCodes.Callvirt, setItem);
            }
            else
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
            }
        }

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitClassMetadataIntegrity(ILGenerator il, ObjectIntegrityInputs inputs, bool freeze)
    {
        if (!_features.UsesUserClasses) return;

        var done = il.DefineLabel();
        var owner = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, owner);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldtoken, inputs.IHasFieldsInterface);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Brfalse, done);

        // Virtual constructor metadata must acquire the same canonical descriptor
        // used by deletion and redefinition. Own-descriptor discovery invokes no
        // getters and skips deleted slots instead of copying inherited metadata.
        foreach (string key in new[] { "name", "length" })
        {
            var next = il.DefineLabel();
            var snapshot = il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Call, inputs.GetOwnPropertyDescriptor);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, snapshot);
            il.Emit(OpCodes.Ldloc, snapshot);
            il.Emit(OpCodes.Brfalse, next);

            var descriptor = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
            var hasValue = il.DeclareLocal(_types.Boolean);
            var value = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorWritable.GetSetMethod()!);

            // Copy only present fields. In particular, undefined get/set values
            // retain accessor classification rather than becoming a data slot.
            foreach (var (name, property, isBoolean) in new[]
            {
                ("value", inputs.DescriptorStorage.DescriptorValue, false),
                ("get", inputs.DescriptorStorage.DescriptorGetter, false),
                ("set", inputs.DescriptorStorage.DescriptorSetter, false),
                ("writable", inputs.DescriptorStorage.DescriptorWritable, true),
                ("enumerable", inputs.DescriptorStorage.DescriptorEnumerable, true)
            })
            {
                var missing = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, snapshot);
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Ldloca, value);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
                il.Emit(OpCodes.Brfalse, missing);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Ldloc, value);
                if (isBoolean) il.Emit(OpCodes.Unbox_Any, _types.Boolean);
                il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
                if (name == "value")
                {
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Stloc, hasValue);
                }
                il.MarkLabel(missing);
            }

            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
            if (freeze)
            {
                var accessor = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, hasValue);
                il.Emit(OpCodes.Brfalse, accessor);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorWritable.GetSetMethod()!);
                il.MarkLabel(accessor);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(next);
        }
        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits Object.isFrozen(obj) - checks if an object is frozen.
    /// Signature: bool ObjectIsFrozen(object obj)
    /// </summary>
    private void EmitObjectIsFrozen(TypeBuilder typeBuilder, EmittedObjectStateRuntime objectState)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIsFrozen",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        objectState.IsFrozen = method;

        var il = method.GetILGenerator();
        var returnTrueLabel = il.DefineLabel();
        var checkTableLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var checkNumberLabel = il.DefineLabel();
        var checkBooleanLabel = il.DefineLabel();

        // If obj is null, return true (primitives are frozen by definition)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkStringLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is string, return true (immutable)
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkNumberLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is double (boxed number), return true (immutable)
        il.MarkLabel(checkNumberLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkBooleanLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is bool (boxed boolean), return true (immutable)
        il.MarkLabel(checkBooleanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, checkTableLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(checkTableLabel);
        // Check if obj is in frozen objects table
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, objectState.FrozenObjects);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        var tryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue");
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.isSealed(obj) - checks if an object is sealed.
    /// Signature: bool ObjectIsSealed(object obj)
    /// </summary>
    private void EmitObjectIsSealed(TypeBuilder typeBuilder, EmittedObjectStateRuntime objectState)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIsSealed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        objectState.IsSealed = method;

        var il = method.GetILGenerator();
        var checkTableLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var checkNumberLabel = il.DefineLabel();
        var checkBooleanLabel = il.DefineLabel();

        // If obj is null, return true (primitives are sealed by definition)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkStringLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is string, return true (immutable)
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkNumberLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is double (boxed number), return true (immutable)
        il.MarkLabel(checkNumberLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkBooleanLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // If obj is bool (boxed boolean), return true (immutable)
        il.MarkLabel(checkBooleanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, checkTableLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(checkTableLabel);
        // Check if obj is in sealed objects table
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, objectState.SealedObjects);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        var tryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue");
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Ret);
    }
}
