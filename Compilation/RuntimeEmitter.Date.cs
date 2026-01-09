using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Date-related runtime emission methods.
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
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("Now", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("CreateNoArgs", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("CreateFromValue", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Ldarg_S, (byte)6);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("CreateFromComponents", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("ToString", BindingFlags.Public | BindingFlags.Static)!);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetTime", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetFullYear", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetMonth", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetDate", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetDay", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetHours", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetMinutes", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetSeconds", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetMilliseconds", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("GetTimezoneOffset", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetTime", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetFullYear", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetMonth", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetDate", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetHours", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetMinutes", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetSeconds", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("SetMilliseconds", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("ToISOString", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("ToDateString", BindingFlags.Public | BindingFlags.Static)!);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("ToTimeString", BindingFlags.Public | BindingFlags.Static)!);
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

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(DateRuntimeHelpers).GetMethod("ValueOf", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper methods for Date operations in compiled code.
/// These methods are called by the emitted runtime.
/// </summary>
public static class DateRuntimeHelpers
{
    public static double Now() => SharpTSDate.Now();

    public static object CreateNoArgs() => new SharpTSDate();

    public static object CreateFromValue(object? value)
    {
        return value switch
        {
            double ms => new SharpTSDate(ms),
            string str => new SharpTSDate(str),
            _ => new SharpTSDate()
        };
    }

    public static object CreateFromComponents(double year, double month, double day,
                                               double hours, double minutes, double seconds, double ms)
    {
        return new SharpTSDate((int)year, (int)month, (int)day, (int)hours, (int)minutes, (int)seconds, (int)ms);
    }

    public static string ToString(object? date)
    {
        if (date is SharpTSDate d) return d.ToString();
        return "Invalid Date";
    }

    public static double GetTime(object? date)
    {
        if (date is SharpTSDate d) return d.GetTime();
        return double.NaN;
    }

    public static double GetFullYear(object? date)
    {
        if (date is SharpTSDate d) return d.GetFullYear();
        return double.NaN;
    }

    public static double GetMonth(object? date)
    {
        if (date is SharpTSDate d) return d.GetMonth();
        return double.NaN;
    }

    public static double GetDate(object? date)
    {
        if (date is SharpTSDate d) return d.GetDate();
        return double.NaN;
    }

    public static double GetDay(object? date)
    {
        if (date is SharpTSDate d) return d.GetDay();
        return double.NaN;
    }

    public static double GetHours(object? date)
    {
        if (date is SharpTSDate d) return d.GetHours();
        return double.NaN;
    }

    public static double GetMinutes(object? date)
    {
        if (date is SharpTSDate d) return d.GetMinutes();
        return double.NaN;
    }

    public static double GetSeconds(object? date)
    {
        if (date is SharpTSDate d) return d.GetSeconds();
        return double.NaN;
    }

    public static double GetMilliseconds(object? date)
    {
        if (date is SharpTSDate d) return d.GetMilliseconds();
        return double.NaN;
    }

    public static double GetTimezoneOffset(object? date)
    {
        if (date is SharpTSDate d) return d.GetTimezoneOffset();
        return double.NaN;
    }

    public static double SetTime(object? date, double time)
    {
        if (date is SharpTSDate d) return d.SetTime(time);
        return double.NaN;
    }

    public static double SetFullYear(object? date, object[]? args)
    {
        if (date is not SharpTSDate d || args == null || args.Length == 0) return double.NaN;
        var year = (double)args[0];
        double? month = args.Length > 1 ? (double?)args[1] : null;
        double? day = args.Length > 2 ? (double?)args[2] : null;
        return d.SetFullYear(year, month, day);
    }

    public static double SetMonth(object? date, object[]? args)
    {
        if (date is not SharpTSDate d || args == null || args.Length == 0) return double.NaN;
        var month = (double)args[0];
        double? day = args.Length > 1 ? (double?)args[1] : null;
        return d.SetMonth(month, day);
    }

    public static double SetDate(object? date, double day)
    {
        if (date is SharpTSDate d) return d.SetDate(day);
        return double.NaN;
    }

    public static double SetHours(object? date, object[]? args)
    {
        if (date is not SharpTSDate d || args == null || args.Length == 0) return double.NaN;
        var hours = (double)args[0];
        double? min = args.Length > 1 ? (double?)args[1] : null;
        double? sec = args.Length > 2 ? (double?)args[2] : null;
        double? ms = args.Length > 3 ? (double?)args[3] : null;
        return d.SetHours(hours, min, sec, ms);
    }

    public static double SetMinutes(object? date, object[]? args)
    {
        if (date is not SharpTSDate d || args == null || args.Length == 0) return double.NaN;
        var min = (double)args[0];
        double? sec = args.Length > 1 ? (double?)args[1] : null;
        double? ms = args.Length > 2 ? (double?)args[2] : null;
        return d.SetMinutes(min, sec, ms);
    }

    public static double SetSeconds(object? date, object[]? args)
    {
        if (date is not SharpTSDate d || args == null || args.Length == 0) return double.NaN;
        var sec = (double)args[0];
        double? ms = args.Length > 1 ? (double?)args[1] : null;
        return d.SetSeconds(sec, ms);
    }

    public static double SetMilliseconds(object? date, double ms)
    {
        if (date is SharpTSDate d) return d.SetMilliseconds(ms);
        return double.NaN;
    }

    public static string ToISOString(object? date)
    {
        if (date is SharpTSDate d) return d.ToISOString();
        throw new Exception("Runtime Error: Invalid Date");
    }

    public static string ToDateString(object? date)
    {
        if (date is SharpTSDate d) return d.ToDateString();
        return "Invalid Date";
    }

    public static string ToTimeString(object? date)
    {
        if (date is SharpTSDate d) return d.ToTimeString();
        return "Invalid Date";
    }

    public static double ValueOf(object? date)
    {
        if (date is SharpTSDate d) return d.ValueOf();
        return double.NaN;
    }
}

