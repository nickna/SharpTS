using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Date-related runtime emission methods.
/// Uses the emitted $TSDate class for standalone support.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly record struct DateMethodsInputs(ConstructorBuilder NonConstructibleAttrCtor, EmittedTimerRuntime Timers);

    private readonly record struct DateNowInputs(ConstructorBuilder NonConstructibleAttrCtor, EmittedTimerRuntime Timers);

    private void EmitDateMethods(TypeBuilder typeBuilder, EmittedDateImplementation date, DateMethodsInputs inputs)
    {
        EmitDateNow(typeBuilder, date, new DateNowInputs(inputs.NonConstructibleAttrCtor, inputs.Timers));
        EmitCreateDateNoArgs(typeBuilder, date);
        EmitCreateDateFromValue(typeBuilder, date);
        EmitCreateDateFromComponents(typeBuilder, date);
        EmitDateToString(typeBuilder, date);
        EmitDateGetTime(typeBuilder, date);
        EmitDateGetFullYear(typeBuilder, date);
        EmitDateGetMonth(typeBuilder, date);
        EmitDateGetDate(typeBuilder, date);
        EmitDateGetDay(typeBuilder, date);
        EmitDateGetHours(typeBuilder, date);
        EmitDateGetMinutes(typeBuilder, date);
        EmitDateGetSeconds(typeBuilder, date);
        EmitDateGetMilliseconds(typeBuilder, date);
        EmitDateGetTimezoneOffset(typeBuilder, date);
        EmitDateSetTime(typeBuilder, date);
        // Multi-argument setters package args as object[] and honor every supplied
        // component (#536); single-argument setters take a direct double.
        date.SetFullYear = EmitDateArgsArraySetter(typeBuilder, date, "DateSetFullYear", "SetFullYear");
        date.SetMonth = EmitDateArgsArraySetter(typeBuilder, date, "DateSetMonth", "SetMonth");
        EmitDateSetDate(typeBuilder, date);
        date.SetHours = EmitDateArgsArraySetter(typeBuilder, date, "DateSetHours", "SetHours");
        date.SetMinutes = EmitDateArgsArraySetter(typeBuilder, date, "DateSetMinutes", "SetMinutes");
        date.SetSeconds = EmitDateArgsArraySetter(typeBuilder, date, "DateSetSeconds", "SetSeconds");
        EmitDateSetMilliseconds(typeBuilder, date);
        EmitDateToISOString(typeBuilder, date);
        EmitDateToDateString(typeBuilder, date);
        EmitDateToTimeString(typeBuilder, date);
        EmitDateToJSON(typeBuilder, date);
        EmitDateValueOf(typeBuilder, date);

        // UTC getters + legacy getYear (#516): 0-arg, return double, NaN on non-Date.
        date.GetUTCFullYear = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCFullYear", "GetUTCFullYear");
        date.GetUTCMonth = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCMonth", "GetUTCMonth");
        date.GetUTCDate = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCDate", "GetUTCDate");
        date.GetUTCDay = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCDay", "GetUTCDay");
        date.GetUTCHours = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCHours", "GetUTCHours");
        date.GetUTCMinutes = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCMinutes", "GetUTCMinutes");
        date.GetUTCSeconds = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCSeconds", "GetUTCSeconds");
        date.GetUTCMilliseconds = EmitDateDoubleGetter(typeBuilder, date, "DateGetUTCMilliseconds", "GetUTCMilliseconds");
        date.GetYear = EmitDateDoubleGetter(typeBuilder, date, "DateGetYear", "GetYear");

        // UTC setters (#516). Multi-arg setters package args as object[] (primary arg read);
        // single-arg setters take a direct double. Legacy setYear takes a direct double.
        date.SetUTCFullYear = EmitDateArgsArraySetter(typeBuilder, date, "DateSetUTCFullYear", "SetUTCFullYear");
        date.SetUTCMonth = EmitDateArgsArraySetter(typeBuilder, date, "DateSetUTCMonth", "SetUTCMonth");
        date.SetUTCDate = EmitDateDoubleArgSetter(typeBuilder, date, "DateSetUTCDate", "SetUTCDate");
        date.SetUTCHours = EmitDateArgsArraySetter(typeBuilder, date, "DateSetUTCHours", "SetUTCHours");
        date.SetUTCMinutes = EmitDateArgsArraySetter(typeBuilder, date, "DateSetUTCMinutes", "SetUTCMinutes");
        date.SetUTCSeconds = EmitDateArgsArraySetter(typeBuilder, date, "DateSetUTCSeconds", "SetUTCSeconds");
        date.SetUTCMilliseconds = EmitDateDoubleArgSetter(typeBuilder, date, "DateSetUTCMilliseconds", "SetUTCMilliseconds");
        date.SetYear = EmitDateDoubleArgSetter(typeBuilder, date, "DateSetYear", "SetYear");

        // Conversion methods (#516): return string, "Invalid Date" on non-Date.
        date.ToUTCString = EmitDateStringMethod(typeBuilder, date, "DateToUTCString", "ToUTCString");
        date.ToLocaleDateString = EmitDateStringMethod(typeBuilder, date, "DateToLocaleDateString", "ToLocaleDateString");
        date.ToLocaleTimeString = EmitDateStringMethod(typeBuilder, date, "DateToLocaleTimeString", "ToLocaleTimeString");
        date.ToLocaleString = EmitDateStringMethod(typeBuilder, date, "DateToLocaleString", "ToLocaleString");
        // toLocale* with locale/options (#538-family follow-up #539). Emitted unconditionally but
        // only reached by toLocale* calls that pass arguments — those call sites record the soft
        // SharpTS dependency, so argument-less toLocale* programs stay standalone.
        EmitDateToLocaleWithOptions(typeBuilder, date);
    }

    /// <summary>
    /// Emits <c>$Runtime.DateToLocaleWithOptions(object receiver, int kind, object[] args) → string</c>,
    /// which reflects to <c>RuntimeTypes.FormatDateToLocale</c> so the locale/options-aware formatting
    /// lives in SharpTS (a soft dependency). <paramref name="kind"/> is 0/1/2 = date/time/both.
    /// </summary>
    private void EmitDateToLocaleWithOptions(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToLocaleWithOptions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.ObjectArray]
        );
        date.ToLocaleWithOptions = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();
        var epochMs = il.DeclareLocal(_types.Double);
        var localeLoc = il.DeclareLocal(_types.Object);
        var optionsLoc = il.DeclareLocal(_types.Object);

        // Non-$TSDate receiver -> "Invalid Date" (unreachable for type-checked code).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // epochMs = ((($TSDate)receiver).GetTime();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("GetTime"));
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
    private MethodBuilder EmitDateDoubleGetter(
        TypeBuilder typeBuilder,
        EmittedDateImplementation date,
        string runtimeName,
        string instanceMethodName
    )
    {
        var method = typeBuilder.DefineMethod(
            runtimeName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        EmitDateInstanceMethodCall(typeBuilder, date, runtimeName, instanceMethodName, method);
        return method;
    }

    /// <summary>
    /// Emits a static $Runtime helper for a $TSDate setter taking a single double argument
    /// (NaN when the receiver is not a $TSDate). Returns the emitted method.
    /// </summary>
    private MethodBuilder EmitDateDoubleArgSetter(
        TypeBuilder typeBuilder,
        EmittedDateImplementation date,
        string runtimeName,
        string instanceMethodName
    )
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
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod(instanceMethodName));
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
    private MethodBuilder EmitDateArgsArraySetter(
        TypeBuilder typeBuilder,
        EmittedDateImplementation date,
        string runtimeName,
        string instanceMethodName
    )
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
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Ldarg_1); // the full object[] — $TSDate reads each supplied component
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod(instanceMethodName));
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
    private MethodBuilder EmitDateStringMethod(
        TypeBuilder typeBuilder,
        EmittedDateImplementation date,
        string runtimeName,
        string instanceMethodName
    )
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
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod(instanceMethodName));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitDateNow(TypeBuilder typeBuilder, EmittedDateImplementation date, DateNowInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "DateNow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            _types.EmptyTypes
        );
        date.Now = method;
        method.SetCustomAttribute(
            inputs.NonConstructibleAttrCtor, CustomAttributeEncoder.EmptyBlob);

        var il = method.GetILGenerator();
        // Process any pending virtual timers before returning
        // This implements JavaScript-like single-threaded timer semantics
        il.Emit(OpCodes.Call, inputs.Timers.ProcessPendingTimers);
        il.Emit(OpCodes.Pop); // discard int return (next timer delay)
        // Call $TSDate.Now() static method
        il.Emit(OpCodes.Call, date.StaticNow);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateNoArgs(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateNoArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        date.CreateNoArgs = method;

        var il = method.GetILGenerator();
        // new $TSDate()
        il.Emit(OpCodes.Newobj, date.NoArgsConstructor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateFromValue(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateFromValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        date.CreateFromValue = method;

        var il = method.GetILGenerator();
        var dateLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check if value is double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, dateLabel);

        // Double case: new $TSDate((double)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Newobj, date.MillisecondsConstructor);
        il.Emit(OpCodes.Ret);

        // Date(value) copies the [[DateValue]] directly. It must not run the
        // source object's overridable valueOf/toString coercion hooks.
        il.MarkLabel(dateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("GetTime"));
        il.Emit(OpCodes.Newobj, date.MillisecondsConstructor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        // Check if value is string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        // String case: new $TSDate((string)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Newobj, date.StringConstructor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        // Default: new $TSDate()
        il.Emit(OpCodes.Newobj, date.NoArgsConstructor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateDateFromComponents(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "CreateDateFromComponents",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Double, _types.Double, _types.Double, _types.Double, _types.Double, _types.Double]
        );
        date.CreateFromComponents = method;

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
        il.Emit(OpCodes.Newobj, date.ComponentsConstructor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToString(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        date.ToStringMethod = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        // Check if date is $TSDate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call date.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateInstanceMethodCall(
        TypeBuilder typeBuilder,
        EmittedDateImplementation date,
        string helperName,
        string instanceMethodName,
        MethodBuilder targetMethod
    )
    {
        var il = targetMethod.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        // Check if date is $TSDate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call date.Method()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod(instanceMethodName));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateGetTime(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetTime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetTime = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetTime", "GetTime", method);
    }

    private void EmitDateGetFullYear(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetFullYear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetFullYear = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetFullYear", "GetFullYear", method);
    }

    private void EmitDateGetMonth(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMonth",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetMonth = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetMonth", "GetMonth", method);
    }

    private void EmitDateGetDate(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetDate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetDate = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetDate", "GetDate", method);
    }

    private void EmitDateGetDay(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetDay",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetDay = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetDay", "GetDay", method);
    }

    private void EmitDateGetHours(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetHours",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetHours = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetHours", "GetHours", method);
    }

    private void EmitDateGetMinutes(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMinutes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetMinutes = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetMinutes", "GetMinutes", method);
    }

    private void EmitDateGetSeconds(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetSeconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetSeconds = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetSeconds", "GetSeconds", method);
    }

    private void EmitDateGetMilliseconds(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetMilliseconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetMilliseconds = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetMilliseconds", "GetMilliseconds", method);
    }

    private void EmitDateGetTimezoneOffset(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateGetTimezoneOffset",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.GetTimezoneOffset = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateGetTimezoneOffset", "GetTimezoneOffset", method);
    }

    private void EmitDateSetTime(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetTime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        date.SetTime = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("SetTime"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetDate(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetDate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        date.SetDate = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetDate with arg1 (direct double parameter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("SetDate"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateSetMilliseconds(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateSetMilliseconds",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double]
        );
        date.SetMilliseconds = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // Call SetMilliseconds with arg1 (direct double parameter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("SetMilliseconds"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToISOString(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToISOString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        date.ToISOString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("ToISOString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid Date");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitDateToDateString(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToDateString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        date.ToDateString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("ToDateString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateToTimeString(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToTimeString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        date.ToTimeString = method;

        var il = method.GetILGenerator();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("ToTimeString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid Date");
        il.Emit(OpCodes.Ret);
    }

    // ECMA-262 §21.4.4.37: toJSON returns the ISO string, or null for a non-finite (Invalid)
    // date. Returns object (string | null), mirroring DateBuiltIns' interpreted implementation.
    private void EmitDateToJSON(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateToJSON",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        date.ToJSON = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Non-Date receiver → null (lenient; unreachable when the type checker is satisfied).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, date.Type);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Invalid (NaN timestamp) → null, before ToISOString's RangeError throw can be reached.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("GetTime"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN")!);
        il.Emit(OpCodes.Brtrue, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, date.Type);
        il.Emit(OpCodes.Callvirt, date.GetInstanceMethod("ToISOString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDateValueOf(TypeBuilder typeBuilder, EmittedDateImplementation date)
    {
        var method = typeBuilder.DefineMethod(
            "DateValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        date.ValueOf = method;
        EmitDateInstanceMethodCall(typeBuilder, date, "DateValueOf", "ValueOf", method);
    }
}
