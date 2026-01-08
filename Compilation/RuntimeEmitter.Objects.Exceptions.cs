using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitCreateException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateException",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Exception,
            [_types.Object]
        );
        runtime.CreateException = method;

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

    private static void EmitWrapException(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        var tsValueLocal = il.DeclareLocal(_types.Object);
        var exLocal = il.DeclareLocal(_types.Exception);

        // Store exception in local (we might need to unwrap it)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, exLocal);

        // Check if ex is TargetInvocationException and unwrap to InnerException
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Isinst, _types.TargetInvocationException);
        il.Emit(OpCodes.Brfalse, checkTsValueLabel);

        // It's a TargetInvocationException - get InnerException if not null
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        var innerLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, innerLocal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Brfalse_S, checkTsValueLabel);  // If InnerException is null, use original

        // InnerException is not null - use it
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Stloc, exLocal);

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

        // Fallback: wrap standard .NET exceptions as Dictionary
        il.MarkLabel(fallbackLabel);
        // return new Dictionary<string, object> { ["message"] = ex.Message, ["name"] = ex.GetType().Name }
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }
}
