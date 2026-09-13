using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all TypedArray types for standalone DLLs.
    /// </summary>
    private void EmitTypedArrayTypes(ModuleBuilder module, EmittedRuntime runtime)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
        // First emit the base class
        EmitTypedArrayBaseType(module, arrays);

        // Then emit concrete types
        EmitConcreteTypedArrayType(module, runtime, "Int8Array", 1, true, false);
        EmitConcreteTypedArrayType(module, runtime, "Uint8Array", 1, false, false);
        EmitConcreteTypedArrayType(module, runtime, "Uint8ClampedArray", 1, false, true);
        EmitConcreteTypedArrayType(module, runtime, "Int16Array", 2, true, false);
        EmitConcreteTypedArrayType(module, runtime, "Uint16Array", 2, false, false);
        EmitConcreteTypedArrayType(module, runtime, "Int32Array", 4, true, false);
        EmitConcreteTypedArrayType(module, runtime, "Uint32Array", 4, false, false);
        EmitConcreteTypedArrayType(module, runtime, "Float32Array", 4, false, false, isFloat: true);
        EmitConcreteTypedArrayType(module, runtime, "Float64Array", 8, false, false, isFloat: true);
        EmitConcreteTypedArrayType(module, runtime, "BigInt64Array", 8, true, false, isBigInt: true);
        EmitConcreteTypedArrayType(module, runtime, "BigUint64Array", 8, false, false, isBigInt: true);

        // Finalize base type after all derived types are defined
        arrays.BaseType.CreateType();
    }

    /// <summary>
    /// Emits the abstract $TypedArray base class.
    /// </summary>
    private void EmitTypedArrayBaseType(ModuleBuilder module, EmittedTypedArrayImplementation arrays)
    {
        arrays.BaseType = EmitTypeDefinitions.DefineType(module,
            "$TypedArray",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            _types.Object
        );

        // Fields
        arrays.BufferField = arrays.BaseType.DefineField("_buffer", typeof(byte[]), FieldAttributes.Family);
        arrays.ByteOffsetField = arrays.BaseType.DefineField("_byteOffset", _types.Int32, FieldAttributes.Family);
        arrays.LengthField = arrays.BaseType.DefineField("_length", _types.Int32, FieldAttributes.Family);
        arrays.ArrayBufferField = arrays.BaseType.DefineField("_arrayBuffer", _types.Object, FieldAttributes.Family);

        // Abstract properties
        arrays.BytesPerElementGetter = EmitTypedArrayAbstractProperty(arrays.BaseType, "BytesPerElement", _types.Int32);
        EmitTypedArrayAbstractProperty(arrays.BaseType, "TypeName", _types.String);
        arrays.ElementGet = arrays.BaseType.DefineMethod(
            "Get",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Int32]
        );
        arrays.ElementSet = arrays.BaseType.DefineMethod(
            "Set",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        // Concrete properties: Length, ByteOffset, ByteLength, Buffer
        EmitTypedArrayLengthProperty(arrays.BaseType, arrays);
        EmitTypedArrayByteOffsetProperty(arrays.BaseType, arrays);
        EmitTypedArrayByteLengthProperty(arrays.BaseType, arrays);
        EmitTypedArrayBufferProperty(arrays.BaseType, arrays);

        // Protected constructor
        var baseCtor = arrays.BaseType.DefineConstructor(
            MethodAttributes.Family,
            CallingConventions.Standard,
            [typeof(byte[]), _types.Int32, _types.Int32, _types.Object]
        );
        arrays.BaseCtor = baseCtor;

        var il = baseCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, arrays.BufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, arrays.ByteOffsetField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, arrays.LengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Stfld, arrays.ArrayBufferField);
        il.Emit(OpCodes.Ret);

        // GetBuffer method for internal access
        var getBufferMethod = arrays.BaseType.DefineMethod(
            "GetBuffer",
            MethodAttributes.Public,
            typeof(byte[]),
            Type.EmptyTypes
        );
        arrays.GetBuffer = getBufferMethod;
        var getBufferIl = getBufferMethod.GetILGenerator();
        getBufferIl.Emit(OpCodes.Ldarg_0);
        getBufferIl.Emit(OpCodes.Ldfld, arrays.BufferField);
        getBufferIl.Emit(OpCodes.Ret);

        // Abstract factories overridden by each concrete type — let the base-class Slice
        // (fresh same-kind copy) and Subarray (buffer-sharing view) build the right concrete
        // type without the base needing to know the concrete constructors (#940).
        arrays.CreateOfLength = arrays.BaseType.DefineMethod(
            "CreateOfLength",
            MethodAttributes.Family | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            arrays.BaseType,
            [_types.Int32]
        );
        arrays.CreateView = arrays.BaseType.DefineMethod(
            "CreateView",
            MethodAttributes.Family | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            arrays.BaseType,
            [_types.Int32, _types.Int32]
        );

        // Bulk instance methods (fill/copyWithin/reverse/set/slice/subarray/indexOf/…) mirroring
        // the interpreter's GetMember surface. Emitted here, before CreateType, so they live on
        // the base type. BCL-only — standalone-safe.
        EmitTypedArrayBulkMethods(arrays.BaseType, arrays);
    }

    private MethodBuilder EmitTypedArrayAbstractProperty(TypeBuilder typeBuilder, string name, Type returnType)
    {
        var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, returnType, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            returnType,
            Type.EmptyTypes
        );
        prop.SetGetMethod(getter);
        return getter;
    }

    private void EmitTypedArrayLengthProperty(TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays)
    {
        var prop = typeBuilder.DefineProperty("Length", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        arrays.LengthGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.LengthField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayByteOffsetProperty(TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays)
    {
        var prop = typeBuilder.DefineProperty("ByteOffset", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_ByteOffset",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        arrays.ByteOffsetGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.ByteOffsetField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayByteLengthProperty(TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays)
    {
        var prop = typeBuilder.DefineProperty("ByteLength", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_ByteLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Int32,
            Type.EmptyTypes
        );
        arrays.ByteLengthGetter = getter;
        var il = getter.GetILGenerator();
        // return _length * BytesPerElement
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.LengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, arrays.BytesPerElementGetter);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayBufferProperty(TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays)
    {
        var prop = typeBuilder.DefineProperty("Buffer", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Buffer",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        arrays.BufferGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.ArrayBufferField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits a concrete TypedArray type (e.g., $Uint8Array).
    /// </summary>
    private void EmitConcreteTypedArrayType(
        ModuleBuilder module,
        EmittedRuntime runtime,
        string name,
        int bytesPerElement,
        bool signed,
        bool clamped,
        bool isFloat = false,
        bool isBigInt = false)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
        var typeBuilder = EmitTypeDefinitions.DefineType(
            module,
            $"${name}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            arrays.BaseType
        );

        // Store type reference in runtime
        StoreTypedArrayType(arrays, name, typeBuilder);

        // Override BytesPerElement
        EmitBytesPerElementProperty(typeBuilder, bytesPerElement);

        // Override TypeName
        EmitTypeNameProperty(typeBuilder, name);

        // Constructor: public $Uint8Array(int length)
        var lengthCtor = EmitTypedArrayLengthConstructor(typeBuilder, arrays, bytesPerElement);
        StoreTypedArrayLengthCtor(arrays, name, lengthCtor);

        // Constructor: public $Uint8Array(object buffer, int byteOffset, int? length)
        var bufferCtor = EmitTypedArrayBufferConstructor(typeBuilder, runtime, bytesPerElement);
        StoreTypedArrayBufferCtor(arrays, name, bufferCtor);

        // Indexer: public object this[int index] { get; set; }
        EmitTypedArrayIndexer(typeBuilder, arrays, bytesPerElement, signed, clamped, isFloat, isBigInt);

        // Unboxed numeric element accessors (#3): GetUnboxed/SetUnboxed return/accept a native
        // double, for the compiled fast path (ILEmitter binds them at statically-typed sites).
        // Skip BigInt (bigint, not number) and Uint8Clamped (keeps the boxed clamp/round path).
        if (!isBigInt && !clamped)
        {
            var elementType = name.EndsWith("Array") ? name[..^5] : name;
            EmitUnboxedNumericAccessors(typeBuilder, arrays, elementType, bytesPerElement, signed, isFloat);
        }

        // Buffer-sharing ctor + CreateOfLength/CreateView overrides backing the base
        // Slice/Subarray (#940).
        EmitTypedArrayFactoryMembers(typeBuilder, arrays, lengthCtor);

        // Finalize type
        typeBuilder.CreateType();
    }

    private void StoreTypedArrayType(EmittedTypedArrayImplementation arrays, string name, TypeBuilder type)
    {
        switch (name)
        {
            case "Int8Array": arrays.Int8ArrayType = type; break;
            case "Uint8Array": arrays.Uint8ArrayType = type; break;
            case "Uint8ClampedArray": arrays.Uint8ClampedArrayType = type; break;
            case "Int16Array": arrays.Int16ArrayType = type; break;
            case "Uint16Array": arrays.Uint16ArrayType = type; break;
            case "Int32Array": arrays.Int32ArrayType = type; break;
            case "Uint32Array": arrays.Uint32ArrayType = type; break;
            case "Float32Array": arrays.Float32ArrayType = type; break;
            case "Float64Array": arrays.Float64ArrayType = type; break;
            case "BigInt64Array": arrays.BigInt64ArrayType = type; break;
            case "BigUint64Array": arrays.BigUint64ArrayType = type; break;
        }
    }

    private void StoreTypedArrayBufferCtor(EmittedTypedArrayImplementation arrays, string name, ConstructorBuilder ctor)
    {
        switch (name)
        {
            case "Int8Array": arrays.Int8ArrayBufferCtor = ctor; break;
            case "Uint8Array": arrays.Uint8ArrayBufferCtor = ctor; break;
            case "Uint8ClampedArray": arrays.Uint8ClampedArrayBufferCtor = ctor; break;
            case "Int16Array": arrays.Int16ArrayBufferCtor = ctor; break;
            case "Uint16Array": arrays.Uint16ArrayBufferCtor = ctor; break;
            case "Int32Array": arrays.Int32ArrayBufferCtor = ctor; break;
            case "Uint32Array": arrays.Uint32ArrayBufferCtor = ctor; break;
            case "Float32Array": arrays.Float32ArrayBufferCtor = ctor; break;
            case "Float64Array": arrays.Float64ArrayBufferCtor = ctor; break;
            case "BigInt64Array": arrays.BigInt64ArrayBufferCtor = ctor; break;
            case "BigUint64Array": arrays.BigUint64ArrayBufferCtor = ctor; break;
        }
    }

    private void EmitBytesPerElementProperty(TypeBuilder typeBuilder, int bytesPerElement)
    {
        var prop = typeBuilder.DefineProperty("BytesPerElement", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_BytesPerElement",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypeNameProperty(TypeBuilder typeBuilder, string typeName)
    {
        var prop = typeBuilder.DefineProperty("TypeName", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_TypeName",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldstr, typeName);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private ConstructorBuilder EmitTypedArrayLengthConstructor(TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays, int bytesPerElement)
    {
        // Constructor: public $XArray(int length)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );

        var il = ctor.GetILGenerator();

        // Create new byte array: new byte[length * bytesPerElement]
        var bufferLocal = il.DeclareLocal(typeof(byte[]));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc, bufferLocal);

        // Call base(buffer, 0, length, null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, arrays.BaseCtor);
        il.Emit(OpCodes.Ret);

        return ctor;
    }

    private void StoreTypedArrayLengthCtor(EmittedTypedArrayImplementation arrays, string name, ConstructorBuilder ctor)
    {
        switch (name)
        {
            case "Int8Array": arrays.Int8ArrayLengthCtor = ctor; break;
            case "Uint8Array": arrays.Uint8ArrayLengthCtor = ctor; break;
            case "Uint8ClampedArray": arrays.Uint8ClampedArrayLengthCtor = ctor; break;
            case "Int16Array": arrays.Int16ArrayLengthCtor = ctor; break;
            case "Uint16Array": arrays.Uint16ArrayLengthCtor = ctor; break;
            case "Int32Array": arrays.Int32ArrayLengthCtor = ctor; break;
            case "Uint32Array": arrays.Uint32ArrayLengthCtor = ctor; break;
            case "Float32Array": arrays.Float32ArrayLengthCtor = ctor; break;
            case "Float64Array": arrays.Float64ArrayLengthCtor = ctor; break;
            case "BigInt64Array": arrays.BigInt64ArrayLengthCtor = ctor; break;
            case "BigUint64Array": arrays.BigUint64ArrayLengthCtor = ctor; break;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2111",
        Justification = "The fixed Type.GetMethod(string, Type[]) BCL overload is used only as an IL token for CoreCLR-generated output; the native host never reflects over a trimmed application type.")]
    private ConstructorBuilder EmitTypedArrayBufferConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime, int bytesPerElement)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
        // Constructor: public $XArray(object buffer, int byteOffset, int? length)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Int32, typeof(int?)]
        );

        var il = ctor.GetILGenerator();

        var byteArrayLocal = il.DeclareLocal(typeof(byte[]));
        var bufByteLengthLocal = il.DeclareLocal(_types.Int32);
        var actualLengthLocal = il.DeclareLocal(_types.Int32);

        // Get byte[] from buffer
        var isSharedArrayBufferLabel = il.DefineLabel();
        var foreignSharedArrayBufferMethodFoundLabel = il.DefineLabel();
        var invalidBufferLabel = il.DefineLabel();
        var afterBufferLabel = il.DefineLabel();

        // Check if buffer is $ArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.RequireArrayBuffer().Type);
        il.Emit(OpCodes.Brfalse, isSharedArrayBufferLabel);

        // It's $ArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.RequireArrayBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireArrayBuffer().GetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.RequireArrayBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireArrayBuffer().ByteLengthGetter);
        il.Emit(OpCodes.Stloc, bufByteLengthLocal);
        il.Emit(OpCodes.Br, afterBufferLabel);

        il.MarkLabel(isSharedArrayBufferLabel);
        // Check if buffer is $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.RequireSharedArrayBuffer().Type);
        il.Emit(OpCodes.Brfalse, invalidBufferLabel);

        // It's $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.RequireSharedArrayBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireSharedArrayBuffer().GetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.RequireSharedArrayBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireSharedArrayBuffer().ByteLengthGetter);
        il.Emit(OpCodes.Stloc, bufByteLengthLocal);
        il.Emit(OpCodes.Br, afterBufferLabel);

        // A compiled Worker runs in a collectible AssemblyLoadContext, so the parent's emitted
        // $SharedArrayBuffer is not assignable to the worker realm's identically named type. Its
        // public GetBuffer shape is deliberately BCL-only: recover the SAME byte[] rather than
        // copying it, preserving SharedArrayBuffer aliasing across compiled realms.
        il.MarkLabel(invalidBufferLabel);
        il.Emit(OpCodes.Ldarg_1);
        var trulyInvalidBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, trulyInvalidBufferLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(GetType))!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.Name))!.GetMethod!);
        il.Emit(OpCodes.Ldstr, "$SharedArrayBuffer");
        il.Emit(OpCodes.Call, typeof(string).GetMethod(
            "op_Equality", BindingFlags.Public | BindingFlags.Static,
            binder: null, [typeof(string), typeof(string)], modifiers: null)!);
        il.Emit(OpCodes.Brfalse, trulyInvalidBufferLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(GetType))!);
        il.Emit(OpCodes.Ldstr, "GetBuffer");
        il.Emit(OpCodes.Ldsfld, typeof(Type).GetField(nameof(Type.EmptyTypes))!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod(
            nameof(Type.GetMethod), [typeof(string), typeof(Type[])])!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, foreignSharedArrayBufferMethodFoundLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, trulyInvalidBufferLabel);

        il.MarkLabel(foreignSharedArrayBufferMethodFoundLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(
            nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Castclass, typeof(byte[]));
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Ldloc, byteArrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bufByteLengthLocal);
        il.Emit(OpCodes.Br, afterBufferLabel);

        il.MarkLabel(trulyInvalidBufferLabel);
        il.Emit(OpCodes.Ldstr, "TypedArray buffer constructor requires emitted ArrayBuffer/SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(afterBufferLabel);

        // Calculate actual length
        var hasLengthLabel = il.DefineLabel();
        var afterLengthLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarga, 3);
        il.Emit(OpCodes.Call, typeof(int?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, hasLengthLabel);

        // Has length - use it
        il.Emit(OpCodes.Ldarga, 3);
        il.Emit(OpCodes.Call, typeof(int?).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, actualLengthLocal);
        il.Emit(OpCodes.Br, afterLengthLabel);

        il.MarkLabel(hasLengthLabel);
        // No length - calculate from buffer: (bufByteLength - byteOffset) / bytesPerElement
        il.Emit(OpCodes.Ldloc, bufByteLengthLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, actualLengthLocal);

        il.MarkLabel(afterLengthLabel);

        // Call base(buffer, byteOffset, actualLength, buffer)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, byteArrayLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, actualLengthLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, arrays.BaseCtor);
        il.Emit(OpCodes.Ret);

        return ctor;
    }

    private void EmitTypedArrayIndexer(
        TypeBuilder typeBuilder,
        EmittedTypedArrayImplementation arrays,
        int bytesPerElement,
        bool signed,
        bool clamped,
        bool isFloat,
        bool isBigInt)
    {
        // Getter: public object Get(int index)
        var getter = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Int32]
        );

        var getIl = getter.GetILGenerator();
        var indexLocal = getIl.DeclareLocal(_types.Int32);

        // Calculate byte index: _byteOffset + index * bytesPerElement
        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, arrays.ByteOffsetField);
        getIl.Emit(OpCodes.Ldarg_1);
        getIl.Emit(OpCodes.Ldc_I4, bytesPerElement);
        getIl.Emit(OpCodes.Mul);
        getIl.Emit(OpCodes.Add);
        getIl.Emit(OpCodes.Stloc, indexLocal);

        // Read value based on type
        if (bytesPerElement == 1)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            getIl.Emit(OpCodes.Ldelem_U1);
            if (signed)
                getIl.Emit(OpCodes.Conv_I1);
            getIl.Emit(OpCodes.Conv_R8);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 2)
        {
            // Use BitConverter.ToInt16/ToUInt16
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            if (signed)
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToInt16", [typeof(byte[]), typeof(int)])!);
            else
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToUInt16", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Conv_R8);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 4 && isFloat)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToSingle", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Conv_R8);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 4)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            if (signed)
            {
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToInt32", [typeof(byte[]), typeof(int)])!);
                getIl.Emit(OpCodes.Conv_R8);
            }
            else
            {
                // For unsigned, zero-extend to int64 first to get correct double value
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToUInt32", [typeof(byte[]), typeof(int)])!);
                getIl.Emit(OpCodes.Conv_U8);  // Zero-extend uint32 to uint64
                getIl.Emit(OpCodes.Conv_R8);  // Convert to double (now correctly as 4294967295, not -1)
            }
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 8 && isFloat)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToDouble", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 8 && isBigInt)
        {
            // For BigInt, return as BigInteger
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            if (signed)
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToInt64", [typeof(byte[]), typeof(int)])!);
            else
                getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToUInt64", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([signed ? typeof(long) : typeof(ulong)])!);
            getIl.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));
        }
        else
        {
            // Default - shouldn't reach here
            getIl.Emit(OpCodes.Ldnull);
        }
        getIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(getter, arrays.ElementGet);

        // Setter: public void Set(int index, object value)
        var setter = typeBuilder.DefineMethod(
            "Set",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        var setIl = setter.GetILGenerator();
        var setIndexLocal = setIl.DeclareLocal(_types.Int32);

        // Calculate byte index
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldfld, arrays.ByteOffsetField);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Ldc_I4, bytesPerElement);
        setIl.Emit(OpCodes.Mul);
        setIl.Emit(OpCodes.Add);
        setIl.Emit(OpCodes.Stloc, setIndexLocal);

        // Write value based on type
        if (bytesPerElement == 1)
        {
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            setIl.Emit(OpCodes.Ldloc, setIndexLocal);
            setIl.Emit(OpCodes.Ldarg_2);
            setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            setIl.Emit(OpCodes.Conv_I4);
            if (clamped)
            {
                // Clamp to 0-255
                var inRangeLabel = setIl.DefineLabel();
                var endClampLabel = setIl.DefineLabel();
                var valueLocal = setIl.DeclareLocal(_types.Int32);
                setIl.Emit(OpCodes.Stloc, valueLocal);

                // Check < 0
                setIl.Emit(OpCodes.Ldloc, valueLocal);
                setIl.Emit(OpCodes.Ldc_I4_0);
                setIl.Emit(OpCodes.Bge_S, inRangeLabel);
                setIl.Emit(OpCodes.Ldc_I4_0);
                setIl.Emit(OpCodes.Br_S, endClampLabel);

                setIl.MarkLabel(inRangeLabel);
                // Check > 255
                var notOverLabel = setIl.DefineLabel();
                setIl.Emit(OpCodes.Ldloc, valueLocal);
                setIl.Emit(OpCodes.Ldc_I4, 255);
                setIl.Emit(OpCodes.Ble_S, notOverLabel);
                setIl.Emit(OpCodes.Ldc_I4, 255);
                setIl.Emit(OpCodes.Br_S, endClampLabel);

                setIl.MarkLabel(notOverLabel);
                setIl.Emit(OpCodes.Ldloc, valueLocal);

                setIl.MarkLabel(endClampLabel);
            }
            setIl.Emit(OpCodes.Conv_U1);
            setIl.Emit(OpCodes.Stelem_I1);
        }
        else if (bytesPerElement == 2)
        {
            // Unsafe.WriteUnaligned(ref _buffer[byteIdx], (short|ushort)(int)Convert.ToDouble(value));
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            setIl.Emit(OpCodes.Ldloc, setIndexLocal);
            setIl.Emit(OpCodes.Ldelema, typeof(byte));
            setIl.Emit(OpCodes.Ldarg_2);
            setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            setIl.Emit(OpCodes.Conv_I4);
            if (signed)
            {
                setIl.Emit(OpCodes.Conv_I2);
                setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(short)));
            }
            else
            {
                setIl.Emit(OpCodes.Conv_U2);
                setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(ushort)));
            }
        }
        else if (bytesPerElement == 4)
        {
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            setIl.Emit(OpCodes.Ldloc, setIndexLocal);
            setIl.Emit(OpCodes.Ldelema, typeof(byte));
            if (isFloat)
            {
                // Unsafe.WriteUnaligned(ref _buffer[byteIdx], Convert.ToSingle(value));
                setIl.Emit(OpCodes.Ldarg_2);
                setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToSingle", [typeof(object)])!);
                setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(float)));
            }
            else
            {
                setIl.Emit(OpCodes.Ldarg_2);
                setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                if (signed)
                {
                    setIl.Emit(OpCodes.Conv_I4);
                    setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(int)));
                }
                else
                {
                    setIl.Emit(OpCodes.Conv_U4);
                    setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(uint)));
                }
            }
        }
        else if (bytesPerElement == 8)
        {
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldfld, arrays.BufferField);
            setIl.Emit(OpCodes.Ldloc, setIndexLocal);
            setIl.Emit(OpCodes.Ldelema, typeof(byte));
            if (isFloat)
            {
                // Unsafe.WriteUnaligned(ref _buffer[byteIdx], Convert.ToDouble(value));
                setIl.Emit(OpCodes.Ldarg_2);
                setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(double)));
            }
            else if (isBigInt)
            {
                // For BigInt, convert from BigInteger to long/ulong (preserves prior ToInt64 form).
                setIl.Emit(OpCodes.Ldarg_2);
                setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt64", [typeof(object)])!);
                if (signed)
                    setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(long)));
                else
                    setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(ulong)));
            }
            else
            {
                setIl.Emit(OpCodes.Ldarg_2);
                setIl.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                setIl.Emit(OpCodes.Conv_I8);
                setIl.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(long)));
            }
        }

        setIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(setter, arrays.ElementSet);
    }

    // double GetUnboxed(int index) / void SetUnboxed(int index, double value) on each concrete
    // numeric $XArray (#3, generalizing the Float64-only #878 path). They mirror the byte logic
    // of the boxed Get/Set above but take/return a native `double` — no Box or Convert.ToDouble.
    // One-byte arrays use direct byte[] element operations (#1431); wider elements reinterpret the
    // backing store via Unsafe.Read/WriteUnaligned. The IL emitter binds these methods at statically
    // typed index sites, eliminating runtime dispatch and per-element boxing. AggressiveInlining +
    // a non-virtual `call` let the JIT fold them into the caller's loop. Direct ldelem/stelem and
    // the wider path's ldelema both preserve the current bounds fault. The double→element narrowing
    // matches the boxed Set's conv opcodes so the fast path and boxed fallback agree.
    private void EmitUnboxedNumericAccessors(
        TypeBuilder typeBuilder, EmittedTypedArrayImplementation arrays, string elementType,
        int bytesPerElement, bool signed, bool isFloat)
    {
        var getU = typeBuilder.DefineMethod(
            "GetUnboxed",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Double,
            [_types.Int32]
        );
        getU.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var gil = getU.GetILGenerator();
        if (bytesPerElement == 1)
        {
            EmitOneByteArrayAndIndex(arrays, gil);
            gil.Emit(signed ? OpCodes.Ldelem_I1 : OpCodes.Ldelem_U1);
            gil.Emit(OpCodes.Conv_R8);
        }
        else
        {
            EmitElementRef(arrays, gil, bytesPerElement);
            EmitReadElementAsDouble(gil, bytesPerElement, signed, isFloat);
        }
        gil.Emit(OpCodes.Ret);
        arrays.RegisterGetUnboxed(elementType, getU);

        var setU = typeBuilder.DefineMethod(
            "SetUnboxed",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Int32, _types.Double]
        );
        setU.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var sil = setU.GetILGenerator();
        if (bytesPerElement == 1)
        {
            EmitOneByteArrayAndIndex(arrays, sil);
            sil.Emit(OpCodes.Ldarg_2);
            sil.Emit(OpCodes.Conv_I4);
            sil.Emit(signed ? OpCodes.Conv_I1 : OpCodes.Conv_U1);
            sil.Emit(OpCodes.Stelem_I1);
        }
        else
        {
            EmitElementRef(arrays, sil, bytesPerElement);   // ref byte destination
            sil.Emit(OpCodes.Ldarg_2);              // double value
            EmitNarrowDoubleAndWrite(sil, bytesPerElement, signed, isFloat);
        }
        sil.Emit(OpCodes.Ret);
        arrays.RegisterSetUnboxed(elementType, setU);
    }

    // Pushes the byte[] and absolute element index for a one-byte typed-array access.
    // Keeping the array reference (rather than taking a managed ref and calling Unsafe) lets
    // RyuJIT optimize the ordinary ldelem/stelem sequence in hot loops.
    private void EmitOneByteArrayAndIndex(EmittedTypedArrayImplementation arrays, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.BufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.ByteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
    }

    // Pushes `ref byte` at _buffer[_byteOffset + index * bytesPerElement] (this=arg0, index=arg1).
    private void EmitElementRef(EmittedTypedArrayImplementation arrays, ILGenerator il, int bytesPerElement)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.BufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrays.ByteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        if (bytesPerElement != 1)
        {
            il.Emit(OpCodes.Ldc_I4, bytesPerElement);
            il.Emit(OpCodes.Mul);
        }
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelema, typeof(byte));
    }

    // Stack in: [ref byte]. Stack out: [double]. Reads the element and widens to double.
    internal static void EmitReadElementAsDouble(ILGenerator il, int bytesPerElement, bool signed, bool isFloat)
    {
        if (bytesPerElement == 1)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(signed ? typeof(sbyte) : typeof(byte)));
            il.Emit(OpCodes.Conv_R8);
        }
        else if (bytesPerElement == 2)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(signed ? typeof(short) : typeof(ushort)));
            il.Emit(OpCodes.Conv_R8);
        }
        else if (bytesPerElement == 4 && isFloat)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(typeof(float)));
            il.Emit(OpCodes.Conv_R8);
        }
        else if (bytesPerElement == 4 && signed)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(typeof(int)));
            il.Emit(OpCodes.Conv_R8);
        }
        else if (bytesPerElement == 4)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(typeof(uint)));
            il.Emit(OpCodes.Conv_U8);  // zero-extend uint32 → int64 so the double is 0..4294967295
            il.Emit(OpCodes.Conv_R8);
        }
        else // bytesPerElement == 8 && isFloat (Float64)
        {
            il.Emit(OpCodes.Call, UnsafeReadUnaligned(typeof(double)));
        }
    }

    // Stack in: [ref byte]. Stack out: [int64]. The exact Int32 stencil lowering keeps
    // its three loads and arithmetic integral, then widens the final (at most 34-bit)
    // result once. This is bit-identical to JavaScript Number arithmetic for Int32 inputs
    // while replacing three per-element conversions with one conversion of the final term.
    internal static void EmitReadInt32AsInt64(ILGenerator il)
    {
        il.Emit(OpCodes.Call, UnsafeReadUnaligned(typeof(int)));
        il.Emit(OpCodes.Conv_I8);
    }

    // Stack in: [ref byte, double value]. Narrows the double to the element type and stores it.
    // Conv opcodes mirror the boxed Set so the fast path and boxed fallback agree exactly.
    internal static void EmitNarrowDoubleAndWrite(ILGenerator il, int bytesPerElement, bool signed, bool isFloat)
    {
        if (bytesPerElement == 1)
        {
            il.Emit(OpCodes.Conv_I4);
            if (signed) { il.Emit(OpCodes.Conv_I1); il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(sbyte))); }
            else { il.Emit(OpCodes.Conv_U1); il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(byte))); }
        }
        else if (bytesPerElement == 2)
        {
            il.Emit(OpCodes.Conv_I4);
            if (signed) { il.Emit(OpCodes.Conv_I2); il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(short))); }
            else { il.Emit(OpCodes.Conv_U2); il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(ushort))); }
        }
        else if (bytesPerElement == 4 && isFloat)
        {
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(float)));
        }
        else if (bytesPerElement == 4 && signed)
        {
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(int)));
        }
        else if (bytesPerElement == 4)
        {
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(uint)));
        }
        else // bytesPerElement == 8 && isFloat (Float64)
        {
            il.Emit(OpCodes.Call, UnsafeWriteUnaligned(typeof(double)));
        }
    }

    // Reflects the `ref byte` overloads of Unsafe.Read/WriteUnaligned (not the `void*` ones)
    // and instantiates them for the element type. Unsafe lives in System.Private.CoreLib (BCL),
    // so the emitted token references the BCL, never SharpTS.dll — standalone DLLs stay standalone.
    private static MethodInfo UnsafeWriteUnaligned(Type elementType)
    {
        var methods = typeof(System.Runtime.CompilerServices.Unsafe).GetMethods();
        var open = Array.Find(methods, m => m.Name == "WriteUnaligned"
            && m.GetParameters()[0].ParameterType == typeof(byte).MakeByRefType())!;
        return EmitGenerics.MakeGenericMethod(open, elementType);
    }

    private static MethodInfo UnsafeReadUnaligned(Type elementType)
    {
        var methods = typeof(System.Runtime.CompilerServices.Unsafe).GetMethods();
        var open = Array.Find(methods, m => m.Name == "ReadUnaligned"
            && m.GetParameters()[0].ParameterType == typeof(byte).MakeByRefType())!;
        return EmitGenerics.MakeGenericMethod(open, elementType);
    }

    internal static MethodInfo GetByteArrayDataReference()
    {
        var methods = typeof(System.Runtime.InteropServices.MemoryMarshal).GetMethods();
        var open = Array.Find(methods, m => m.Name == "GetArrayDataReference"
            && m.IsGenericMethodDefinition
            && m.GetParameters() is [{ ParameterType.IsArray: true }])!;
        return EmitGenerics.MakeGenericMethod(open, typeof(byte));
    }

    internal static MethodInfo UnsafeAddByteOffset()
    {
        var methods = typeof(System.Runtime.CompilerServices.Unsafe).GetMethods();
        var open = Array.Find(methods, m => m.Name == "Add"
            && m.IsGenericMethodDefinition
            && m.GetParameters() is
            [
                { ParameterType.IsByRef: true },
                { ParameterType: var offsetType }
            ]
            && offsetType == typeof(int))!;
        return EmitGenerics.MakeGenericMethod(open, typeof(byte));
    }
}
