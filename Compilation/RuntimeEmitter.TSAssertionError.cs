using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $AssertionError class for standalone assert module support.
/// NOTE: Must stay in sync with AssertionError in AssertModuleInterpreter.cs
/// </summary>
public partial class RuntimeEmitter
{
    // $AssertionError fields
    private FieldBuilder _assertionErrorActualField = null!;
    private FieldBuilder _assertionErrorExpectedField = null!;
    private FieldBuilder _assertionErrorOperatorField = null!;

    private void EmitTSAssertionErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $AssertionError : Exception
        var typeBuilder = moduleBuilder.DefineType(
            "$AssertionError",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            _types.Exception
        );
        runtime.TSAssertionErrorType = typeBuilder;

        // Fields
        _assertionErrorActualField = typeBuilder.DefineField("_actual", _types.Object, FieldAttributes.Private);
        _assertionErrorExpectedField = typeBuilder.DefineField("_expected", _types.Object, FieldAttributes.Private);
        _assertionErrorOperatorField = typeBuilder.DefineField("_operator", _types.String, FieldAttributes.Private);

        // Constructor
        EmitAssertionErrorCtor(typeBuilder, runtime);

        // Property getters
        EmitAssertionErrorActualGetter(typeBuilder, runtime);
        EmitAssertionErrorExpectedGetter(typeBuilder, runtime);
        EmitAssertionErrorOperatorGetter(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitAssertionErrorCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $AssertionError(string message, object? actual, object? expected, string @operator)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.Object, _types.Object, _types.String]
        );
        runtime.TSAssertionErrorCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base(string message): Exception("AssertionError: " + message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "AssertionError: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Call, _types.Exception.GetConstructor([_types.String])!);

        // _actual = actual
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _assertionErrorActualField);

        // _expected = expected
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _assertionErrorExpectedField);

        // _operator = @operator
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, _assertionErrorOperatorField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitAssertionErrorActualGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Actual",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TSAssertionErrorActualGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _assertionErrorActualField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAssertionErrorExpectedGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Expected",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TSAssertionErrorExpectedGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _assertionErrorExpectedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAssertionErrorOperatorGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Operator",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSAssertionErrorOperatorGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _assertionErrorOperatorField);
        il.Emit(OpCodes.Ret);
    }
}
