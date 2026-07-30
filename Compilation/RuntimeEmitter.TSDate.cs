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
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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

        // Static Now method (UTC/parse are emitted after the instance members they reuse)
        EmitTSDateNowStatic(typeBuilder, runtime);

        // Instance getter methods
        EmitTSDateGetTime(typeBuilder, runtime);
        EmitTSDateGetFullYear(typeBuilder, runtime);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetMonth", "Month", subtractAfter: 1);
        EmitTSDateGetDate(typeBuilder, runtime);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetDay", "DayOfWeek");
        EmitTSDateGetHours(typeBuilder, runtime);
        EmitTSDateGetMinutes(typeBuilder, runtime);
        EmitTSDateGetSeconds(typeBuilder, runtime);
        EmitTSDateGetMilliseconds(typeBuilder, runtime);
        EmitTSDateGetTimezoneOffset(typeBuilder, runtime);

        // UTC getter methods (#516)
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCFullYear", "Year", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCMonth", "Month", utc: true, subtractAfter: 1);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCDate", "Day", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCDay", "DayOfWeek", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCHours", "Hour", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCMinutes", "Minute", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCSeconds", "Second", utc: true);
        EmitSimpleDateGetter(typeBuilder, runtime, "GetUTCMilliseconds", "Millisecond", utc: true);
        // Legacy getYear: local-time year minus 1900 (Annex B, #516)
        EmitSimpleDateGetter(typeBuilder, runtime, "GetYear", "Year", subtractAfter: 1900);

        // Instance setter methods (all route through the shared component setter). The
        // multi-component setters list the contiguous run they may write — index 0 is the
        // primary, the rest are optional trailing components honored when supplied (#536).
        EmitTSDateSetTime(typeBuilder, runtime);
        EmitDateComponentSetter(typeBuilder, runtime, "SetFullYear", [DateComponent.Year, DateComponent.Month, DateComponent.Day], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetMonth", [DateComponent.Month, DateComponent.Day], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetDate", [DateComponent.Day], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetHours", [DateComponent.Hour, DateComponent.Minute, DateComponent.Second, DateComponent.Millisecond], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetMinutes", [DateComponent.Minute, DateComponent.Second, DateComponent.Millisecond], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetSeconds", [DateComponent.Second, DateComponent.Millisecond], utc: false);
        EmitDateComponentSetter(typeBuilder, runtime, "SetMilliseconds", [DateComponent.Millisecond], utc: false);

        // UTC setter methods (#516)
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCFullYear", [DateComponent.Year, DateComponent.Month, DateComponent.Day], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCMonth", [DateComponent.Month, DateComponent.Day], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCDate", [DateComponent.Day], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCHours", [DateComponent.Hour, DateComponent.Minute, DateComponent.Second, DateComponent.Millisecond], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCMinutes", [DateComponent.Minute, DateComponent.Second, DateComponent.Millisecond], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCSeconds", [DateComponent.Second, DateComponent.Millisecond], utc: true);
        EmitDateComponentSetter(typeBuilder, runtime, "SetUTCMilliseconds", [DateComponent.Millisecond], utc: true);
        // Legacy setYear (Annex B, #516) — emitted after SetFullYear, which it delegates to.
        EmitTSDateSetYear(typeBuilder, runtime);

        // Conversion methods
        EmitTSDateToString(typeBuilder, runtime);
        EmitTSDateToISOString(typeBuilder, runtime);
        EmitTSDateToDateString(typeBuilder, runtime);
        EmitTSDateToTimeString(typeBuilder, runtime);
        EmitTSDateToUTCString(typeBuilder, runtime);
        // toLocale* format in local time using the host's current culture (#516)
        EmitTSDateLocaleString(typeBuilder, runtime, "ToLocaleDateString", "d");
        EmitTSDateLocaleString(typeBuilder, runtime, "ToLocaleTimeString", "T");
        EmitTSDateLocaleString(typeBuilder, runtime, "ToLocaleString", "G");
        EmitTSDateValueOf(typeBuilder, runtime);

        // Static Date.UTC / Date.parse (#538) — emitted last as they reuse the string ctor and
        // GetTime defined above.
        EmitTSDateUTCStatic(typeBuilder, runtime);
        EmitTSDateParseStatic(typeBuilder, runtime);

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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DateTime, [
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.DateTimeKind
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
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // _utcDateTime = DateTime.UtcNow
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DateTime, "UtcNow")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // Check for NaN or Infinity
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity")!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMilliseconds")!);
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
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // if (string.IsNullOrWhiteSpace(isoString)) goto invalid
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrWhiteSpace")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        // Try DateTime.TryParse with RoundtripKind
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)(DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces));
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "TryParse", [_types.String, typeof(IFormatProvider), typeof(DateTimeStyles), _types.DateTime.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Convert to UTC if needed: result.Kind == Utc ? result : result.ToUniversalTime()
        var notUtcLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DateTime, "Kind")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToUniversalTime")!);
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
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DateTime, [
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.DateTimeKind
        ])!);
        il.Emit(OpCodes.Stloc, baseDateLocal);

        // localDateTime = baseDate.AddMonths(month).AddDays(day-1).AddHours(hours).AddMinutes(minutes).AddSeconds(seconds).AddMilliseconds(milliseconds)
        il.Emit(OpCodes.Ldloca, baseDateLocal);
        il.Emit(OpCodes.Ldarg_2); // month
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMonths")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_3); // day
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddDays")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)4); // hours
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddHours")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)5); // minutes
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMinutes")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)6); // seconds
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddSeconds")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Ldarg_S, (byte)7); // milliseconds
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMilliseconds")!);
        il.Emit(OpCodes.Stloc, localDateTimeLocal);

        // _utcDateTime = localDateTime.ToUniversalTime()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, localDateTimeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToUniversalTime")!);
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
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DateTime, "UtcNow")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, utcNowLocal);

        // UnixEpoch
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Stloc, unixEpochLocal);

        // Subtract: utcNow - unixEpoch
        il.Emit(OpCodes.Ldloc, utcNowLocal);
        il.Emit(OpCodes.Ldloc, unixEpochLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "op_Subtraction", [_types.DateTime, _types.DateTime])!);

        // .TotalMilliseconds
        var timeSpanLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, timeSpanLocal);
        il.Emit(OpCodes.Ldloca, timeSpanLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMilliseconds")!.GetGetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    // ECMA-262 §21.4.3.4 (Date.UTC): builds a UTC instant from the supplied components (packaged
    // as object[]) and returns the timestamp in ms since the epoch, or NaN if a supplied component
    // is non-finite or the date is out of range. Mirrors SharpTSDate.UTC. The components are read
    // and validated up front (outside the try) so a non-finite branch is a plain jump; the instant
    // is then built inside a try/catch (out-of-range -> NaN), matching the constructor.
    private void EmitTSDateUTCStatic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UTC",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ObjectArray]
        );
        runtime.TSDateUTCStatic = method;

        var il = method.GetILGenerator();
        var nanLabel = il.DefineLabel();
        var afterTry = il.DefineLabel();
        var tmp = il.DeclareLocal(_types.Double);
        var result = il.DeclareLocal(_types.Double);
        var yLocal = il.DeclareLocal(_types.Int32);
        var moLocal = il.DeclareLocal(_types.Int32);
        var dLocal = il.DeclareLocal(_types.Int32);
        var hLocal = il.DeclareLocal(_types.Int32);
        var miLocal = il.DeclareLocal(_types.Int32);
        var sLocal = il.DeclareLocal(_types.Int32);
        var msLocal = il.DeclareLocal(_types.Int32);

        // Date.UTC() with no arguments -> NaN (year is required).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, nanLabel);

        // Read & finite-validate each component into a local (defaults: month 0, date 1, time 0).
        PushUtcComponentInt(il, 0, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, yLocal);

        // Two-digit year mapping: if (y >= 0 && y <= 99) y += 1900;
        var skipMap = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, skipMap);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4, 99);
        il.Emit(OpCodes.Bgt, skipMap);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4, 1900);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, yLocal);
        il.MarkLabel(skipMap);

        PushUtcComponentInt(il, 1, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, moLocal);
        PushUtcComponentInt(il, 2, 1, nanLabel, tmp); il.Emit(OpCodes.Stloc, dLocal);
        PushUtcComponentInt(il, 3, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, hLocal);
        PushUtcComponentInt(il, 4, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, miLocal);
        PushUtcComponentInt(il, 5, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, sLocal);
        PushUtcComponentInt(il, 6, 0, nanLabel, tmp); il.Emit(OpCodes.Stloc, msLocal);

        il.BeginExceptionBlock();
        var cur = il.DeclareLocal(_types.DateTime);
        // cur = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1); // DateTimeKind.Utc
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DateTime, [
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.DateTimeKind
        ])!);
        il.Emit(OpCodes.Stloc, cur);
        // AddMonths takes an int; AddDays/AddHours/AddMinutes/AddSeconds/AddMilliseconds take a double.
        EmitAddIntComponent(il, cur, moLocal, "AddMonths", subtractOne: false, asDouble: false);
        EmitAddIntComponent(il, cur, dLocal, "AddDays", subtractOne: true, asDouble: true);
        EmitAddIntComponent(il, cur, hLocal, "AddHours", subtractOne: false, asDouble: true);
        EmitAddIntComponent(il, cur, miLocal, "AddMinutes", subtractOne: false, asDouble: true);
        EmitAddIntComponent(il, cur, sLocal, "AddSeconds", subtractOne: false, asDouble: true);
        EmitAddIntComponent(il, cur, msLocal, "AddMilliseconds", subtractOne: false, asDouble: true);

        // result = (cur - UnixEpoch).TotalMilliseconds
        il.Emit(OpCodes.Ldloc, cur);
        il.Emit(OpCodes.Ldsfld, _tsDateUnixEpochField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "op_Subtraction", [_types.DateTime, _types.DateTime])!);
        var tsLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, tsLocal);
        il.Emit(OpCodes.Ldloca, tsLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMilliseconds")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, afterTry);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, afterTry);
        il.EndExceptionBlock();

        il.MarkLabel(afterTry);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Pushes <c>cur = cur.&lt;addMethod&gt;(component[- 1])</c> onto the in-progress UTC instant rebuild
    /// for <see cref="EmitTSDateUTCStatic"/>. <paramref name="subtractOne"/> turns the 1-indexed JS day
    /// into a day offset (AddDays(day - 1)). <paramref name="asDouble"/> converts the int component to a
    /// double for the Add* overloads that take a double (only AddMonths takes an int).
    /// </summary>
    private void EmitAddIntComponent(ILGenerator il, LocalBuilder cur, LocalBuilder component, string addMethod, bool subtractOne, bool asDouble)
    {
        il.Emit(OpCodes.Ldloca, cur);
        il.Emit(OpCodes.Ldloc, component);
        if (subtractOne)
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
        }
        if (asDouble)
            il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, addMethod)!);
        il.Emit(OpCodes.Stloc, cur);
    }

    /// <summary>
    /// Pushes the int value of Date.UTC argument <paramref name="index"/> from the object[] parameter:
    /// the truncated (int) of the boxed double when present, or <paramref name="defaultInt"/> when the
    /// argument was not supplied. A non-finite supplied value jumps to <paramref name="nanLabel"/>.
    /// </summary>
    private void PushUtcComponentInt(ILGenerator il, int index, int defaultInt, Label nanLabel, LocalBuilder tmp)
    {
        var present = il.DefineLabel();
        var done = il.DefineLabel();

        // if (index < args.Length) goto present;
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, present);
        // absent -> default
        il.Emit(OpCodes.Ldc_I4, defaultInt);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(present);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, tmp);
        il.Emit(OpCodes.Ldloc, tmp);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, nanLabel);
        il.Emit(OpCodes.Ldloc, tmp);
        il.Emit(OpCodes.Conv_I4);
        il.MarkLabel(done);
    }

    // ECMA-262 §21.4.3.2 (Date.parse): parse a date string to a timestamp (ms since epoch) or NaN.
    // Reuses the $TSDate string constructor + GetTime, mirroring SharpTSDate.Parse.
    private void EmitTSDateParseStatic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Parse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.TSDateParseStatic = method;

        var il = method.GetILGenerator();
        // return new $TSDate((string)s).GetTime();  — invalid strings yield an Invalid date => NaN.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Newobj, runtime.TSDateCtorString);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "op_Subtraction", [_types.DateTime, _types.DateTime])!);
        var tsLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, tsLocal);
        il.Emit(OpCodes.Ldloca, tsLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMilliseconds")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDateGetFullYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetFullYear", "Year");
    }

    private void EmitTSDateGetDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleDateGetter(typeBuilder, runtime, "GetDate", "Day");
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

    /// <summary>
    /// Emits a zero-argument $TSDate getter returning a DateTime component as a double.
    /// When <paramref name="utc"/> is true the stored UTC instant is read directly; otherwise
    /// it is converted to local time first. <paramref name="subtractAfter"/> offsets the result
    /// (e.g. 1 for the 0-indexed month, 1900 for the Annex B getYear).
    /// </summary>
    private void EmitSimpleDateGetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, string propertyName, bool utc = false, int subtractAfter = 0)
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
        if (utc)
        {
            // Read the UTC instant directly; the property getter is called on the field address.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        }
        else
        {
            var localTimeLocal = il.DeclareLocal(_types.DateTime);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
            il.Emit(OpCodes.Stloc, localTimeLocal);
            il.Emit(OpCodes.Ldloca, localTimeLocal);
        }
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DateTime, propertyName)!.GetGetMethod()!);
        if (subtractAfter != 0)
        {
            il.Emit(OpCodes.Ldc_I4, subtractAfter);
            il.Emit(OpCodes.Sub);
        }
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
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMinutes")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN")!);
        il.Emit(OpCodes.Brtrue, invalidLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity")!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMilliseconds")!);
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

    /// <summary>Identifies which component a <see cref="EmitDateComponentSetter"/> replaces.</summary>
    private enum DateComponent { Year, Month, Day, Hour, Minute, Second, Millisecond }

    /// <summary>
    /// Emits a $TSDate setter that replaces a contiguous run of date components (keeping the
    /// rest) and returns the new timestamp. <paramref name="settable"/> lists those components
    /// in canonical order: index 0 is the primary (always written); the remainder are the
    /// optional trailing components. A multi-component setter receives its arguments as an
    /// <c>object[]</c> whose length is the number of arguments actually supplied, so absent
    /// trailing components keep their current value (matching the interpreter — #536); a
    /// single-component setter takes a plain <c>double</c>.
    /// The instant is rebuilt with DateTime.Add* from a normalized base so overflowing
    /// components roll over per JavaScript semantics (e.g. setMonth(13) advances the year);
    /// an out-of-range result (e.g. a year beyond DateTime's domain) is caught and marks the
    /// date Invalid, mirroring <see cref="Runtime.Types.SharpTSDate"/>. When <paramref name="utc"/>
    /// is true the instant is read and written directly in UTC; otherwise it round-trips through
    /// local time.
    /// </summary>
    private void EmitDateComponentSetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, DateComponent[] settable, bool utc)
    {
        bool multi = settable.Length > 1;
        var method = typeBuilder.DefineMethod(methodName, MethodAttributes.Public, _types.Double,
            multi ? [_types.ObjectArray] : [_types.Double]);
        runtime.TSDateMethods[methodName] = method;

        var il = method.GetILGenerator();
        var computeLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var dtLocal = il.DeclareLocal(_types.DateTime);
        var cur = il.DeclareLocal(_types.DateTime);
        int kind = utc ? 1 : 2; // DateTimeKind.Utc = 1, Local = 2

        // if (_isInvalid) return GetTime(); // stays NaN
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, computeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(computeLabel);
        // dt = utc ? _utcDateTime : _utcDateTime.ToLocalTime();
        if (utc)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsDateUtcDateTimeField);
            il.Emit(OpCodes.Stloc, dtLocal);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
            il.Emit(OpCodes.Stloc, dtLocal);
        }

        il.BeginExceptionBlock();

        // cur = new DateTime(year, 1, 1, 0, 0, 0, kind)
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Year, "Year", 0);
        il.Emit(OpCodes.Ldc_I4_1); // month
        il.Emit(OpCodes.Ldc_I4_1); // day
        il.Emit(OpCodes.Ldc_I4_0); // hour
        il.Emit(OpCodes.Ldc_I4_0); // minute
        il.Emit(OpCodes.Ldc_I4_0); // second
        il.Emit(OpCodes.Ldc_I4, kind);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DateTime, [
            _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.Int32, _types.DateTimeKind
        ])!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddMonths(month0)   (month0 = 0-indexed month to add)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Month, "Month", 1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMonths")!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddDays(day - 1)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Day, "Day", 0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddDays")!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddHours(hour)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Hour, "Hour", 0);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddHours")!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddMinutes(minute)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Minute, "Minute", 0);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMinutes")!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddSeconds(second)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Second, "Second", 0);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddSeconds")!);
        il.Emit(OpCodes.Stloc, cur);

        // cur = cur.AddMilliseconds(millisecond)
        il.Emit(OpCodes.Ldloca, cur);
        PushDateComponentValue(il, dtLocal, settable, multi, DateComponent.Millisecond, "Millisecond", 0);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "AddMilliseconds")!);
        il.Emit(OpCodes.Stloc, cur);

        // _utcDateTime = utc ? cur : cur.ToUniversalTime();
        il.Emit(OpCodes.Ldarg_0);
        if (utc)
        {
            il.Emit(OpCodes.Ldloc, cur);
        }
        else
        {
            il.Emit(OpCodes.Ldloca, cur);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToUniversalTime")!);
        }
        il.Emit(OpCodes.Stfld, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Leave, endLabel);

        // catch { _isInvalid = true; }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Leave, endLabel);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Pushes the int value for one <paramref name="slot"/> of the DateTime rebuilt by
    /// <see cref="EmitDateComponentSetter"/>. If the slot is not in <paramref name="settable"/>
    /// it keeps the current component (read from <paramref name="dtLocal"/>, minus
    /// <paramref name="subtract"/> — e.g. 1 to convert .NET's 1-indexed month to 0-indexed).
    /// Otherwise it uses the matching setter argument (truncated toward zero, matching
    /// <c>(int)value</c> in SharpTSDate): the lone <c>double</c> parameter for a single-component
    /// setter, or — for a multi-component setter whose args arrive as an <c>object[]</c> — element
    /// k, where the primary (k == 0) is always present and an optional trailing component
    /// (k &gt; 0) is used only when <c>args.Length &gt; k</c>, otherwise the current value is kept.
    /// </summary>
    private void PushDateComponentValue(ILGenerator il, LocalBuilder dtLocal, DateComponent[] settable, bool multi, DateComponent slot, string propertyName, int subtract)
    {
        int k = Array.IndexOf(settable, slot);
        if (k < 0)
        {
            // Not written by this setter — keep the current component.
            PushCurrentDateComponent(il, dtLocal, propertyName, subtract);
            return;
        }

        if (!multi)
        {
            // Single-component setter: the lone double parameter (already 0-indexed where the
            // caller expects it, so no subtract is applied to a supplied argument).
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Conv_I4);
            return;
        }

        if (k == 0)
        {
            // Primary argument is always supplied (arity >= 1): (int)(double)args[0].
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Conv_I4);
            return;
        }

        // Optional trailing argument: args.Length > k ? (int)(double)args[k] : current.
        var useCurrent = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, k);
        il.Emit(OpCodes.Ble, useCurrent); // args.Length <= k → component not supplied
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, k);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(useCurrent);
        PushCurrentDateComponent(il, dtLocal, propertyName, subtract);
        il.MarkLabel(done);
    }

    /// <summary>Pushes the current value of a DateTime component (minus <paramref name="subtract"/>) as an int.</summary>
    private void PushCurrentDateComponent(ILGenerator il, LocalBuilder dtLocal, string propertyName, int subtract)
    {
        il.Emit(OpCodes.Ldloca, dtLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DateTime, propertyName)!.GetGetMethod()!);
        if (subtract != 0)
        {
            il.Emit(OpCodes.Ldc_I4, subtract);
            il.Emit(OpCodes.Sub);
        }
    }

    // ECMA-262 Annex B B.2.4.2: setYear maps 0-99 to 1900-1999, then delegates to SetFullYear.
    private void EmitTSDateSetYear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetYear", MethodAttributes.Public, _types.Double, [_types.Double]);
        runtime.TSDateMethods["SetYear"] = method;

        var il = method.GetILGenerator();
        var computeLabel = il.DefineLabel();
        var skipMapLabel = il.DefineLabel();
        var yLocal = il.DeclareLocal(_types.Int32);

        // if (_isInvalid) return GetTime();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, computeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsDateGetTimeMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(computeLabel);
        // int y = (int)year;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, yLocal);
        // if (y < 0 || y > 99) goto skipMap;
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, skipMapLabel);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4, 99);
        il.Emit(OpCodes.Bgt, skipMapLabel);
        // y += 1900;
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4, 1900);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, yLocal);

        il.MarkLabel(skipMapLabel);
        // return SetFullYear(new object[] { (double)y });  — SetFullYear takes the multi-arg
        // object[] form; a single element sets only the year, keeping month and day (#536).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSDateMethods["SetFullYear"]);
        il.Emit(OpCodes.Ret);
    }

    // RFC 7231 UTC string, e.g. "Thu, 01 Jan 1970 00:00:00 GMT".
    private void EmitTSDateToUTCString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("ToUTCString", MethodAttributes.Public, _types.String, Type.EmptyTypes);
        runtime.TSDateMethods["ToUTCString"] = method;

        var il = method.GetILGenerator();
        var validLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDateIsInvalidField);
        il.Emit(OpCodes.Brfalse, validLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Ldstr, "ddd, dd MMM yyyy HH:mm:ss 'GMT'");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    // toLocale* family: format in local time using the host's current culture (#516).
    private void EmitTSDateLocaleString(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName, string format)
    {
        var method = typeBuilder.DefineMethod(methodName, MethodAttributes.Public, _types.String, Type.EmptyTypes);
        runtime.TSDateMethods[methodName] = method;

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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);
        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, format);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("CurrentCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "ddd MMM dd yyyy HH:mm:ss");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDateUtcDateTimeField);
        il.Emit(OpCodes.Ldstr, "yyyy-MM-ddTHH:mm:ss.fffZ");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "ddd MMM dd yyyy");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
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
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToLocalTime")!);
        il.Emit(OpCodes.Stloc, localTimeLocal);

        il.Emit(OpCodes.Ldloca, localTimeLocal);
        il.Emit(OpCodes.Ldstr, "HH:mm:ss");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String, typeof(IFormatProvider)])!);
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
