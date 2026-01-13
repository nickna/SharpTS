using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $TSDate class for standalone Date support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDate
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsDateUtcDateTimeField = null!;
    private FieldBuilder _tsDateIsInvalidField = null!;
    private FieldBuilder _tsDateUnixEpochField = null!;
    private MethodBuilder _tsDateGetTimeMethod = null!;

    private void EmitTSDateClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSDate
        var typeBuilder = moduleBuilder.DefineType(
            "$TSDate",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSDateType = typeBuilder;

        // Fields
        _tsDateUtcDateTimeField = typeBuilder.DefineField("_utcDateTime", _types.DateTime, FieldAttributes.Private);
        _tsDateIsInvalidField = typeBuilder.DefineField("_isInvalid", _types.Boolean, FieldAttributes.Private);
        _tsDateUnixEpochField = typeBuilder.DefineField("UnixEpoch", _types.DateTime,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);

        // Static constructor to initialize UnixEpoch
        EmitTSDateStaticConstructor(typeBuilder);

        // Constructors
        EmitTSDateCtorNoArgs(typeBuilder, runtime);
        EmitTSDateCtorMilliseconds(typeBuilder, runtime);
        EmitTSDateCtorString(typeBuilder, runtime);
        EmitTSDateCtorComponents(typeBuilder, runtime);

        // Static Now method
        EmitTSDateNowStatic(typeBuilder, runtime);

        // Instance getter methods
        EmitTSDateGetTime(typeBuilder, runtime);
        EmitTSDateGetFullYear(typeBuilder, runtime);
        EmitTSDateGetMonth(typeBuilder, runtime);
        EmitTSDateGetDate(typeBuilder, runtime);
        EmitTSDateGetDay(typeBuilder, runtime);
        EmitTSDateGetHours(typeBuilder, runtime);
        EmitTSDateGetMinutes(typeBuilder, runtime);
        EmitTSDateGetSeconds(typeBuilder, runtime);
        EmitTSDateGetMilliseconds(typeBuilder, runtime);
        EmitTSDateGetTimezoneOffset(typeBuilder, runtime);

        // Instance setter methods
        EmitTSDateSetTime(typeBuilder, runtime);
        EmitTSDateSetFullYear(typeBuilder, runtime);
        EmitTSDateSetMonth(typeBuilder, runtime);
        EmitTSDateSetDate(typeBuilder, runtime);
        EmitTSDateSetHours(typeBuilder, runtime);
        EmitTSDateSetMinutes(typeBuilder, runtime);
        EmitTSDateSetSeconds(typeBuilder, runtime);
        EmitTSDateSetMilliseconds(typeBuilder, runtime);

        // Conversion methods
        EmitTSDateToString(typeBuilder, runtime);
        EmitTSDateToISOString(typeBuilder, runtime);
        EmitTSDateToDateString(typeBuilder, runtime);
        EmitTSDateToTimeString(typeBuilder, runtime);
        EmitTSDateValueOf(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSDateStaticConstructor(TypeBuilder typeBuilder)
    {
        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var il = cctor.GetILGenerator();

        // UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        il.Emit(OpCodes.Ldc_I4, 1970);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1); // DateTimeKind.Utc
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateCtorNoArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $TSDate() { _utcDateTime = DateTime.UtcNow; _isInvalid = false; }
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSDateCtorNoArgs = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _utcDateTime = DateTime.UtcNow
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("UtcNow")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);

        // _isInvalid = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateCtorMilliseconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $TSDate(double milliseconds)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Double]
        );
        runtime.TSDateCtorMilliseconds = ctor;

        var il = ctor.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // Check for NaN or Infinity
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsInfinity")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        // Check range: -8640000000000000 to 8640000000000000
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_R8, -8640000000000000.0);
        il.Emit(OpCodes.Blt, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_R8, 8640000000000000.0);
        il.Emit(OpCodes.Bgt, invalidLabel);

        // _utcDateTime = UnixEpoch.AddMilliseconds(milliseconds)
        // For value type instance methods, we need the address of the struct
        var epochLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Stloc, epochLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, epochLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddMilliseconds")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);

        // _isInvalid = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Br, endLabel);

        // invalidLabel: _isInvalid = true
        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateCtorString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $TSDate(string isoString) - simplified: try parse, if fail set invalid
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSDateCtorString = ctor;

        var il = ctor.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var validLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var resultLocal = il.DeclareLocal(_types.DateTime);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // if (string.IsNullOrWhiteSpace(isoString)) goto invalid
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrWhiteSpace")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        // Try DateTime.TryParse with RoundtripKind
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)(DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces));
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("TryParse", [_types.String, typeof(IFormatProvider), typeof(DateTimeStyles), _types.DateTime.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Convert to UTC if needed: result.Kind == Utc ? result : result.ToUniversalTime()
        var notUtcLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Kind")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1); // DateTimeKind.Utc
        il.Emit(OpCodes.Bne_Un, notUtcLabel);

        // Already UTC - store directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Br, validLabel);

        // Not UTC - convert to UTC
        il.MarkLabel(notUtcLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);

        // _isInvalid = false
        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Br, endLabel);

        // invalidLabel: _isInvalid = true
        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateCtorComponents(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $TSDate(int year, int month, int day, int hours, int minutes, int seconds, int milliseconds)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32]
        );
        runtime.TSDateCtorComponents = ctor;

        var il = ctor.GetILGenerator();
        var endLabel = il.DefineLabel();
        var yearLocal = il.DeclareLocal(_types.Int32);
        var baseDateLocal = il.DeclareLocal(_types.DateTime);
        var localDateTimeLocal = il.DeclareLocal(_types.DateTime);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // Begin try block
        il.BeginExceptionBlock();

        // JavaScript quirk: 2-digit years (0-99) map to 1900-1999
        // if (year >= 0 && year <= 99) year += 1900;
        var skipYearAdjustLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, yearLocal);

        il.Emit(OpCodes.Ldloc, yearLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, skipYearAdjustLabel); // Skip if < 0
        il.Emit(OpCodes.Ldloc, yearLocal);
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)99);
        il.Emit(OpCodes.Bgt, skipYearAdjustLabel); // Skip if > 99

        // Within 0-99 range, add 1900
        il.Emit(OpCodes.Ldloc, yearLocal);
        il.Emit(OpCodes.Ldc_I4, 1900);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, yearLocal);

        il.MarkLabel(skipYearAdjustLabel);

        // baseDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Local)
        il.Emit(OpCodes.Ldloc, yearLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_2); // DateTimeKind.Local
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stloc, baseDateLocal);

        // localDateTime = baseDate.AddMonths(month).AddDays(day-1).AddHours(hours).AddMinutes(minutes).AddSeconds(seconds).AddMilliseconds(milliseconds)
        il.Emit(OpCodes.Ldloca, baseDateLocal);
        il.Emit(OpCodes.Ldarg_2); // month
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddMonths")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_3); // day
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddDays")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)4); // hours
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddHours")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)5); // minutes
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddMinutes")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)6); // seconds
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddSeconds")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)7); // milliseconds
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddMilliseconds")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        // _utcDateTime = localDateTime.ToUniversalTime()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);

        // _isInvalid = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Leave, endLabel);

        // Catch block
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // Discard exception
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateNowStatic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public static double Now() => (DateTime.UtcNow - UnixEpoch).TotalMilliseconds
        var method = typeBuilder.DefineMethod(
            "Now",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateNowStatic = method;

        var il = method.GetILGenerator();
        var utcNowLocal = il.DeclareLocal(_types.DateTime);
        var unixEpochLocal = il.DeclareLocal(_types.DateTime);

        // DateTime.UtcNow
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("UtcNow")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, utcNowLocal);

        // UnixEpoch
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Stloc, unixEpochLocal);

        // Subtract: utcNow - unixEpoch
        il.Emit(OpCodes.Ldloc, utcNowLocal);
        il.Emit(OpCodes.Ldloc, unixEpochLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("op_Subtraction", [_types.DateTime, _types.DateTime])!);

        // .TotalMilliseconds
        var timeSpanLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, timeSpanLocal);
        il.Emit(OpCodes.Ldloca, timeSpanLocal);
        il.Emit(OpCodes.Call, _types.TimeSpan.GetProperty("TotalMilliseconds")!.GetGetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetTime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public double GetTime() { if (_isInvalid) return NaN; return (_utcDateTime - UnixEpoch).TotalMilliseconds; }
        var method = typeBuilder.DefineMethod(
            "GetTime",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        _tsDateGetTimeMethod = method; // Save for later use
        runtime.TSDateMethods["GetTime"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        // if (_isInvalid) return NaN
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        // (_utcDateTime - UnixEpoch).TotalMilliseconds
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("op_Subtraction", [_types.DateTime, _types.DateTime])!);
        var tsLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, tsLocal);
        il.Emit(OpCodes.Ldloca, tsLocal);
        il.Emit(OpCodes.Call, _types.TimeSpan.GetProperty("TotalMilliseconds")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetFullYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetFullYear", "Year");
    }

    private void EmitTSDateGetMonth(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Month needs -1 adjustment (JS is 0-indexed)
        var method = typeBuilder.DefineMethod(
            "GetMonth",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["GetMonth"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Month")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetDate", "Day");
    }

    private void EmitTSDateGetDay(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // DayOfWeek returns enum, cast to int then double
        var method = typeBuilder.DefineMethod(
            "GetDay",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["GetDay"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("DayOfWeek")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetHours(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetHours", "Hour");
    }

    private void EmitTSDateGetMinutes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetMinutes", "Minute");
    }

    private void EmitTSDateGetSeconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetSeconds", "Second");
    }

    private void EmitTSDateGetMilliseconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetMilliseconds", "Millisecond");
    }

    private void EmitSimpleDateGetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, string propertyName)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateMethods[methodName] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty(propertyName)!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetTimezoneOffset(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetTimezoneOffset",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["GetTimezoneOffset"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        // -TimeZoneInfo.Local.GetUtcOffset(_utcDateTime).TotalMinutes
        il.Emit(OpCodes.Call, typeof(TimeZoneInfo).GetProperty("Local")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Callvirt, typeof(TimeZoneInfo).GetMethod("GetUtcOffset", [_types.DateTime])!);
        var tsLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, tsLocal);
        il.Emit(OpCodes.Ldloca, tsLocal);
        il.Emit(OpCodes.Call, _types.TimeSpan.GetProperty("TotalMinutes")!.GetGetMethod()!);
        il.Emit(OpCodes.Neg);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateSetTime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public double SetTime(double time) - sets from epoch ms, returns new timestamp
        var method = typeBuilder.DefineMethod(
            "SetTime",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double]
        );
        runtime.TSDateMethods["SetTime"] = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var validLabel = il.DefineLabel();

        // Check for NaN/Infinity/out of range
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsInfinity")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_R8, -8640000000000000.0);
        il.Emit(OpCodes.Blt, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_R8, 8640000000000000.0);
        il.Emit(OpCodes.Bgt, invalidLabel);

        // Valid - set time
        // For value type instance methods, we need the address of the struct
        var epochLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Stloc, epochLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, epochLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("AddMilliseconds")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Br, validLabel);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);

        il.MarkLabel(validLabel);
        // Call GetTime() to return result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    // Date setters: modify date component, store in UTC, return new timestamp
    private void EmitTSDateSetFullYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetFullYear", MethodAttributes.Public, _types.Double, [_types.Double]);
        runtime.TSDateMethods["SetFullYear"] = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if invalid
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        // Get local time components
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        // Create new DateTime with new year
        var newDateLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // year
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Month")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Day")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Hour")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Minute")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Second")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Millisecond")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);  // DateTimeKind.Local
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stloc, newDateLocal);

        // Store as UTC
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, newDateLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(invalidLabel);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateSetMonth(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetMonth", MethodAttributes.Public, _types.Double, [_types.Double]);
        runtime.TSDateMethods["SetMonth"] = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        var newDateLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Year")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);  // month + 1 (JS is 0-based)
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Day")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Hour")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Minute")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Second")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Millisecond")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stloc, newDateLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, newDateLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(invalidLabel);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateSetDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetDate", MethodAttributes.Public, _types.Double, [_types.Double]);
        runtime.TSDateMethods["SetDate"] = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        var newDateLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Year")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Month")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // day
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Hour")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Minute")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Second")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Millisecond")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stloc, newDateLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, newDateLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(invalidLabel);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateSetHours(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetHours", MethodAttributes.Public, _types.Double, [_types.Double]);
        runtime.TSDateMethods["SetHours"] = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        var newDateLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Year")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Month")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Day")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // hours
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Minute")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Second")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetProperty("Millisecond")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, _types.DateTime.GetConstructor([
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, typeof(DateTimeKind)
        ])!);
        il.Emit(OpCodes.Stloc, newDateLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, newDateLocal);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToUniversalTime")!);
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(invalidLabel);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateSetMinutes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitDateSetterStub(typeBuilder, runtime, "SetMinutes", [_types.Double]);
    }

    private void EmitTSDateSetSeconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitDateSetterStub(typeBuilder, runtime, "SetSeconds", [_types.Double]);
    }

    private void EmitTSDateSetMilliseconds(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitDateSetterStub(typeBuilder, runtime, "SetMilliseconds", [_types.Double]);
    }

    private void EmitDateSetterStub(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, Type[] paramTypes)
    {
        // Stub setter for less common setters - just returns current time
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public,
            _types.Double,
            paramTypes
        );
        runtime.TSDateMethods[name] = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["ToString"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        // Simple format: return local.ToString("ddd MMM dd yyyy HH:mm:ss") + offset
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "ddd MMM dd yyyy HH:mm:ss");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateToISOString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToISOString",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["ToISOString"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid Date");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Ldstr, "yyyy-MM-ddTHH:mm:ss.fffZ");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateToDateString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToDateString",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["ToDateString"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "ddd MMM dd yyyy");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateToTimeString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToTimeString",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["ToTimeString"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        var localTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "HH:mm:ss");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DateTime.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateValueOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValueOf",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSDateMethods["ValueOf"] = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }
}
