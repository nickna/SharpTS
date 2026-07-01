using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits util module helper methods into $Runtime for standalone execution.
    /// </summary>
    private void EmitUtilMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit util.types.* methods
        EmitUtilTypesIsArray(typeBuilder, runtime);
        EmitUtilTypesIsFunction(typeBuilder, runtime);
        EmitUtilTypesIsNull(typeBuilder, runtime);
        EmitUtilTypesIsUndefined(typeBuilder, runtime);
        EmitUtilTypesIsDate(typeBuilder, runtime);
        EmitUtilTypesIsPromise(typeBuilder, runtime);
        EmitUtilTypesIsRegExp(typeBuilder, runtime);
        EmitUtilTypesIsMap(typeBuilder, runtime);
        EmitUtilTypesIsSet(typeBuilder, runtime);
        EmitUtilTypesIsTypedArray(typeBuilder, runtime);
        EmitUtilTypesIsNativeError(typeBuilder, runtime);
        EmitUtilTypesIsBoxedPrimitive(typeBuilder, runtime);
        EmitUtilTypesIsWeakMap(typeBuilder, runtime);
        EmitUtilTypesIsWeakSet(typeBuilder, runtime);
        EmitUtilTypesIsArrayBuffer(typeBuilder, runtime);

        // util.deprecate / callbackify / promisify reference $DeprecatedFunction,
        // $CallbackifiedFunction, $PromisifiedFunction respectively. Skip together
        // with the type emission when UsesUtilPromisify is off.
        if (_features.UsesUtilPromisify)
        {
            // Emit util.deprecate
            EmitUtilDeprecate(typeBuilder, runtime);

            // Emit util.callbackify
            EmitUtilCallbackify(typeBuilder, runtime);

            // Emit util.promisify
            EmitUtilPromisify(typeBuilder, runtime);
        }

        // util.inherits / toUSVString / stripVTControlCharacters / getSystemError*
        // are user-facing utility functions — gated on UsesUtilFormat.
        if (_features.UsesUtilFormat)
        {
            EmitUtilInherits(typeBuilder, runtime);
            EmitUtilToUSVString(typeBuilder, runtime);
            EmitUtilStripVTControlCharacters(typeBuilder, runtime);
            EmitUtilGetSystemErrorName(typeBuilder, runtime);
            EmitUtilGetSystemErrorMap(typeBuilder, runtime);
        }

        // Define method signatures for format, inspect, isDeepStrictEqual, parseArgs.
        // UtilInspect is used by console.dir (always-on), so it's still defined
        // regardless of UsesUtilFormat — its body comes from EmitUtilStandaloneMethods
        // which is also always emitted (the inspect family is the only mandatory part).
        runtime.UtilFormat = typeBuilder.DefineMethod(
            "UtilFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]);

        runtime.UtilInspect = typeBuilder.DefineMethod(
            "UtilInspect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]);

        if (_features.UsesUtilFormat)
        {
            runtime.UtilIsDeepStrictEqual = typeBuilder.DefineMethod(
                "UtilIsDeepStrictEqual",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Boolean,
                [_types.Object, _types.Object]);

            runtime.UtilParseArgs = typeBuilder.DefineMethod(
                "UtilParseArgs",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]);
        }

        // Emit standalone helper method bodies. Some pieces (UtilInspect*) feed
        // console.dir and are always required; others (UtilFormat / parseArgs /
        // IsDeepStrictEqual) are gated inside that method on UsesUtilFormat.
        EmitUtilStandaloneMethods(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsArray(object value)
    /// </summary>
    private void EmitUtilTypesIsArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for IList<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for $Array
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsFunction(object value)
    /// </summary>
    private void EmitUtilTypesIsFunction(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsFunction",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsFunction = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for Delegate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for $TSFunction
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for $BoundTSFunction
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsNull(object value)
    /// </summary>
    private void EmitUtilTypesIsNull(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsNull",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsNull = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsUndefined(object value)
    /// </summary>
    private void EmitUtilTypesIsUndefined(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsUndefined",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsUndefined = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null first — null is NOT undefined in JS
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for $Undefined
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsDate(object value)
    /// </summary>
    private void EmitUtilTypesIsDate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsDate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsDate = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for DateTime
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(DateTime));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for $TSDate (only if UsesDate gated on)
        if (_features.UsesDate)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsPromise(object value)
    /// </summary>
    private void EmitUtilTypesIsPromise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsPromise",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsPromise = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for $Promise
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for Task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Threading.Tasks.Task));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsRegExp(object value)
    /// </summary>
    private void EmitUtilTypesIsRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsRegExp = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for Regex
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Text.RegularExpressions.Regex));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for $RegExp (only if UsesRegExp gated on)
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsMap(object value)
    /// Uses reflection to check for generic Dictionary type.
    /// </summary>
    private void EmitUtilTypesIsMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsMap = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var checkGenericLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for Dictionary<object, object?> (direct check — the compiled Map representation)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for generic Dictionary<,> via reflection, but EXCLUDE Dictionary<string,object?>
        // which is used for object literals, not Maps
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ldtoken, typeof(Dictionary<,>));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", _types.Type, _types.Type));
        il.Emit(OpCodes.Brfalse, falseLabel);

        // It's a generic Dictionary — but exclude Dictionary<string,...> (object literals)
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetGenericArguments"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", _types.Type, _types.Type));
        il.Emit(OpCodes.Brtrue, falseLabel); // string-keyed = object literal, not a Map

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsSet(object value)
    /// Uses reflection to check for generic HashSet type.
    /// </summary>
    private void EmitUtilTypesIsSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsSet = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for HashSet<object> (direct check)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(HashSet<object>));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for generic HashSet<> via reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ldtoken, typeof(HashSet<>));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", _types.Type, _types.Type));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsTypedArray(object value)
    /// </summary>
    private void EmitUtilTypesIsTypedArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsTypedArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsTypedArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for $Buffer (only when emitted; without UsesBuffer the type
        // can't appear and Isinst on a null token would NRE).
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsNativeError(object value)
    /// </summary>
    private void EmitUtilTypesIsNativeError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsNativeError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsNativeError = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for Exception
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Exception));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsBoxedPrimitive(object value)
    /// Always returns false in SharpTS since we don't have boxed primitive types.
    /// </summary>
    private void EmitUtilTypesIsBoxedPrimitive(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsBoxedPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsBoxedPrimitive = method;

        var il = method.GetILGenerator();
        // Always return false - we don't have explicit boxed primitive types
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsWeakMap(object value)
    /// </summary>
    private void EmitUtilTypesIsWeakMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsWeakMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsWeakMap = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for System.Runtime.CompilerServices.ConditionalWeakTable (underlying WeakMap implementation)
        // In compiled mode, WeakMap is backed by ConditionalWeakTable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);

        // Check type name contains "WeakMap" (for interpreter SharpTSWeakMap)
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "WeakMap");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for ConditionalWeakTable generic type
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ldtoken, typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", _types.Type, _types.Type));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsWeakSet(object value)
    /// </summary>
    private void EmitUtilTypesIsWeakSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsWeakSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsWeakSet = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check type name contains "WeakSet" (for interpreter SharpTSWeakSet)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "WeakSet");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for ConditionalWeakTable (compiled WeakSet uses ConditionalWeakTable<object, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        var wsTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, wsTypeLocal);

        il.Emit(OpCodes.Ldloc, wsTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        il.Emit(OpCodes.Ldloc, wsTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ldtoken, typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", _types.Type, _types.Type));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilTypesIsArrayBuffer(object value)
    /// </summary>
    private void EmitUtilTypesIsArrayBuffer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilTypesIsArrayBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.UtilTypesIsArrayBuffer = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check for $Buffer (which backs ArrayBuffer in SharpTS) — gated.
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        // Check for byte[]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(byte[]));
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilStripVTControlCharacters(object value)
    /// Strips ANSI escape codes from the input string.
    /// Pure IL emission - no SharpTS.dll dependency.
    /// </summary>
    private void EmitUtilStripVTControlCharacters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilStripVTControlCharacters",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.UtilStripVTControlCharacters = method;

        var il = method.GetILGenerator();

        // var input = value?.ToString() ?? ""
        var inputLocal = il.DeclareLocal(_types.String);
        var returnEmptyLabel = il.DefineLabel();
        var haveStringLabel = il.DefineLabel();

        // if (value == null) return ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // input = value.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveStringLabel);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(haveStringLabel);
        il.Emit(OpCodes.Stloc, inputLocal);

        // return Regex.Replace(input, pattern, "")
        // Pattern matches ANSI escape sequences:
        // \x1b\[[0-9;]*[a-zA-Z] - CSI sequences
        // \x1b\][^\x07]*\x07 - OSC sequences
        // \x1b[PX^_][^\x1b]*\x1b\\ - DCS/SOS/PM/APC sequences
        // \x1b\[[0-9;]*m - SGR sequences
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldstr, @"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[PX^_][^\x1b]*\x1b\\|\x1b\[[0-9;]*m");
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Regex, "Replace", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilGetSystemErrorName(object errno)
    /// Returns the POSIX error name for the given error code.
    /// Pure IL emission - no SharpTS.dll dependency.
    /// </summary>
    private void EmitUtilGetSystemErrorName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilGetSystemErrorName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.UtilGetSystemErrorName = method;

        var il = method.GetILGenerator();

        var errorCodeLocal = il.DeclareLocal(_types.Int32);
        var notDoubleLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // if (errno is not double d) throw new Exception("The value of \"err\" is out of range")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // var errorCode = (int)(double)errno
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, errorCodeLocal);

        // POSIX error codes and their names (emit as if-else chain for clarity)
        // Using negative values as per Node.js libuv convention
        var errorCodes = new (int code, string name)[]
        {
            (-1, "EPERM"), (-2, "ENOENT"), (-3, "ESRCH"), (-4, "EINTR"), (-5, "EIO"),
            (-6, "ENXIO"), (-7, "E2BIG"), (-8, "ENOEXEC"), (-9, "EBADF"), (-10, "ECHILD"),
            (-11, "EAGAIN"), (-12, "ENOMEM"), (-13, "EACCES"), (-14, "EFAULT"), (-16, "EBUSY"),
            (-17, "EEXIST"), (-18, "EXDEV"), (-19, "ENODEV"), (-20, "ENOTDIR"), (-21, "EISDIR"),
            (-22, "EINVAL"), (-23, "ENFILE"), (-24, "EMFILE"), (-25, "ENOTTY"), (-26, "ETXTBSY"),
            (-27, "EFBIG"), (-28, "ENOSPC"), (-29, "ESPIPE"), (-30, "EROFS"), (-31, "EMLINK"),
            (-32, "EPIPE"), (-33, "EDOM"), (-34, "ERANGE"), (-35, "EDEADLK"), (-36, "ENAMETOOLONG"),
            (-37, "ENOLCK"), (-38, "ENOSYS"), (-39, "ENOTEMPTY"), (-40, "ELOOP"), (-42, "ENOMSG"),
            (-43, "EIDRM"), (-60, "ENOSTR"), (-62, "ETIME"), (-63, "ENOSR"), (-71, "EPROTO"),
            (-74, "EMULTIHOP"), (-84, "EOVERFLOW"), (-88, "EILSEQ"), (-89, "ENOTSOCK"),
            (-90, "EDESTADDRREQ"), (-91, "EMSGSIZE"), (-92, "EPROTOTYPE"), (-93, "ENOPROTOOPT"),
            (-94, "EPROTONOSUPPORT"), (-96, "EOPNOTSUPP"), (-97, "EAFNOSUPPORT"), (-98, "EADDRINUSE"),
            (-99, "EADDRNOTAVAIL"), (-100, "ENETDOWN"), (-101, "ENETUNREACH"), (-102, "ENETRESET"),
            (-103, "ECONNABORTED"), (-104, "ECONNRESET"), (-105, "ENOBUFS"), (-106, "EISCONN"),
            (-107, "ENOTCONN"), (-110, "ETIMEDOUT"), (-111, "ECONNREFUSED"), (-113, "EHOSTUNREACH"),
            (-114, "EALREADY"), (-115, "EINPROGRESS"), (-122, "EDQUOT"), (-125, "ECANCELED"),
            (-4094, "ENOTRECOVERABLE"), (-4095, "EOWNERDEAD")
        };

        foreach (var (code, name) in errorCodes)
        {
            var nextLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, errorCodeLocal);
            il.Emit(OpCodes.Ldc_I4, code);
            il.Emit(OpCodes.Bne_Un, nextLabel);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(nextLabel);
        }

        // Default: return "Unknown system error {code}"
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "Unknown system error ");
        il.Emit(OpCodes.Ldloca, errorCodeLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // throw new Exception("The value of \"err\" is out of range")
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldstr, "The value of \"err\" is out of range");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static object UtilGetSystemErrorMap()
    /// Returns a Map of error codes to [name, description] tuples.
    /// Calls RuntimeTypes.CreateSystemErrorMap() which builds the dictionary properly.
    /// </summary>
    private void EmitUtilGetSystemErrorMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilGetSystemErrorMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.UtilGetSystemErrorMap = method;

        var il = method.GetILGenerator();

        // Create the error map directly instead of using reflection
        // var map = new Dictionary<object, object?>()
        var mapLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryObjectObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, mapLocal);

        // Helper to add an entry: map[(double)code] = new List<object?> { name, message }
        void AddErrorEntry(double code, string name, string message)
        {
            // map[(double)code] = ...
            il.Emit(OpCodes.Ldloc, mapLocal);
            il.Emit(OpCodes.Ldc_R8, code);
            il.Emit(OpCodes.Box, _types.Double);

            // new List<object?> { name, message }
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, message);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            // map[key] = value
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        }

        // Add all error codes
        AddErrorEntry(-2, "ENOENT", "no such file or directory");
        AddErrorEntry(-1, "EPERM", "operation not permitted");
        AddErrorEntry(-13, "EACCES", "permission denied");
        AddErrorEntry(-17, "EEXIST", "file already exists");
        AddErrorEntry(-22, "EINVAL", "invalid argument");
        AddErrorEntry(-28, "ENOSPC", "no space left on device");
        AddErrorEntry(-39, "ENOTEMPTY", "directory not empty");
        AddErrorEntry(-110, "ETIMEDOUT", "connection timed out");
        AddErrorEntry(-111, "ECONNREFUSED", "connection refused");

        // return map
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $DeprecatedFunction UtilDeprecate(object fn, string message)
    /// </summary>
    private void EmitUtilDeprecate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilDeprecate",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSDeprecatedFunctionType,
            [_types.Object, _types.String]);
        runtime.UtilDeprecate = method;

        var il = method.GetILGenerator();
        // return new $DeprecatedFunction(fn, message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSDeprecatedFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object UtilCallbackify(object fn)
    /// Returns a $CallbackifiedFunction that wraps fn and invokes a callback with (err, result).
    /// </summary>
    private void EmitUtilCallbackify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilCallbackify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.UtilCallbackify = method;

        var il = method.GetILGenerator();
        // return new $CallbackifiedFunction(fn)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSCallbackifiedFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $PromisifiedFunction UtilPromisify(object fn)
    /// </summary>
    private void EmitUtilPromisify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilPromisify",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSPromisifiedFunctionType,
            [_types.Object]);
        runtime.UtilPromisify = method;

        var il = method.GetILGenerator();
        // return new $PromisifiedFunction(fn)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSPromisifiedFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void UtilInherits(object ctor, object superCtor)
    /// </summary>
    private void EmitUtilInherits(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilInherits",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]);
        runtime.UtilInherits = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var notDictLabel = il.DefineLabel();
        var checkFieldsLabel = il.DefineLabel();

        // if (ctor is Dictionary<string, object?> dict) dict["super_"] = superCtor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        il.Emit(OpCodes.Ldstr, "super_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Pop);

        // if (ctor is $IHasFields hasFields) hasFields.SetProperty("super_", superCtor)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, checkFieldsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "super_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsSetProperty);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkFieldsLabel);

        // Fallback for any object type (System.Type for class constructors, TSFunction, etc.)
        // Use PropertyDescriptorStore to attach the property
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "super_");
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);  // Discard bool result

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    // NOTE: EmitUtilFormat, EmitUtilInspect, EmitUtilIsDeepStrictEqual, EmitUtilParseArgs
    // have been removed. The method signatures are defined in EmitUtilMethods() and the
    // bodies are emitted by EmitUtilStandaloneMethods() in RuntimeEmitter.UtilStandalone.cs
    // using pure IL emission without any SharpTS.dll dependencies.

    /// <summary>
    /// Emits: public static string UtilToUSVString(object value)
    /// Self-contained implementation - emits full IL without calling UtilHelpers.
    /// Converts a string to a well-formed Unicode string by replacing lone surrogates with U+FFFD.
    /// </summary>
    private void EmitUtilToUSVString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilToUSVString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.UtilToUSVString = method;

        var il = method.GetILGenerator();

        // Local variables
        var inputLocal = il.DeclareLocal(_types.String);        // loc.0: string input
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));   // loc.1: StringBuilder sb
        var iLocal = il.DeclareLocal(_types.Int32);             // loc.2: int i
        var cLocal = il.DeclareLocal(_types.Char);              // loc.3: char c
        var lengthLocal = il.DeclareLocal(_types.Int32);        // loc.4: int length

        // Labels
        var returnEmptyLabel = il.DefineLabel();
        var processLoopLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopConditionLabel = il.DefineLabel();
        var highSurrogateLabel = il.DefineLabel();
        var lowSurrogateLabel = il.DefineLabel();
        var regularCharLabel = il.DefineLabel();
        var appendReplacementLabel = il.DefineLabel();
        var appendBothLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // --- Convert input to string ---
        // if (value == null) return "";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // input = value.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue_S, processLoopLabel);
        il.Emit(OpCodes.Pop);

        // Return empty string for null ToString result
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(processLoopLabel);
        il.Emit(OpCodes.Stloc, inputLocal);

        // length = input.Length
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // if (length == 0) return input
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // sb = new StringBuilder(length)
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Newobj, _types.StringBuilderIntCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopConditionLabel);

        // --- Loop body ---
        il.MarkLabel(loopStartLabel);

        // c = input[i]
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, cLocal);

        // if (char.IsHighSurrogate(c)) goto highSurrogateLabel
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("IsHighSurrogate", [typeof(char)])!);
        il.Emit(OpCodes.Brtrue, highSurrogateLabel);

        // if (char.IsLowSurrogate(c)) goto lowSurrogateLabel
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Call, _types.CharIsLowSurrogate);
        il.Emit(OpCodes.Brtrue, lowSurrogateLabel);

        // Regular character - append and continue
        il.MarkLabel(regularCharLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop); // Discard StringBuilder return value
        il.Emit(OpCodes.Br, loopEndLabel);

        // --- High surrogate handling ---
        il.MarkLabel(highSurrogateLabel);

        // Check if i + 1 < length && char.IsLowSurrogate(input[i + 1])
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, appendReplacementLabel); // if (i + 1 >= length) append replacement

        // Check if next char is low surrogate
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Call, _types.CharIsLowSurrogate);
        il.Emit(OpCodes.Brfalse, appendReplacementLabel); // if not low surrogate, append replacement

        // Valid surrogate pair - append both
        il.MarkLabel(appendBothLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // Append the low surrogate
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // i++ (skip the low surrogate in next iteration)
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopEndLabel);

        // --- Low surrogate or lone high surrogate - append U+FFFD ---
        il.MarkLabel(lowSurrogateLabel);
        il.MarkLabel(appendReplacementLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFFFD); // U+FFFD replacement character
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopEndLabel);

        // --- Loop increment and condition ---
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // Return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        // Return input (for empty string case)
        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Wrapper for deprecated functions that logs a warning on first invocation.
