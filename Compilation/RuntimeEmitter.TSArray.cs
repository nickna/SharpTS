using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Array class for standalone array support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray.
/// </summary>
public partial class RuntimeEmitter
{
    // $Array class fields
    private FieldBuilder _tsArrayElementsField = null!;
    private FieldBuilder _tsArrayIsFrozenField = null!;
    private FieldBuilder _tsArrayIsSealedField = null!;

    private void EmitTSArrayClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Array
        var typeBuilder = moduleBuilder.DefineType(
            "$Array",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSArrayType = typeBuilder;

        // Fields
        _tsArrayElementsField = typeBuilder.DefineField("_elements", _types.ListOfObject, FieldAttributes.Private);
        _tsArrayIsFrozenField = typeBuilder.DefineField("_isFrozen", _types.Boolean, FieldAttributes.Private);
        _tsArrayIsSealedField = typeBuilder.DefineField("_isSealed", _types.Boolean, FieldAttributes.Private);

        // Constructor: public $Array(List<object?> elements)
        EmitTSArrayConstructor(typeBuilder, runtime);

        // Property: Elements (getter only)
        EmitTSArrayElementsProperty(typeBuilder, runtime);

        // Properties: IsFrozen, IsSealed
        EmitTSArrayIsFrozenProperty(typeBuilder, runtime);
        EmitTSArrayIsSealedProperty(typeBuilder, runtime);

        // Methods: Freeze, Seal
        EmitTSArrayFreeze(typeBuilder, runtime);
        EmitTSArraySeal(typeBuilder, runtime);

        // Methods: Get, Set, SetStrict
        EmitTSArrayGet(typeBuilder, runtime);
        EmitTSArraySet(typeBuilder, runtime);
        EmitTSArraySetStrict(typeBuilder, runtime);

        // Override: ToString()
        EmitTSArrayToString(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSArrayConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ListOfObject]
        );
        runtime.TSArrayCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _elements = elements
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsArrayElementsField);

        // _isFrozen = false (default)
        // _isSealed = false (default)

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArrayElementsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty(
            "Elements",
            PropertyAttributes.None,
            _types.ListOfObject,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Elements",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.ListOfObject,
            Type.EmptyTypes
        );
        runtime.TSArrayElementsGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayIsFrozenProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSArrayIsFrozenGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayIsSealedProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSArrayIsSealedGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsSealedField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayFreeze(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Freeze",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSArrayFreeze = method;

        var il = method.GetILGenerator();

        // _isFrozen = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsFrozenField);

        // _isSealed = true (frozen implies sealed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsSealedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArraySeal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Seal",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSArraySeal = method;

        var il = method.GetILGenerator();

        // _isSealed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsSealedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArrayGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public,
            _types.Object,
            [_types.Int32]
        );
        runtime.TSArrayGet = method;

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (index < 0 || index >= _elements.Count) throw
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, throwLabel);

        // return _elements[index]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Ret);

        // throw new Exception("Index out of bounds.")
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Index out of bounds.");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitTSArraySet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Set",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object]
        );
        runtime.TSArraySet = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();
        var boundsCheckLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // if (_isFrozen) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brtrue, frozenLabel);

        // if (index < 0 || index >= _elements.Count) throw
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, throwLabel);

        // _elements[index] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("set_Item", [_types.Int32, _types.Object])!);

        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ret);

        // throw new Exception("Index out of bounds.")
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Index out of bounds.");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitTSArraySetStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetStrict",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );
        runtime.TSArraySetStrict = method;

        var il = method.GetILGenerator();
        var notFrozenLabel = il.DefineLabel();
        var frozenReturnLabel = il.DefineLabel();
        var throwBoundsLabel = il.DefineLabel();

        // if (_isFrozen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // if (strictMode) throw TypeError
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, frozenReturnLabel);

        // throw new Exception($"TypeError: Cannot assign to read only property...")
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot assign to read only property of array");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(frozenReturnLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        // if (index < 0 || index >= _elements.Count) throw bounds
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwBoundsLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, throwBoundsLabel);

        // _elements[index] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("set_Item", [_types.Int32, _types.Object])!);
        il.Emit(OpCodes.Ret);

        // throw new Exception("Index out of bounds.")
        il.MarkLabel(throwBoundsLabel);
        il.Emit(OpCodes.Ldstr, "Index out of bounds.");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitTSArrayToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSArrayToString = method;

        var il = method.GetILGenerator();

        // JavaScript Array.toString() returns elements joined by comma
        // Use string.Join(string, IEnumerable<object>) via Enumerable.Cast
        // For simplicity, call _elements.ToArray() then use Join(string, object[])
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ListOfObject, "ToArray"));
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, _types.ObjectArray])!);
        il.Emit(OpCodes.Ret);
    }
}
