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
public static class RuntimeEmitter
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

        // Get parameter count: int paramCount = _method.GetParameters().Length
        var paramCountLocal = invokeIL.DeclareLocal(typeof(int));
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("GetParameters")!);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, paramCountLocal);

        // Get args length
        var argsLengthLocal = invokeIL.DeclareLocal(typeof(int));
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, argsLengthLocal);

        var exactMatch = invokeIL.DefineLabel();
        var doInvoke = invokeIL.DefineLabel();
        var adjustedArgsLocal = invokeIL.DeclareLocal(typeof(object[]));

        // If args.Length == paramCount, use args directly
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Beq, exactMatch);

        // If args.Length < paramCount, pad with nulls
        var tooManyArgs = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Bge, tooManyArgs);

        // Pad with nulls: adjustedArgs = new object[paramCount]; Array.Copy(args, adjustedArgs, args.Length)
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, typeof(object));
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_1); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal); // length
        invokeIL.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Too many args: trim to paramCount
        invokeIL.MarkLabel(tooManyArgs);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, typeof(object));
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_1); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal); // length
        invokeIL.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Exact match - use args directly
        invokeIL.MarkLabel(exactMatch);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);

        // Do invoke
        invokeIL.MarkLabel(doInvoke);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
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

        // Emit all methods
        EmitStringify(typeBuilder, runtime);
        EmitConsoleLog(typeBuilder, runtime);
        EmitConsoleLogMultiple(typeBuilder, runtime);
        EmitToNumber(typeBuilder, runtime);
        EmitIsTruthy(typeBuilder, runtime);
        EmitTypeOf(typeBuilder, runtime);
        EmitInstanceOf(typeBuilder, runtime);
        EmitAdd(typeBuilder, runtime);
        EmitEquals(typeBuilder, runtime);
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
        EmitGetSuperMethod(typeBuilder, runtime);
        EmitCreateException(typeBuilder, runtime);
        EmitWrapException(typeBuilder, runtime);
        EmitRandom(typeBuilder, runtime, randomField);
        EmitGetEnumMemberName(typeBuilder, runtime);
        EmitConcatTemplate(typeBuilder, runtime);
        EmitObjectRest(typeBuilder, runtime);
        EmitJsonParse(typeBuilder, runtime);
        EmitJsonParseWithReviver(typeBuilder, runtime);
        EmitJsonStringify(typeBuilder, runtime);
        EmitJsonStringifyFull(typeBuilder, runtime);
        // BigInt methods
        EmitCreateBigInt(typeBuilder, runtime);
        EmitBigIntArithmetic(typeBuilder, runtime);
        EmitBigIntComparison(typeBuilder, runtime);
        EmitBigIntBitwise(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    #region Method Emitters

    private static void EmitStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Stringify",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object)]
        );
        runtime.Stringify = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool b) return b ? "true" : "false"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double d) return d.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is List<object?>) return array string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is BigInteger) return value.ToString() + "n"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Default: return value.ToString() ?? "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "null");
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Br, endLabel);

        // null case
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Br, endLabel);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Br, endLabel);

        // double case - simple ToString
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        var doubleLocal = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Stloc, doubleLocal);
        il.Emit(OpCodes.Ldloca, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, endLabel);

        // BigInteger case - format as value.ToString() + "n"
        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(System.Numerics.BigInteger));
        var bigintLocal = il.DeclareLocal(typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Stloc, bigintLocal);
        il.Emit(OpCodes.Ldloca, bigintLocal);
        il.Emit(OpCodes.Call, typeof(System.Numerics.BigInteger).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, "n");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, endLabel);

        // List case - format as "[elem1, elem2, ...]"
        il.MarkLabel(listLabel);
        // Use StringBuilder to build the result
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "["
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // Loop through list elements
        var listLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, listLocal);

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= list.Count) break
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (index > 0) append ", "
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // Append Stringify(list[index])
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call to Stringify
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Append "]" and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConsoleLog(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLog",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object)]
        );
        runtime.ConsoleLog = method;

        var il = method.GetILGenerator();
        // Call Stringify then Console.WriteLine
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConsoleLogMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLogMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object[])]
        );
        runtime.ConsoleLogMultiple = method;

        var il = method.GetILGenerator();
        // Simple implementation: join with spaces and print
        // string.Join(" ", values.Select(Stringify))
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Join", [typeof(string), typeof(object[])])!);
        il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(object)]
        );
        runtime.ToNumber = method;

        var il = method.GetILGenerator();
        // Use Convert.ToDouble with try-catch fallback to NaN
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, endLabel);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.EndExceptionBlock();
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIsTruthy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsTruthy",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsTruthy = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var checkBool = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // bool => return value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, checkBool);

        // everything else => true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(checkBool);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitTypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object)]
        );
        runtime.TypeOf = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var symbolLabel = il.DefineLabel();
        var functionLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // null => "object" (JS typeof null === "object")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // bool => "boolean"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // double => "number"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, numberLabel);

        // string => "string"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // TSSymbol => "symbol"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, symbolLabel);

        // TSFunction => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // Delegate => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brtrue, functionLabel);

        // BigInteger => "bigint"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Default => "object"
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "string");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(symbolLabel);
        il.Emit(OpCodes.Ldstr, "symbol");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(functionLabel);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldstr, "bigint");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitInstanceOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InstanceOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.InstanceOf = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();

        // if (instance == null || classType == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Get type of instance and check IsAssignableFrom
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(Type));
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTypeLabel);

        // classType is Type, use it directly
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(Type));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("IsAssignableFrom", [typeof(Type)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTypeLabel);
        // classType is not Type, get its type
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("IsAssignableFrom", [typeof(Type)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Add",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.Add = method;

        var il = method.GetILGenerator();
        var stringConcatLabel = il.DefineLabel();

        // if (left is string || right is string) string concat
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringConcatLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringConcatLabel);

        // Numeric addition
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);

        // String concat
        il.MarkLabel(stringConcatLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.Equals = method;

        var il = method.GetILGenerator();
        // Use object.Equals(left, right)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object[])]
        );
        runtime.CreateArray = method;

        var il = method.GetILGenerator();
        // new List<object>(elements)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetLength",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(object)]
        );
        runtime.GetLength = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetElement",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(int)]
        );
        runtime.GetElement = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetKeys = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default - empty list
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Convert keys to List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSpreadArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SpreadArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.SpreadArray = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // Not a list - return empty
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Return new list with same elements
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConcatArrays(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatArrays",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object[])]
        );
        runtime.ConcatArrays = method;

        var il = method.GetILGenerator();
        // var result = new List<object>();
        // foreach (var arr in arrays) if (arr is List<object> list) result.AddRange(list);
        // return result;
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var addRangeLabel = il.DefineLabel();

        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Get element
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, addRangeLabel);
        il.Emit(OpCodes.Pop);
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, skipLabel);

        il.MarkLabel(addRangeLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitExpandCallArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandCallArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object[]),
            [typeof(object[]), typeof(bool[])]
        );
        runtime.ExpandCallArgs = method;

        var il = method.GetILGenerator();
        // Simple implementation: create result list, iterate args, expand spreads
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if this is a spread
        var notSpreadLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_I1);
        il.Emit(OpCodes.Brfalse, notSpreadLabel);

        // Is spread - add range if it's a list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Dup);
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Br, continueLabel);

        il.MarkLabel(notListLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // Not spread - add single element
        il.MarkLabel(notSpreadLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayPop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPop",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>)]
        );
        runtime.ArrayPop = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var last = list[list.Count - 1]
        var lastLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lastLocal);

        // list.RemoveAt(list.Count - 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("RemoveAt", [typeof(int)])!);

        // return last
        il.Emit(OpCodes.Ldloc, lastLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayShift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayShift",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>)]
        );
        runtime.ArrayShift = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var first = list[0]
        var firstLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, firstLocal);

        // list.RemoveAt(0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("RemoveAt", [typeof(int)])!);

        // return first
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayUnshift",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayUnshift = method;

        var il = method.GetILGenerator();

        // list.Insert(0, element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Insert", [typeof(int), typeof(object)])!);

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArraySlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySlice",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object[])]
        );
        runtime.ArraySlice = method;

        var il = method.GetILGenerator();

        // For simplicity, call the static helper method in RuntimeTypes
        // This would require the RuntimeTypes class to be available, so instead
        // we'll emit inline IL for a basic implementation

        var startLocal = il.DeclareLocal(typeof(int));
        var endLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));

        // count = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // start = args.Length > 0 ? (int)(double)args[0] : 0
        var noStartArg = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noStartArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(noStartArg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startDone);

        // end = args.Length > 1 ? (int)(double)args[1] : count
        var noEndArg = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noEndArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endDone);
        il.MarkLabel(noEndArg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endDone);

        // Clamp start and end, handle negatives
        // For simplicity, we'll just call GetRange with clamped values
        // if (start < 0) start = max(0, count + start)
        var startNotNeg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNeg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotNeg);

        // if (end < 0) end = max(0, count + end)
        var endNotNeg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNeg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotNeg);

        // Clamp to count
        // if (start > count) start = count
        var startNotOver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ble, startNotOver);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotOver);

        // if (end > count) end = count
        var endNotOver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ble, endNotOver);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotOver);

        // if (end <= start) return new List<object>()
        var rangeValid = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, rangeValid);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rangeValid);
        // return list.GetRange(start, end - start)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("GetRange", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayMap",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayMap = method;

        var il = method.GetILGenerator();

        // var result = new List<object>()
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Call callback with (element, index, list) -> create args array
        // var args = new object[] { list[i], (double)i, list }
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // Stack: args array
        // Load callback and args, call InvokeValue
        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal); // args
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Store the call result
        var callResultLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, callResultLocal);

        // result.Add(callResult)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFilter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFilter",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFilter = method;

        var il = method.GetILGenerator();

        // var result = new List<object>()
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipAdd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Call IsTruthy
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        // if (!truthy) skip add
        il.Emit(OpCodes.Brfalse, skipAdd);

        // result.Add(list[i])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(skipAdd);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayForEach = method;

        var il = method.GetILGenerator();

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback (discard result)
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop); // Discard result

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayPush(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPush",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayPush = method;

        var il = method.GetILGenerator();

        // list.Add(element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFind(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFind",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFind = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // if (IsTruthy(result)) return list[i]
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFindIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFindIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFindIndex = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArraySome(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySome",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArraySome = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayEvery(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayEvery",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayEvery = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var continueLoop = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, continueLoop);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayReduce(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReduce",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>), typeof(object[])]
        );
        runtime.ArrayReduce = method;

        var il = method.GetILGenerator();

        // args[0] = callback, args[1] = initial value (optional)
        var accLocal = il.DeclareLocal(typeof(object));
        var indexLocal = il.DeclareLocal(typeof(int));
        var callbackLocal = il.DeclareLocal(typeof(object));

        // callback = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Check if initial value provided (args.Length > 1)
        var hasInitial = il.DefineLabel();
        var noInitial = il.DefineLabel();
        var startLoop = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasInitial);

        // No initial value: acc = list[0], start from index 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        // Empty array with no initial - throw or return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(hasInitial);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(startLoop);
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args: [acc, list[i], i, list]
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, accLocal);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayIncludes",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayIncludes = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (Equals(list[i], searchElement)) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Equals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayIndexOf = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Equals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayJoin(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayJoin",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayJoin = method;

        var il = method.GetILGenerator();

        // separator = arg1 ?? ","
        var sepLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        var hasSep = il.DefineLabel();
        var afterSep = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasSep);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Br, afterSep);
        il.MarkLabel(hasSep);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.MarkLabel(afterSep);
        il.Emit(OpCodes.Stloc, sepLocal);

        // StringBuilder sb = new()
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(separator)
        var skipSep = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipSep);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sepLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipSep);
        // sb.Append(Stringify(list[i]))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayConcat = method;

        var il = method.GetILGenerator();

        // result = new List<object>(list)
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (arg1 is List<object> otherList) result.AddRange(otherList)
        // else result.Add(arg1)
        var notList = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notList);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notList);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayReverse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReverse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>)]
        );
        runtime.ArrayReverse = method;

        var il = method.GetILGenerator();

        // list.Reverse()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Reverse", Type.EmptyTypes)!);

        // return list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // String methods

    private static void EmitStringCharAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringCharAt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(object[])]
        );
        runtime.StringCharAt = method;

        var il = method.GetILGenerator();

        // index = (int)(double)args[0]
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        var returnEmpty = il.DefineLabel();
        var validIndex = il.DefineLabel();

        // if (index < 0) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnEmpty);

        // if (index >= str.Length) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnEmpty);

        // Return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        var charLocal = il.DeclareLocal(typeof(char));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringSubstring(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSubstring",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(object[])]
        );
        runtime.StringSubstring = method;

        var il = method.GetILGenerator();

        // start = Math.Max(0, (int)(double)args[0])
        var startLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);

        // end = args.Length > 1 ? (int)(double)args[1] : str.Length
        var endLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        var hasEnd = il.DefineLabel();
        var afterEnd = il.DefineLabel();
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, afterEnd);
        il.MarkLabel(hasEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.MarkLabel(afterEnd);
        il.Emit(OpCodes.Stloc, endLocal);

        // Clamp end to str.Length
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);

        // if (start >= str.Length || end <= start) return ""
        var validRange = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, validRange);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validRange);
        var validRange2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, validRange2);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validRange2);
        // return str.Substring(start, end - start)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(string), typeof(string)]
        );
        runtime.StringIndexOf = method;

        var il = method.GetILGenerator();

        // return (double)str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("IndexOf", [typeof(string)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplace",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(string), typeof(string)]
        );
        runtime.StringReplace = method;

        var il = method.GetILGenerator();

        // JavaScript replace only replaces first occurrence
        // var index = str.IndexOf(search)
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("IndexOf", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) return str
        var found = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, found);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(found);
        // return str.Substring(0, index) + replacement + str.Substring(index + search.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int), typeof(int)])!);

        il.Emit(OpCodes.Ldarg_2); // replacement

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int)])!);

        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringSplit(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSplit",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(string), typeof(string)]
        );
        runtime.StringSplit = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (separator == "") split into chars
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notEmpty);

        // Split into characters
        var charIndex = il.DeclareLocal(typeof(int));
        var charLocal = il.DeclareLocal(typeof(char));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, charIndex);

        var charLoopStart = il.DefineLabel();
        var charLoopEnd = il.DefineLabel();

        il.MarkLabel(charLoopStart);
        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, charLoopEnd);

        // Get char at index and convert to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, charLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, charIndex);
        il.Emit(OpCodes.Br, charLoopStart);

        il.MarkLabel(charLoopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);
        // Regular split: str.Split(separator)
        var partsLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, (int)StringSplitOptions.None);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Split", [typeof(string), typeof(StringSplitOptions)])!);
        il.Emit(OpCodes.Stloc, partsLocal);

        // Add each part to result
        var partIndex = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, partIndex);

        var partLoopStart = il.DefineLabel();
        var partLoopEnd = il.DefineLabel();

        il.MarkLabel(partLoopStart);
        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, partLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, partIndex);
        il.Emit(OpCodes.Br, partLoopStart);

        il.MarkLabel(partLoopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIncludes",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(string), typeof(string)]
        );
        runtime.StringIncludes = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Contains", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringStartsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringStartsWith",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(string), typeof(string)]
        );
        runtime.StringStartsWith = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringEndsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringEndsWith",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(string), typeof(string)]
        );
        runtime.StringEndsWith = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("EndsWith", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringSlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSlice(string str, int argCount, object[] args) -> string
        // Handles negative indices and optional end parameter
        var method = typeBuilder.DefineMethod(
            "StringSlice",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(int), typeof(object[])]
        );
        runtime.StringSlice = method;

        var il = method.GetILGenerator();
        var startLocal = il.DeclareLocal(typeof(int));
        var endLocal = il.DeclareLocal(typeof(int));
        var lengthLocal = il.DeclareLocal(typeof(int));

        // lengthLocal = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // start = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, startLocal);

        // end = argCount > 1 ? (int)(double)args[1] : length
        var noEndArg = il.DefineLabel();
        var endArgDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noEndArg);
        // has end arg
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endArgDone);
        il.MarkLabel(noEndArg);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endArgDone);

        // Handle negative start: if (start < 0) start = max(0, length + start)
        var startNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);
        // start is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotNegative);

        // Handle negative end: if (end < 0) end = max(0, length + end)
        var endNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNegative);
        // end is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotNegative);

        // Clamp start to length: start = min(start, length)
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);

        // Clamp end to length: end = min(end, length)
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);

        // if (end <= start) return ""
        var returnSubstring = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, returnSubstring);
        // return ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        // return str.Substring(start, end - start)
        il.MarkLabel(returnSubstring);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Substring", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringRepeat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringRepeat(string str, double count) -> string
        var method = typeBuilder.DefineMethod(
            "StringRepeat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(double)]
        );
        runtime.StringRepeat = method;

        var il = method.GetILGenerator();
        var countLocal = il.DeclareLocal(typeof(int));
        var resultLocal = il.DeclareLocal(typeof(string));
        var iLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // count = (int)countArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, countLocal);

        // if (count <= 0 || str.Length == 0) return ""
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // result = ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < count; i++) result += str
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringPadStart(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadStart(string str, int argCount, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringPadStart",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(int), typeof(object[])]
        );
        runtime.StringPadStart = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(typeof(int));
        var padStringLocal = il.DeclareLocal(typeof(string));
        var padLengthLocal = il.DeclareLocal(typeof(int));
        var resultLocal = il.DeclareLocal(typeof(string));
        var iLocal = il.DeclareLocal(typeof(int));
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = argCount > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and prepend
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringPadEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadEnd(string str, int argCount, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringPadEnd",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(int), typeof(object[])]
        );
        runtime.StringPadEnd = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(typeof(int));
        var padStringLocal = il.DeclareLocal(typeof(string));
        var padLengthLocal = il.DeclareLocal(typeof(int));
        var resultLocal = il.DeclareLocal(typeof(string));
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = argCount > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and append
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringCharCodeAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringCharCodeAt(string str, double index) -> double
        var method = typeBuilder.DefineMethod(
            "StringCharCodeAt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(string), typeof(double)]
        );
        runtime.StringCharCodeAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(typeof(int));
        var nanLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0 || index >= str.Length) return NaN
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nanLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, nanLabel);

        // return (double)str[index]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringConcat(string str, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(object[])]
        );
        runtime.StringConcat = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(typeof(string));
        var iLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notNullLabel = il.DefineLabel();

        // result = str
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < args.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // result += args[i]?.ToString() ?? ""
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var concatLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, concatLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.MarkLabel(concatLabel);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringLastIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringLastIndexOf(string str, string search) -> double
        var method = typeBuilder.DefineMethod(
            "StringLastIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(string), typeof(string)]
        );
        runtime.StringLastIndexOf = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("LastIndexOf", [typeof(string)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringReplaceAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringReplaceAll(string str, string search, string replacement) -> string
        var method = typeBuilder.DefineMethod(
            "StringReplaceAll",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(string), typeof(string)]
        );
        runtime.StringReplaceAll = method;

        var il = method.GetILGenerator();
        var returnOriginal = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (search.Length == 0) return str
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // return str.Replace(search, replacement)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Replace", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringAt(string str, double index) -> object (string or null)
        var method = typeBuilder.DefineMethod(
            "StringAt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(string), typeof(double)]
        );
        runtime.StringAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(typeof(int));
        var lengthLocal = il.DeclareLocal(typeof(int));
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // length = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) index = length + index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegative = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegative);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(notNegative);

        // if (index < 0 || index >= length) return null
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nullLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, nullLabel);

        // return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        // Box the char and call ToString on it
        il.Emit(OpCodes.Box, typeof(char));
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateObject",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<string, object>),
            [typeof(Dictionary<string, object>)]
        );
        runtime.CreateObject = method;

        var il = method.GetILGenerator();
        // Just return the dictionary as-is (it's already created)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFieldsProperty(object obj, string name) -> object
        // Uses reflection to access _fields dictionary on class instances
        // IMPORTANT: Check for getter methods (get_<name>) first, then fall back to _fields
        // Walks up the type hierarchy to find fields in parent classes
        var method = typeBuilder.DefineMethod(
            "GetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetFieldsProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var tryMethodLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var valueLocal = il.DeclareLocal(typeof(object));
        var getterMethodLocal = il.DeclareLocal(typeof(MethodInfo));
        var currentTypeLocal = il.DeclareLocal(typeof(Type));

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check for getter method first: var getterMethod = obj.GetType().GetMethod("get_" + name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "get_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, getterMethodLocal);

        // if (getterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // return getterMethod.Invoke(obj, null);
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy
        il.MarkLabel(tryFieldsLabel);

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);

        // Loop through type hierarchy
        var loopStart = il.DefineLabel();
        var nextType = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (currentType == null) goto tryMethod;
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, tryMethodLabel);

        // var fieldsField = currentType.GetField("_fields", DeclaredOnly | Instance | NonPublic);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // if (dict.TryGetValue(name, out value)) return value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Br, nextType);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Try to find a method with this name and wrap as TSFunction
        il.MarkLabel(tryMethodLabel);

        // First try array methods if it's an array
        var tryReflectionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetArrayMethod);
        var arrayMethodLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, arrayMethodLocal);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Brfalse, tryReflectionLabel);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Ret);

        // Try reflection for regular methods
        il.MarkLabel(tryReflectionLabel);
        // var methodInfo = obj.GetType().GetMethod(name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        var methodLocal = il.DeclareLocal(typeof(MethodInfo));
        il.Emit(OpCodes.Stloc, methodLocal);

        // if (methodInfo == null) return null;
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // return new $TSFunction(obj, methodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // SetFieldsProperty(object obj, string name, object value) -> void
        // Uses reflection to access _fields dictionary on class instances
        // IMPORTANT: Check for setter methods (set_<name>) first, then fall back to _fields
        var method = typeBuilder.DefineMethod(
            "SetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object), typeof(string), typeof(object)]
        );
        runtime.SetFieldsProperty = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var setterMethodLocal = il.DeclareLocal(typeof(MethodInfo));
        var argsArrayLocal = il.DeclareLocal(typeof(object[]));

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check for setter method first: var setterMethod = obj.GetType().GetMethod("set_" + name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "set_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, setterMethodLocal);

        // if (setterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // Create args array: new object[] { value }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);

        // setterMethod.Invoke(obj, args); return;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Pop); // Discard return value (setters return void but Invoke returns object)
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        // Add currentType local
        var currentTypeLocal = il.DeclareLocal(typeof(Type));

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);

        // Loop through type hierarchy
        var loopStart = il.DefineLabel();
        var nextType = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (currentType == null) return;
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // var fieldsField = currentType.GetField("_fields", DeclaredOnly | Instance | NonPublic);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetArrayMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetArrayMethod(object arr, string methodName) -> TSFunction or null
        // Maps TypeScript array method names to .NET List methods
        var method = typeBuilder.DefineMethod(
            "GetArrayMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetArrayMethod = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();

        // Check if obj is List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notArrayLabel);

        // Map TypeScript method name to .NET method name
        // push -> Add, pop -> RemoveAt(Count-1), etc.
        var pushLabel = il.DefineLabel();
        var popLabel = il.DefineLabel();
        var shiftLabel = il.DefineLabel();

        // Check for "push"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "push");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, pushLabel);

        // Check for "pop"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pop");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, popLabel);

        // Unknown array method - return null
        il.Emit(OpCodes.Br, nullLabel);

        // Handle push - wrap List.Add as TSFunction
        il.MarkLabel(pushLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, typeof(List<object>).GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Ldtoken, typeof(List<object>));
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        // Handle pop - need special handling since pop returns removed element
        il.MarkLabel(popLabel);
        // For pop, we'll create a TSFunction that wraps a helper method
        // For now, return null and handle pop differently
        il.Emit(OpCodes.Br, nullLabel);

        il.MarkLabel(notArrayLabel);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // List - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default - try to access _fields dictionary via reflection for class instances
        var classInstanceLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, classInstanceLabel);

        // Class instance handler - uses reflection to access _fields
        il.MarkLabel(classInstanceLabel);
        // Call GetFieldsProperty(obj, name) helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // dict.TryGetValue(name, out value) ? value : null
        var valueLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLengthLabel);
        // For other properties on List (like methods push, pop, etc.), use GetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        var notStrLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrLenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrLenLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object), typeof(string), typeof(object)]
        );
        runtime.SetProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - try SetFieldsProperty for class instances
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitMergeIntoObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MergeIntoObject",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(Dictionary<string, object>), typeof(object)]
        );
        runtime.MergeIntoObject = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Check if source is dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - do nothing
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Iterate and copy
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current and add to target
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        var kvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.GetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Dict with string key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use GetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: use GetSymbolDict(obj).TryGetValue(index, out value)
        il.MarkLabel(symbolKeyLabel);
        var symbolDictLocal = il.DeclareLocal(typeof(Dictionary<object, object?>));
        var symbolValueLocal = il.DeclareLocal(typeof(object));
        // var symbolDict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);
        // if (symbolDict.TryGetValue(index, out value)) return value; else return null;
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, symbolValueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object?>).GetMethod("TryGetValue")!);
        var symbolFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, symbolFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolFoundLabel);
        il.Emit(OpCodes.Ldloc, symbolValueLocal);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use GetFieldsProperty(obj, index as string)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise return null (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        var valueLocal = il.DeclareLocal(typeof(object));

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundNumLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundNumLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object), typeof(object), typeof(object)]
        );
        runtime.SetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use SetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return (ignore)
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: GetSymbolDict(obj)[index] = value
        il.MarkLabel(symbolKeyLabel);
        // GetSymbolDict(obj)[index] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object?>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use SetFieldsProperty(obj, index as string, value)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("set_Item", [typeof(int), typeof(object)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise ignore (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitInvokeValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeValue",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object[])]
        );
        runtime.InvokeValue = method;

        var il = method.GetILGenerator();
        // Check if value is $TSFunction and call Invoke
        // For now, use reflection
        il.Emit(OpCodes.Ldarg_0);
        var nullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Try to find and call Invoke method
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Dup);
        var noInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noInvokeLabel);

        // Has Invoke - call it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noInvokeLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetSuperMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetSuperMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetSuperMethod = method;

        var il = method.GetILGenerator();
        // Get base type and find method
        il.Emit(OpCodes.Ldarg_0);
        var nullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Exception),
            [typeof(object)]
        );
        runtime.CreateException = method;

        var il = method.GetILGenerator();
        var exLocal = il.DeclareLocal(typeof(Exception));

        // var ex = new Exception(value?.ToString() ?? "null")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, exLocal);

        // ex.Data["__tsValue"] = value;  (preserve original value)
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("set_Item")!);

        // return ex;
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWrapException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(Exception)]
        );
        runtime.WrapException = method;

        var il = method.GetILGenerator();
        var fallbackLabel = il.DefineLabel();
        var tsValueLocal = il.DeclareLocal(typeof(object));

        // Check if ex.Data contains "__tsValue" (TypeScript throw value)
        // if (ex.Data.Contains("__tsValue")) return ex.Data["__tsValue"];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("Contains")!);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // Return the original TypeScript value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("get_Item")!);
        il.Emit(OpCodes.Ret);

        // Fallback: wrap standard .NET exceptions as Dictionary
        il.MarkLabel(fallbackLabel);
        // return new Dictionary<string, object> { ["message"] = ex.Message, ["name"] = ex.GetType().Name }
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRandom(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder randomField)
    {
        var method = typeBuilder.DefineMethod(
            "Random",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            Type.EmptyTypes
        );
        runtime.Random = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, randomField);
        il.Emit(OpCodes.Callvirt, typeof(Random).GetMethod("NextDouble")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetEnumMemberName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEnumMemberName",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(double), typeof(double[]), typeof(string[])]
        );
        runtime.GetEnumMemberName = method;

        var il = method.GetILGenerator();
        // Simple linear search through keys to find matching value
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if keys[i] == value
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatchLabel);

        // Found - return values[i]
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatchLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Not found - throw
        il.Emit(OpCodes.Ldstr, "Value not found in enum");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    private static void EmitConcatTemplate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatTemplate",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object[])]
        );
        runtime.ConcatTemplate = method;

        var il = method.GetILGenerator();

        // Use StringBuilder to concatenate stringified parts
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        var indexLocal = il.DeclareLocal(typeof(int));
        var lengthLocal = il.DeclareLocal(typeof(int));

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // length = parts.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= length) goto end
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // sb.Append(Stringify(parts[index]))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop); // discard StringBuilder return value

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitObjectRest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectRest",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<string, object>),
            [typeof(Dictionary<string, object>), typeof(List<object>)]
        );
        runtime.ObjectRest = method;

        var il = method.GetILGenerator();

        // Create result dictionary
        var resultLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Create HashSet<string> from excludeKeys
        var excludeSetLocal = il.DeclareLocal(typeof(HashSet<string>));
        il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, excludeSetLocal);

        // Add each exclude key to the set
        var excludeIndexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);

        var excludeLoopStart = il.DefineLabel();
        var excludeLoopEnd = il.DefineLabel();

        il.MarkLabel(excludeLoopStart);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, excludeLoopEnd);

        // Get excludeKeys[i] and add to set if not null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        var keyLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, keyLocal);

        var skipAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add", [typeof(string)])!);
        il.Emit(OpCodes.Pop); // discard bool return

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);
        il.Emit(OpCodes.Br, excludeLoopStart);

        il.MarkLabel(excludeLoopEnd);

        // Iterate over source dictionary keys
        // Get enumerator from source.Keys
        var keysEnumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.KeyCollection.Enumerator));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);
        // MoveNext
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get Current key
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.KeyCollection.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        var currentKeyLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // Check if key is in excludeSet
        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Contains", [typeof(string)])!);
        var skipKey = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipKey);

        // Add to result: result[key] = source[key]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Item")!.GetSetMethod()!);

        il.MarkLabel(skipKey);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.KeyCollection.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetValues = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Check if Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default - empty list
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Get values: new List<object>(dict.Values)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Values")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetEntries is more complex - we need to iterate and create [key, value] pairs
        // We'll delegate to a static helper in the emitted class
        var method = typeBuilder.DefineMethod(
            "GetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetEntries = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var entryLocal = il.DeclareLocal(typeof(List<object>));

        // Check if Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default - empty list
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);

        // Create result list
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop start
        il.MarkLabel(loopStart);

        // MoveNext
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // Create entry [key, value]
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);

        // Add key
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add value
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add entry to result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Loop back
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // Return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIsArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // False
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, endLabel);

        // True
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitJsonParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonParse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.JsonParse = method;

        // We need to emit a call to a method that converts JSON to Dict/List
        // Since this requires recursive conversion, we emit a helper method
        var il = method.GetILGenerator();

        // Call JsonParseHelper which we'll emit separately
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseHelper(typeBuilder));
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonParseHelper(TypeBuilder typeBuilder)
    {
        // Parse JSON using RuntimeTypes helper
        var method = typeBuilder.DefineMethod(
            "JsonParseHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );

        var il = method.GetILGenerator();

        // Call RuntimeTypes.JsonParse directly - this method exists in the emitted assembly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseStaticHelper(typeBuilder));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static MethodBuilder EmitJsonParseStaticHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ParseJsonValue",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );

        var il = method.GetILGenerator();

        // Simple implementation: just call ToString and return
        // This won't properly parse, but at least we can test the infrastructure
        var strLocal = il.DeclareLocal(typeof(string));
        var notNullLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Parse using JsonDocument - need to include JsonDocumentOptions parameter
        var parseMethod = typeof(System.Text.Json.JsonDocument).GetMethod("Parse",
            [typeof(string), typeof(System.Text.Json.JsonDocumentOptions)])!;
        var optionsLocal = il.DeclareLocal(typeof(System.Text.Json.JsonDocumentOptions));
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Initobj, typeof(System.Text.Json.JsonDocumentOptions));
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, parseMethod);

        // Get RootElement
        var docLocal = il.DeclareLocal(typeof(System.Text.Json.JsonDocument));
        il.Emit(OpCodes.Stloc, docLocal);
        il.Emit(OpCodes.Ldloc, docLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Json.JsonDocument).GetProperty("RootElement")!.GetGetMethod()!);

        // Store element first before calling Clone (Clone is an instance method on struct)
        var elemLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));
        il.Emit(OpCodes.Stloc, elemLocal);

        // Clone the element to avoid disposal issues (need address for struct instance method)
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("Clone")!);

        // Store cloned element and convert it
        var clonedLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.Emit(OpCodes.Ldloca, clonedLocal);
        il.Emit(OpCodes.Call, EmitConvertJsonElementHelper(typeBuilder));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static MethodBuilder EmitConvertJsonElementHelper(TypeBuilder typeBuilder)
    {
        // Convert JsonElement to appropriate runtime type
        var method = typeBuilder.DefineMethod(
            "ConvertJsonElement",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(System.Text.Json.JsonElement).MakeByRefType()] // byref for struct
        );

        var il = method.GetILGenerator();

        var resultDictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var resultListLocal = il.DeclareLocal(typeof(List<object>));
        var propEnumLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement.ObjectEnumerator));
        var arrEnumLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement.ArrayEnumerator));
        var propLocal = il.DeclareLocal(typeof(System.Text.Json.JsonProperty));
        var elemLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));

        var undefinedLabel = il.DefineLabel();
        var objectLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var trueLiteralLabel = il.DefineLabel();
        var falseLiteralLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();
        var objLoopStart = il.DefineLabel();
        var objLoopEnd = il.DefineLabel();
        var arrLoopStart = il.DefineLabel();
        var arrLoopEnd = il.DefineLabel();

        // switch on element.ValueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetProperty("ValueKind")!.GetGetMethod()!);

        // Check each value kind
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Object);
        il.Emit(OpCodes.Beq, objectLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Array);
        il.Emit(OpCodes.Beq, arrayLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.String);
        il.Emit(OpCodes.Beq, stringLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Number);
        il.Emit(OpCodes.Beq, numberLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.True);
        il.Emit(OpCodes.Beq, trueLiteralLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.False);
        il.Emit(OpCodes.Beq, falseLiteralLabel);

        il.Emit(OpCodes.Pop); // pop the valueKind we've been checking
        il.Emit(OpCodes.Br, nullLabel);

        // Object
        il.MarkLabel(objectLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("EnumerateObject")!);
        il.Emit(OpCodes.Stloc, propEnumLocal);

        il.MarkLabel(objLoopStart);
        il.Emit(OpCodes.Ldloca, propEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ObjectEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, objLoopEnd);

        il.Emit(OpCodes.Ldloca, propEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ObjectEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, propLocal);

        // dict[name] = ConvertJsonElement(ref value)
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldloca, propLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonProperty).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, propLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonProperty).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Br, objLoopStart);

        il.MarkLabel(objLoopEnd);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ret);

        // Array
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("EnumerateArray")!);
        il.Emit(OpCodes.Stloc, arrEnumLocal);

        il.MarkLabel(arrLoopStart);
        il.Emit(OpCodes.Ldloca, arrEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ArrayEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, arrLoopEnd);

        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ldloca, arrEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ArrayEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Br, arrLoopStart);

        il.MarkLabel(arrLoopEnd);
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ret);

        // String
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("GetString")!);
        il.Emit(OpCodes.Ret);

        // Number
        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("GetDouble")!);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);

        // True
        il.MarkLabel(trueLiteralLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        // False
        il.MarkLabel(falseLiteralLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        // Null/Undefined
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static void EmitJsonParseWithReviver(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the ApplyReviver helper
        var applyReviver = EmitApplyReviverHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonParseWithReviver",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.JsonParseWithReviver = method;

        var il = method.GetILGenerator();
        var noReviverLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if reviver is null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noReviverLabel);

        // Parse JSON
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        // Apply reviver: ApplyReviver(parsed, "", reviver)
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, applyReviver);
        il.Emit(OpCodes.Br, endLabel);

        // No reviver - just parse
        il.MarkLabel(noReviverLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitApplyReviverHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ApplyReviver(object value, object key, object reviver) -> object
        var method = typeBuilder.DefineMethod(
            "ApplyReviver",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object), typeof(object)]
        );

        var il = method.GetILGenerator();

        // Locals
        var newDictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var newListLocal = il.DeclareLocal(typeof(List<object>));
        var enumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var listEnumLocal = il.DeclareLocal(typeof(List<object>.Enumerator));
        var kvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var elemLocal = il.DeclareLocal(typeof(object));
        var resultLocal = il.DeclareLocal(typeof(object));
        var valueLocal = il.DeclareLocal(typeof(object));
        var argsLocal = il.DeclareLocal(typeof(object[]));

        var notDictLabel = il.DefineLabel();
        var notListLabel = il.DefineLabel();
        var invokeReviverLabel = il.DefineLabel();
        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();

        // Store value in local for modification
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Check if value is Dictionary<string, object>
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // It's a dictionary - process it
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, newDictLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumLocal);

        // Loop
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get current kvp
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // Recursively apply reviver: result = ApplyReviver(kvp.Value, kvp.Key, reviver)
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // If result is not null, add to new dict
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brfalse, dictLoopStart);

        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, invokeReviverLabel);

        // Check if value is List<object>
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notListLabel);

        // It's a list - process it
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, newListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Get list enumerator
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, listEnumLocal);

        // Loop
        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloca, listEnumLocal);
        il.Emit(OpCodes.Call, typeof(List<object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, listLoopEnd);

        // Get current element
        il.Emit(OpCodes.Ldloca, listEnumLocal);
        il.Emit(OpCodes.Call, typeof(List<object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);

        // Recursively apply reviver: result = ApplyReviver(elem, index, reviver)
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add result to new list
        il.Emit(OpCodes.Ldloc, newListLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Increment index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Ldloc, newListLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, invokeReviverLabel);

        // Not dict or list - just call reviver on the value
        il.MarkLabel(notListLabel);

        // Call reviver for this node
        il.MarkLabel(invokeReviverLabel);

        // Create args array [key, value]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // Call reviver.Invoke(args)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static void EmitJsonStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.JsonStringify = method;

        var il = method.GetILGenerator();

        // Call the stringify helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0); // indent = 0
        il.Emit(OpCodes.Ldc_I4_0); // depth = 0
        il.Emit(OpCodes.Call, EmitJsonStringifyHelper(typeBuilder));
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonStringifyHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "StringifyValue",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(int), typeof(int)] // value, indent, depth
        );

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumber(il);

        // string - use JsonSerializer.Serialize<string>(value, null)
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldnull); // options = null
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array
        il.MarkLabel(listLabel);
        EmitStringifyArray(il, method);

        // Dictionary<string, object> - stringify object
        il.MarkLabel(dictLabel);
        EmitStringifyObject(il, method);

        return method;
    }

    private static void EmitFormatNumber(ILGenerator il)
    {
        var local = il.DeclareLocal(typeof(double));
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, local);

        // Check NaN/Infinity
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        // Check if integer
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        // Float format
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        // NaN/Infinity -> "null"
        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // Integer format
        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, typeof(long).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyArray(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var arrLocal = il.DeclareLocal(typeof(List<object>));
        var iLocal = il.DeclareLocal(typeof(int));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(StringifyValue(arr[i], indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyObject(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var firstLocal = il.DeclareLocal(typeof(bool));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(JsonSerializer.Serialize<string>(key, null));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldnull); // options = null
        var serializeKeyMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeKeyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValue(value, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitJsonStringifyFull(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the helper that supports allowedKeys
        var stringifyWithKeys = EmitJsonStringifyWithKeysHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "JsonStringifyFull",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object), typeof(object)]
        );
        runtime.JsonStringifyFull = method;

        var il = method.GetILGenerator();

        // Declare ALL locals upfront
        var indentLocal = il.DeclareLocal(typeof(int));
        var allowedKeysLocal = il.DeclareLocal(typeof(HashSet<string>));
        var listLocal = il.DeclareLocal(typeof(List<object>));
        var listIdxLocal = il.DeclareLocal(typeof(int));

        // Define labels
        var notDoubleLabel = il.DefineLabel();
        var notStringLabel = il.DefineLabel();
        var doneIndentLabel = il.DefineLabel();
        var notListLabel = il.DefineLabel();
        var doneKeysLabel = il.DefineLabel();
        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        var notStringItem = il.DefineLabel();

        // Parse space argument to get indent value
        // if (space is double d)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // indent = (int)Math.Min(d, 10)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(double), typeof(double)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indentLocal);
        il.Emit(OpCodes.Br, doneIndentLabel);

        // else if (space is string s)
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // indent = Math.Min(s.Length, 10)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, indentLocal);
        il.Emit(OpCodes.Br, doneIndentLabel);

        // else indent = 0
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indentLocal);

        il.MarkLabel(doneIndentLabel);

        // Parse replacer argument to get allowedKeys
        // if (replacer is List<object?> list)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notListLabel);

        // allowedKeys = new HashSet<string>()
        il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        // Loop through list and add strings
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, listIdxLocal);

        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, listLoopEnd);

        // if (list[i] is string s) allowedKeys.Add(s)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringItem);

        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notStringItem);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, listIdxLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Br, doneKeysLabel);

        // else allowedKeys = null
        il.MarkLabel(notListLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        il.MarkLabel(doneKeysLabel);

        // Call StringifyWithKeys(value, allowedKeys, indent, depth=0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, indentLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, stringifyWithKeys);
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonStringifyWithKeysHelper(TypeBuilder typeBuilder)
    {
        // StringifyWithKeys(object value, HashSet<string> allowedKeys, int indent, int depth) -> string
        var method = typeBuilder.DefineMethod(
            "StringifyValueWithKeys",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(HashSet<string>), typeof(int), typeof(int)]
        );

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumberForFullStringify(il);

        // string
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array with indent
        il.MarkLabel(listLabel);
        EmitStringifyArrayWithIndent(il, method);

        // Dictionary<string, object> - stringify object with indent and allowedKeys
        il.MarkLabel(dictLabel);
        EmitStringifyObjectWithKeysAndIndent(il, method);

        return method;
    }

    private static void EmitFormatNumberForFullStringify(ILGenerator il)
    {
        var local = il.DeclareLocal(typeof(double));
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, local);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, typeof(long).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyArrayWithIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var arrLocal = il.DeclareLocal(typeof(List<object>));
        var iLocal = il.DeclareLocal(typeof(int));
        var newlineLocal = il.DeclareLocal(typeof(string));
        var closeLocal = il.DeclareLocal(typeof(string));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();
        var doneFormatting = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // if (indent > 0) compute newline/close strings
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        // newline = "\n" + new string(' ', indent * (depth + 1))
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, newlineLocal);

        // close = "\n" + new string(' ', indent * depth)
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(arr[i], allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyObjectWithKeysAndIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var firstLocal = il.DeclareLocal(typeof(bool));
        var newlineLocal = il.DeclareLocal(typeof(string));
        var closeLocal = il.DeclareLocal(typeof(string));
        var keyStrLocal = il.DeclareLocal(typeof(string));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // Compute newline/close if indent > 0
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, newlineLocal);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // first = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // foreach (var kv in dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (allowedKeys != null && !allowedKeys.Contains(kv.Key)) continue;
        var skipKeyCheck = il.DefineLabel();
        var keyAllowed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Brfalse, skipKeyCheck);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Contains", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, keyAllowed);
        il.Emit(OpCodes.Br, loopStart); // skip this key

        il.MarkLabel(skipKeyCheck);
        il.MarkLabel(keyAllowed);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // first = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // keyStr = JsonSerializer.Serialize(kv.Key)
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Stloc, keyStrLocal);

        // sb.Append(keyStr);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(indent > 0 ? ": " : ":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_2);
        var colonNoSpace = il.DefineLabel();
        var colonDone = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, colonNoSpace);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Br, colonDone);
        il.MarkLabel(colonNoSpace);
        il.Emit(OpCodes.Ldstr, ":");
        il.MarkLabel(colonDone);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(kv.Value, allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static Dictionary&lt;object, object?&gt; GetSymbolDict(object obj)
    /// Returns the symbol dictionary for an object from the ConditionalWeakTable.
    /// </summary>
    private static void EmitGetSymbolDict(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder symbolStorageField)
    {
        var symbolDictType = typeof(Dictionary<object, object?>);
        var symbolStorageType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), symbolDictType);

        var method = typeBuilder.DefineMethod(
            "GetSymbolDict",
            MethodAttributes.Private | MethodAttributes.Static,
            symbolDictType,
            [typeof(object)]
        );
        runtime.GetSymbolDictMethod = method;

        var il = method.GetILGenerator();

        // return _symbolStorage.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, symbolStorageField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, symbolStorageType.GetMethod("GetOrCreateValue", [typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool IsSymbol(object obj)
    /// Returns true if the object is a TSSymbol.
    /// </summary>
    private static void EmitIsSymbol(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsSymbol",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsSymbolMethod = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (obj == null) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return obj.GetType().Name == "$TSSymbol";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "$TSSymbol");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region BigInt Methods

    private static void EmitCreateBigInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // CreateBigInt: object -> BigInteger (boxed)
        var method = typeBuilder.DefineMethod(
            "CreateBigInt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.CreateBigInt = method;

        var il = method.GetILGenerator();
        var bigIntType = typeof(System.Numerics.BigInteger);

        // If already BigInteger, return as-is (boxed)
        var notBigIntLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, bigIntType);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBigIntLabel);

        // If double, convert to BigInteger
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Newobj, bigIntType.GetConstructor([typeof(long)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);

        // If string, parse it
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        // Handle hex prefix "0x" or "0X"
        var hexCheckLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Stloc, hexCheckLocal);
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldstr, "0x");
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        var notHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHexLabel);
        // Parse hex - prepend "0" to ensure positive interpretation
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        il.Emit(OpCodes.Call, bigIntType.GetMethod("Parse", [typeof(string), typeof(System.Globalization.NumberStyles)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notHexLabel);
        // Parse decimal
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Call, bigIntType.GetMethod("Parse", [typeof(string)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);
        // Default: throw or return 0n
        il.Emit(OpCodes.Ldstr, "Cannot convert to BigInt");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    private static void EmitBigIntArithmetic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        // Helper to emit binary BigInt operations
        void EmitBinaryBigIntOp(string name, string opMethodName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntAdd") runtime.BigIntAdd = method;
            else if (name == "BigIntSubtract") runtime.BigIntSubtract = method;
            else if (name == "BigIntMultiply") runtime.BigIntMultiply = method;
            else if (name == "BigIntDivide") runtime.BigIntDivide = method;
            else if (name == "BigIntRemainder") runtime.BigIntRemainder = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opMethodName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        EmitBinaryBigIntOp("BigIntAdd", "op_Addition", null!);
        EmitBinaryBigIntOp("BigIntSubtract", "op_Subtraction", null!);
        EmitBinaryBigIntOp("BigIntMultiply", "op_Multiply", null!);
        EmitBinaryBigIntOp("BigIntDivide", "op_Division", null!);
        EmitBinaryBigIntOp("BigIntRemainder", "op_Modulus", null!);

        // BigIntPow
        {
            var method = typeBuilder.DefineMethod(
                "BigIntPow",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntPow = method;

            var il = method.GetILGenerator();
            // Use explicit int cast - find the method that returns int
            var explicitToIntMethod = bigIntType.GetMethods().First(m =>
                m.Name == "op_Explicit" && m.ReturnType == typeof(int) &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert exponent to int for BigInteger.Pow (value on stack, not address)
            il.Emit(OpCodes.Call, explicitToIntMethod);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("Pow", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntNegate
        {
            var method = typeBuilder.DefineMethod(
                "BigIntNegate",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object)]
            );
            runtime.BigIntNegate = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_UnaryNegation", [bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }

    private static void EmitBigIntComparison(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        void EmitCompare(string name, string opName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(bool),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntEquals") runtime.BigIntEquals = method;
            else if (name == "BigIntLessThan") runtime.BigIntLessThan = method;
            else if (name == "BigIntLessThanOrEqual") runtime.BigIntLessThanOrEqual = method;
            else if (name == "BigIntGreaterThan") runtime.BigIntGreaterThan = method;
            else if (name == "BigIntGreaterThanOrEqual") runtime.BigIntGreaterThanOrEqual = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Ret);
        }

        EmitCompare("BigIntEquals", "op_Equality", null!);
        EmitCompare("BigIntLessThan", "op_LessThan", null!);
        EmitCompare("BigIntLessThanOrEqual", "op_LessThanOrEqual", null!);
        EmitCompare("BigIntGreaterThan", "op_GreaterThan", null!);
        EmitCompare("BigIntGreaterThanOrEqual", "op_GreaterThanOrEqual", null!);
    }

    private static void EmitBigIntBitwise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        void EmitBinaryBitwise(string name, string opName)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntBitwiseAnd") runtime.BigIntBitwiseAnd = method;
            else if (name == "BigIntBitwiseOr") runtime.BigIntBitwiseOr = method;
            else if (name == "BigIntBitwiseXor") runtime.BigIntBitwiseXor = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        EmitBinaryBitwise("BigIntBitwiseAnd", "op_BitwiseAnd");
        EmitBinaryBitwise("BigIntBitwiseOr", "op_BitwiseOr");
        EmitBinaryBitwise("BigIntBitwiseXor", "op_ExclusiveOr");

        // BigIntBitwiseNot
        {
            var method = typeBuilder.DefineMethod(
                "BigIntBitwiseNot",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object)]
            );
            runtime.BigIntBitwiseNot = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_OnesComplement", [bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // Get the explicit to int method once for shift operations
        var explicitToInt = bigIntType.GetMethods().First(m =>
            m.Name == "op_Explicit" && m.ReturnType == typeof(int) &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

        // BigIntLeftShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntLeftShift",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntLeftShift = method;

            var il = method.GetILGenerator();
            // Stack after setup: [value, shiftAmount]
            // Need: [value, (int)shiftAmount] for op_LeftShift
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert shift count to int (value on stack)
            il.Emit(OpCodes.Call, explicitToInt);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_LeftShift", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntRightShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntRightShift",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntRightShift = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert shift count to int (value on stack)
            il.Emit(OpCodes.Call, explicitToInt);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_RightShift", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }

    #endregion
}