/// Used by util.deprecate() in compiled mode.
/// Has an Invoke method that can be called by the compiled code's InvokeValue.
/// </summary>
public class DeprecatedFunction
{
    private readonly object _wrapped;
    private readonly string _message;
    private bool _warned;

    public DeprecatedFunction(object fn, string message)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
        _message = message ?? "";
        _warned = false;
    }

    /// <summary>
    /// Invoke the wrapped function, logging a deprecation warning on first call.
    /// This method signature matches what InvokeValue looks for via reflection.
    /// </summary>
    public object? Invoke(params object?[] args)
    {
        if (!_warned)
        {
            _warned = true;
            Console.Error.WriteLine($"DeprecationWarning: {_message}");
        }

        // Handle different callable types
        if (_wrapped is TSFunction tsFunc)
        {
            return tsFunc.Invoke(args);
        }

        if (_wrapped is Delegate del)
        {
            return del.DynamicInvoke(new object?[] { args });
        }

        // Try to find an Invoke method via reflection (for $TSFunction and other callable types)
        var invokeMethod = _wrapped.GetType().GetMethod("Invoke");
        if (invokeMethod != null)
        {
            // Call Invoke(args) on the wrapped object
            return invokeMethod.Invoke(_wrapped, [args]);
        }

        throw new InvalidOperationException($"Cannot invoke deprecated function: wrapped value is not callable ({_wrapped.GetType().Name})");
    }

    public override string ToString() => "[Function: deprecated]";
}

