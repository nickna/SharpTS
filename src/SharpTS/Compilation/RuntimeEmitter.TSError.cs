using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Error class hierarchy for standalone error support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSError and subclasses.
/// </summary>
public partial class RuntimeEmitter
{
    // Base error class fields

    // AggregateError errors field

    private void EmitTSErrorClasses(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        // Emit base $Error class first
        EmitTSErrorBaseClass(moduleBuilder, errors);

        // Emit error subclasses
        EmitTSTypeErrorClass(moduleBuilder, errors);
        EmitTSRangeErrorClass(moduleBuilder, errors);
        EmitTSReferenceErrorClass(moduleBuilder, errors);
        EmitTSSyntaxErrorClass(moduleBuilder, errors);
        EmitTSURIErrorClass(moduleBuilder, errors);
        EmitTSEvalErrorClass(moduleBuilder, errors);

        // Emit $AggregateError (extends $Error, has Errors property)
        EmitTSAggregateErrorClass(moduleBuilder, errors);
    }

    private void EmitTSErrorBaseClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        // Define class: public class $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Error",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        errors.Type = typeBuilder;

        // Fields
        var errorNameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);
        var errorMessageField = typeBuilder.DefineField("_message", _types.String, FieldAttributes.Private);
        var errorStackField = typeBuilder.DefineField("_stack", _types.String, FieldAttributes.Private);
        var errorCapturedStackField = typeBuilder.DefineField(
            "_capturedStack", _types.String, FieldAttributes.Private);
        var errorCauseField = typeBuilder.DefineField("_cause", _types.Object, FieldAttributes.Private);
        var errorHasCauseField = typeBuilder.DefineField("_hasCause", _types.Boolean, FieldAttributes.Private);
        var errorCodeField = typeBuilder.DefineField("_code", _types.String, FieldAttributes.Private);
        var errorSyscallField = typeBuilder.DefineField("_syscall", _types.String, FieldAttributes.Private);

        // Protected constructor: protected $Error(string name, string? message)
        // Must be emitted before message constructor since it calls this one
        EmitTSErrorCtorNameMessage(typeBuilder, errors, errorCapturedStackField, errorMessageField, errorNameField);

        // Constructor: public $Error(string? message) : this("Error", message)
        EmitTSErrorCtorMessage(typeBuilder, errors);

        // Properties: Name, Message, Stack, Cause (get/set)
        EmitTSErrorNameProperty(typeBuilder, errors, errorNameField);
        EmitTSErrorMessageProperty(typeBuilder, errors, errorMessageField);
        EmitTSErrorStackProperty(typeBuilder, errors, errorCapturedStackField, errorStackField);
        EmitTSErrorCapturedStackSetter(typeBuilder, errors, errorCapturedStackField, errorStackField);
        EmitTSErrorCauseProperty(typeBuilder, errors, errorCauseField, errorHasCauseField);
        EmitTSErrorCodeProperty(typeBuilder, errors, errorCodeField);
        EmitTSErrorSyscallProperty(typeBuilder, errors, errorSyscallField);

        // ToString override
        EmitTSErrorToStringMethod(typeBuilder, errorMessageField, errorNameField);

        typeBuilder.CreateType();
    }

    private void EmitTSErrorCtorMessage(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        // public $Error(string? message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        errors.MessageConstructor = ctor;

        var il = ctor.GetILGenerator();

        // Call this("Error", message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, errors.NameMessageConstructor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCtorNameMessage(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        FieldBuilder errorCapturedStackField,
        FieldBuilder errorMessageField,
        FieldBuilder errorNameField
    )
    {
        // protected $Error(string name, string? message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Family, // protected
            CallingConventions.Standard,
            [_types.String, _types.String]
        );
        errors.NameMessageConstructor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // _name = name
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, errorNameField);

        // _message = message ?? ""
        var hasMessage = il.DefineLabel();
        var afterMessage = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasMessage);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(hasMessage);
        il.Emit(OpCodes.Stfld, errorMessageField);

        // Runtime-created errors get a stable marker. Direct guest construction
        // replaces it with the emitting method's interned creation-site token.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "<runtime>");
        il.Emit(OpCodes.Stfld, errorCapturedStackField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorNameProperty(TypeBuilder typeBuilder, EmittedErrorRuntime errors, FieldBuilder errorNameField)
    {
        // public string Name { get; set; }
        var prop = typeBuilder.DefineProperty(
            "Name",
            PropertyAttributes.None,
            _types.String,
            null
        );

        // Getter
        var getter = typeBuilder.DefineMethod(
            "get_Name",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        errors.NameGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorNameField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Name",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        errors.NameSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorNameField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorMessageProperty(TypeBuilder typeBuilder, EmittedErrorRuntime errors, FieldBuilder errorMessageField)
    {
        // public string Message { get; set; }
        var prop = typeBuilder.DefineProperty(
            "Message",
            PropertyAttributes.None,
            _types.String,
            null
        );

        // Getter
        var getter = typeBuilder.DefineMethod(
            "get_Message",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        errors.MessageGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorMessageField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Message",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        errors.MessageSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorMessageField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorStackProperty(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        FieldBuilder errorCapturedStackField,
        FieldBuilder errorStackField
    )
    {
        // public string Stack { get; set; }
        var prop = typeBuilder.DefineProperty(
            "Stack",
            PropertyAttributes.None,
            _types.String,
            null
        );

        // Getter
        var getter = typeBuilder.DefineMethod(
            "get_Stack",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        errors.StackGetter = getter;
        var getIL = getter.GetILGenerator();
        var formatCapture = getIL.DefineLabel();

        // Return an explicitly assigned or already-formatted value.
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorStackField);
        getIL.Emit(OpCodes.Dup);
        getIL.Emit(OpCodes.Brtrue, formatCapture);
        getIL.Emit(OpCodes.Pop);

        // No capture can only occur after an explicit null assignment; expose
        // the same string-shaped fallback as an empty captured trace.
        var captured = getIL.DeclareLocal(_types.String);
        var hasCapture = getIL.DefineLabel();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorCapturedStackField);
        getIL.Emit(OpCodes.Stloc, captured);
        getIL.Emit(OpCodes.Ldloc, captured);
        getIL.Emit(OpCodes.Brtrue, hasCapture);
        getIL.Emit(OpCodes.Ldstr, "");
        getIL.Emit(OpCodes.Ret);

        // Format once, cache the string, and release the captured frames.
        getIL.MarkLabel(hasCapture);
        var formatted = getIL.DeclareLocal(_types.String);
        getIL.Emit(OpCodes.Ldstr, "    at ");
        getIL.Emit(OpCodes.Ldloc, captured);
        getIL.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "Concat", [_types.String, _types.String])!);
        getIL.Emit(OpCodes.Stloc, formatted);
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldloc, formatted);
        getIL.Emit(OpCodes.Stfld, errorStackField);
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldnull);
        getIL.Emit(OpCodes.Stfld, errorCapturedStackField);
        getIL.Emit(OpCodes.Ldloc, formatted);
        getIL.Emit(OpCodes.Ret);

        getIL.MarkLabel(formatCapture);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Stack",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        errors.StackSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorStackField);
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldnull);
        setIL.Emit(OpCodes.Stfld, errorCapturedStackField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorCapturedStackSetter(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        FieldBuilder errorCapturedStackField,
        FieldBuilder errorStackField
    )
    {
        var method = typeBuilder.DefineMethod(
            "SetCapturedStackFrame",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Void,
            [_types.String]);
        errors.CapturedStackSetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, errorStackField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, errorCapturedStackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCauseProperty(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        FieldBuilder errorCauseField,
        FieldBuilder errorHasCauseField
    )
    {
        // public object? Cause { get; set; }
        var prop = typeBuilder.DefineProperty(
            "Cause",
            PropertyAttributes.None,
            _types.Object,
            null
        );

        // Getter
        var getter = typeBuilder.DefineMethod(
            "get_Cause",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Object,
            Type.EmptyTypes
        );
        errors.CauseGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorCauseField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Cause",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.Object]
        );
        errors.CauseSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorCauseField);
        // Also set _hasCause = true
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldc_I4_1);
        setIL.Emit(OpCodes.Stfld, errorHasCauseField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);

        // HasCause getter (for runtime checks)
        var hasCauseGetter = typeBuilder.DefineMethod(
            "get_HasCause",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Boolean,
            Type.EmptyTypes
        );
        errors.HasCauseGetter = hasCauseGetter;
        var hcIL = hasCauseGetter.GetILGenerator();
        hcIL.Emit(OpCodes.Ldarg_0);
        hcIL.Emit(OpCodes.Ldfld, errorHasCauseField);
        hcIL.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCodeProperty(TypeBuilder typeBuilder, EmittedErrorRuntime errors, FieldBuilder errorCodeField)
    {
        // public string? Code { get; set; }
        var prop = typeBuilder.DefineProperty("Code", PropertyAttributes.None, _types.String, null);

        var getter = typeBuilder.DefineMethod(
            "get_Code",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        errors.CodeGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorCodeField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_Code",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        errors.CodeSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorCodeField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorSyscallProperty(TypeBuilder typeBuilder, EmittedErrorRuntime errors, FieldBuilder errorSyscallField)
    {
        // public string? Syscall { get; set; }
        var prop = typeBuilder.DefineProperty("Syscall", PropertyAttributes.None, _types.String, null);

        var getter = typeBuilder.DefineMethod(
            "get_Syscall",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        errors.SyscallGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, errorSyscallField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_Syscall",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        errors.SyscallSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, errorSyscallField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorToStringMethod(TypeBuilder typeBuilder, FieldBuilder errorMessageField, FieldBuilder errorNameField)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        var hasMessageLabel = il.DefineLabel();

        // if (string.IsNullOrEmpty(_message))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, errorMessageField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty")!);
        il.Emit(OpCodes.Brfalse, hasMessageLabel);

        // return _name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, errorNameField);
        il.Emit(OpCodes.Ret);

        // return _name + ": " + _message;
        il.MarkLabel(hasMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, errorNameField);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, errorMessageField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSimpleErrorSubclass(
        ModuleBuilder moduleBuilder,
        EmittedErrorRuntime errors,
        string className,
        string errorName,
        Action<TypeBuilder,
        ConstructorBuilder> setOnRuntime
    )
    {
        // Define class that extends $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            className,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            errors.Type
        );

        // Constructor: public $XxxError(string? message) : base("XxxError", message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, errorName);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, errors.NameMessageConstructor);
        il.Emit(OpCodes.Ret);

        setOnRuntime(typeBuilder, ctor);
        typeBuilder.CreateType();
    }

    private void EmitTSTypeErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$TypeError",
            "TypeError",
            (type, ctor) =>
        {
            errors.TypeErrorType = type;
            errors.TypeErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSRangeErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$RangeError",
            "RangeError",
            (type, ctor) =>
        {
            errors.RangeErrorType = type;
            errors.RangeErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSReferenceErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$ReferenceError",
            "ReferenceError",
            (type, ctor) =>
        {
            errors.ReferenceErrorType = type;
            errors.ReferenceErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSSyntaxErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$SyntaxError",
            "SyntaxError",
            (type, ctor) =>
        {
            errors.SyntaxErrorType = type;
            errors.SyntaxErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSURIErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$URIError",
            "URIError",
            (type, ctor) =>
        {
            errors.URIErrorType = type;
            errors.URIErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSEvalErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        EmitSimpleErrorSubclass(
            moduleBuilder,
            errors,
            "$EvalError",
            "EvalError",
            (type, ctor) =>
        {
            errors.EvalErrorType = type;
            errors.EvalErrorConstructor = ctor;
        }
        );
    }

    private void EmitTSAggregateErrorClass(ModuleBuilder moduleBuilder, EmittedErrorRuntime errors)
    {
        // Define class that extends $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$AggregateError",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            errors.Type
        );
        errors.AggregateErrorType = typeBuilder;

        // Field: private readonly List<object?> _errors
        var aggregateErrorErrorsField = typeBuilder.DefineField(
            "_errors",
            _types.ListOfObject,
            FieldAttributes.Private
        );

        // Constructor: public $AggregateError(object? errors, string? message)
        // Note: JavaScript AggregateError takes (errors, message) - errors first!
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.String]
        );
        errors.AggregateErrorConstructor = ctor;

        var il = ctor.GetILGenerator();

        // Call base("AggregateError", message ?? "")
        // Note: arg1 = errors, arg2 = message
        var hasMessageLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "AggregateError");
        il.Emit(OpCodes.Ldarg_2);  // message
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasMessageLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(hasMessageLabel);
        il.Emit(OpCodes.Call, errors.NameMessageConstructor);

        // _errors = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stfld, aggregateErrorErrorsField);

        // If errors (arg1) is List<object?>, copy elements
        var notListLabel = il.DefineLabel();
        var endCtorLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        // errors is List<object?> - add all elements
        var errorsListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, errorsListLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, aggregateErrorErrorsField);
        il.Emit(OpCodes.Ldloc, errorsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", [typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Br, endCtorLabel);

        il.MarkLabel(notListLabel);
        // If errors is not null and not list, add as single element
        var errorsNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Brfalse, errorsNullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, aggregateErrorErrorsField);
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.MarkLabel(errorsNullLabel);
        il.MarkLabel(endCtorLabel);
        il.Emit(OpCodes.Ret);

        // Property: public List<object?> Errors { get; }
        var prop = typeBuilder.DefineProperty(
            "Errors",
            PropertyAttributes.None,
            _types.ListOfObject,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Errors",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.ListOfObject,
            Type.EmptyTypes
        );
        errors.AggregateErrorErrorsGetter = getter;

        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, aggregateErrorErrorsField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        typeBuilder.CreateType();
    }
}
