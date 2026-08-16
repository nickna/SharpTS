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
        // Multi-argument setters package args as object[] and honor every supplied
        // component (#536); single-argument setters take a direct double.
        runtime.DateSetFullYear = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetFullYear", "SetFullYear");
        runtime.DateSetMonth = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetMonth", "SetMonth");
        EmitDateSetDate(typeBuilder, runtime);
        runtime.DateSetHours = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetHours", "SetHours");
        runtime.DateSetMinutes = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetMinutes", "SetMinutes");
        runtime.DateSetSeconds = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetSeconds", "SetSeconds");
        EmitDateSetMilliseconds(typeBuilder, runtime);
        EmitDateToISOString(typeBuilder, runtime);
        EmitDateToDateString(typeBuilder, runtime);
        EmitDateToTimeString(typeBuilder, runtime);
        EmitDateToJSON(typeBuilder, runtime);
        EmitDateValueOf(typeBuilder, runtime);

        // UTC getters + legacy getYear (#516): 0-arg, return double, NaN on non-Date.
        runtime.DateGetUTCFullYear = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCFullYear", "GetUTCFullYear");
        runtime.DateGetUTCMonth = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCMonth", "GetUTCMonth");
        runtime.DateGetUTCDate = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCDate", "GetUTCDate");
        runtime.DateGetUTCDay = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCDay", "GetUTCDay");
        runtime.DateGetUTCHours = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCHours", "GetUTCHours");
        runtime.DateGetUTCMinutes = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCMinutes", "GetUTCMinutes");
        runtime.DateGetUTCSeconds = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCSeconds", "GetUTCSeconds");
        runtime.DateGetUTCMilliseconds = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetUTCMilliseconds", "GetUTCMilliseconds");
        runtime.DateGetYear = EmitDateDoubleGetter(typeBuilder, runtime, "DateGetYear", "GetYear");

        // UTC setters (#516). Multi-arg setters package args as object[] (primary arg read);
        // single-arg setters take a direct double. Legacy setYear takes a direct double.
        runtime.DateSetUTCFullYear = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetUTCFullYear", "SetUTCFullYear");
        runtime.DateSetUTCMonth = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetUTCMonth", "SetUTCMonth");
        runtime.DateSetUTCDate = EmitDateDoubleArgSetter(typeBuilder, runtime, "DateSetUTCDate", "SetUTCDate");
        runtime.DateSetUTCHours = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetUTCHours", "SetUTCHours");
        runtime.DateSetUTCMinutes = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetUTCMinutes", "SetUTCMinutes");
        runtime.DateSetUTCSeconds = EmitDateArgsArraySetter(typeBuilder, runtime, "DateSetUTCSeconds", "SetUTCSeconds");
        runtime.DateSetUTCMilliseconds = EmitDateDoubleArgSetter(typeBuilder, runtime, "DateSetUTCMilliseconds", "SetUTCMilliseconds");
        runtime.DateSetYear = EmitDateDoubleArgSetter(typeBuilder, runtime, "DateSetYear", "SetYear");

        // Conversion methods (#516): return string, "Invalid Date" on non-Date.
        runtime.DateToUTCString = EmitDateStringMethod(typeBuilder, runtime, "DateToUTCString", "ToUTCString");
        runtime.DateToLocaleDateString = EmitDateStringMethod(typeBuilder, runtime, "DateToLocaleDateString", "ToLocaleDateString");
        runtime.DateToLocaleTimeString = EmitDateStringMethod(typeBuilder, runtime, "DateToLocaleTimeString", "ToLocaleTimeString");
        runtime.DateToLocaleString = EmitDateStringMethod(typeBuilder, runtime, "DateToLocaleString", "ToLocaleString");
        // toLocale* with locale/options (#538-family follow-up #539). Emitted unconditionally but
        // only reached by toLocale* calls that pass arguments — those call sites record the soft
        // SharpTS dependency, so argument-less toLocale* programs stay standalone.
        EmitDateToLocaleWithOptions(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits <c>$Runtime.DateToLocaleWithOptions(object receiver, int kind, object[] args) → string</c>,
    /// which reflects to <c>RuntimeTypes.FormatDateToLocale</c> so the locale/options-aware formatting
    /// lives in SharpTS (a soft dependency). <paramref name="kind"/> is 0/1/2 = date/time/both.
    /// </summary>
    private void EmitDateToLocaleWithOptions(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToLocaleWithOptions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.ObjectArray]
        );
        runtime.DateToLocaleWithOptions = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var epochMs = il.DeclareLocal(_types.Double);
        var localeLoc = il.DeclareLocal(_types.Object);
        var optionsLoc = il.DeclareLocal(_types.Object);

        // Non-$TSDate receiver -> "Invalid Date" (unreachable for type-checked code).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // epochMs = ((($TSDate)receiver).GetTime();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["GetTime"]);
        il.Emit(OpCodes.Stloc, epochMs);

        // locale = args.Length > 0 ? args[0] : null;  options = args.Length > 1 ? args[1] : null;
        EmitArgOrNull(il, 0, localeLoc);
        EmitArgOrNull(il, 1, optionsLoc);

        // return (string)RuntimeTypes.FormatDateToLocale(epochMs, kind, locale, options);  (reflected)
        EmitReflectionCall(il, RuntimeTypesLateBoundName, "FormatDateToLocale", 4, emitArg: i =>
        {
            switch (i)
            {
                case 0: il.Emit(OpCodes.Ldloc, epochMs); il.Emit(OpCodes.Box, _types.Double); break;
                case 1: il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Box, _types.Int32); break; // kind
                case 2: il.Emit(OpCodes.Ldloc, localeLoc); break;
                case 3: il.Emit(OpCodes.Ldloc, optionsLoc); break;
            }
        });
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Stores <c>args.Length &gt; index ? args[index] : null</c> (args = arg2) into <paramref name="target"/>.</summary>
    private void EmitArgOrNull(ILGenerator il, int index, LocalBuilder target)
    {
        var has = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Bgt, has); // args.Length > index
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(has);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(done);
        il.Emit(OpCodes.Stloc, target);
    }

    /// <summary>
    /// Emits a static $Runtime helper for a 0-arg $TSDate getter returning a double
    /// (NaN when the receiver is not a $TSDate). Returns the emitted method.
    /// </summary>
    private MethodBuilder EmitDateDoubleGetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, string instanceMethodName)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        EmitDateInstanceMethodCall(typeBuilder, runtime, runtimeName, instanceMethodName, method);
        return method;
    }

    /// <summary>
    /// Emits a static $Runtime helper for a $TSDate setter taking a single double argument
    /// (NaN when the receiver is not a $TSDate). Returns the emitted method.
    /// </summary>
    private MethodBuilder EmitDateDoubleArgSetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, string instanceMethodName)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods[instanceMethodName]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits a static $Runtime helper for a multi-argument $TSDate setter. The arguments are
    /// passed through as the object[] supplied at the call site (its length tells $TSDate how
    /// many optional trailing components were provided — #536), so all supplied components are
    /// honored. Returns NaN when the receiver is not a $TSDate. Returns the emitted method.
    /// </summary>
    private MethodBuilder EmitDateArgsArraySetter(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, string instanceMethodName)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Ldarg_1); // the full object[] — $TSDate reads each supplied component
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods[instanceMethodName]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits a static $Runtime helper for a 0-arg $TSDate conversion method returning a string
    /// ("Invalid Date" when the receiver is not a $TSDate). Returns the emitted method.
    /// </summary>
    private MethodBuilder EmitDateStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime, string runtimeName, string instanceMethodName)
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods[instanceMethodName]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
        return method;
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
        il.Emit(OpCodes.Pop); // discard int return (next timer delay)
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
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

    // ECMA-262 §21.4.4.37: toJSON returns the ISO string, or null for a non-finite (Invalid)
    // date. Returns object (string | null), mirroring DateBuiltIns' interpreted implementation.
    private void EmitDateToJSON(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DateToJSON",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.DateToJSON = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Non-Date receiver → null (lenient; unreachable when the type checker is satisfied).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Invalid (NaN timestamp) → null, before ToISOString's RangeError throw can be reached.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["GetTime"]);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN")!);
        il.Emit(OpCodes.Brtrue, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSDateType);
        il.Emit(OpCodes.Callvirt, runtime.TSDateMethods["ToISOString"]);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
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
