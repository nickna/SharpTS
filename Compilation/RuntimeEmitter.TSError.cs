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
        var typeBuilder = moduleBuilder.DefineType(
            "$Error",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSErrorType = typeBuilder;

        // Fields
        _tsErrorNameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);
        _tsErrorMessageField = typeBuilder.DefineField("_message", _types.String, FieldAttributes.Private);
        _tsErrorStackField = typeBuilder.DefineField("_stack", _types.String, FieldAttributes.Private);

        // CaptureStackTrace helper - must be emitted first since the constructor calls it
        EmitCaptureStackTrace(typeBuilder, runtime);

        // Protected constructor: protected $Error(string name, string? message)
        // Must be emitted before message constructor since it calls this one
        EmitTSErrorCtorNameMessage(typeBuilder, runtime);

        // Constructor: public $Error(string? message) : this("Error", message)
        EmitTSErrorCtorMessage(typeBuilder, runtime);

        // Properties: Name, Message, Stack (get/set)
        EmitTSErrorNameProperty(typeBuilder, runtime);
        EmitTSErrorMessageProperty(typeBuilder, runtime);
        EmitTSErrorStackProperty(typeBuilder, runtime);

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
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

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

        // _stack = CaptureStackTrace()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSErrorCaptureStackTrace);
        il.Emit(OpCodes.Stfld, _tsErrorStackField);

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
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, _tsErrorStackField);
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
        runtime.TSErrorToStringMethod = method;

        var il = method.GetILGenerator();
        var hasMessageLabel = il.DefineLabel();

        // if (string.IsNullOrEmpty(_message))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsErrorMessageField);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty")!);
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
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCaptureStackTrace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static string CaptureStackTrace()
        var method = typeBuilder.DefineMethod(
            "CaptureStackTrace",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSErrorCaptureStackTrace = method;

        var il = method.GetILGenerator();

        // var stackTrace = new StackTrace(skipFrames: 3, fNeedFileInfo: true);
        var stackTraceLocal = il.DeclareLocal(_types.StackTrace);
        il.Emit(OpCodes.Ldc_I4_3); // skipFrames
        il.Emit(OpCodes.Ldc_I4_1); // fNeedFileInfo = true
        il.Emit(OpCodes.Newobj, _types.StackTrace.GetConstructor([_types.Int32, _types.Boolean])!);
        il.Emit(OpCodes.Stloc, stackTraceLocal);

        // var frames = stackTrace.GetFrames();
        var framesLocal = il.DeclareLocal(_types.MakeArrayType(_types.StackFrame));
        il.Emit(OpCodes.Ldloc, stackTraceLocal);
        il.Emit(OpCodes.Callvirt, _types.StackTrace.GetMethod("GetFrames")!);
        il.Emit(OpCodes.Stloc, framesLocal);

        // if (frames == null || frames.Length == 0) return "";
        var hasFramesLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, framesLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);
        il.Emit(OpCodes.Ldloc, framesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasFramesLabel);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasFramesLabel);

        // var sb = new StringBuilder();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.StringBuilder.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // Loop through frames
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var frameLocal = il.DeclareLocal(_types.StackFrame);
        var methodLocal = il.DeclareLocal(_types.MethodBase);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, framesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // frame = frames[index]
        il.Emit(OpCodes.Ldloc, framesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, frameLocal);

        // method = frame.GetMethod()
        il.Emit(OpCodes.Ldloc, frameLocal);
        il.Emit(OpCodes.Callvirt, _types.StackFrame.GetMethod("GetMethod")!);
        il.Emit(OpCodes.Stloc, methodLocal);

        // if (method == null) continue
        var processMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brtrue, processMethodLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(processMethodLabel);

        // sb.Append("    at ");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "    at ");
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        // Get type name
        var typeNameLocal = il.DeclareLocal(_types.String);
        var declaringTypeLocal = il.DeclareLocal(_types.Type);
        var skipTypeLabel = il.DefineLabel();
        var afterTypeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Callvirt, _types.MethodBase.GetProperty("DeclaringType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, declaringTypeLocal);
        il.Emit(OpCodes.Ldloc, declaringTypeLocal);
        il.Emit(OpCodes.Brfalse, skipTypeLabel);

        // sb.Append(typeName + ".")
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, declaringTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.Type.GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipTypeLabel);

        // sb.Append(method.Name)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Callvirt, _types.MethodBase.GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        // Add file info if available
        var fileNameLocal = il.DeclareLocal(_types.String);
        var noFileLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, frameLocal);
        il.Emit(OpCodes.Callvirt, _types.StackFrame.GetMethod("GetFileName")!);
        il.Emit(OpCodes.Stloc, fileNameLocal);
        il.Emit(OpCodes.Ldloc, fileNameLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty")!);
        il.Emit(OpCodes.Brtrue, noFileLabel);

        // sb.Append(" (" + fileName + ":" + lineNumber + ")")
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " (");
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, fileNameLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, frameLocal);
        il.Emit(OpCodes.Callvirt, _types.StackFrame.GetMethod("GetFileLineNumber")!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.Int32])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ")");
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noFileLabel);

        // sb.AppendLine()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("AppendLine", Type.EmptyTypes)!);
        il.Emit(OpCodes.Pop);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return sb.ToString().TrimEnd()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("TrimEnd", Type.EmptyTypes)!);
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
        var typeBuilder = moduleBuilder.DefineType(
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
        var typeBuilder = moduleBuilder.DefineType(
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

        // Call base("AggregateError", message ?? "All promises were rejected")
        // Note: arg1 = errors, arg2 = message
        var hasMessageLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "AggregateError");
        il.Emit(OpCodes.Ldarg_2);  // message
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasMessageLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "All promises were rejected");
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
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("AddRange", [typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Br, endCtorLabel);

        il.MarkLabel(notListLabel);
        // If errors is not null and not list, add as single element
        var errorsNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Brfalse, errorsNullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsAggregateErrorErrorsField);
        il.Emit(OpCodes.Ldarg_1);  // errors
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

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
