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
        // Define class: public class $Array : IList<object?>
        var typeBuilder = moduleBuilder.DefineType(
            "$Array",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IListOfObject]  // Implement IList<object?> for Array.isArray() compatibility
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

        // IList<object?> implementation - enables Array.isArray() to work
        EmitTSArrayIListImplementation(typeBuilder, runtime);

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

    /// <summary>
    /// Emits the IList&lt;object?&gt; interface implementation for $Array.
    /// All methods delegate to the _elements field.
    /// </summary>
    private void EmitTSArrayIListImplementation(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Get interface methods we need to implement
        var ilistType = _types.IListOfObject;
        var icollectionType = _types.ICollectionOfObject;
        var ienumerableGenericType = _types.IEnumerableOfObject;
        var ienumerableType = _types.IEnumerable;

        // IList<T>.this[int index] { get; set; } - Indexer
        EmitTSArrayIndexerGet(typeBuilder);
        EmitTSArrayIndexerSet(typeBuilder);

        // ICollection<T>.Count property
        EmitTSArrayCount(typeBuilder);

        // ICollection<T>.IsReadOnly property
        EmitTSArrayIsReadOnly(typeBuilder);

        // IList<T>.IndexOf(T item)
        EmitTSArrayIndexOf(typeBuilder);

        // IList<T>.Insert(int index, T item)
        EmitTSArrayInsert(typeBuilder);

        // IList<T>.RemoveAt(int index)
        EmitTSArrayRemoveAt(typeBuilder);

        // ICollection<T>.Add(T item)
        EmitTSArrayAdd(typeBuilder);

        // ICollection<T>.Clear()
        EmitTSArrayClear(typeBuilder);

        // ICollection<T>.Contains(T item)
        EmitTSArrayContains(typeBuilder);

        // ICollection<T>.CopyTo(T[] array, int arrayIndex)
        EmitTSArrayCopyTo(typeBuilder);

        // ICollection<T>.Remove(T item)
        EmitTSArrayRemove(typeBuilder);

        // IEnumerable<T>.GetEnumerator()
        EmitTSArrayGetEnumeratorGeneric(typeBuilder);

        // IEnumerable.GetEnumerator()
        EmitTSArrayGetEnumeratorNonGeneric(typeBuilder);
    }

    private void EmitTSArrayIndexerGet(TypeBuilder typeBuilder)
    {
        // Explicit interface implementation: object? IList<object?>.this[int index] { get; }
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IList<System.Object>.get_Item",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Int32]
        );

        var il = method.GetILGenerator();
        // return _elements[index];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Ret);

        // Map to the interface method
        var interfaceMethod = _types.IListOfObject.GetMethod("get_Item", [_types.Int32])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayIndexerSet(TypeBuilder typeBuilder)
    {
        // Explicit interface implementation: object? IList<object?>.this[int index] { set; }
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IList<System.Object>.set_Item",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        var il = method.GetILGenerator();
        // _elements[index] = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("set_Item", [_types.Int32, _types.Object])!);
        il.Emit(OpCodes.Ret);

        // Map to the interface method
        var interfaceMethod = _types.IListOfObject.GetMethod("set_Item", [_types.Int32, _types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayCount(TypeBuilder typeBuilder)
    {
        // Explicit interface implementation: int ICollection<object?>.Count { get; }
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.get_Count",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
            _types.Int32,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // return _elements.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // Map to the interface method
        var interfaceMethod = _types.ICollectionOfObject.GetProperty("Count")!.GetGetMethod()!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayIsReadOnly(TypeBuilder typeBuilder)
    {
        // Explicit interface implementation: bool ICollection<object?>.IsReadOnly { get; }
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.get_IsReadOnly",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // return false;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Map to the interface method
        var interfaceMethod = _types.ICollectionOfObject.GetProperty("IsReadOnly")!.GetGetMethod()!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayIndexOf(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IList<System.Object>.IndexOf",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Int32,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        // return _elements.IndexOf(item);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("IndexOf", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.IListOfObject.GetMethod("IndexOf", [_types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayInsert(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IList<System.Object>.Insert",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        var il = method.GetILGenerator();
        // _elements.Insert(index, item);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Insert", [_types.Int32, _types.Object])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.IListOfObject.GetMethod("Insert", [_types.Int32, _types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayRemoveAt(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IList<System.Object>.RemoveAt",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.Int32]
        );

        var il = method.GetILGenerator();
        // _elements.RemoveAt(index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("RemoveAt", [_types.Int32])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.IListOfObject.GetMethod("RemoveAt", [_types.Int32])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayAdd(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.Add",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        // _elements.Add(item);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.ICollectionOfObject.GetMethod("Add", [_types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayClear(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.Clear",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // _elements.Clear();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Clear", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.ICollectionOfObject.GetMethod("Clear", Type.EmptyTypes)!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayContains(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.Contains",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Boolean,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        // return _elements.Contains(item);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Contains", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.ICollectionOfObject.GetMethod("Contains", [_types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayCopyTo(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.CopyTo",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.ObjectArray, _types.Int32]
        );

        var il = method.GetILGenerator();
        // _elements.CopyTo(array, arrayIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("CopyTo", [_types.ObjectArray, _types.Int32])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.ICollectionOfObject.GetMethod("CopyTo", [_types.ObjectArray, _types.Int32])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayRemove(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.ICollection<System.Object>.Remove",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Boolean,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        // return _elements.Remove(item);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Remove", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.ICollectionOfObject.GetMethod("Remove", [_types.Object])!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayGetEnumeratorGeneric(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.Generic.IEnumerable<System.Object>.GetEnumerator",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IEnumeratorOfObject,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // return _elements.GetEnumerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("GetEnumerator", Type.EmptyTypes)!);
        // List<T>.GetEnumerator() returns List<T>.Enumerator (a struct), but we need IEnumerator<T>
        // Box the struct to get the interface
        var listEnumeratorType = _types.ListOfObject.GetMethod("GetEnumerator", Type.EmptyTypes)!.ReturnType;
        il.Emit(OpCodes.Box, listEnumeratorType);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.IEnumerableOfObject.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }

    private void EmitTSArrayGetEnumeratorNonGeneric(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "System.Collections.IEnumerable.GetEnumerator",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IEnumerator,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // return _elements.GetEnumerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayElementsField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("GetEnumerator", Type.EmptyTypes)!);
        // Box the struct enumerator to get IEnumerator
        var listEnumeratorType = _types.ListOfObject.GetMethod("GetEnumerator", Type.EmptyTypes)!.ReturnType;
        il.Emit(OpCodes.Box, listEnumeratorType);
        il.Emit(OpCodes.Ret);

        var interfaceMethod = _types.IEnumerable.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        typeBuilder.DefineMethodOverride(method, interfaceMethod);
    }
}
