using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitClassStaticPropertyAttribute(ModuleBuilder module, EmittedObjectKeysRuntime keys)
    {
        var type = EmitTypeDefinitions.DefineType(module, "$ClassStaticProperty",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, typeof(Attribute));
        var name = type.DefineField("Name", _types.String, FieldAttributes.Public | FieldAttributes.InitOnly);
        var enumerable = type.DefineField("Enumerable", _types.Boolean, FieldAttributes.Public | FieldAttributes.InitOnly);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [_types.String, _types.Boolean]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, name);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, enumerable);
        il.Emit(OpCodes.Ret);
        keys.ClassStaticPropertyType = type;
        keys.ClassStaticPropertyConstructor = constructor;
        keys.ClassStaticPropertyName = name;
        keys.ClassStaticPropertyEnumerable = enumerable;
        var tableType = typeof(ConditionalWeakTable<object, List<object>>);
        var order = type.DefineField("CreationOrder", tableType, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        keys.ConstructorCreationOrder = order;
        var initialize = type.DefineTypeInitializer().GetILGenerator();
        initialize.Emit(OpCodes.Newobj, tableType.GetConstructor(Type.EmptyTypes)!);
        initialize.Emit(OpCodes.Stsfld, order); initialize.Emit(OpCodes.Ret);
        keys.RecordConstructorKey = type.DefineMethod("RecordKey", MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, [_types.Object, _types.String]);
        keys.ForgetConstructorKey = type.DefineMethod("ForgetKey", MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, [_types.Object, _types.String]);
        var forget = keys.ForgetConstructorKey.GetILGenerator();
        var list = forget.DeclareLocal(_types.ListOfObject);
        var done = forget.DefineLabel();
        forget.Emit(OpCodes.Ldarg_0); forget.Emit(OpCodes.Brfalse, done);
        forget.Emit(OpCodes.Ldsfld, order); forget.Emit(OpCodes.Ldarg_0); forget.Emit(OpCodes.Ldloca, list);
        forget.Emit(OpCodes.Callvirt, _types.GetMethod(tableType, "TryGetValue", _types.Object, _types.ListOfObject.MakeByRefType()));
        forget.Emit(OpCodes.Brfalse, done);
        forget.Emit(OpCodes.Ldloc, list); forget.Emit(OpCodes.Ldarg_1);
        forget.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Remove", _types.Object)); forget.Emit(OpCodes.Pop);
        forget.MarkLabel(done); forget.Emit(OpCodes.Ret);
    }

    private void EmitClassConstructorKeyRecording(EmittedObjectKeysRuntime keys, EmittedDescriptorStorageRuntime descriptors)
    {
        var il = keys.RecordConstructorKey.GetILGenerator();
        var list = il.DeclareLocal(_types.ListOfObject);
        var extras = il.DeclareLocal(_types.ListOfObject);
        var key = il.DeclareLocal(_types.Object);
        var index = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldsfld, keys.ConstructorCreationOrder); il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keys.ConstructorCreationOrder.FieldType, "GetOrCreateValue", _types.Object));
        il.Emit(OpCodes.Stloc, list);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, descriptors.GetAllExtraKeys); il.Emit(OpCodes.Stloc, extras);
        var loop = il.DefineLabel(); var next = il.DefineLabel(); var end = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, extras);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, end);
        il.Emit(OpCodes.Ldloc, extras); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32)); il.Emit(OpCodes.Stloc, key);
        AddUnique(next);
        il.MarkLabel(next); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(end); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, key);
        var complete = il.DefineLabel(); AddUnique(complete); il.MarkLabel(complete); il.Emit(OpCodes.Ret);
        ((TypeBuilder)keys.ClassStaticPropertyType).CreateType();
        void AddUnique(Label skip)
        {
            il.Emit(OpCodes.Ldloc, list); il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Contains", _types.Object)); il.Emit(OpCodes.Brtrue, skip);
            il.Emit(OpCodes.Ldloc, list); il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        }
    }

    // Null means this is not a nominal source constructor. Existing carrier
    // paths retain authority; no host/BCL member inventory is added.
    private void EmitClassConstructorKeys(TypeBuilder runtime, EmittedObjectKeysRuntime keys,
        EmittedDescriptorStorageRuntime descriptors, EmittedObjectStateRuntime state,
        EmittedFunctionAttributesRuntime functions, Type fieldsInterface, NativeSourceOwnerCatalog? sourceOwners = null)
    {
        EmitClassConstructorKeyRecording(keys, descriptors);
        var method = runtime.DefineMethod("GetClassConstructorKeys", MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject, [_types.Object, _types.Boolean]);
        keys.ConstructorKeys = method;
        var il = method.GetILGenerator();
        var owner = il.DeclareLocal(_types.Type);
        var notConstructor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, _types.Type); il.Emit(OpCodes.Stloc, owner);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Brfalse, notConstructor);
        il.Emit(OpCodes.Ldtoken, fieldsInterface); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Brfalse, notConstructor);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Ldtoken, functions.FunctionNameType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, notConstructor);

        // Reflection uses a closed canonical lookup owner; descriptor/deletion
        // identity always remains the original guest constructor argument.
        var lookup = EmitStaticMemberLookupOwner(il, owner, fieldsInterface, sourceOwners);
        var candidates = il.DeclareLocal(_types.DictionaryStringObject);
        var result = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor); il.Emit(OpCodes.Stloc, candidates);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Stloc, result);
        var setCandidate = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        foreach (string name in new[] { "length", "name", "prototype" })
        {
            il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Box, _types.Boolean); il.Emit(OpCodes.Callvirt, setCandidate);
        }
        var index = il.DeclareLocal(_types.Int32);
        var recorded = il.DeclareLocal(_types.ListOfObject);
        var noRecorded = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, keys.ConstructorCreationOrder); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, recorded);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keys.ConstructorCreationOrder.FieldType, "TryGetValue", _types.Object, _types.ListOfObject.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, noRecorded);
        var recordedLoop = il.DefineLabel(); var recordedNext = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(recordedLoop); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, recorded);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, noRecorded);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, recorded); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32)); il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String)); il.Emit(OpCodes.Brtrue, recordedNext);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, recorded); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32)); il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Box, _types.Boolean); il.Emit(OpCodes.Callvirt, setCandidate);
        il.MarkLabel(recordedNext); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index); il.Emit(OpCodes.Br, recordedLoop);
        il.MarkLabel(noRecorded);
        // Evaluated computed names and expandos are already stored under their
        // JavaScript keys. Synthetic CLR backing fields/methods are unmarked.
        var extras = il.DeclareLocal(_types.ListOfObject); var key = il.DeclareLocal(_types.String);
        var extraLoop = il.DefineLabel(); var extraNext = il.DefineLabel(); var extraEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, descriptors.GetAllExtraKeys);
        il.Emit(OpCodes.Stloc, extras); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(extraLoop);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, extras);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, extraEnd);
        il.Emit(OpCodes.Ldloc, extras); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Castclass, _types.String); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, extraNext);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean); il.Emit(OpCodes.Callvirt, setCandidate);
        il.MarkLabel(extraNext);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, extraLoop); il.MarkLabel(extraEnd);

        var members = il.DeclareLocal(typeof(MemberInfo[]));
        var member = il.DeclareLocal(typeof(MemberInfo));
        var attribute = il.DeclareLocal(keys.ClassStaticPropertyType);

        var memberLoop = il.DefineLabel(); var memberNext = il.DefineLabel(); var memberEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lookup);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        if (_nativeConstructorMembers is { } memberLookup) il.Emit(OpCodes.Call, memberLookup);
        else il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMembers", typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, members); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(memberLoop);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, members); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, memberEnd);
        il.Emit(OpCodes.Ldloc, members); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Stloc, member);
        il.Emit(OpCodes.Ldloc, member); il.Emit(OpCodes.Ldtoken, keys.ClassStaticPropertyType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(MemberInfo), "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, memberNext);
        il.Emit(OpCodes.Ldloc, member); il.Emit(OpCodes.Ldtoken, keys.ClassStaticPropertyType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(MemberInfo), "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Castclass, keys.ClassStaticPropertyType);
        il.Emit(OpCodes.Stloc, attribute);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, attribute); il.Emit(OpCodes.Ldfld, keys.ClassStaticPropertyName);
        il.Emit(OpCodes.Ldloc, attribute); il.Emit(OpCodes.Ldfld, keys.ClassStaticPropertyEnumerable); il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setCandidate);
        il.MarkLabel(memberNext);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, memberLoop); il.MarkLabel(memberEnd);

        var iterator = il.DeclareLocal(_types.IEnumeratorOfString);
        var descriptor = il.DeclareLocal(descriptors.DescriptorType);
        var loop = il.DefineLabel(); var next = il.DefineLabel(); var add = il.DefineLabel(); var noDescriptor = il.DefineLabel(); var end = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.DictionaryStringObject, "Keys"));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerableOfString, "GetEnumerator")); il.Emit(OpCodes.Stloc, iterator);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iterator); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext")); il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Ldloc, iterator); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfString, "Current")); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Call, descriptors.GetPropertyDescriptor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, noDescriptor);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, add);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, descriptors.DescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, next); il.Emit(OpCodes.Br, add);
        il.MarkLabel(noDescriptor);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Call, state.IsBuiltinDeleted); il.Emit(OpCodes.Brtrue, next);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, add);
        il.Emit(OpCodes.Ldloc, candidates); il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean); il.Emit(OpCodes.Brfalse, next);
        il.MarkLabel(add);
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(next); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Call, keys.Normalize); il.Emit(OpCodes.Ret);
        il.MarkLabel(notConstructor); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
    }
}
