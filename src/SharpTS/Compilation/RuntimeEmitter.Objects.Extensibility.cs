using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Object.freeze(obj) - freezes an object to prevent property changes.
    /// Uses PropertyDescriptorStore to track frozen objects for compiled code.
    /// Signature: object ObjectFreeze(object obj)
    /// </summary>
    private void EmitObjectFreeze(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenObjectsField, FieldBuilder sealedObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectFreeze",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ObjectFreeze = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // If obj is null, just return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnLabel);

        // Stage E.2 M2: also call $Array.Freeze() to set the internal _isFrozen
        // bit. SetStrict / SetLong / the frozen-check branches in SetIndex all
        // prefer the internal flag; the legacy FrozenObjectsField weak table is
        // updated below for backward compatibility with other dispatch paths
        // that predate the $Array encapsulation.
        var notTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayFreeze);
        il.MarkLabel(notTSArrayLabel);

        // Mirror for $Object: set its internal _isFrozen/_isSealed/_isNonExtensible
        // bits so TSObjectSetProperty's instance-method gate honors freezing.
        // `new fn()` (constructor invocation on a $TSFunction) creates a $Object,
        // and that's the common path for tests that freeze user objects.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFreeze);
        il.MarkLabel(notTSObjectLabel);

        // Call $PropertyDescriptorStore.Freeze(obj) - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSFreeze);

        // Also add to legacy frozen objects table for backward compatibility
        il.Emit(OpCodes.Ldsfld, frozenObjectsField);
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
        il.Emit(OpCodes.Ldsfld, sealedObjectsField);
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
    private void EmitObjectSeal(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder sealedObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectSeal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ObjectSeal = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // If obj is null, just return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnLabel);

        // Mirror for $Object: set its _isSealed/_isNonExtensible bits so
        // TSObjectSetProperty's instance-method gate honors sealing.
        var notTSObjectSealLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectSealLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSeal);
        il.MarkLabel(notTSObjectSealLabel);

        // number[] unboxing: mark a $Array non-extensible so the unboxed PushDouble fast path refuses to
        // append (Object.seal otherwise only registers the array in the external collections the boxed
        // ArrayPush consults, which PushDouble can't reach).
        var notTSArraySealLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArraySealLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayMarkNonExtensible);
        il.MarkLabel(notTSArraySealLabel);

        // Call $PropertyDescriptorStore.Seal(obj) - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSSeal);

        // Also add to legacy sealed objects table for backward compatibility
        il.Emit(OpCodes.Ldsfld, sealedObjectsField);
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

    /// <summary>
    /// Emits Object.isFrozen(obj) - checks if an object is frozen.
    /// Signature: bool ObjectIsFrozen(object obj)
    /// </summary>
    private void EmitObjectIsFrozen(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIsFrozen",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.ObjectIsFrozen = method;

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
        il.Emit(OpCodes.Ldsfld, frozenObjectsField);
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
    private void EmitObjectIsSealed(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder sealedObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIsSealed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.ObjectIsSealed = method;

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
        il.Emit(OpCodes.Ldsfld, sealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        var tryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue");
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Ret);
    }
}
