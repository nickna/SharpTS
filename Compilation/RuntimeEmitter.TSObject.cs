using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Object class for standalone object literal support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSObject.
/// </summary>
public partial class RuntimeEmitter
{
    // $Object class fields
    private FieldBuilder _tsObjectFieldsField = null!;
    private FieldBuilder _tsObjectIsFrozenField = null!;
    private FieldBuilder _tsObjectIsSealedField = null!;

    private void EmitTSObjectClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Object
        var typeBuilder = moduleBuilder.DefineType(
            "$Object",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSObjectType = typeBuilder;

        // Fields
        _tsObjectFieldsField = typeBuilder.DefineField("_fields", _types.DictionaryStringObject, FieldAttributes.Private);
        _tsObjectIsFrozenField = typeBuilder.DefineField("_isFrozen", _types.Boolean, FieldAttributes.Private);
        _tsObjectIsSealedField = typeBuilder.DefineField("_isSealed", _types.Boolean, FieldAttributes.Private);

        // Constructor: public $Object(Dictionary<string, object?> fields)
        EmitTSObjectConstructor(typeBuilder, runtime);

        // Property: Fields (getter only)
        EmitTSObjectFieldsProperty(typeBuilder, runtime);

        // Properties: IsFrozen, IsSealed
        EmitTSObjectIsFrozenProperty(typeBuilder, runtime);
        EmitTSObjectIsSealedProperty(typeBuilder, runtime);

        // Methods: Freeze, Seal
        EmitTSObjectFreeze(typeBuilder, runtime);
        EmitTSObjectSeal(typeBuilder, runtime);

        // Methods: GetProperty, SetProperty, SetPropertyStrict, HasProperty, DeleteProperty
        EmitTSObjectGetProperty(typeBuilder, runtime);
        EmitTSObjectSetProperty(typeBuilder, runtime);
        EmitTSObjectSetPropertyStrict(typeBuilder, runtime);
        EmitTSObjectHasProperty(typeBuilder, runtime);
        EmitTSObjectDeleteProperty(typeBuilder, runtime);

        // Property: PropertyNames (for Object.keys/for-in)
        EmitTSObjectPropertyNames(typeBuilder, runtime);

        // Override: ToString()
        EmitTSObjectToString(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSObjectConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.DictionaryStringObject]
        );
        runtime.TSObjectCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _fields = fields
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsObjectFieldsField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Return IReadOnlyDictionary<string, object?> for Fields property
        var prop = typeBuilder.DefineProperty(
            "Fields",
            PropertyAttributes.None,
            _types.DictionaryStringObject,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Fields",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.DictionaryStringObject,
            Type.EmptyTypes
        );
        runtime.TSObjectFieldsGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSObjectIsFrozenProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "IsFrozen",
            PropertyAttributes.None,
            _types.Boolean,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_IsFrozen",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSObjectIsFrozenGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsFrozenField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSObjectIsSealedProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "IsSealed",
            PropertyAttributes.None,
            _types.Boolean,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_IsSealed",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSObjectIsSealedGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsSealedField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSObjectFreeze(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Freeze",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSObjectFreeze = method;

        var il = method.GetILGenerator();

        // _isFrozen = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsObjectIsFrozenField);

        // _isSealed = true (frozen implies sealed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsObjectIsSealedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectSeal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Seal",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSObjectSeal = method;

        var il = method.GetILGenerator();

        // _isSealed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsObjectIsSealedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSObjectGetProperty = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var foundLabel = il.DefineLabel();

        // if (_fields.TryGetValue(name, out value)) return value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, foundLabel);

        // return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetProperty",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );
        runtime.TSObjectSetProperty = method;

        var il = method.GetILGenerator();
        var notFrozenLabel = il.DefineLabel();
        var notSealedOrExistsLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (_isFrozen) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsFrozenField);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        // if (_isSealed && !_fields.ContainsKey(name)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsSealedField);
        il.Emit(OpCodes.Brfalse, notSealedOrExistsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Brtrue, notSealedOrExistsLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSealedOrExistsLabel);

        // _fields[name] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectSetPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPropertyStrict",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object, _types.Boolean]
        );
        runtime.TSObjectSetPropertyStrict = method;

        var il = method.GetILGenerator();
        var notFrozenLabel = il.DefineLabel();
        var frozenReturnLabel = il.DefineLabel();
        var notSealedOrExistsLabel = il.DefineLabel();
        var sealedReturnLabel = il.DefineLabel();

        // if (_isFrozen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsFrozenField);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // if (strictMode) throw TypeError
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, frozenReturnLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Cannot assign to read only property of object");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(frozenReturnLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        // if (_isSealed && !_fields.ContainsKey(name))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsSealedField);
        il.Emit(OpCodes.Brfalse, notSealedOrExistsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Brtrue, notSealedOrExistsLabel);

        // if (strictMode) throw TypeError
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, sealedReturnLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Cannot add property to a sealed object");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(sealedReturnLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSealedOrExistsLabel);

        // _fields[name] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectHasProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HasProperty",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String]
        );
        runtime.TSObjectHasProperty = method;

        var il = method.GetILGenerator();

        // return _fields.ContainsKey(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectDeleteProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DeleteProperty",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String]
        );
        runtime.TSObjectDeleteProperty = method;

        var il = method.GetILGenerator();
        var notFrozenSealedLabel = il.DefineLabel();
        var falseReturnLabel = il.DefineLabel();

        // if (_isFrozen || _isSealed) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsFrozenField);
        il.Emit(OpCodes.Brtrue, falseReturnLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectIsSealedField);
        il.Emit(OpCodes.Brtrue, falseReturnLabel);

        // return _fields.Remove(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("Remove", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // return false for frozen/sealed
        il.MarkLabel(falseReturnLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSObjectPropertyNames(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "PropertyNames",
            PropertyAttributes.None,
            typeof(IEnumerable<string>),
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_PropertyNames",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(IEnumerable<string>),
            Type.EmptyTypes
        );
        runtime.TSObjectGetKeys = getter;

        var il = getter.GetILGenerator();

        // return _fields.Keys
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsObjectFieldsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSObjectToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSObjectToString = method;

        var il = method.GetILGenerator();

        // Simple implementation: return "[object Object]"
        il.Emit(OpCodes.Ldstr, "[object Object]");
        il.Emit(OpCodes.Ret);
    }
}