/// <summary>
/// Wrapper for util.promisify - converts callback-style functions to Promise-returning.
/// </summary>
public class PromisifiedFunction
{
    private readonly object _wrapped;

    public PromisifiedFunction(object fn)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
    }

    /// <summary>
    /// Invoke the wrapped function, passing args plus a callback, and return a Task.
    /// </summary>
    public Task<object?> Invoke(params object?[] args)
    {
        var tcs = new TaskCompletionSource<object?>();
        var callback = new PromisifyCallback(tcs);

        // Create args array with callback appended
        var argsWithCallback = new object?[(args?.Length ?? 0) + 1];
        if (args != null)
        {
            Array.Copy(args, argsWithCallback, args.Length);
        }
        argsWithCallback[^1] = callback;

        try
        {
            // Handle different callable types
            if (_wrapped is TSFunction tsFunc)
            {
                tsFunc.Invoke(argsWithCallback);
            }
            else if (_wrapped is Delegate del)
            {
                del.DynamicInvoke(new object?[] { argsWithCallback });
            }
            else
            {
                // Try to find an Invoke method via reflection
                var invokeMethod = _wrapped.GetType().GetMethod("Invoke");
                if (invokeMethod != null)
                {
                    invokeMethod.Invoke(_wrapped, [argsWithCallback]);
                }
                else
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"Cannot invoke promisified function: wrapped value is not callable ({_wrapped.GetType().Name})"));
                }
            }
        }
        catch (Exception ex)
        {
            // If the function throws synchronously, reject the promise
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    public override string ToString() => "[Function: promisified]";
}

/// <summary>
/// Internal callback used by PromisifiedFunction to resolve/reject the Task.
/// </summary>
public class PromisifyCallback
{
    private readonly TaskCompletionSource<object?> _tcs;

    public PromisifyCallback(TaskCompletionSource<object?> tcs)
    {
        _tcs = tcs;
    }

    /// <summary>
    /// Called with (err, value) - resolves or rejects the Task accordingly.
    /// </summary>
    public object? Invoke(params object?[] args)
    {
        var err = args?.Length > 0 ? args[0] : null;
        var value = args?.Length > 1 ? args[1] : null;

        // Check if err is truthy
        bool hasError = err switch
        {
            null => false,
            false => false,
            "" => false,
            0.0 => false,
            0 => false,
            _ => true
        };

        if (hasError)
        {
            _tcs.TrySetException(new Exception(err?.ToString() ?? "Unknown error"));
        }
        else
        {
            _tcs.TrySetResult(value);
        }

        return null;
    }

    public override string ToString() => "[Function: promisify callback]";
}
