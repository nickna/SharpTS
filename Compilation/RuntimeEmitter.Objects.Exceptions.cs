using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCreateException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1; just
        // emit the body on the existing MethodBuilder.
        var method = (MethodBuilder)runtime.CreateException;

        var il = method.GetILGenerator();
        var exLocal = il.DeclareLocal(_types.Exception);

        // var ex = new Exception(value?.ToString() ?? "null")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, exLocal);

        // ex.Data["__tsValue"] = value;  (preserve original value)
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // return ex;
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWrapException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapException",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Exception]
        );
        runtime.WrapException = method;

        var il = method.GetILGenerator();
        var fallbackLabel = il.DefineLabel();
        var checkTsValueLabel = il.DefineLabel();
        var unwrapLoopLabel = il.DefineLabel();
        var tsValueLocal = il.DeclareLocal(_types.Object);
        var exLocal = il.DeclareLocal(_types.Exception);
        var innerLocal = il.DeclareLocal(_types.Exception);

        // Store exception in local (we might need to unwrap it)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, exLocal);

        // Loop: while (ex is TargetInvocationException && ex.InnerException != null)
        //         ex = ex.InnerException
        // Multiple levels can stack when JS code calls JS code calls a runtime
        // helper — each MethodInfo.Invoke wraps once, so we may see TIE(TIE(real)).
        il.MarkLabel(unwrapLoopLabel);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Isinst, _types.TargetInvocationException);
        il.Emit(OpCodes.Brfalse, checkTsValueLabel);

        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, innerLocal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Brfalse, checkTsValueLabel);  // InnerException is null — stop unwrapping

        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Br, unwrapLoopLabel);

        il.MarkLabel(checkTsValueLabel);

        // Check if ex.Data contains "__tsValue" (TypeScript throw value)
        // if (ex.Data.Contains("__tsValue")) return ex.Data["__tsValue"];
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "Contains", _types.Object));
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // Return the original TypeScript value
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "get_Item", _types.Object));
        il.Emit(OpCodes.Ret);

        // Check for $PromiseRejectedException - return the Reason property
        il.MarkLabel(fallbackLabel);
        var checkDataCloneErrorLabel = il.DefineLabel();
        var checkNodeErrorLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseRejectedExceptionType);
        il.Emit(OpCodes.Brfalse, checkDataCloneErrorLabel);

        // It's a $PromiseRejectedException - return its Reason property
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseRejectedExceptionType);
        il.Emit(OpCodes.Call, runtime.TSPromiseRejectedExceptionReasonGetter);
        il.Emit(OpCodes.Ret);

        // Check for $DataCloneError (thrown by StructuredCloneCore, #1255) — return its
        // Message directly (a raw string, not wrapped in $Error), matching the
        // interpreter's catch binding for a non-guest-throw exception (Interpreter.
        // Statements.cs: `ex is ThrowException tex ? tex.Value : ex.Message`).
        il.MarkLabel(checkDataCloneErrorLabel);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Isinst, runtime.TSDataCloneErrorType);
        il.Emit(OpCodes.Brfalse, checkNodeErrorLabel);

        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // Check for __nodeError marker in Data (Node.js-style fs errors)
        il.MarkLabel(checkNodeErrorLabel);
        var standardFallbackLabel = il.DefineLabel();

        // if (ex.Data.Contains("__nodeError"))
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__nodeError");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "Contains", _types.Object));
        il.Emit(OpCodes.Brfalse, standardFallbackLabel);

        // It's a Node.js error with metadata - create Dictionary with all properties
        // Create the dictionary
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

        // name: "Error"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // message: exception message
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // code: ex.Data["__code"]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__code");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "get_Item", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // syscall: ex.Data["__syscall"]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "syscall");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__syscall");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "get_Item", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // path: ex.Data["__path"]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "path");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__path");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "get_Item", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Return the dictionary
        il.Emit(OpCodes.Ret);

        // Standard fallback: a host-originated .NET exception (no guest __tsValue,
        // not a rejected promise, no Node metadata). Wrap as a real $Error so guest
        // `catch` sees a proper Error instance — `e instanceof Error` is true and
        // `e.name` is "Error" rather than the .NET type name. Previously this returned
        // a plain { message, name=<.NET type> } dictionary. (#700)
        // return new $Error(ex.Message)
        il.MarkLabel(standardFallbackLabel);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper method that throws a ReferenceError for undefined variables.
    /// This is called when accessing a variable that is out of scope (e.g., after a for-loop exits).
    /// </summary>
    private void EmitThrowUndefinedVariable(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public static void ThrowUndefinedVariable(string name)
        var method = typeBuilder.DefineMethod(
            "ThrowUndefinedVariable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.String]
        );
        runtime.ThrowUndefinedVariable = method;

        var il = method.GetILGenerator();

        // Build error message: "Undefined variable '" + name + "'."
        il.Emit(OpCodes.Ldstr, "Undefined variable '");
        il.Emit(OpCodes.Ldarg_0);  // name
        il.Emit(OpCodes.Ldstr, "'.");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));

        // Create new $ReferenceError(message)
        il.Emit(OpCodes.Newobj, runtime.TSReferenceErrorCtor);

        // Wrap as System.Exception using CreateException helper (stores original value in Data["__tsValue"])
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);
    }
}

