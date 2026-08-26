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
    private FieldBuilder _tsErrorNameField = null!;
    private FieldBuilder _tsErrorMessageField = null!;
    private FieldBuilder _tsErrorStackField = null!;
    private FieldBuilder _tsErrorCapturedStackField = null!;
    private FieldBuilder _tsErrorCauseField = null!;
    private FieldBuilder _tsErrorHasCauseField = null!;
    private FieldBuilder _tsErrorCodeField = null!;
    private FieldBuilder _tsErrorSyscallField = null!;

    // AggregateError errors field
    private FieldBuilder _tsAggregateErrorErrorsField = null!;

    private void EmitTSErrorClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Emit base $Error class first
        EmitTSErrorBaseClass(moduleBuilder, runtime);

        // Emit error subclasses
        EmitTSTypeErrorClass(moduleBuilder, runtime);
        EmitTSRangeErrorClass(moduleBuilder, runtime);
        EmitTSReferenceErrorClass(moduleBuilder, runtime);
        EmitTSSyntaxErrorClass(moduleBuilder, runtime);
        EmitTSURIErrorClass(moduleBuilder, runtime);
        EmitTSEvalErrorClass(moduleBuilder, runtime);

        // Emit $AggregateError (extends $Error, has Errors property)
        EmitTSAggregateErrorClass(moduleBuilder, runtime);
    }

    private void EmitTSErrorBaseClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Error",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSErrorType = typeBuilder;

        // Fields
        _tsErrorNameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);
        _tsErrorMessageField = typeBuilder.DefineField("_message", _types.String, FieldAttributes.Private);
        _tsErrorStackField = typeBuilder.DefineField("_stack", _types.String, FieldAttributes.Private);
        _tsErrorCapturedStackField = typeBuilder.DefineField(
            "_capturedStack", _types.String, FieldAttributes.Private);
        _tsErrorCauseField = typeBuilder.DefineField("_cause", _types.Object, FieldAttributes.Private);
        _tsErrorHasCauseField = typeBuilder.DefineField("_hasCause", _types.Boolean, FieldAttributes.Private);
        _tsErrorCodeField = typeBuilder.DefineField("_code", _types.String, FieldAttributes.Private);
        _tsErrorSyscallField = typeBuilder.DefineField("_syscall", _types.String, FieldAttributes.Private);

        // Protected constructor: protected $Error(string name, string? message)
        // Must be emitted before message constructor since it calls this one
        EmitTSErrorCtorNameMessage(typeBuilder, runtime);

        // Constructor: public $Error(string? message) : this("Error", message)
        EmitTSErrorCtorMessage(typeBuilder, runtime);

        // Properties: Name, Message, Stack, Cause (get/set)
        EmitTSErrorNameProperty(typeBuilder, runtime);
        EmitTSErrorMessageProperty(typeBuilder, runtime);
        EmitTSErrorStackProperty(typeBuilder, runtime);
        EmitTSErrorCapturedStackSetter(typeBuilder, runtime);
        EmitTSErrorCauseProperty(typeBuilder, runtime);
        EmitTSErrorCodeProperty(typeBuilder, runtime);
        EmitTSErrorSyscallProperty(typeBuilder, runtime);

        // ToString override
        EmitTSErrorToStringMethod(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSErrorCtorMessage(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Error(string? message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSErrorCtorMessage = ctor;

        var il = ctor.GetILGenerator();

        // Call this("Error", message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSErrorCtorNameMessage);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCtorNameMessage(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // protected $Error(string name, string? message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Family, // protected
            CallingConventions.Standard,
            [_types.String, _types.String]
        );
        runtime.TSErrorCtorNameMessage = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // _name = name
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsErrorNameField);

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
        il.Emit(OpCodes.Stfld, _tsErrorMessageField);

        // Runtime-created errors get a stable marker. Direct guest construction
        // replaces it with the emitting method's interned creation-site token.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "<runtime>");
        il.Emit(OpCodes.Stfld, _tsErrorCapturedStackField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorNameProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSErrorNameGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorNameField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Name",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        runtime.TSErrorNameSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorNameField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorMessageProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSErrorMessageGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorMessageField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Message",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        runtime.TSErrorMessageSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorMessageField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorStackProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSErrorStackGetter = getter;
        var getIL = getter.GetILGenerator();
        var formatCapture = getIL.DefineLabel();

        // Return an explicitly assigned or already-formatted value.
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorStackField);
        getIL.Emit(OpCodes.Dup);
        getIL.Emit(OpCodes.Brtrue, formatCapture);
        getIL.Emit(OpCodes.Pop);

        // No capture can only occur after an explicit null assignment; expose
        // the same string-shaped fallback as an empty captured trace.
        var captured = getIL.DeclareLocal(_types.String);
        var hasCapture = getIL.DefineLabel();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorCapturedStackField);
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
        getIL.Emit(OpCodes.Stfld, _tsErrorStackField);
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldnull);
        getIL.Emit(OpCodes.Stfld, _tsErrorCapturedStackField);
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
        runtime.TSErrorStackSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorStackField);
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldnull);
        setIL.Emit(OpCodes.Stfld, _tsErrorCapturedStackField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorCapturedStackSetter(
        TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetCapturedStackFrame",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Void,
            [_types.String]);
        runtime.TSErrorCapturedStackSetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsErrorStackField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsErrorCapturedStackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCauseProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.TSErrorCauseGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorCauseField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            "set_Cause",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.Object]
        );
        runtime.TSErrorCauseSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorCauseField);
        // Also set _hasCause = true
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldc_I4_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorHasCauseField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);

        // HasCause getter (for runtime checks)
        var hasCauseGetter = typeBuilder.DefineMethod(
            "get_HasCause",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSErrorHasCauseGetter = hasCauseGetter;
        var hcIL = hasCauseGetter.GetILGenerator();
        hcIL.Emit(OpCodes.Ldarg_0);
        hcIL.Emit(OpCodes.Ldfld, _tsErrorHasCauseField);
        hcIL.Emit(OpCodes.Ret);
    }

    private void EmitTSErrorCodeProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public string? Code { get; set; }
        var prop = typeBuilder.DefineProperty("Code", PropertyAttributes.None, _types.String, null);

        var getter = typeBuilder.DefineMethod(
            "get_Code",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSErrorCodeGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorCodeField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_Code",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        runtime.TSErrorCodeSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorCodeField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorSyscallProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public string? Syscall { get; set; }
        var prop = typeBuilder.DefineProperty("Syscall", PropertyAttributes.None, _types.String, null);

        var getter = typeBuilder.DefineMethod(
            "get_Syscall",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSErrorSyscallGetter = getter;
        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorSyscallField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_Syscall",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        runtime.TSErrorSyscallSetter = setter;
        var setIL = setter.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Stfld, _tsErrorSyscallField);
        setIL.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    private void EmitTSErrorToStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        il.Emit(OpCodes.Ldfld, _tsErrorMessageField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty")!);
        il.Emit(OpCodes.Brfalse, hasMessageLabel);

        // return _name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsErrorNameField);
        il.Emit(OpCodes.Ret);

        // return _name + ": " + _message;
        il.MarkLabel(hasMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsErrorNameField);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsErrorMessageField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSimpleErrorSubclass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        string className,
        string errorName,
        Action<TypeBuilder, ConstructorBuilder> setOnRuntime)
    {
        // Define class that extends $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            className,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSErrorType
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
        il.Emit(OpCodes.Call, runtime.TSErrorCtorNameMessage);
        il.Emit(OpCodes.Ret);

        setOnRuntime(typeBuilder, ctor);
        typeBuilder.CreateType();
    }

    private void EmitTSTypeErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$TypeError", "TypeError", (type, ctor) =>
        {
            runtime.TSTypeErrorType = type;
            runtime.TSTypeErrorCtor = ctor;
        });
    }

    private void EmitTSRangeErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$RangeError", "RangeError", (type, ctor) =>
        {
            runtime.TSRangeErrorType = type;
            runtime.TSRangeErrorCtor = ctor;
        });
    }

    private void EmitTSReferenceErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$ReferenceError", "ReferenceError", (type, ctor) =>
        {
            runtime.TSReferenceErrorType = type;
            runtime.TSReferenceErrorCtor = ctor;
        });
    }

    private void EmitTSSyntaxErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$SyntaxError", "SyntaxError", (type, ctor) =>
        {
            runtime.TSSyntaxErrorType = type;
            runtime.TSSyntaxErrorCtor = ctor;
        });
    }

    private void EmitTSURIErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$URIError", "URIError", (type, ctor) =>
        {
            runtime.TSURIErrorType = type;
            runtime.TSURIErrorCtor = ctor;
        });
    }

    private void EmitTSEvalErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSimpleErrorSubclass(moduleBuilder, runtime, "$EvalError", "EvalError", (type, ctor) =>
        {
            runtime.TSEvalErrorType = type;
            runtime.TSEvalErrorCtor = ctor;
        });
    }

    private void EmitTSAggregateErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class that extends $Error
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$AggregateError",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSErrorType
        );
        runtime.TSAggregateErrorType = typeBuilder;

        // Field: private readonly List<object?> _errors
        _tsAggregateErrorErrorsField = typeBuilder.DefineField(
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
        runtime.TSAggregateErrorCtor = ctor;

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
        il.Emit(OpCodes.Call, runtime.TSErrorCtorNameMessage);

        // _errors = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stfld, _tsAggregateErrorErrorsField);

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
        il.Emit(OpCodes.Ldfld, _tsAggregateErrorErrorsField);
        il.Emit(OpCodes.Ldloc, errorsListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", [typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Br, endCtorLabel);

        il.MarkLabel(notListLabel);
        // If errors is not null and not list, add as single element
        var errorsNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Brfalse, errorsNullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsAggregateErrorErrorsField);
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
        runtime.TSAggregateErrorErrorsGetter = getter;

        var getIL = getter.GetILGenerator();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsAggregateErrorErrorsField);
        getIL.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        typeBuilder.CreateType();
    }
}
