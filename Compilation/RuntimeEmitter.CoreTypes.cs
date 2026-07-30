using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $Undefined singleton class.
    /// This is used instead of referencing SharpTS.Runtime.Types.SharpTSUndefined
    /// so that compiled assemblies are standalone.
    /// </summary>
    private void EmitUndefinedClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Undefined
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Undefined",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.UndefinedType = typeBuilder;

        // Static field: public static readonly $Undefined Instance = new $Undefined();
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.UndefinedInstance = instanceField;

        // Private constructor to ensure singleton
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Static constructor to initialize Instance
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIL = cctor.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, ctor);
        cctorIL.Emit(OpCodes.Stsfld, instanceField);
        cctorIL.Emit(OpCodes.Ret);

        // Override ToString() to return "undefined"
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "undefined");
        toStringIL.Emit(OpCodes.Ret);

        // Create the type immediately so other emitters can reference it
        var createdType = typeBuilder.CreateType()!;
        runtime.UndefinedType = createdType;
        runtime.UndefinedInstance = createdType.GetField("Instance")!;
    }

    /// <summary>
    /// Emits the $IUnionType marker interface for fast union type detection.
    /// All generated union types implement this interface.
    /// </summary>
    private void EmitIUnionTypeInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IUnionType
        var typeBuilder = moduleBuilder.DefineType(
            "$IUnionType",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null
        );

        // Define Value property getter: object? Value { get; }
        var valueGetter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );

        var valueProp = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            _types.Object,
            null
        );
        valueProp.SetGetMethod(valueGetter);

        // Create and store the interface type
        runtime.IUnionTypeInterface = typeBuilder.CreateType()!;
        runtime.IUnionTypeValueGetter = _types.GetProperty(runtime.IUnionTypeInterface, "Value")!.GetGetMethod()!;
    }

    /// <summary>
    /// Emits the $IHasFields interface for unified property access.
    /// Implemented by $Object and user-defined classes to provide a standard way
    /// to access fields without reflection.
    /// </summary>
    private void EmitHasFieldsInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IHasFields
        var typeBuilder = moduleBuilder.DefineType(
            "$IHasFields",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null
        );

        // Define Fields property getter: Dictionary<string, object?> Fields { get; }
        var fieldsGetter = typeBuilder.DefineMethod(
            "get_Fields",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.DictionaryStringObject,
            Type.EmptyTypes
        );

        var fieldsProp = typeBuilder.DefineProperty(
            "Fields",
            PropertyAttributes.None,
            _types.DictionaryStringObject,
            null
        );
        fieldsProp.SetGetMethod(fieldsGetter);

        // Define GetProperty method: object? GetProperty(string name)
        typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.String]
        );

        // Define SetProperty method: void SetProperty(string name, object? value)
        typeBuilder.DefineMethod(
            "SetProperty",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.String, _types.Object]
        );

        // Define HasProperty method: bool HasProperty(string name)
        typeBuilder.DefineMethod(
            "HasProperty",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Boolean,
            [_types.String]
        );

        // Create the interface type
        var interfaceType = typeBuilder.CreateType()!;
        runtime.IHasFieldsInterface = interfaceType;

        // Get the actual methods from the created type (not the MethodBuilder refs)
        runtime.IHasFieldsFieldsGetter = interfaceType.GetMethod("get_Fields")!;
        runtime.IHasFieldsGetProperty = interfaceType.GetMethod("GetProperty")!;
        runtime.IHasFieldsSetProperty = interfaceType.GetMethod("SetProperty")!;
        runtime.IHasFieldsHasProperty = interfaceType.GetMethod("HasProperty")!;
    }
}
