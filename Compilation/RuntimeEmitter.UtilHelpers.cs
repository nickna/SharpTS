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

        // Emit util.deprecate
        EmitUtilDeprecate(typeBuilder, runtime);

        // Emit util.callbackify
        EmitUtilCallbackify(typeBuilder, runtime);

        // Emit util.promisify
        EmitUtilPromisify(typeBuilder, runtime);

        // Emit util.inherits
        EmitUtilInherits(typeBuilder, runtime);

        // Emit util.toUSVString (already standalone)
        EmitUtilToUSVString(typeBuilder, runtime);

        // Emit util.stripVTControlCharacters
        EmitUtilStripVTControlCharacters(typeBuilder, runtime);

        // Emit util.getSystemErrorName and getSystemErrorMap
        EmitUtilGetSystemErrorName(typeBuilder, runtime);
        EmitUtilGetSystemErrorMap(typeBuilder, runtime);

        // Define method signatures for format, inspect, isDeepStrictEqual, parseArgs
        // (bodies will be emitted by EmitUtilStandaloneMethods)
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

        // Emit standalone helper method bodies
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

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

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

        // Check for $TSDate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDateType);
        il.Emit(OpCodes.Brtrue, trueLabel);

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

        // Check for $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Brtrue, trueLabel);

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

        // Check for Dictionary<object, object?> (direct check)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Check for generic Dictionary<,> via reflection
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
        il.Emit(OpCodes.Brtrue, trueLabel);

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

        // Check for $Buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, trueLabel);

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

        // Check for $Buffer (which backs ArrayBuffer in SharpTS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, trueLabel);

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

        // Call UtilHelpers.StripVTControlCharacters(value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.StripVTControlCharacters))!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilGetSystemErrorName(object errno)
    /// Returns the POSIX error name for the given error code.
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

        // Call UtilHelpers.GetSystemErrorName(errno)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.GetSystemErrorName))!);
        il.Emit(OpCodes.Ret);
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

        // Call RuntimeTypes.CreateSystemErrorMap()
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod(nameof(RuntimeTypes.CreateSystemErrorMap))!);
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
    /// For now, returns the function as-is (callbackify is rarely used in compiled mode).
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
        // For simplicity, just return the function as-is
        // Full callbackify implementation would require significant IL
        il.Emit(OpCodes.Ldarg_0);
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

        // if (ctor is IDictionary<string, object?> dict) dict["super_"] = superCtor
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

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilFormat(object[] args)
    /// Calls into UtilHelpers.Format for proper format specifier handling.
    /// TODO: Emit full implementation for truly standalone execution.
    /// </summary>
    private void EmitUtilFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]);
        runtime.UtilFormat = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.Format(args) - still uses helper for complex string parsing
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.Format))!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilInspect(object obj, object options)
    /// Calls into UtilHelpers.Inspect for proper formatting.
    /// TODO: Emit full implementation for truly standalone execution.
    /// </summary>
    private void EmitUtilInspect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilInspect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]);
        runtime.UtilInspect = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.Inspect(obj, options) - still uses helper for complex recursion
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.Inspect))!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool UtilIsDeepStrictEqual(object a, object b)
    /// Calls into UtilHelpers.IsDeepStrictEqual for proper deep comparison.
    /// </summary>
    private void EmitUtilIsDeepStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilIsDeepStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.UtilIsDeepStrictEqual = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.IsDeepStrictEqual(a, b)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.IsDeepStrictEqual))!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object UtilParseArgs(object config)
    /// Calls into UtilHelpers.ParseArgs for argument parsing.
    /// </summary>
    private void EmitUtilParseArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilParseArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.UtilParseArgs = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.ParseArgs(config)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.ParseArgs))!);
        il.Emit(OpCodes.Ret);
    }

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
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor([typeof(int)])!);
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
        il.Emit(OpCodes.Call, typeof(char).GetMethod("IsLowSurrogate", [typeof(char)])!);
        il.Emit(OpCodes.Brtrue, lowSurrogateLabel);

        // Regular character - append and continue
        il.MarkLabel(regularCharLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
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
        il.Emit(OpCodes.Call, typeof(char).GetMethod("IsLowSurrogate", [typeof(char)])!);
        il.Emit(OpCodes.Brfalse, appendReplacementLabel); // if not low surrogate, append replacement

        // Valid surrogate pair - append both
        il.MarkLabel(appendBothLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
        il.Emit(OpCodes.Pop);

        // Append the low surrogate
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // Return input (for empty string case)
        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper methods for util module, called from emitted IL.
/// These match the interpreter's behavior for parity.
/// </summary>
public static class UtilHelpers
{
    /// <summary>
    /// util.types.isArray - checks if value is an array.
    /// Checks for both emitted $Array (implements IList) and interpreter SharpTSArray.
    /// </summary>
    public static bool IsArray(object? value) =>
        value is IList<object?> || value is SharpTS.Runtime.Types.SharpTSArray;

    /// <summary>
    /// util.types.isFunction - checks if value is a function.
    /// Checks for Delegate, TSFunction (from RuntimeTypes), and $TSFunction (emitted into compiled DLLs).
    /// </summary>
    public static bool IsFunction(object? value)
    {
        if (value is null) return false;
        if (value is Delegate) return true;
        if (value is TSFunction) return true;
        // Check for emitted $TSFunction type (or $BoundTSFunction)
        var typeName = value.GetType().Name;
        return typeName is "$TSFunction" or "$BoundTSFunction";
    }

    /// <summary>
    /// util.types.isNull - checks if value is null.
    /// </summary>
    public static bool IsNull(object? value) => value is null;

    /// <summary>
    /// util.promisify - wraps a callback-style function to return a Promise (Task).
    /// </summary>
    public static PromisifiedFunction Promisify(object fn) => new PromisifiedFunction(fn);

    /// <summary>
    /// util.types.isUndefined - checks if value is undefined (null in SharpTS).
    /// </summary>
    public static bool IsUndefined(object? value) => value is null;

    /// <summary>
    /// util.types.isDate - checks if value is a date.
    /// </summary>
    public static bool IsDate(object? value) => value is DateTime or DateTimeOffset;

    /// <summary>
    /// util.types.isPromise - checks if value is a Promise.
    /// Supports both interpreter (SharpTSPromise) and compiled (Task) modes.
    /// </summary>
    public static bool IsPromise(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSPromise) return true;
        // In compiled mode, promises are represented as Task<object?> or Task
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>)
            || value is System.Threading.Tasks.Task;
    }

    /// <summary>
    /// util.types.isRegExp - checks if value is a RegExp.
    /// Supports both interpreter (SharpTSRegExp) and compiled ($RegExp or Regex) modes.
    /// </summary>
    public static bool IsRegExp(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSRegExp) return true;
        if (value is System.Text.RegularExpressions.Regex) return true;
        // In compiled mode, regex is represented as $RegExp emitted type
        var typeName = value.GetType().Name;
        return typeName == "$RegExp";
    }

    /// <summary>
    /// util.types.isMap - checks if value is a Map.
    /// Supports both interpreter (SharpTSMap) and compiled (Dictionary) modes.
    /// </summary>
    public static bool IsMap(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSMap) return true;
        // In compiled mode, maps are represented as Dictionary<object, object?>
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
    }

    /// <summary>
    /// util.types.isSet - checks if value is a Set.
    /// Supports both interpreter (SharpTSSet) and compiled (HashSet) modes.
    /// </summary>
    public static bool IsSet(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSSet) return true;
        // In compiled mode, sets are represented as HashSet<object>
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);
    }

    /// <summary>
    /// util.types.isTypedArray - checks if value is a typed array (Buffer).
    /// </summary>
    public static bool IsTypedArray(object? value) => value is SharpTS.Runtime.Types.SharpTSBuffer;

    /// <summary>
    /// util.types.isNativeError - checks if value is an Error instance.
    /// </summary>
    public static bool IsNativeError(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSError) return true;
        if (value is Exception) return true;
        return false;
    }

    /// <summary>
    /// util.types.isBoxedPrimitive - checks if value is a boxed primitive.
    /// Always returns false in SharpTS since we don't have explicit boxed primitive types.
    /// </summary>
    public static bool IsBoxedPrimitive(object? value) => false;

    /// <summary>
    /// util.types.isWeakMap - checks if value is a WeakMap.
    /// </summary>
    public static bool IsWeakMap(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSWeakMap) return true;
        // Check type name for compiled WeakMap
        var typeName = value.GetType().Name;
        return typeName.Contains("WeakMap");
    }

    /// <summary>
    /// util.types.isWeakSet - checks if value is a WeakSet.
    /// </summary>
    public static bool IsWeakSet(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSWeakSet) return true;
        // Check type name for compiled WeakSet
        var typeName = value.GetType().Name;
        return typeName.Contains("WeakSet");
    }

    /// <summary>
    /// util.types.isArrayBuffer - checks if value is an ArrayBuffer.
    /// In SharpTS, this is equivalent to Buffer.
    /// </summary>
    public static bool IsArrayBuffer(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSBuffer) return true;
        if (value is byte[]) return true;
        return false;
    }

    // ===================== stripVTControlCharacters =====================

    /// <summary>
    /// Regex pattern to match ANSI escape sequences.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex _ansiEscapeRegex = new(
        @"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[PX^_][^\x1b]*\x1b\\|\x1b\[[0-9;]*m",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// util.stripVTControlCharacters - strips ANSI escape codes from strings.
    /// </summary>
    public static string StripVTControlCharacters(object? value)
    {
        var input = value?.ToString() ?? "";
        return _ansiEscapeRegex.Replace(input, "");
    }

    // ===================== getSystemErrorName and getSystemErrorMap =====================

    /// <summary>
    /// Mapping of POSIX error codes to their names.
    /// </summary>
    private static readonly Dictionary<int, string> _posixErrorNames = new()
    {
        [-1] = "EPERM",
        [-2] = "ENOENT",
        [-3] = "ESRCH",
        [-4] = "EINTR",
        [-5] = "EIO",
        [-6] = "ENXIO",
        [-7] = "E2BIG",
        [-8] = "ENOEXEC",
        [-9] = "EBADF",
        [-10] = "ECHILD",
        [-11] = "EAGAIN",
        [-12] = "ENOMEM",
        [-13] = "EACCES",
        [-14] = "EFAULT",
        [-16] = "EBUSY",
        [-17] = "EEXIST",
        [-18] = "EXDEV",
        [-19] = "ENODEV",
        [-20] = "ENOTDIR",
        [-21] = "EISDIR",
        [-22] = "EINVAL",
        [-23] = "ENFILE",
        [-24] = "EMFILE",
        [-25] = "ENOTTY",
        [-26] = "ETXTBSY",
        [-27] = "EFBIG",
        [-28] = "ENOSPC",
        [-29] = "ESPIPE",
        [-30] = "EROFS",
        [-31] = "EMLINK",
        [-32] = "EPIPE",
        [-33] = "EDOM",
        [-34] = "ERANGE",
        [-35] = "EDEADLK",
        [-36] = "ENAMETOOLONG",
        [-37] = "ENOLCK",
        [-38] = "ENOSYS",
        [-39] = "ENOTEMPTY",
        [-40] = "ELOOP",
        [-42] = "ENOMSG",
        [-43] = "EIDRM",
        [-60] = "ENOSTR",
        [-61] = "ENODATA",
        [-62] = "ETIME",
        [-63] = "ENOSR",
        [-71] = "EPROTO",
        [-74] = "EBADMSG",
        [-75] = "EOVERFLOW",
        [-88] = "ENOTSOCK",
        [-89] = "EDESTADDRREQ",
        [-90] = "EMSGSIZE",
        [-91] = "EPROTOTYPE",
        [-92] = "ENOPROTOOPT",
        [-93] = "EPROTONOSUPPORT",
        [-95] = "EOPNOTSUPP",
        [-97] = "EAFNOSUPPORT",
        [-98] = "EADDRINUSE",
        [-99] = "EADDRNOTAVAIL",
        [-100] = "ENETDOWN",
        [-101] = "ENETUNREACH",
        [-102] = "ENETRESET",
        [-103] = "ECONNABORTED",
        [-104] = "ECONNRESET",
        [-105] = "ENOBUFS",
        [-106] = "EISCONN",
        [-107] = "ENOTCONN",
        [-110] = "ETIMEDOUT",
        [-111] = "ECONNREFUSED",
        [-112] = "EHOSTDOWN",
        [-113] = "EHOSTUNREACH",
        [-114] = "EALREADY",
        [-115] = "EINPROGRESS",
        [-116] = "ESTALE",
        [-122] = "EDQUOT",
        [-125] = "ECANCELED",
    };

    /// <summary>
    /// Mapping of error names to their descriptions.
    /// </summary>
    private static readonly Dictionary<string, string> _errorDescriptions = new()
    {
        ["EPERM"] = "operation not permitted",
        ["ENOENT"] = "no such file or directory",
        ["ESRCH"] = "no such process",
        ["EINTR"] = "interrupted system call",
        ["EIO"] = "i/o error",
        ["ENXIO"] = "no such device or address",
        ["E2BIG"] = "argument list too long",
        ["ENOEXEC"] = "exec format error",
        ["EBADF"] = "bad file descriptor",
        ["ECHILD"] = "no child processes",
        ["EAGAIN"] = "resource temporarily unavailable",
        ["ENOMEM"] = "not enough memory",
        ["EACCES"] = "permission denied",
        ["EFAULT"] = "bad address",
        ["EBUSY"] = "resource busy or locked",
        ["EEXIST"] = "file already exists",
        ["EXDEV"] = "cross-device link not permitted",
        ["ENODEV"] = "no such device",
        ["ENOTDIR"] = "not a directory",
        ["EISDIR"] = "illegal operation on a directory",
        ["EINVAL"] = "invalid argument",
        ["ENFILE"] = "file table overflow",
        ["EMFILE"] = "too many open files",
        ["ENOTTY"] = "inappropriate ioctl for device",
        ["ETXTBSY"] = "text file is busy",
        ["EFBIG"] = "file too large",
        ["ENOSPC"] = "no space left on device",
        ["ESPIPE"] = "invalid seek",
        ["EROFS"] = "read-only file system",
        ["EMLINK"] = "too many links",
        ["EPIPE"] = "broken pipe",
        ["EDOM"] = "argument out of domain",
        ["ERANGE"] = "result too large",
        ["EDEADLK"] = "resource deadlock avoided",
        ["ENAMETOOLONG"] = "name too long",
        ["ENOLCK"] = "no locks available",
        ["ENOSYS"] = "function not implemented",
        ["ENOTEMPTY"] = "directory not empty",
        ["ELOOP"] = "too many symbolic links encountered",
        ["ENOMSG"] = "no message of desired type",
        ["EIDRM"] = "identifier removed",
        ["ENOSTR"] = "device not a stream",
        ["ENODATA"] = "no data available",
        ["ETIME"] = "timer expired",
        ["ENOSR"] = "out of streams resources",
        ["EPROTO"] = "protocol error",
        ["EBADMSG"] = "bad message",
        ["EOVERFLOW"] = "value too large for defined data type",
        ["ENOTSOCK"] = "socket operation on non-socket",
        ["EDESTADDRREQ"] = "destination address required",
        ["EMSGSIZE"] = "message too long",
        ["EPROTOTYPE"] = "protocol wrong type for socket",
        ["ENOPROTOOPT"] = "protocol not available",
        ["EPROTONOSUPPORT"] = "protocol not supported",
        ["EOPNOTSUPP"] = "operation not supported on socket",
        ["EAFNOSUPPORT"] = "address family not supported",
        ["EADDRINUSE"] = "address already in use",
        ["EADDRNOTAVAIL"] = "address not available",
        ["ENETDOWN"] = "network is down",
        ["ENETUNREACH"] = "network is unreachable",
        ["ENETRESET"] = "connection reset by network",
        ["ECONNABORTED"] = "connection aborted",
        ["ECONNRESET"] = "connection reset by peer",
        ["ENOBUFS"] = "no buffer space available",
        ["EISCONN"] = "socket is connected",
        ["ENOTCONN"] = "socket is not connected",
        ["ETIMEDOUT"] = "connection timed out",
        ["ECONNREFUSED"] = "connection refused",
        ["EHOSTDOWN"] = "host is down",
        ["EHOSTUNREACH"] = "host is unreachable",
        ["EALREADY"] = "connection already in progress",
        ["EINPROGRESS"] = "operation in progress",
        ["ESTALE"] = "stale file handle",
        ["EDQUOT"] = "disk quota exceeded",
        ["ECANCELED"] = "operation canceled",
    };

    /// <summary>
    /// util.getSystemErrorName - returns the error name for a POSIX error code.
    /// </summary>
    public static string GetSystemErrorName(object? errno)
    {
        if (errno is not double d)
            throw new Exception("The value of \"err\" is out of range");

        var errorCode = (int)d;
        if (_posixErrorNames.TryGetValue(errorCode, out var name))
            return name;

        return $"Unknown system error {errorCode}";
    }

    /// <summary>
    /// util.getSystemErrorMap - returns a Map of error codes to [name, description] tuples.
    /// Returns a Dictionary compatible with the compiled Map type.
    /// Note: We don't use ReferenceEqualityComparer here because keys are value types (doubles).
    /// </summary>
    public static object GetSystemErrorMap()
    {
        // Use a regular Dictionary without ReferenceEqualityComparer since keys are value types
        var map = new Dictionary<object, object?>();
        foreach (var (code, name) in _posixErrorNames)
        {
            var description = _errorDescriptions.TryGetValue(name, out var desc) ? desc : "";
            var entry = new List<object?> { name, description };
            map[(double)code] = entry;
        }
        return map;
    }

    /// <summary>
    /// util.isDeepStrictEqual - performs deep strict equality comparison.
    /// </summary>
    public static bool IsDeepStrictEqual(object? a, object? b)
    {
        return DeepStrictEqualImpl(a, b, new HashSet<(object?, object?)>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// util.toUSVString - converts a string to a well-formed Unicode string by replacing
    /// lone surrogates with the Unicode replacement character (U+FFFD).
    /// </summary>
    public static string ToUSVString(object? value)
    {
        var input = value?.ToString() ?? "";

        if (string.IsNullOrEmpty(input))
            return input;

        var result = new StringBuilder(input.Length);

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsHighSurrogate(c))
            {
                // Check if followed by a low surrogate
                if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    // Valid surrogate pair - keep both
                    result.Append(c);
                    result.Append(input[i + 1]);
                    i++; // Skip the low surrogate
                }
                else
                {
                    // Lone high surrogate - replace with U+FFFD
                    result.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                // Lone low surrogate (not preceded by high) - replace with U+FFFD
                result.Append('\uFFFD');
            }
            else
            {
                // Regular character
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static bool DeepStrictEqualImpl(object? a, object? b, HashSet<(object?, object?)> seen)
    {
        // Same reference or both null
        if (ReferenceEquals(a, b))
            return true;

        // One is null, other is not
        if (a == null || b == null)
            return false;

        // Different types (strict equality) - but allow numeric comparison
        var typeA = a.GetType();
        var typeB = b.GetType();

        // Primitives
        if (a is string sa && b is string sb)
            return sa == sb;

        if (a is double d1 && b is double d2)
        {
            // NaN === NaN for deep strict equal
            if (double.IsNaN(d1) && double.IsNaN(d2))
                return true;
            return d1 == d2;
        }

        if (a is bool ba && b is bool bb)
            return ba == bb;

        // Different non-primitive types
        if (typeA != typeB)
            return false;

        // Circular reference detection
        var pair = (a, b);
        if (seen.Contains(pair))
            return true;
        seen.Add(pair);

        // Arrays (IList<object?>)
        if (a is IList<object?> listA && b is IList<object?> listB)
        {
            if (listA.Count != listB.Count)
                return false;

            for (int i = 0; i < listA.Count; i++)
            {
                if (!DeepStrictEqualImpl(listA[i], listB[i], seen))
                    return false;
            }
            return true;
        }

        // Objects (IDictionary<string, object?>)
        if (a is IDictionary<string, object?> dictA && b is IDictionary<string, object?> dictB)
        {
            if (dictA.Count != dictB.Count)
                return false;

            foreach (var key in dictA.Keys)
            {
                if (!dictB.ContainsKey(key))
                    return false;
                if (!DeepStrictEqualImpl(dictA[key], dictB[key], seen))
                    return false;
            }
            return true;
        }

        // Buffers
        if (a is SharpTS.Runtime.Types.SharpTSBuffer bufA && b is SharpTS.Runtime.Types.SharpTSBuffer bufB)
        {
            return bufA.Data.SequenceEqual(bufB.Data);
        }

        // Default: use Object.Equals
        return Equals(a, b);
    }

    /// <summary>
    /// Comparer for reference equality in HashSet (for cycle detection).
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<(object?, object?)>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals((object?, object?) x, (object?, object?) y)
            => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object?, object?) obj)
            => HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item2));
    }

    /// <summary>
    /// util.parseArgs - parses command-line arguments.
    /// Returns an object with { values, positionals } properties.
    /// </summary>
    public static IDictionary<string, object?> ParseArgs(object? config)
    {
        var configDict = config as IDictionary<string, object?>;

        // Extract config properties
        var argsArray = GetArgsArray(configDict);
        var optionsDef = GetOptionsDef(configDict);
        var strict = GetBoolOption(configDict, "strict", true);
        var allowPositionals = GetBoolOption(configDict, "allowPositionals", !strict);
        var allowNegative = GetBoolOption(configDict, "allowNegative", false);
        var returnTokens = GetBoolOption(configDict, "tokens", false);

        // Initialize result
        var values = new Dictionary<string, object?>();
        var positionals = new List<object?>();
        var tokens = new List<object?>();

        // Apply defaults from options definitions
        foreach (var (name, optDef) in optionsDef)
        {
            if (optDef.TryGetValue("default", out var defaultVal) && defaultVal != null)
            {
                values[name] = defaultVal;
            }
        }

        // Parse arguments
        var i = 0;
        while (i < argsArray.Count)
        {
            var arg = argsArray[i]?.ToString() ?? "";

            if (arg == "--")
            {
                // Option terminator
                if (returnTokens)
                {
                    tokens.Add(new Dictionary<string, object?>
                    {
                        ["kind"] = "option-terminator",
                        ["index"] = (double)i
                    });
                }
                i++;
                // Rest are positionals
                while (i < argsArray.Count)
                {
                    var positional = argsArray[i]?.ToString() ?? "";
                    if (!allowPositionals && strict)
                        throw new Exception($"Unexpected argument: {positional}");
                    positionals.Add(positional);
                    if (returnTokens)
                    {
                        tokens.Add(new Dictionary<string, object?>
                        {
                            ["kind"] = "positional",
                            ["index"] = (double)i,
                            ["value"] = positional
                        });
                    }
                    i++;
                }
                break;
            }
            else if (arg.StartsWith("--"))
            {
                i = ParseLongOptionCompiled(arg, i, argsArray, optionsDef, values, tokens, strict, allowNegative, returnTokens);
            }
            else if (arg.StartsWith("-") && arg.Length > 1)
            {
                i = ParseShortOptionsCompiled(arg, i, argsArray, optionsDef, values, tokens, strict, returnTokens);
            }
            else
            {
                if (!allowPositionals && strict)
                    throw new Exception($"Unexpected argument: {arg}");
                positionals.Add(arg);
                if (returnTokens)
                {
                    tokens.Add(new Dictionary<string, object?>
                    {
                        ["kind"] = "positional",
                        ["index"] = (double)i,
                        ["value"] = arg
                    });
                }
                i++;
            }
        }

        // Build result object
        var result = new Dictionary<string, object?>
        {
            ["values"] = values,
            ["positionals"] = positionals
        };

        if (returnTokens)
        {
            result["tokens"] = tokens;
        }

        return result;
    }

    private static List<object?> GetArgsArray(IDictionary<string, object?>? config)
    {
        if (config != null && config.TryGetValue("args", out var argsVal) && argsVal is IList<object?> arr)
        {
            return arr.ToList();
        }

        // Default to empty - in compiled mode we don't have easy access to process.argv
        return new List<object?>();
    }

    private static Dictionary<string, Dictionary<string, object?>> GetOptionsDef(IDictionary<string, object?>? config)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>();

        if (config == null || !config.TryGetValue("options", out var optionsVal))
            return result;

        if (optionsVal is not IDictionary<string, object?> options)
            return result;

        foreach (var (name, value) in options)
        {
            if (value is IDictionary<string, object?> optDef)
            {
                result[name] = new Dictionary<string, object?>(optDef);
            }
        }

        return result;
    }

    private static bool GetBoolOption(IDictionary<string, object?>? config, string name, bool defaultValue)
    {
        if (config == null || !config.TryGetValue(name, out var val))
            return defaultValue;

        if (val is bool b)
            return b;

        return defaultValue;
    }

    private static int ParseLongOptionCompiled(
        string arg,
        int index,
        List<object?> argsArray,
        Dictionary<string, Dictionary<string, object?>> optionsDef,
        Dictionary<string, object?> values,
        List<object?> tokens,
        bool strict,
        bool allowNegative,
        bool returnTokens)
    {
        var rawName = arg;
        string name;
        string? inlineValue = null;
        var hasInlineValue = false;

        var eqIndex = arg.IndexOf('=');
        if (eqIndex > 0)
        {
            name = arg[2..eqIndex];
            inlineValue = arg[(eqIndex + 1)..];
            hasInlineValue = true;
        }
        else
        {
            name = arg[2..];
        }

        var isNegated = false;
        var originalName = name;
        if (allowNegative && name.StartsWith("no-"))
        {
            var positiveName = name[3..];
            if (optionsDef.TryGetValue(positiveName, out var posDef) &&
                posDef.TryGetValue("type", out var typeVal) &&
                typeVal?.ToString() == "boolean")
            {
                name = positiveName;
                isNegated = true;
            }
        }

        if (!optionsDef.TryGetValue(name, out var optDef))
        {
            if (strict)
                throw new Exception($"Unknown option '--{originalName}'");
            values[name] = !isNegated;
            return index + 1;
        }

        var optType = optDef.TryGetValue("type", out var t) ? t?.ToString() : "boolean";
        var multiple = optDef.TryGetValue("multiple", out var m) && m is true;

        object? value;

        if (optType == "boolean")
        {
            if (hasInlineValue && strict)
                throw new Exception($"Option '--{name}' does not take an argument");
            value = !isNegated;
            index++;
        }
        else
        {
            if (isNegated && strict)
                throw new Exception($"Option '--{name}' cannot be negated");

            if (hasInlineValue)
            {
                value = inlineValue;
                index++;
            }
            else if (index + 1 < argsArray.Count)
            {
                value = argsArray[index + 1]?.ToString() ?? "";
                index += 2;
            }
            else
            {
                if (strict)
                    throw new Exception($"Option '--{name}' requires an argument");
                value = "";
                index++;
            }
        }

        if (multiple)
        {
            if (!values.TryGetValue(name, out var existing) || existing is not IList<object?> existingList)
            {
                existingList = new List<object?>();
                values[name] = existingList;
            }
            existingList.Add(value);
        }
        else
        {
            values[name] = value;
        }

        if (returnTokens)
        {
            tokens.Add(new Dictionary<string, object?>
            {
                ["kind"] = "option",
                ["index"] = (double)(index - (optType == "string" && !hasInlineValue ? 2 : 1)),
                ["name"] = name,
                ["rawName"] = rawName.Split('=')[0],
                ["value"] = optType == "string" ? value : null,
                ["inlineValue"] = hasInlineValue
            });
        }

        return index;
    }

    private static int ParseShortOptionsCompiled(
        string arg,
        int index,
        List<object?> argsArray,
        Dictionary<string, Dictionary<string, object?>> optionsDef,
        Dictionary<string, object?> values,
        List<object?> tokens,
        bool strict,
        bool returnTokens)
    {
        var shortOpts = arg[1..];

        for (var j = 0; j < shortOpts.Length; j++)
        {
            var shortChar = shortOpts[j].ToString();
            string? optName = null;
            Dictionary<string, object?>? optDef = null;

            foreach (var (name, def) in optionsDef)
            {
                if (def.TryGetValue("short", out var shortVal) && shortVal?.ToString() == shortChar)
                {
                    optName = name;
                    optDef = def;
                    break;
                }
            }

            if (optName == null || optDef == null)
            {
                if (strict)
                    throw new Exception($"Unknown option '-{shortChar}'");
                continue;
            }

            var optType = optDef.TryGetValue("type", out var t) ? t?.ToString() : "boolean";
            var multiple = optDef.TryGetValue("multiple", out var m) && m is true;

            object? value;

            if (optType == "boolean")
            {
                value = true;
            }
            else
            {
                if (j + 1 < shortOpts.Length)
                {
                    value = shortOpts[(j + 1)..];
                    j = shortOpts.Length;
                }
                else if (index + 1 < argsArray.Count)
                {
                    value = argsArray[index + 1]?.ToString() ?? "";
                    index++;
                }
                else
                {
                    if (strict)
                        throw new Exception($"Option '-{shortChar}' requires an argument");
                    value = "";
                }
            }

            if (multiple)
            {
                if (!values.TryGetValue(optName, out var existing) || existing is not IList<object?> existingList)
                {
                    existingList = new List<object?>();
                    values[optName] = existingList;
                }
                existingList.Add(value);
            }
            else
            {
                values[optName] = value;
            }

            if (returnTokens)
            {
                tokens.Add(new Dictionary<string, object?>
                {
                    ["kind"] = "option",
                    ["index"] = (double)index,
                    ["name"] = optName,
                    ["rawName"] = $"-{shortChar}",
                    ["value"] = optType == "string" ? value : null,
                    ["inlineValue"] = j + 1 < shortOpts.Length && optType == "string"
                });
            }
        }

        return index + 1;
    }

    /// <summary>
    /// util.deprecate - wraps a function to log a deprecation warning on first call.
    /// Returns a DeprecatedFunction wrapper that can be invoked by compiled code.
    /// </summary>
    public static DeprecatedFunction Deprecate(object fn, string message)
    {
        return new DeprecatedFunction(fn, message);
    }

    /// <summary>
    /// util.callbackify - wraps a function to use callback-style error handling.
    /// The returned function takes original args + a callback as the last argument.
    /// Callback is called with (error, result).
    /// </summary>
    public static Func<object?[], object?> Callbackify(Delegate fn)
    {
        return args =>
        {
            if (args.Length == 0)
                throw new Exception("callbackified function requires at least a callback argument");

            // Last argument is the callback
            var callback = args[^1] as Delegate
                ?? throw new Exception("Last argument to callbackified function must be a callback");

            // Get original args (all except last)
            var originalArgs = args.Take(args.Length - 1).ToArray();

            try
            {
                var result = fn.DynamicInvoke(originalArgs);
                callback.DynamicInvoke(new object?[] { new object?[] { null, result } });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.InnerException?.Message ?? ex.Message;
                callback.DynamicInvoke(new object?[] { new object?[] { errorMessage, null } });
            }

            return null;
        };
    }

    /// <summary>
    /// util.inherits - sets constructor.super_ = superConstructor.
    /// This is a legacy Node.js pattern for pseudo-classical inheritance.
    /// </summary>
    public static void Inherits(object ctor, object superCtor)
    {
        // In compiled mode, we use a dictionary to track the super_ property
        // This is a no-op for actual prototype chain manipulation
        // Real classes in .NET don't use this pattern
        if (ctor is IDictionary<string, object?> dict)
        {
            dict["super_"] = superCtor;
        }
    }

    /// <summary>
    /// Creates a new TextEncoder instance.
    /// TextEncoder always uses UTF-8 encoding.
    /// </summary>
    public static SharpTS.Runtime.Types.SharpTSTextEncoder CreateTextEncoder()
    {
        return new SharpTS.Runtime.Types.SharpTSTextEncoder();
    }

    /// <summary>
    /// Creates a new TextDecoder instance with the specified options.
    /// </summary>
    public static SharpTS.Runtime.Types.SharpTSTextDecoder CreateTextDecoder(string? encoding = null, bool fatal = false, bool ignoreBOM = false)
    {
        return new SharpTS.Runtime.Types.SharpTSTextDecoder(encoding ?? "utf-8", fatal, ignoreBOM);
    }

    /// <summary>
    /// Implements util.format() with proper format specifier handling.
    /// </summary>
    public static string Format(object?[] args)
    {
        if (args.Length == 0)
            return "";

        var format = args[0]?.ToString() ?? "";
        // Note: We can't early-return here even with 1 arg because we need to process %% escapes
        var result = new StringBuilder();
        var argIndex = 1;
        var i = 0;

        while (i < format.Length)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                var specifier = format[i + 1];
                switch (specifier)
                {
                    case 's': // String
                        result.Append(argIndex < args.Length ? args[argIndex++]?.ToString() ?? "undefined" : "%s");
                        i += 2;
                        continue;
                    case 'd': // Integer
                    case 'i':
                        if (argIndex < args.Length && args[argIndex] is double d)
                        {
                            result.Append((int)d);
                            argIndex++;
                        }
                        else
                            result.Append('%').Append(specifier);
                        i += 2;
                        continue;
                    case 'f': // Float
                        if (argIndex < args.Length && args[argIndex] is double f)
                        {
                            result.Append(f);
                            argIndex++;
                        }
                        else
                            result.Append("%f");
                        i += 2;
                        continue;
                    case 'j': // JSON
                        if (argIndex < args.Length)
                        {
                            result.Append(System.Text.Json.JsonSerializer.Serialize(args[argIndex++]));
                        }
                        else
                            result.Append("%j");
                        i += 2;
                        continue;
                    case 'o': // Object
                    case 'O':
                        if (argIndex < args.Length)
                        {
                            result.Append(InspectValue(args[argIndex++], 2, 0));
                        }
                        else
                            result.Append('%').Append(specifier);
                        i += 2;
                        continue;
                    case '%': // Literal %
                        result.Append('%');
                        i += 2;
                        continue;
                }
            }
            result.Append(format[i]);
            i++;
        }

        // Append remaining arguments
        while (argIndex < args.Length)
        {
            result.Append(' ');
            result.Append(args[argIndex++]?.ToString() ?? "undefined");
        }

        return result.ToString();
    }

    /// <summary>
    /// Implements util.inspect() with proper value formatting.
    /// </summary>
    public static string Inspect(object? obj, object? options)
    {
        int depth = 2;
        if (options is IDictionary<string, object?> dict && dict.TryGetValue("depth", out var depthVal) && depthVal is double d)
            depth = (int)d;

        return InspectValue(obj, depth, 0);
    }

    private static string InspectValue(object? value, int depth, int currentDepth)
    {
        if (value == null)
            return "null";

        if (currentDepth > depth)
            return "[Object]";

        return value switch
        {
            string s => $"'{s}'",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IList<object?> list => InspectArray(list, depth, currentDepth),
            IDictionary<string, object?> dict => InspectObject(dict, depth, currentDepth),
            Delegate => "[Function]",
            _ => value.ToString() ?? "undefined"
        };
    }

    private static string InspectArray(IList<object?> arr, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Array]";

        var elements = arr.Select(e => InspectValue(e, depth, currentDepth + 1));
        return $"[ {string.Join(", ", elements)} ]";
    }

    private static string InspectObject(IDictionary<string, object?> obj, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Object]";

        var props = obj.Select(kv => $"{kv.Key}: {InspectValue(kv.Value, depth, currentDepth + 1)}");
        return $"{{ {string.Join(", ", props)} }}";
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
