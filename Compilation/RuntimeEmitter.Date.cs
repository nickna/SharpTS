using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Date-related runtime emission methods.
/// Uses the emitted $TSDate class for standalone support.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitDateMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitDateNow(typeBuilder, runtime);
        EmitCreateDateNoArgs(typeBuilder, runtime);
        EmitCreateDateFromValue(typeBuilder, runtime);
        EmitCreateDateFromComponents(typeBuilder, runtime);
        EmitDateToString(typeBuilder, runtime);
        EmitDateGetTime(typeBuilder, runtime);
        EmitDateGetFullYear(typeBuilder, runtime);
        EmitDateGetMonth(typeBuilder, runtime);
        EmitDateGetDate(typeBuilder, runtime);
        EmitDateGetDay(typeBuilder, runtime);
        EmitDateGetHours(typeBuilder, runtime);
        EmitDateGetMinutes(typeBuilder, runtime);
        EmitDateGetSeconds(typeBuilder, runtime);
        EmitDateGetMilliseconds(typeBuilder, runtime);
        EmitDateGetTimezoneOffset(typeBuilder, runtime);
        EmitDateSetTime(typeBuilder, runtime);
        EmitDateSetFullYear(typeBuilder, runtime);
        EmitDateSetMonth(typeBuilder, runtime);
        EmitDateSetDate(typeBuilder, runtime);
        EmitDateSetHours(typeBuilder, runtime);
        EmitDateSetMinutes(typeBuilder, runtime);
        EmitDateSetSeconds(typeBuilder, runtime);
        EmitDateSetMilliseconds(typeBuilder, runtime);
        EmitDateToISOString(typeBuilder, runtime);
        EmitDateToDateString(typeBuilder, runtime);
        EmitDateToTimeString(typeBuilder, runtime);
        EmitDateValueOf(typeBuilder, runtime);
    }

    private void EmitDateNow(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateNow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            _types.EmptyTypes
        );
        runtime.DateNow = method;

        var il = method.GetILGenerator();
        // Process any pending virtual timers before returning
        // This implements JavaScript-like single-threaded timer semantics
        il.Emit(OpCodes.Call, runtime.ProcessPendingTimers);
        // Call $TSDate.Now() static method
        il.Emit(OpCodes.Call, runtime.TSDateNowStatic);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateNoArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateNoArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateDateNoArgs = method;

        var il = method.GetILGenerator();
        // new $TSDate()
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorNoArgs);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateFromValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateFromValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateDateFromValue = method;

        var il = method.GetILGenerator();
        var stringLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check if value is double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, stringLabel);

        // Double case: new $TSDate((double)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorMilliseconds);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        // Check if value is string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        // String case: new $TSDate((string)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorString);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        // Default: new $TSDate()
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorNoArgs);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateFromComponents(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateFromComponents",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Double, _types.Double, _types.Double, _types.Double, _types.Double, _types.Double]
        );
        runtime.CreateDateFromComponents = method;

        var il = method.GetILGenerator();
        // new $TSDate((int)year, (int)month, (int)day, (int)hours, (int)minutes, (int)seconds, (int)ms)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_S, (byte)6);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorComponents);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.DateToString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        // Check if date is $TSDate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call date.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateInstanceMethodCall(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string helperName, string instanceMethodName, MethodBuilder targetMethod)
    {
        var il = targetMethod.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        // Check if date is $TSDate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call date.Method()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods[instanceMethodName]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateGetTime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetTime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetTime = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetTime", "GetTime", method);
    }

    private void EmitDateGetFullYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetFullYear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetFullYear = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetFullYear", "GetFullYear", method);
    }

    private void EmitDateGetMonth(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMonth",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetMonth = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetMonth", "GetMonth", method);
    }

    private void EmitDateGetDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetDate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetDate = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetDate", "GetDate", method);
    }

    private void EmitDateGetDay(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetDay",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetDay = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetDay", "GetDay", method);
    }

    private void EmitDateGetHours(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetHours",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetHours = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetHours", "GetHours", method);
    }

    private void EmitDateGetMinutes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMinutes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetMinutes = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetMinutes", "GetMinutes", method);
    }

    private void EmitDateGetSeconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetSeconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetSeconds = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetSeconds", "GetSeconds", method);
    }

    private void EmitDateGetMilliseconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMilliseconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetMilliseconds = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetMilliseconds", "GetMilliseconds", method);
    }

    private void EmitDateGetTimezoneOffset(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetTimezoneOffset",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateGetTimezoneOffset = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateGetTimezoneOffset", "GetTimezoneOffset", method);
    }

    private void EmitDateSetTime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetTime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        runtime.DateSetTime = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetTime"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetFullYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetFullYear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        runtime.DateSetFullYear = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetFullYear with args[0] as the year
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);  // args array
        il.Emit(OpCodes.Ldc_I4_0);  // index 0
        il.Emit(OpCodes.Ldelem_Ref);  // args[0]
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetFullYear"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetMonth(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetMonth",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        runtime.DateSetMonth = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetMonth with args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetMonth"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetDate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        runtime.DateSetDate = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetDate with arg1 (direct double parameter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetDate"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetHours(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetHours",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        runtime.DateSetHours = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetHours with args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetHours"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetMinutes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetMinutes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        runtime.DateSetMinutes = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetMinutes with args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetMinutes"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetSeconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetSeconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        runtime.DateSetSeconds = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetSeconds with args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetSeconds"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetMilliseconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetMilliseconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        runtime.DateSetMilliseconds = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetMilliseconds with arg1 (direct double parameter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["SetMilliseconds"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToISOString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToISOString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.DateToISOString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["ToISOString"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid Date");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitDateToDateString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToDateString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.DateToDateString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["ToDateString"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToTimeString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToTimeString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.DateToTimeString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["ToTimeString"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateValueOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.DateValueOf = method;
        EmitDateInstanceMethodCall(typeBuilder, runtime, "DateValueOf", "ValueOf", method);
    }
}
