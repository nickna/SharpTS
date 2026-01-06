using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the runtime support types into the generated assembly.
/// This makes compiled DLLs standalone without requiring SharpTS.dll.
/// </summary>
public static partial class RuntimeEmitter
{
    public static EmittedRuntime EmitAll(ModuleBuilder moduleBuilder)
    {
        var runtime = new EmittedRuntime();

        // Emit TSFunction class first (other methods depend on it)
        EmitTSFunctionClass(moduleBuilder, runtime);

        // Emit TSSymbol class for symbol support
        EmitTSSymbolClass(moduleBuilder, runtime);

        // Emit $Runtime class with all helper methods
        EmitRuntimeClass(moduleBuilder, runtime);

        return runtime;
    }

    private static void EmitTSFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSFunction
        var typeBuilder = moduleBuilder.DefineType(
            "$TSFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );
        runtime.TSFunctionType = typeBuilder;

        // Fields
        var targetField = typeBuilder.DefineField("_target", typeof(object), FieldAttributes.Private);
        var methodField = typeBuilder.DefineField("_method", typeof(MethodInfo), FieldAttributes.Private);

        // Constructor: public $TSFunction(object target, MethodInfo method)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(object), typeof(MethodInfo)]
        );
        runtime.TSFunctionCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        // this._target = target
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        // this._method = method
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, methodField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke method: public object Invoke(object[] args)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            typeof(object),
            [typeof(object[])]
        );
        runtime.TSFunctionInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();

        // Local variables
        var paramCountLocal = invokeIL.DeclareLocal(typeof(int));
        var effectiveArgsLocal = invokeIL.DeclareLocal(typeof(object[]));
        var invokeTargetLocal = invokeIL.DeclareLocal(typeof(object));

        // Get parameter count: int paramCount = _method.GetParameters().Length
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("GetParameters")!);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, paramCountLocal);

        // Check if this is a static method with a bound target
        // if (_method.IsStatic && _target != null)
        var notStaticWithTarget = invokeIL.DefineLabel();
        var afterArgPrep = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetProperty("IsStatic")!.GetGetMethod()!);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        // Static method with bound target: prepend target to args
        // effectiveArgs = new object[args.Length + 1];
        // effectiveArgs[0] = _target;
        // Array.Copy(args, 0, effectiveArgs, 1, args.Length);
        // invokeTarget = null;
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Add);
        invokeIL.Emit(OpCodes.Newarr, typeof(object));
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);

        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        invokeIL.Emit(OpCodes.Ldarg_1);  // source
        invokeIL.Emit(OpCodes.Ldc_I4_0); // sourceIndex
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldc_I4_1); // destIndex
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);  // length
        invokeIL.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);

        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Br, afterArgPrep);

        // Not a static with target: use args directly, target is _target
        invokeIL.MarkLabel(notStaticWithTarget);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);

        invokeIL.MarkLabel(afterArgPrep);

        // Now handle padding/trimming based on paramCount
        var argsLengthLocal = invokeIL.DeclareLocal(typeof(int));
        var adjustedArgsLocal = invokeIL.DeclareLocal(typeof(object[]));

        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, argsLengthLocal);

        var exactMatch = invokeIL.DefineLabel();
        var doInvoke = invokeIL.DefineLabel();

        // If effectiveArgs.Length == paramCount, use effectiveArgs directly
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Beq, exactMatch);

        // If effectiveArgs.Length < paramCount, pad with nulls
        var tooManyArgs = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Bge, tooManyArgs);

        // Pad with nulls
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, typeof(object));
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal); // length
        invokeIL.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Too many args: trim
        invokeIL.MarkLabel(tooManyArgs);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, typeof(object));
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal); // length
        invokeIL.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Exact match - use effectiveArgs directly
        invokeIL.MarkLabel(exactMatch);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);

        // Do invoke
        invokeIL.MarkLabel(doInvoke);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        invokeIL.Emit(OpCodes.Ret);

        // ToString method
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(string),
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function]");
        toStringIL.Emit(OpCodes.Ret);

        // BindThis method: public void BindThis(object thisValue)
        // Sets the 'this' field in the display class to the given value
        var bindThisBuilder = typeBuilder.DefineMethod(
            "BindThis",
            MethodAttributes.Public,
            typeof(void),
            [typeof(object)]
        );
        runtime.TSFunctionBindThis = bindThisBuilder;

        var bindThisIL = bindThisBuilder.GetILGenerator();
        var noTargetLabel = bindThisIL.DefineLabel();
        var endLabel = bindThisIL.DefineLabel();
        var thisFieldLocal = bindThisIL.DeclareLocal(typeof(FieldInfo));

        // if (_target == null) return;
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // var thisField = _target.GetType().GetField("this", BindingFlags.Public | BindingFlags.Instance);
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        bindThisIL.Emit(OpCodes.Ldstr, "this");
        bindThisIL.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
        bindThisIL.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        bindThisIL.Emit(OpCodes.Stloc, thisFieldLocal);

        // if (thisField == null) return;
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // thisField.SetValue(_target, thisValue);
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Ldarg_1);
        bindThisIL.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("SetValue", [typeof(object), typeof(object)])!);
        bindThisIL.Emit(OpCodes.Br, endLabel);

        bindThisIL.MarkLabel(noTargetLabel);
        bindThisIL.MarkLabel(endLabel);
        bindThisIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    private static void EmitTSSymbolClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSSymbol
        var typeBuilder = moduleBuilder.DefineType(
            "$TSSymbol",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );
        runtime.TSSymbolType = typeBuilder;

        // Static field for next ID
        var nextIdField = typeBuilder.DefineField("_nextId", typeof(int), FieldAttributes.Private | FieldAttributes.Static);

        // Instance fields
        var idField = typeBuilder.DefineField("_id", typeof(int), FieldAttributes.Private);
        var descriptionField = typeBuilder.DefineField("_description", typeof(string), FieldAttributes.Private);

        // Constructor: public $TSSymbol(string? description)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(string)]
        );
        runtime.TSSymbolCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        // _id = Interlocked.Increment(ref _nextId)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldsflda, nextIdField);
        ctorIL.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Increment", [typeof(int).MakeByRefType()])!);
        ctorIL.Emit(OpCodes.Stfld, idField);
        // _description = description
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, descriptionField);
        ctorIL.Emit(OpCodes.Ret);

        // Equals method: public override bool Equals(object? obj)
        var equalsBuilder = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(bool),
            [typeof(object)]
        );
        var equalsIL = equalsBuilder.GetILGenerator();
        var notSymbol = equalsIL.DefineLabel();
        var returnFalse = equalsIL.DefineLabel();
        // if (obj is not $TSSymbol other) return false
        equalsIL.Emit(OpCodes.Ldarg_1);
        equalsIL.Emit(OpCodes.Isinst, typeBuilder);
        equalsIL.Emit(OpCodes.Brfalse, returnFalse);
        // return this._id == other._id
        equalsIL.Emit(OpCodes.Ldarg_0);
        equalsIL.Emit(OpCodes.Ldfld, idField);
        equalsIL.Emit(OpCodes.Ldarg_1);
        equalsIL.Emit(OpCodes.Castclass, typeBuilder);
        equalsIL.Emit(OpCodes.Ldfld, idField);
        equalsIL.Emit(OpCodes.Ceq);
        equalsIL.Emit(OpCodes.Ret);
        equalsIL.MarkLabel(returnFalse);
        equalsIL.Emit(OpCodes.Ldc_I4_0);
        equalsIL.Emit(OpCodes.Ret);

        // GetHashCode method: public override int GetHashCode()
        var hashCodeBuilder = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes
        );
        var hashCodeIL = hashCodeBuilder.GetILGenerator();
        hashCodeIL.Emit(OpCodes.Ldarg_0);
        hashCodeIL.Emit(OpCodes.Ldfld, idField);
        hashCodeIL.Emit(OpCodes.Ret);

        // ToString method: public override string ToString()
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(string),
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        var hasDescription = toStringIL.DefineLabel();
        var doneToString = toStringIL.DefineLabel();
        // if (_description != null)
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, descriptionField);
        toStringIL.Emit(OpCodes.Brtrue, hasDescription);
        // return "Symbol()"
        toStringIL.Emit(OpCodes.Ldstr, "Symbol()");
        toStringIL.Emit(OpCodes.Br, doneToString);
        // return $"Symbol({_description})"
        toStringIL.MarkLabel(hasDescription);
        toStringIL.Emit(OpCodes.Ldstr, "Symbol(");
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, descriptionField);
        toStringIL.Emit(OpCodes.Ldstr, ")");
        toStringIL.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        toStringIL.MarkLabel(doneToString);
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    private static void EmitRuntimeClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public static class $Runtime
        var typeBuilder = moduleBuilder.DefineType(
            "$Runtime",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );
        runtime.RuntimeType = typeBuilder;

        // Static field for Random
        var randomField = typeBuilder.DefineField("_random", typeof(Random), FieldAttributes.Private | FieldAttributes.Static);

        // Static field for symbol storage: ConditionalWeakTable<object, Dictionary<object, object?>>
        var symbolDictType = typeof(Dictionary<object, object?>);
        var symbolStorageType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), symbolDictType);
        var symbolStorageField = typeBuilder.DefineField(
            "_symbolStorage",
            symbolStorageType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.SymbolStorageField = symbolStorageField;

        // Static constructor to initialize Random and symbol storage
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

        // Initialize _random = new Random()
        cctorIL.Emit(OpCodes.Newobj, typeof(Random).GetConstructor(Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Stsfld, randomField);

        // Initialize _symbolStorage = new ConditionalWeakTable<object, Dictionary<object, object?>>()
        cctorIL.Emit(OpCodes.Newobj, symbolStorageType.GetConstructor(Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Stsfld, symbolStorageField);

        cctorIL.Emit(OpCodes.Ret);

        // Emit all methods - these are now in partial class files
        // Core utilities
        EmitStringify(typeBuilder, runtime);
        EmitConsoleLog(typeBuilder, runtime);
        EmitConsoleLogMultiple(typeBuilder, runtime);
        EmitToNumber(typeBuilder, runtime);
        EmitIsTruthy(typeBuilder, runtime);
        EmitTypeOf(typeBuilder, runtime);
        EmitInstanceOf(typeBuilder, runtime);
        EmitAdd(typeBuilder, runtime);
        EmitEquals(typeBuilder, runtime);
        // Arrays
        EmitCreateArray(typeBuilder, runtime);
        EmitGetLength(typeBuilder, runtime);
        EmitGetElement(typeBuilder, runtime);
        EmitGetKeys(typeBuilder, runtime);
        EmitGetValues(typeBuilder, runtime);
        EmitGetEntries(typeBuilder, runtime);
        EmitIsArray(typeBuilder, runtime);
        EmitSpreadArray(typeBuilder, runtime);
        EmitConcatArrays(typeBuilder, runtime);
        EmitExpandCallArgs(typeBuilder, runtime);
        EmitArrayPop(typeBuilder, runtime);
        EmitArrayShift(typeBuilder, runtime);
        EmitArrayUnshift(typeBuilder, runtime);
        EmitArraySlice(typeBuilder, runtime);
        // Objects
        EmitCreateObject(typeBuilder, runtime);
        EmitGetArrayMethod(typeBuilder, runtime);
        EmitGetFieldsProperty(typeBuilder, runtime);
        EmitSetFieldsProperty(typeBuilder, runtime);
        EmitGetProperty(typeBuilder, runtime);
        EmitSetProperty(typeBuilder, runtime);
        EmitMergeIntoObject(typeBuilder, runtime);
        // Symbol support helpers - must come before EmitGetIndex/EmitSetIndex which depend on them
        EmitGetSymbolDict(typeBuilder, runtime, symbolStorageField);
        EmitIsSymbol(typeBuilder, runtime);
        EmitGetIndex(typeBuilder, runtime);
        EmitSetIndex(typeBuilder, runtime);
        EmitInvokeValue(typeBuilder, runtime);
        // Array callback methods must come after InvokeValue and IsTruthy
        EmitArrayMap(typeBuilder, runtime);
        EmitArrayFilter(typeBuilder, runtime);
        EmitArrayForEach(typeBuilder, runtime);
        EmitArrayPush(typeBuilder, runtime);
        EmitArrayFind(typeBuilder, runtime);
        EmitArrayFindIndex(typeBuilder, runtime);
        EmitArraySome(typeBuilder, runtime);
        EmitArrayEvery(typeBuilder, runtime);
        EmitArrayReduce(typeBuilder, runtime);
        EmitArrayIncludes(typeBuilder, runtime);
        EmitArrayIndexOf(typeBuilder, runtime);
        EmitArrayJoin(typeBuilder, runtime);
        EmitArrayConcat(typeBuilder, runtime);
        EmitArrayReverse(typeBuilder, runtime);
        // String methods
        EmitStringCharAt(typeBuilder, runtime);
        EmitStringSubstring(typeBuilder, runtime);
        EmitStringIndexOf(typeBuilder, runtime);
        EmitStringReplace(typeBuilder, runtime);
        EmitStringSplit(typeBuilder, runtime);
        EmitStringIncludes(typeBuilder, runtime);
        EmitStringStartsWith(typeBuilder, runtime);
        EmitStringEndsWith(typeBuilder, runtime);
        EmitStringSlice(typeBuilder, runtime);
        EmitStringRepeat(typeBuilder, runtime);
        EmitStringPadStart(typeBuilder, runtime);
        EmitStringPadEnd(typeBuilder, runtime);
        EmitStringCharCodeAt(typeBuilder, runtime);
        EmitStringConcat(typeBuilder, runtime);
        EmitStringLastIndexOf(typeBuilder, runtime);
        EmitStringReplaceAll(typeBuilder, runtime);
        EmitStringAt(typeBuilder, runtime);
        // Object utilities
        EmitGetSuperMethod(typeBuilder, runtime);
        EmitCreateException(typeBuilder, runtime);
        EmitWrapException(typeBuilder, runtime);
        EmitRandom(typeBuilder, runtime, randomField);
        EmitGetEnumMemberName(typeBuilder, runtime);
        EmitConcatTemplate(typeBuilder, runtime);
        EmitObjectRest(typeBuilder, runtime);
        // JSON methods
        EmitJsonParse(typeBuilder, runtime);
        EmitJsonParseWithReviver(typeBuilder, runtime);
        EmitJsonStringify(typeBuilder, runtime);
        EmitJsonStringifyFull(typeBuilder, runtime);
        // BigInt methods
        EmitCreateBigInt(typeBuilder, runtime);
        EmitBigIntArithmetic(typeBuilder, runtime);
        EmitBigIntComparison(typeBuilder, runtime);
        EmitBigIntBitwise(typeBuilder, runtime);
        // Promise methods
        EmitPromiseMethods(typeBuilder, runtime);
        // Number methods
        EmitNumberMethods(typeBuilder, runtime);

        typeBuilder.CreateType();
    }
}
