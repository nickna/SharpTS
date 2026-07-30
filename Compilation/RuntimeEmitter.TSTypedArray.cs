using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // TypedArray type definitions
    private TypeBuilder? _typedArrayBaseType;
    private FieldBuilder? _typedArrayBufferField;
    private FieldBuilder? _typedArrayByteOffsetField;
    private FieldBuilder? _typedArrayLengthField;
    private FieldBuilder? _typedArrayArrayBufferField;
    private MethodBuilder? _typedArrayBytesPerElementGetter;
    // Abstract per-concrete factories used by the base Slice/Subarray (#940): create a fresh
    // same-kind array (slice copies) / a view sharing the backing buffer (subarray aliases).
    private MethodBuilder? _typedArrayCreateOfLength;
    private MethodBuilder? _typedArrayCreateView;

    /// <summary>
    /// Emits all TypedArray types for standalone DLLs.
    /// </summary>
    private void EmitTypedArrayTypes(ModuleBuilder module, EmittedRuntime runtime)
    {
        // First emit the base class
        EmitTypedArrayBaseType(module, runtime);

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
        _typedArrayBaseType!.CreateType();
    }

    /// <summary>
    /// Emits the abstract $TypedArray base class.
    /// </summary>
    private void EmitTypedArrayBaseType(ModuleBuilder module, EmittedRuntime runtime)
    {
        _typedArrayBaseType = EmitTypeDefinitions.DefineType(module,
            "$TypedArray",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            _types.Object
        );
        runtime.TypedArrayBaseType = _typedArrayBaseType;

        // Fields
        _typedArrayBufferField = _typedArrayBaseType.DefineField("_buffer", typeof(byte[]), FieldAttributes.Family);
        _typedArrayByteOffsetField = _typedArrayBaseType.DefineField("_byteOffset", _types.Int32, FieldAttributes.Family);
        _typedArrayLengthField = _typedArrayBaseType.DefineField("_length", _types.Int32, FieldAttributes.Family);
        _typedArrayArrayBufferField = _typedArrayBaseType.DefineField("_arrayBuffer", _types.Object, FieldAttributes.Family);

        // Abstract properties
        _typedArrayBytesPerElementGetter = EmitTypedArrayAbstractProperty(_typedArrayBaseType, "BytesPerElement", _types.Int32);
        EmitTypedArrayAbstractProperty(_typedArrayBaseType, "TypeName", _types.String);
        runtime.TypedArrayElementGet = _typedArrayBaseType.DefineMethod(
            "Get",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Int32]
        );
        runtime.TypedArrayElementSet = _typedArrayBaseType.DefineMethod(
            "Set",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        // Concrete properties: Length, ByteOffset, ByteLength, Buffer
        EmitTypedArrayLengthProperty(_typedArrayBaseType, runtime);
        EmitTypedArrayByteOffsetProperty(_typedArrayBaseType, runtime);
        EmitTypedArrayByteLengthProperty(_typedArrayBaseType, runtime);
        EmitTypedArrayBufferProperty(_typedArrayBaseType, runtime);

        // Protected constructor
        var baseCtor = _typedArrayBaseType.DefineConstructor(
            MethodAttributes.Family,
            CallingConventions.Standard,
            [typeof(byte[]), _types.Int32, _types.Int32, _types.Object]
        );
        runtime.TypedArrayBaseCtor = baseCtor;

        var il = baseCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _typedArrayBufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _typedArrayByteOffsetField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _typedArrayLengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Stfld, _typedArrayArrayBufferField);
        il.Emit(OpCodes.Ret);

        // GetBuffer method for internal access
        var getBufferMethod = _typedArrayBaseType.DefineMethod(
            "GetBuffer",
            MethodAttributes.Public,
            typeof(byte[]),
            Type.EmptyTypes
        );
        runtime.TypedArrayGetBuffer = getBufferMethod;
        var getBufferIl = getBufferMethod.GetILGenerator();
        getBufferIl.Emit(OpCodes.Ldarg_0);
        getBufferIl.Emit(OpCodes.Ldfld, _typedArrayBufferField);
        getBufferIl.Emit(OpCodes.Ret);

        // Abstract factories overridden by each concrete type — let the base-class Slice
        // (fresh same-kind copy) and Subarray (buffer-sharing view) build the right concrete
        // type without the base needing to know the concrete constructors (#940).
        _typedArrayCreateOfLength = _typedArrayBaseType.DefineMethod(
            "CreateOfLength",
            MethodAttributes.Family | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _typedArrayBaseType,
            [_types.Int32]
        );
        _typedArrayCreateView = _typedArrayBaseType.DefineMethod(
            "CreateView",
            MethodAttributes.Family | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _typedArrayBaseType,
            [_types.Int32, _types.Int32]
        );

        // Bulk instance methods (fill/copyWithin/reverse/set/slice/subarray/indexOf/…) mirroring
        // the interpreter's GetMember surface. Emitted here, before CreateType, so they live on
        // the base type. BCL-only — standalone-safe.
        EmitTypedArrayBulkMethods(_typedArrayBaseType, runtime);
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

    private void EmitTypedArrayLengthProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("Length", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TypedArrayLengthGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayByteOffsetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("ByteOffset", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_ByteOffset",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TypedArrayByteOffsetGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayByteLengthProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("ByteLength", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_ByteLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TypedArrayByteLengthGetter = getter;
        var il = getter.GetILGenerator();
        // return _length * BytesPerElement
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTypedArrayBufferProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("Buffer", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Buffer",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TypedArrayBufferGetter = getter;
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayArrayBufferField!);
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
        var typeBuilder = module.DefineType(
            $"${name}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _typedArrayBaseType
        );

        // Store type reference in runtime
        StoreTypedArrayType(runtime, name, typeBuilder);

        // Override BytesPerElement
        EmitBytesPerElementProperty(typeBuilder, bytesPerElement);

        // Override TypeName
        EmitTypeNameProperty(typeBuilder, name);

        // Constructor: public $Uint8Array(int length)
        var lengthCtor = EmitTypedArrayLengthConstructor(typeBuilder, runtime, bytesPerElement);
        StoreTypedArrayLengthCtor(runtime, name, lengthCtor);

        // Constructor: public $Uint8Array(object buffer, int byteOffset, int? length)
        var bufferCtor = EmitTypedArrayBufferConstructor(typeBuilder, runtime, bytesPerElement);
        StoreTypedArrayBufferCtor(runtime, name, bufferCtor);

        // Indexer: public object this[int index] { get; set; }
        EmitTypedArrayIndexer(typeBuilder, runtime, bytesPerElement, signed, clamped, isFloat, isBigInt);

        // Unboxed numeric element accessors (#3): GetUnboxed/SetUnboxed return/accept a native
        // double, for the compiled fast path (ILEmitter binds them at statically-typed sites).
        // Skip BigInt (bigint, not number) and Uint8Clamped (keeps the boxed clamp/round path).
        if (!isBigInt && !clamped)
        {
            var elementType = name.EndsWith("Array") ? name[..^5] : name;
            EmitUnboxedNumericAccessors(typeBuilder, runtime, elementType, bytesPerElement, signed, isFloat);
        }

        // Buffer-sharing ctor + CreateOfLength/CreateView overrides backing the base
        // Slice/Subarray (#940).
        EmitTypedArrayFactoryMembers(typeBuilder, runtime, lengthCtor);

        // Finalize type
        typeBuilder.CreateType();
    }

    private void StoreTypedArrayType(EmittedRuntime runtime, string name, TypeBuilder type)
    {
        switch (name)
        {
            case "Int8Array": runtime.Int8ArrayType = type; break;
            case "Uint8Array": runtime.Uint8ArrayType = type; break;
            case "Uint8ClampedArray": runtime.Uint8ClampedArrayType = type; break;
            case "Int16Array": runtime.Int16ArrayType = type; break;
            case "Uint16Array": runtime.Uint16ArrayType = type; break;
            case "Int32Array": runtime.Int32ArrayType = type; break;
            case "Uint32Array": runtime.Uint32ArrayType = type; break;
            case "Float32Array": runtime.Float32ArrayType = type; break;
            case "Float64Array": runtime.Float64ArrayType = type; break;
            case "BigInt64Array": runtime.BigInt64ArrayType = type; break;
            case "BigUint64Array": runtime.BigUint64ArrayType = type; break;
        }
    }

    private void StoreTypedArrayBufferCtor(EmittedRuntime runtime, string name, ConstructorBuilder ctor)
    {
        switch (name)
        {
            case "Int8Array": runtime.Int8ArrayBufferCtor = ctor; break;
            case "Uint8Array": runtime.Uint8ArrayBufferCtor = ctor; break;
            case "Uint8ClampedArray": runtime.Uint8ClampedArrayBufferCtor = ctor; break;
            case "Int16Array": runtime.Int16ArrayBufferCtor = ctor; break;
            case "Uint16Array": runtime.Uint16ArrayBufferCtor = ctor; break;
            case "Int32Array": runtime.Int32ArrayBufferCtor = ctor; break;
            case "Uint32Array": runtime.Uint32ArrayBufferCtor = ctor; break;
            case "Float32Array": runtime.Float32ArrayBufferCtor = ctor; break;
            case "Float64Array": runtime.Float64ArrayBufferCtor = ctor; break;
            case "BigInt64Array": runtime.BigInt64ArrayBufferCtor = ctor; break;
            case "BigUint64Array": runtime.BigUint64ArrayBufferCtor = ctor; break;
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

    private ConstructorBuilder EmitTypedArrayLengthConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime, int bytesPerElement)
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
        il.Emit(OpCodes.Call, runtime.TypedArrayBaseCtor);
        il.Emit(OpCodes.Ret);

        return ctor;
    }

    private void StoreTypedArrayLengthCtor(EmittedRuntime runtime, string name, ConstructorBuilder ctor)
    {
        switch (name)
        {
            case "Int8Array": runtime.Int8ArrayLengthCtor = ctor; break;
            case "Uint8Array": runtime.Uint8ArrayLengthCtor = ctor; break;
            case "Uint8ClampedArray": runtime.Uint8ClampedArrayLengthCtor = ctor; break;
            case "Int16Array": runtime.Int16ArrayLengthCtor = ctor; break;
            case "Uint16Array": runtime.Uint16ArrayLengthCtor = ctor; break;
            case "Int32Array": runtime.Int32ArrayLengthCtor = ctor; break;
            case "Uint32Array": runtime.Uint32ArrayLengthCtor = ctor; break;
            case "Float32Array": runtime.Float32ArrayLengthCtor = ctor; break;
            case "Float64Array": runtime.Float64ArrayLengthCtor = ctor; break;
            case "BigInt64Array": runtime.BigInt64ArrayLengthCtor = ctor; break;
            case "BigUint64Array": runtime.BigUint64ArrayLengthCtor = ctor; break;
        }
    }

    private ConstructorBuilder EmitTypedArrayBufferConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime, int bytesPerElement)
    {
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
        var afterBufferLabel = il.DefineLabel();

        // Check if buffer is $ArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        il.Emit(OpCodes.Brfalse, isSharedArrayBufferLabel);

        // It's $ArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferGetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Stloc, bufByteLengthLocal);
        il.Emit(OpCodes.Br, afterBufferLabel);

        il.MarkLabel(isSharedArrayBufferLabel);
        // Check if buffer is $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brfalse, afterBufferLabel);

        // It's $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferGetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Stloc, bufByteLengthLocal);
        il.Emit(OpCodes.Br, afterBufferLabel);

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
        il.Emit(OpCodes.Call, runtime.TypedArrayBaseCtor);
        il.Emit(OpCodes.Ret);

        return ctor;
    }

    private void EmitTypedArrayIndexer(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
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
        getIl.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        getIl.Emit(OpCodes.Ldarg_1);
        getIl.Emit(OpCodes.Ldc_I4, bytesPerElement);
        getIl.Emit(OpCodes.Mul);
        getIl.Emit(OpCodes.Add);
        getIl.Emit(OpCodes.Stloc, indexLocal);

        // Read value based on type
        if (bytesPerElement == 1)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToSingle", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Conv_R8);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 4)
        {
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
            getIl.Emit(OpCodes.Ldloc, indexLocal);
            getIl.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToDouble", [typeof(byte[]), typeof(int)])!);
            getIl.Emit(OpCodes.Box, _types.Double);
        }
        else if (bytesPerElement == 8 && isBigInt)
        {
            // For BigInt, return as BigInteger
            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
        typeBuilder.DefineMethodOverride(getter, runtime.TypedArrayElementGet);

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
        setIl.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Ldc_I4, bytesPerElement);
        setIl.Emit(OpCodes.Mul);
        setIl.Emit(OpCodes.Add);
        setIl.Emit(OpCodes.Stloc, setIndexLocal);

        // Write value based on type
        if (bytesPerElement == 1)
        {
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            setIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            setIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
            setIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
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
        typeBuilder.DefineMethodOverride(setter, runtime.TypedArrayElementSet);
    }

    // double GetUnboxed(int index) / void SetUnboxed(int index, double value) on each concrete
    // numeric $XArray (#3, generalizing the Float64-only #878 path). They mirror the byte logic
    // of the boxed Get/Set above but take/return a native `double` — no Box, no Convert.ToDouble
    // coercion — reinterpreting over the byte[] backing store via Unsafe.Read/WriteUnaligned (no
    // per-element allocation). The IL emitter binds them at statically-typed typed-array index
    // sites, eliminating the GetIndex/SetIndex dispatch, the isinst ladder, and the per-element
    // box on BOTH read and write. AggressiveInlining + a non-virtual `call` let the JIT fold them
    // into the caller's loop and hoist the _buffer load / bounds check. The single ldelema
    // bounds-checks the first byte; a correctly-sized buffer (length a multiple of bytesPerElement,
    // byteOffset aligned) guarantees the rest are in range, so OOB faults exactly as the boxed
    // path does today (semantics unchanged). The double→element narrowing matches the boxed Set's
    // conv opcodes so the fast path and the boxed fallback always agree.
    private void EmitUnboxedNumericAccessors(
        TypeBuilder typeBuilder, EmittedRuntime runtime, string elementType,
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
        EmitElementRef(gil, bytesPerElement);
        EmitReadElementAsDouble(gil, bytesPerElement, signed, isFloat);
        gil.Emit(OpCodes.Ret);
        runtime.TypedArrayGetUnboxedByElement[elementType] = getU;

        var setU = typeBuilder.DefineMethod(
            "SetUnboxed",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Int32, _types.Double]
        );
        setU.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var sil = setU.GetILGenerator();
        EmitElementRef(sil, bytesPerElement);   // ref byte destination
        sil.Emit(OpCodes.Ldarg_2);              // double value
        EmitNarrowDoubleAndWrite(sil, bytesPerElement, signed, isFloat);
        sil.Emit(OpCodes.Ret);
        runtime.TypedArraySetUnboxedByElement[elementType] = setU;

        // Keep the Float64-specific handles populated for any direct references.
        if (elementType == "Float64")
        {
            _ = getU;
            _ = setU;
        }
    }

    // Pushes `ref byte` at _buffer[_byteOffset + index * bytesPerElement] (this=arg0, index=arg1).
    private void EmitElementRef(ILGenerator il, int bytesPerElement)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
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
    private static void EmitReadElementAsDouble(ILGenerator il, int bytesPerElement, bool signed, bool isFloat)
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

    // Stack in: [ref byte, double value]. Narrows the double to the element type and stores it.
    // Conv opcodes mirror the boxed Set so the fast path and boxed fallback agree exactly.
    private static void EmitNarrowDoubleAndWrite(ILGenerator il, int bytesPerElement, bool signed, bool isFloat)
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
}
