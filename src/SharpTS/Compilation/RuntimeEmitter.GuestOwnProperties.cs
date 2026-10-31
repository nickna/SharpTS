using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitGuestRegisteredPrototypeLookup(TypeBuilder host, EmittedClassPrototypeRuntime prototypes,
        FieldBuilder cache, FieldInfo objectPrototype)
    {
        var method = host.DefineMethod("LookupRegisteredClassPrototype", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Type]);
        prototypes.DeclareRegisteredLookup(method);
        var il = method.GetILGenerator(); var result = il.DeclareLocal(_types.Object); var found = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cache); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, result);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cache.FieldType, "TryGetValue", _types.Type, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, found); il.Emit(OpCodes.Ldsfld, objectPrototype); il.Emit(OpCodes.Ret);
        il.MarkLabel(found); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
    }

    /// <summary>Declares an opt-in source contract and a weak ledger. No existing object operation is rerouted.</summary>
    private EmittedGuestOwnPropertyRuntime DeclareGuestOwnProperties(ModuleBuilder module, Type descriptorType, Type symbolType)
    {
        var contract = EmitTypeDefinitions.DefineType(module, "$IGuestOwnProperties",
            TypeAttributes.NotPublic | TypeAttributes.Interface | TypeAttributes.Abstract, null);
        MethodBuilder Slot(string name, Type result, params Type[] parameters) => contract.DefineMethod(name,
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            result, parameters);
        var descriptor = Slot("GetOwnDescriptor", descriptorType, _types.Object);
        var keys = Slot("OwnPropertyKeys", _types.ListOfObject);
        var prototype = Slot("InitialPrototype", _types.Object);
        var isReceiver = Slot("IsOwnPropertyReceiver", _types.Boolean);
        contract.CreateType();

        var state = EmitTypeDefinitions.DefineType(module, "$GuestOwnState", TypeAttributes.NotPublic | TypeAttributes.Sealed, _types.Object);
        var order = state.DefineField("Order", _types.ListOfObject, FieldAttributes.Assembly | FieldAttributes.InitOnly);
        var active = state.DefineField("Active", _types.DictionaryObjectObject, FieldAttributes.Assembly | FieldAttributes.InitOnly);
        var constructor = state.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _types.ObjectDefaultCtor);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Stfld, order);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryObjectObject)); il.Emit(OpCodes.Stfld, active);
        il.Emit(OpCodes.Ret);
        state.CreateType();

        var operations = EmitTypeDefinitions.DefineType(module, "$GuestOwnPropertyState",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, _types.Object);
        MethodBuilder Operation(string name, Type result, params Type[] parameters) => operations.DefineMethod(name,
            MethodAttributes.Assembly | MethodAttributes.Static, result, parameters);
        var commitDefinition = Operation("CommitDefinition", _types.Void, _types.Object, _types.Object, _types.Object, _types.Boolean);
        var commitUpdate = Operation("CommitUpdate", _types.Void, _types.Object, _types.Object, _types.Boolean);
        var commitDelete = Operation("CommitDelete", _types.Void, _types.Object, _types.Object, _types.Boolean);
        var hasOwn = Operation("HasOwn", _types.Boolean, _types.Object, _types.Object);
        var activeField = Operation("ActiveSourceField", _types.Object, _types.Object, _types.Object);
        var snapshot = Operation("SnapshotKeys", _types.ListOfObject, _types.Object);
        var declarations = new Dictionary<string, MethodBuilder>(StringComparer.Ordinal)
        {
            [commitDefinition.Name] = commitDefinition, [commitUpdate.Name] = commitUpdate,
            [commitDelete.Name] = commitDelete, [hasOwn.Name] = hasOwn,
            [activeField.Name] = activeField, [snapshot.Name] = snapshot
        };
        var table = operations.DefineField("States", _types.ConditionalWeakTable, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        var callbackType = _types.MakeGenericType(_types.Resolve("System.Runtime.CompilerServices.ConditionalWeakTable`2+CreateValueCallback"), _types.Object, _types.Object);
        var callback = operations.DefineField("Create", callbackType, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        var create = operations.DefineMethod("CreateState", MethodAttributes.Private | MethodAttributes.Static, _types.Object, [_types.Object]);
        il = create.GetILGenerator(); il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Ret);
        il = operations.DefineTypeInitializer().GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable)); il.Emit(OpCodes.Stsfld, table);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldftn, create);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(callbackType, [_types.Object, _types.IntPtr])!); il.Emit(OpCodes.Stsfld, callback);
        il.Emit(OpCodes.Ret);

        var validate = operations.DefineMethod("ValidateKey", MethodAttributes.Private | MethodAttributes.Static, _types.Void, [_types.Object]);
        il = validate.GetILGenerator(); var valid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Brtrue, valid);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, symbolType); il.Emit(OpCodes.Brtrue, valid);
        il.Emit(OpCodes.Ldstr, "Own-state keys must already be normalized string or Symbol values.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(ArgumentException), [_types.String])!); il.Emit(OpCodes.Throw);
        il.MarkLabel(valid); il.Emit(OpCodes.Ret);

        var getState = operations.DefineMethod("GetState", MethodAttributes.Private | MethodAttributes.Static, state, [_types.Object, _types.Boolean]);
        il = getState.GetILGenerator(); var ownerPresent = il.DefineLabel(); var lookup = il.DefineLabel(); var found = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue, ownerPresent);
        il.Emit(OpCodes.Ldstr, "owner"); il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(ArgumentNullException), [_types.String])!); il.Emit(OpCodes.Throw);
        il.MarkLabel(ownerPresent); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, lookup);
        il.Emit(OpCodes.Ldsfld, table); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldsfld, callback);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "GetValue", _types.Object, callbackType));
        il.Emit(OpCodes.Castclass, state); il.Emit(OpCodes.Ret);
        il.MarkLabel(lookup); il.Emit(OpCodes.Ldsfld, table); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, found);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType())); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, found); il.Emit(OpCodes.Castclass, state); il.Emit(OpCodes.Ret);

        var contains = _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object);
        var addKey = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        void Validate(ILGenerator body) { body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Call, validate); }
        void State(ILGenerator body, bool allocate) { body.Emit(OpCodes.Ldarg_0); body.Emit(allocate ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); body.Emit(OpCodes.Call, getState); }
        void Fail(ILGenerator body, string message) { body.Emit(OpCodes.Ldstr, message); body.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(InvalidOperationException), [_types.String])!); body.Emit(OpCodes.Throw); }

        il = commitDefinition.GetILGenerator(); var defined = il.DefineLabel(); var definitionState = il.DeclareLocal(state);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Brtrue, defined); il.Emit(OpCodes.Ret); il.MarkLabel(defined);
        Validate(il); State(il, true); il.Emit(OpCodes.Stloc, definitionState);
        var alreadyPresent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, definitionState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, contains); il.Emit(OpCodes.Brtrue, alreadyPresent);
        il.Emit(OpCodes.Ldloc, definitionState); il.Emit(OpCodes.Ldfld, order); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, addKey);
        il.MarkLabel(alreadyPresent);
        il.Emit(OpCodes.Ldloc, definitionState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object)); il.Emit(OpCodes.Ret);

        il = commitUpdate.GetILGenerator(); var updated = il.DefineLabel(); var updateState = il.DeclareLocal(state); var updateMissing = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brtrue, updated); il.Emit(OpCodes.Ret); il.MarkLabel(updated);
        Validate(il); State(il, false); il.Emit(OpCodes.Stloc, updateState); il.Emit(OpCodes.Ldloc, updateState); il.Emit(OpCodes.Brfalse, updateMissing);
        il.Emit(OpCodes.Ldloc, updateState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, contains); il.Emit(OpCodes.Brfalse, updateMissing); il.Emit(OpCodes.Ret);
        il.MarkLabel(updateMissing); Fail(il, "Own-property update has no recorded definition.");

        il = commitDelete.GetILGenerator(); var deleting = il.DefineLabel(); var deleteDone = il.DefineLabel(); var deleteState = il.DeclareLocal(state);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brtrue, deleting); il.Emit(OpCodes.Ret); il.MarkLabel(deleting);
        Validate(il); State(il, false); il.Emit(OpCodes.Stloc, deleteState); il.Emit(OpCodes.Ldloc, deleteState); il.Emit(OpCodes.Brfalse, deleteDone);
        il.Emit(OpCodes.Ldloc, deleteState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "Remove", _types.Object)); il.Emit(OpCodes.Brfalse, deleteDone);
        il.Emit(OpCodes.Ldloc, deleteState); il.Emit(OpCodes.Ldfld, order); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Remove", _types.Object)); il.Emit(OpCodes.Pop);
        il.MarkLabel(deleteDone); il.Emit(OpCodes.Ret);

        il = hasOwn.GetILGenerator(); var hasState = il.DefineLabel(); var ownState = il.DeclareLocal(state);
        Validate(il); State(il, false); il.Emit(OpCodes.Stloc, ownState); il.Emit(OpCodes.Ldloc, ownState); il.Emit(OpCodes.Brtrue, hasState);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.MarkLabel(hasState);
        il.Emit(OpCodes.Ldloc, ownState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, contains); il.Emit(OpCodes.Ret);

        il = activeField.GetILGenerator(); var activeDone = il.DefineLabel(); var activeState = il.DeclareLocal(state); var identity = il.DeclareLocal(_types.Object);
        Validate(il); State(il, false); il.Emit(OpCodes.Stloc, activeState); il.Emit(OpCodes.Ldloc, activeState); il.Emit(OpCodes.Brfalse, activeDone);
        il.Emit(OpCodes.Ldloc, activeState); il.Emit(OpCodes.Ldfld, active); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, identity);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue", _types.Object, _types.Object.MakeByRefType())); il.Emit(OpCodes.Pop);
        il.MarkLabel(activeDone); il.Emit(OpCodes.Ldloc, identity); il.Emit(OpCodes.Ret);
        return new(contract, descriptor, keys, prototype, isReceiver, descriptorType, _types.Object, _types.ListOfObject, _types.Boolean,
            state, operations, declarations, order, getState);
    }

    private void EmitGuestOwnPropertySnapshot(EmittedGuestOwnPropertyRuntime runtime, MethodInfo normalize)
    {
        if (runtime.IsComplete || runtime.OperationsType.IsCreated() || !normalize.IsStatic || normalize.IsGenericMethod
            || normalize.Module.ModuleVersionId != runtime.OperationsType.Module.ModuleVersionId
            || normalize.ReturnType != runtime.ListType
            || !normalize.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { runtime.ListType }))
            throw new InvalidOperationException("Own-key snapshots require the exact local list normalization peer before completion.");
        var il = runtime.SnapshotKeys.GetILGenerator();
        var state = il.DeclareLocal(runtime.StateType);
        var strings = il.DeclareLocal(_types.ListOfObject);
        var symbols = il.DeclareLocal(_types.ListOfObject);
        var index = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.Object);
        var order = runtime.StateOrder;
        var getState = runtime.GetState;
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Stloc, strings);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Stloc, symbols);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, getState); il.Emit(OpCodes.Stloc, state);
        var finish = il.DefineLabel(); var loop = il.DefineLabel(); var symbol = il.DefineLabel(); var next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Brfalse, finish);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldfld, order);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, finish);
        il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldfld, order); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32)); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Brfalse, symbol);
        il.Emit(OpCodes.Ldloc, strings); il.Emit(OpCodes.Br, next);
        il.MarkLabel(symbol); il.Emit(OpCodes.Ldloc, symbols);
        il.MarkLabel(next); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(finish); il.Emit(OpCodes.Ldloc, strings); il.Emit(OpCodes.Call, normalize); il.Emit(OpCodes.Stloc, strings);
        il.Emit(OpCodes.Ldloc, strings); il.Emit(OpCodes.Ldloc, symbols);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));
        il.Emit(OpCodes.Ldloc, strings); il.Emit(OpCodes.Ret);
        runtime.OperationsType.CreateType();
    }
}
