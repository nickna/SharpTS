using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the allocation-light carrier used for guest <c>throw</c> values.
    /// The value stays typed and <see cref="Exception.Message"/> is computed only
    /// when a host boundary observes it, so local catches avoid stringification
    /// and the dictionary-backed <see cref="Exception.Data"/> store.
    /// </summary>
    private void EmitThrownValueExceptionType(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$ThrownValueException",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit,
            _types.Exception);
        runtime.ThrownValueExceptionType = typeBuilder;

        var valueField = typeBuilder.DefineField(
            "_value",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]);
        runtime.ThrownValueExceptionCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Exception));
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, valueField);
        ctorIl.Emit(OpCodes.Ret);

        var valueProperty = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            _types.Object,
            null);
        var valueGetter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.SpecialName |
                MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        runtime.ThrownValueExceptionValueGetter = valueGetter;

        var valueIl = valueGetter.GetILGenerator();
        valueIl.Emit(OpCodes.Ldarg_0);
        valueIl.Emit(OpCodes.Ldfld, valueField);
        valueIl.Emit(OpCodes.Ret);
        valueProperty.SetGetMethod(valueGetter);

        var messageProperty = typeBuilder.DefineProperty(
            "Message",
            PropertyAttributes.None,
            _types.String,
            null);
        var messageGetter = typeBuilder.DefineMethod(
            "get_Message",
            MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes);

        var messageIl = messageGetter.GetILGenerator();
        messageIl.Emit(OpCodes.Ldarg_0);
        messageIl.Emit(OpCodes.Ldfld, valueField);
        messageIl.Emit(OpCodes.Call, runtime.Stringify);
        messageIl.Emit(OpCodes.Ret);
        messageProperty.SetGetMethod(messageGetter);
        typeBuilder.DefineMethodOverride(
            messageGetter,
            _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);

        typeBuilder.CreateType();
    }
}
