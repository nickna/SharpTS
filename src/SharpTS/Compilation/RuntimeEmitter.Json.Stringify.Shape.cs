using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _canUseJsonShapeMethod;
    private MethodBuilder? _canUseJsonShapeNodeMethod;
    private MethodBuilder? _jsonShapePrototypesSafeMethod;
    private MethodBuilder? _appendJsonShapedValueMethod;
    private FieldBuilder? _jsonShapeTableField;
    private MethodBuilder? _getJsonShapeTableMethod;
    private MethodBuilder? _registerJsonShapeMethod;
    private MethodBuilder? _tryGetJsonShapeMethod;

    private (MethodBuilder Register, MethodBuilder TryGet) EmitJsonShapeAssociationHelpers(
        TypeBuilder typeBuilder)
    {
        if (_registerJsonShapeMethod is not null && _tryGetJsonShapeMethod is not null)
            return (_registerJsonShapeMethod, _tryGetJsonShapeMethod);

        var tableOpenType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>);
        var tableOpenArguments = tableOpenType.GetGenericArguments();
        var tableType = EmitGenerics.MakeGenericType(
            tableOpenType,
            _types.String,
            _types.Object);
        var tableConstructor = EmitterTypeHelpers.ResolveConstructor(
            tableType,
            tableOpenType.GetConstructor(Type.EmptyTypes)!);
        var tableRemove = EmitterTypeHelpers.ResolveMethod(
            tableType,
            tableOpenType.GetMethod("Remove", [tableOpenArguments[0]])!);
        var tableAdd = EmitterTypeHelpers.ResolveMethod(
            tableType,
            tableOpenType.GetMethod("Add", [tableOpenArguments[0], tableOpenArguments[1]])!);
        var tableTryGetValue = EmitterTypeHelpers.ResolveMethod(
            tableType,
            tableOpenType.GetMethod(
                "TryGetValue",
                [tableOpenArguments[0], tableOpenArguments[1].MakeByRefType()])!);
        _jsonShapeTableField = typeBuilder.DefineField(
            "_jsonShapes", tableType, FieldAttributes.Private | FieldAttributes.Static);

        var getTable = typeBuilder.DefineMethod(
            "GetJsonShapeTable",
            MethodAttributes.Private | MethodAttributes.Static,
            tableType,
            Type.EmptyTypes);
        _getJsonShapeTableMethod = getTable;
        var getIl = getTable.GetILGenerator();
        var ready = getIl.DefineLabel();
        getIl.Emit(OpCodes.Ldsfld, _jsonShapeTableField);
        getIl.Emit(OpCodes.Dup);
        getIl.Emit(OpCodes.Brtrue, ready);
        getIl.Emit(OpCodes.Pop);
        getIl.Emit(OpCodes.Ldsflda, _jsonShapeTableField);
        getIl.Emit(OpCodes.Newobj, tableConstructor);
        getIl.Emit(OpCodes.Ldnull);
        var compareExchangeDefinition = typeof(System.Threading.Interlocked).GetMethods()
            .Single(candidate => candidate.Name == "CompareExchange" &&
                candidate.IsGenericMethodDefinition &&
                candidate.GetParameters().Length == 3);
        var compareExchange = EmitGenerics.MakeGenericMethod(
            compareExchangeDefinition, tableType);
        getIl.Emit(OpCodes.Call, compareExchange);
        getIl.Emit(OpCodes.Pop);
        getIl.Emit(OpCodes.Ldsfld, _jsonShapeTableField);
        getIl.MarkLabel(ready);
        getIl.Emit(OpCodes.Ret);

        var register = typeBuilder.DefineMethod(
            "RegisterJsonShape",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.String, _types.Object]);
        _registerJsonShapeMethod = register;
        var registerIl = register.GetILGenerator();
        var tableLocal = registerIl.DeclareLocal(tableType);
        registerIl.Emit(OpCodes.Call, getTable);
        registerIl.Emit(OpCodes.Stloc, tableLocal);
        registerIl.Emit(OpCodes.Ldloc, tableLocal);
        registerIl.Emit(OpCodes.Ldarg_0);
        registerIl.Emit(OpCodes.Callvirt, tableRemove);
        registerIl.Emit(OpCodes.Pop);
        registerIl.Emit(OpCodes.Ldloc, tableLocal);
        registerIl.Emit(OpCodes.Ldarg_0);
        registerIl.Emit(OpCodes.Ldarg_1);
        registerIl.Emit(OpCodes.Callvirt, tableAdd);
        registerIl.Emit(OpCodes.Ret);

        var tryGet = typeBuilder.DefineMethod(
            "TryGetJsonShape",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object.MakeByRefType()]);
        _tryGetJsonShapeMethod = tryGet;
        var tryIl = tryGet.GetILGenerator();
        var stringLocal = tryIl.DeclareLocal(_types.String);
        var miss = tryIl.DefineLabel();
        var missWithResidual = tryIl.DefineLabel();
        tryIl.Emit(OpCodes.Ldarg_0);
        tryIl.Emit(OpCodes.Isinst, _types.String);
        tryIl.Emit(OpCodes.Stloc, stringLocal);
        tryIl.Emit(OpCodes.Ldloc, stringLocal);
        tryIl.Emit(OpCodes.Brfalse, miss);
        tryIl.Emit(OpCodes.Ldsfld, _jsonShapeTableField);
        tryIl.Emit(OpCodes.Dup);
        tryIl.Emit(OpCodes.Brfalse, missWithResidual);
        tryIl.Emit(OpCodes.Ldloc, stringLocal);
        tryIl.Emit(OpCodes.Ldarg_1);
        tryIl.Emit(OpCodes.Callvirt, tableTryGetValue);
        tryIl.Emit(OpCodes.Ret);
        tryIl.MarkLabel(missWithResidual);
        tryIl.Emit(OpCodes.Pop);
        tryIl.MarkLabel(miss);
        tryIl.Emit(OpCodes.Ldarg_1);
        tryIl.Emit(OpCodes.Ldnull);
        tryIl.Emit(OpCodes.Stind_Ref);
        tryIl.Emit(OpCodes.Ldc_I4_0);
        tryIl.Emit(OpCodes.Ret);
        return (register, tryGet);
    }

    /// <summary>
    /// Ensures the implicit Array/Object prototype chain cannot introduce a
    /// toJSON hook. The check reads only dictionaries and descriptor metadata;
    /// it never invokes a getter, so a failed guard can fall back exactly once.
    /// </summary>
    private MethodBuilder EmitJsonShapePrototypesSafeHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_jsonShapePrototypesSafeMethod is not null)
            return _jsonShapePrototypesSafeMethod;

        var method = typeBuilder.DefineMethod(
            "JsonShapePrototypesSafe",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            Type.EmptyTypes);
        _jsonShapePrototypesSafeMethod = method;

        var il = method.GetILGenerator();
        var scratch = il.DeclareLocal(_types.Object);
        var unsafeLabel = il.DefineLabel();

        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        EmitPrototypeHasNoJsonHook(
            il, runtime.ObjectPrototypeField, scratch, unsafeLabel, runtime,
            expectsObjectPrototype: false);
        il.Emit(OpCodes.Call, runtime.ArrayPrototypePopulateMethod);
        EmitPrototypeHasNoJsonHook(
            il, runtime.ArrayPrototypeField, scratch, unsafeLabel, runtime,
            expectsObjectPrototype: true);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(unsafeLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitPrototypeHasNoJsonHook(
        ILGenerator il,
        FieldBuilder prototypeField,
        LocalBuilder scratch,
        Label unsafeLabel,
        EmittedRuntime runtime,
        bool expectsObjectPrototype)
    {
        il.Emit(OpCodes.Ldsfld, prototypeField);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        if (expectsObjectPrototype)
        {
            il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
            il.Emit(OpCodes.Bne_Un, unsafeLabel);
        }
        else
        {
            il.Emit(OpCodes.Brtrue, unsafeLabel);
        }

        il.Emit(OpCodes.Ldsfld, prototypeField);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, unsafeLabel);

        il.Emit(OpCodes.Ldsfld, prototypeField);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, scratch);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, unsafeLabel);
    }

    /// <summary>
    /// Side-effect-free whole-graph preflight for the typed JSON path. A true
    /// result proves every shaped node still has the exact ordinary runtime
    /// representation, descriptor/prototype state, key set and key order the
    /// compiler observed. Generic leaves impose no assumption.
    /// </summary>
    private MethodBuilder EmitCanUseJsonShapeHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_canUseJsonShapeMethod is not null)
            return _canUseJsonShapeMethod;

        var method = typeBuilder.DefineMethod(
            "CanUseJsonShape",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Int32]);
        _canUseJsonShapeMethod = method;

        var il = method.GetILGenerator();
        var tagLocal = il.DeclareLocal(_types.String);
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Object);
        var keyLocal = il.DeclareLocal(_types.String);

        var getEnumerator = _types.GetMethod(
            _types.DictionaryStringObject, "GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumerator.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentGetter = _types.GetProperty(enumeratorType, "Current").GetGetMethod()!;
        var pairType = currentGetter.ReturnType;
        var pairLocal = il.DeclareLocal(pairType);
        var pairKeyGetter = _types.GetProperty(pairType, "Key").GetGetMethod()!;
        var moveNext = _types.GetMethod(enumeratorType, "MoveNext", Type.EmptyTypes)!;

        var falseLabel = il.DefineLabel();
        var shapeNode = il.DefineLabel();
        var genericTag = il.DefineLabel();
        var numberTag = il.DefineLabel();
        var stringTag = il.DefineLabel();
        var boolTag = il.DefineLabel();
        var arrayNode = il.DefineLabel();
        var objectNode = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 512);
        il.Emit(OpCodes.Bge, falseLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, shapeNode);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$g", genericTag);
        EmitStringTagBranch(il, tagLocal, "$n", numberTag);
        EmitStringTagBranch(il, tagLocal, "$N", numberTag);
        EmitStringTagBranch(il, tagLocal, "$s", stringTag);
        EmitStringTagBranch(il, tagLocal, "$S", stringTag);
        EmitStringTagBranch(il, tagLocal, "$b", boolTag);
        EmitStringTagBranch(il, tagLocal, "$B", boolTag);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(genericTag);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(numberTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(boolTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(shapeNode);
        il.Emit(OpCodes.Pop); // null left by isinst string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$a", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$A", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$o", objectNode);
        EmitStringTagBranch(il, tagLocal, "$O", objectNode);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(arrayNode);
        EmitExactRuntimeTypeCheck(il, 0, _types.ListOfObject, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var arrayLoop = il.DefineLabel();
        var arrayDone = il.DefineLabel();
        il.MarkLabel(arrayLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, arrayDone);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, arrayLoop);
        il.MarkLabel(arrayDone);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(objectNode);
        EmitExactRuntimeTypeCheck(il, 0, _types.DictionaryStringObject, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bne_Un, falseLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        var objectLoop = il.DefineLabel();
        var objectDone = il.DefineLabel();
        il.MarkLabel(objectLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, objectDone);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, moveNext);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentGetter);
        il.Emit(OpCodes.Stloc, pairLocal);
        il.Emit(OpCodes.Ldloca, pairLocal);
        il.Emit(OpCodes.Call, pairKeyGetter);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, objectLoop);

        il.MarkLabel(objectDone);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitStringTagBranch(
        ILGenerator il,
        LocalBuilder tag,
        string expected,
        Label target)
    {
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, expected);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, target);
    }

    private void EmitExactRuntimeTypeCheck(
        ILGenerator il,
        int argument,
        Type exactType,
        Label failure)
    {
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldtoken, exactType);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Bne_Un, failure);
    }

    /// <summary>
    /// Checks only the current shaped node. Closed scalar graphs use this from
    /// the append walk, avoiding a second whole-graph traversal. Because these
    /// checks never invoke user code, a later failure may safely discard the
    /// private builder and enter the generic serializer once.
    /// </summary>
    private MethodBuilder EmitCanUseJsonShapeNodeHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_canUseJsonShapeNodeMethod is not null)
            return _canUseJsonShapeNodeMethod;

        var method = typeBuilder.DefineMethod(
            "CanUseJsonShapeNode",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        _canUseJsonShapeNodeMethod = method;

        var il = method.GetILGenerator();
        var tagLocal = il.DeclareLocal(_types.String);
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var indexLocal = il.DeclareLocal(_types.Int32);

        var getEnumerator = _types.GetMethod(
            _types.DictionaryStringObject, "GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumerator.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentGetter = _types.GetProperty(enumeratorType, "Current").GetGetMethod()!;
        var pairType = currentGetter.ReturnType;
        var pairLocal = il.DeclareLocal(pairType);
        var pairKeyGetter = _types.GetProperty(pairType, "Key").GetGetMethod()!;
        var moveNext = _types.GetMethod(enumeratorType, "MoveNext", Type.EmptyTypes)!;

        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var shapeNode = il.DefineLabel();
        var numberTag = il.DefineLabel();
        var stringTag = il.DefineLabel();
        var boolTag = il.DefineLabel();
        var arrayNode = il.DefineLabel();
        var objectNode = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, shapeNode);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$g", trueLabel);
        EmitStringTagBranch(il, tagLocal, "$n", numberTag);
        EmitStringTagBranch(il, tagLocal, "$s", stringTag);
        EmitStringTagBranch(il, tagLocal, "$b", boolTag);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(numberTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(stringTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(boolTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(shapeNode);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$a", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$A", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$o", objectNode);
        EmitStringTagBranch(il, tagLocal, "$O", objectNode);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(arrayNode);
        EmitExactRuntimeTypeCheck(il, 0, _types.ListOfObject, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(objectNode);
        EmitExactRuntimeTypeCheck(il, 0, _types.DictionaryStringObject, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bne_Un, falseLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        var objectLoop = il.DefineLabel();
        var objectDone = il.DefineLabel();
        il.MarkLabel(objectLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, objectDone);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, moveNext);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentGetter);
        il.Emit(OpCodes.Stloc, pairLocal);
        il.Emit(OpCodes.Ldloca, pairLocal);
        il.Emit(OpCodes.Call, pairKeyGetter);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, objectLoop);

        il.MarkLabel(objectDone);
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitAppendJsonShapedValueHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        MethodBuilder appendValue)
    {
        if (_appendJsonShapedValueMethod is not null)
            return _appendJsonShapedValueMethod;

        var appendNumber = EmitAppendJsonNumberHelper(typeBuilder, runtime);
        var method = typeBuilder.DefineMethod(
            "AppendJsonShapedValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [
                _types.StringBuilder,
                _types.Object,
                _types.Object,
                _types.Int32,
                _types.String,
                _types.Int32,
                _types.Boolean,
                _types.Boolean,
                _types.Boolean
            ]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        _appendJsonShapedValueMethod = method;

        var typedRecordAppenders = _features.JsonScalarRecordShapes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(pair => runtime.JsonTypedScalarRecordTypes.ContainsKey(pair.Key))
            .Select((pair, ordinal) => (
                Fingerprint: pair.Key,
                Shape: pair.Value,
                Method: typeBuilder.DefineMethod(
                    $"AppendJsonTypedScalarRecord{ordinal}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    _types.Boolean,
                    [
                        _types.StringBuilder,
                        runtime.JsonTypedScalarRecordTypes[pair.Key],
                        _types.ObjectArray,
                        _types.Int32
                    ])))
            .ToArray();
        foreach (var appender in typedRecordAppenders)
            appender.Method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();
        var tagLocal = il.DeclareLocal(_types.String);
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var scalarLocal = il.DeclareLocal(runtime.JsonScalarRecordType);
        var valueLocal = il.DeclareLocal(_types.Object);
        var keyLocal = il.DeclareLocal(_types.String);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var closedLocal = il.DeclareLocal(_types.Boolean);
        var getEnumerator = _types.GetMethod(
            _types.DictionaryStringObject, "GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumerator.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentGetter = _types.GetProperty(enumeratorType, "Current").GetGetMethod()!;
        var pairType = currentGetter.ReturnType;
        var pairLocal = il.DeclareLocal(pairType);
        var pairKeyGetter = _types.GetProperty(pairType, "Key").GetGetMethod()!;
        var pairValueGetter = _types.GetProperty(pairType, "Value").GetGetMethod()!;
        var moveNext = _types.GetMethod(enumeratorType, "MoveNext", Type.EmptyTypes)!;

        var genericTag = il.DefineLabel();
        var numberTag = il.DefineLabel();
        var stringTag = il.DefineLabel();
        var boolTag = il.DefineLabel();
        var shapeNode = il.DefineLabel();
        var arrayNode = il.DefineLabel();
        var objectNode = il.DefineLabel();
        var invalidShape = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, shapeNode);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$g", genericTag);
        EmitStringTagBranch(il, tagLocal, "$n", numberTag);
        EmitStringTagBranch(il, tagLocal, "$N", numberTag);
        EmitStringTagBranch(il, tagLocal, "$s", stringTag);
        EmitStringTagBranch(il, tagLocal, "$S", stringTag);
        EmitStringTagBranch(il, tagLocal, "$b", boolTag);
        EmitStringTagBranch(il, tagLocal, "$B", boolTag);
        il.Emit(OpCodes.Br, invalidShape);

        il.MarkLabel(genericTag);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Ldarg, 6);
        il.Emit(OpCodes.Ldarg, 7);
        il.Emit(OpCodes.Ldarg, 8);
        il.Emit(OpCodes.Call, appendValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(numberTag);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, invalidShape);
        EmitAppendShapedPropertyPrefix(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, appendNumber);
        EmitTrueReturn(il);

        il.MarkLabel(stringTag);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, invalidShape);
        EmitAppendShapedPropertyPrefix(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);
        EmitTrueReturn(il);

        il.MarkLabel(boolTag);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, invalidShape);
        EmitAppendShapedPropertyPrefix(il);
        var appendFalse = il.DefineLabel();
        var boolDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, appendFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "true");
        EmitStringBuilderAppendString(il);
        il.Emit(OpCodes.Br, boolDone);
        il.MarkLabel(appendFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "false");
        EmitStringBuilderAppendString(il);
        il.MarkLabel(boolDone);
        EmitTrueReturn(il);

        il.MarkLabel(shapeNode);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, tagLocal);
        EmitStringTagBranch(il, tagLocal, "$a", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$A", arrayNode);
        EmitStringTagBranch(il, tagLocal, "$o", objectNode);
        EmitStringTagBranch(il, tagLocal, "$O", objectNode);
        il.Emit(OpCodes.Br, invalidShape);

        il.MarkLabel(arrayNode);
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Ldstr, "$A");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, closedLocal);
        var arrayGuarded = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brfalse, arrayGuarded);
        var arrayTypeAccepted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, arrayTypeAccepted);
        EmitExactRuntimeTypeCheck(il, 1, _types.ListOfObject, invalidShape);
        il.MarkLabel(arrayTypeAccepted);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.MarkLabel(arrayGuarded);
        var arrayNotTsArray = il.DefineLabel();
        var arrayBoxed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, arrayNotTsArray);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayEnsureBoxed);
        il.Emit(OpCodes.Br, arrayBoxed);
        il.MarkLabel(arrayNotTsArray);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(arrayBoxed);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        EmitAppendShapedPropertyPrefix(il);
        EmitAppendChar(il, '[');
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var arrayLoop = il.DefineLabel();
        var arrayDone = il.DefineLabel();
        var arrayNoComma = il.DefineLabel();
        var arrayAppended = il.DefineLabel();
        il.MarkLabel(arrayLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, arrayDone);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Brfalse, arrayNoComma);
        EmitAppendChar(il, ',');
        il.MarkLabel(arrayNoComma);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Brtrue, arrayAppended);
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brtrue, invalidShape);
        EmitAppendNullLiteral(il);
        il.MarkLabel(arrayAppended);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, arrayLoop);
        il.MarkLabel(arrayDone);
        EmitAppendChar(il, ']');
        EmitTrueReturn(il);

        il.MarkLabel(objectNode);
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Ldstr, "$O");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, closedLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.JsonScalarRecordType);
        il.Emit(OpCodes.Stloc, scalarLocal);
        var dictionaryObject = il.DefineLabel();
        var objectStorageReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, scalarLocal);
        il.Emit(OpCodes.Brfalse, dictionaryObject);
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brfalse, invalidShape);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.Emit(OpCodes.Ldloc, scalarLocal);
        il.Emit(OpCodes.Callvirt, runtime.JsonScalarRecordIsMaterializedGetter);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.Emit(OpCodes.Ldloc, scalarLocal);
        il.Emit(OpCodes.Callvirt, runtime.JsonScalarRecordShapeGetter);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Bne_Un, invalidShape);
        foreach (var appender in typedRecordAppenders)
        {
            var exactType = runtime.JsonTypedScalarRecordTypes[appender.Fingerprint];
            var exactLocal = il.DeclareLocal(exactType);
            var nextAppender = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, scalarLocal);
            il.Emit(OpCodes.Isinst, exactType);
            il.Emit(OpCodes.Stloc, exactLocal);
            il.Emit(OpCodes.Ldloc, exactLocal);
            il.Emit(OpCodes.Brfalse, nextAppender);
            EmitAppendShapedPropertyPrefix(il);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, exactLocal);
            il.Emit(OpCodes.Ldloc, shapeLocal);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, appender.Method);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(nextAppender);
        }
        il.Emit(OpCodes.Br, objectStorageReady);

        il.MarkLabel(dictionaryObject);
        var objectGuarded = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brfalse, objectGuarded);
        EmitExactRuntimeTypeCheck(il, 1, _types.DictionaryStringObject, invalidShape);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.MarkLabel(objectGuarded);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        var objectCountReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brfalse, objectCountReady);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(
            _types.DictionaryStringObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bne_Un, invalidShape);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        il.MarkLabel(objectCountReady);
        il.MarkLabel(objectStorageReady);
        EmitAppendShapedPropertyPrefix(il);
        EmitAppendChar(il, '{');
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        var objectLoop = il.DefineLabel();
        var objectDone = il.DefineLabel();
        var objectOmitted = il.DefineLabel();
        il.MarkLabel(objectLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, objectDone);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, keyLocal);
        var openObjectRead = il.DefineLabel();
        var objectValueReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, scalarLocal);
        il.Emit(OpCodes.Brfalse, openObjectRead);
        il.Emit(OpCodes.Ldloc, scalarLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Callvirt, runtime.JsonScalarRecordGetValue);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, objectValueReady);
        il.MarkLabel(openObjectRead);
        var openDictionaryRead = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brfalse, openDictionaryRead);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, moveNext);
        il.Emit(OpCodes.Brfalse, invalidShape);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentGetter);
        il.Emit(OpCodes.Stloc, pairLocal);
        il.Emit(OpCodes.Ldloca, pairLocal);
        il.Emit(OpCodes.Call, pairKeyGetter);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, invalidShape);
        il.Emit(OpCodes.Ldloca, pairLocal);
        il.Emit(OpCodes.Call, pairValueGetter);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, objectValueReady);
        il.MarkLabel(openDictionaryRead);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(objectValueReady);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Brfalse, objectOmitted);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);
        var objectContinue = il.DefineLabel();
        il.Emit(OpCodes.Br, objectContinue);
        il.MarkLabel(objectOmitted);
        il.Emit(OpCodes.Ldloc, closedLocal);
        il.Emit(OpCodes.Brtrue, invalidShape);
        il.MarkLabel(objectContinue);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, objectLoop);
        il.MarkLabel(objectDone);
        EmitAppendChar(il, '}');
        EmitTrueReturn(il);

        il.MarkLabel(invalidShape);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        foreach (var appender in typedRecordAppenders)
            EmitTypedRecordAppender(
                appender.Fingerprint, appender.Shape, appender.Method);
        return method;

        void EmitTypedRecordAppender(
            string fingerprint,
            JsonSerializationShape.Record shape,
            MethodBuilder appenderMethod)
        {
            var appenderIl = appenderMethod.GetILGenerator();
            EmitAppendChar(appenderIl, '{');
            for (int index = 0; index < shape.Fields.Count; index++)
            {
                if (index != 0)
                    EmitAppendChar(appenderIl, ',');
                appenderIl.Emit(OpCodes.Ldarg_0);
                appenderIl.Emit(OpCodes.Ldstr,
                    Runtime.BuiltIns.JsonStringEscaper.Quote(
                        shape.Fields[index].Key));
                EmitStringBuilderAppendString(appenderIl);
                EmitAppendChar(appenderIl, ':');

                FieldBuilder valueField =
                    runtime.JsonTypedScalarRecordValueFields[(fingerprint, index)];
                switch (shape.Fields[index].Value)
                {
                    case JsonSerializationShape.Number:
                        appenderIl.Emit(OpCodes.Ldarg_0);
                        appenderIl.Emit(OpCodes.Ldarg_1);
                        appenderIl.Emit(OpCodes.Ldfld, valueField);
                        appenderIl.Emit(OpCodes.Call, appendNumber);
                        break;
                    case JsonSerializationShape.String:
                        appenderIl.Emit(OpCodes.Ldarg_0);
                        appenderIl.Emit(OpCodes.Ldarg_1);
                        appenderIl.Emit(OpCodes.Ldfld, valueField);
                        appenderIl.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);
                        break;
                    case JsonSerializationShape.Boolean:
                    {
                        var appendFalse = appenderIl.DefineLabel();
                        var boolDone = appenderIl.DefineLabel();
                        appenderIl.Emit(OpCodes.Ldarg_1);
                        appenderIl.Emit(OpCodes.Ldfld, valueField);
                        appenderIl.Emit(OpCodes.Brfalse, appendFalse);
                        appenderIl.Emit(OpCodes.Ldarg_0);
                        appenderIl.Emit(OpCodes.Ldstr, "true");
                        EmitStringBuilderAppendString(appenderIl);
                        appenderIl.Emit(OpCodes.Br, boolDone);
                        appenderIl.MarkLabel(appendFalse);
                        appenderIl.Emit(OpCodes.Ldarg_0);
                        appenderIl.Emit(OpCodes.Ldstr, "false");
                        EmitStringBuilderAppendString(appenderIl);
                        appenderIl.MarkLabel(boolDone);
                        break;
                    }
                    default:
                    {
                        var appended = appenderIl.DefineLabel();
                        appenderIl.Emit(OpCodes.Ldarg_0);
                        appenderIl.Emit(OpCodes.Ldarg_1);
                        appenderIl.Emit(OpCodes.Ldfld, valueField);
                        appenderIl.Emit(OpCodes.Ldarg_2);
                        appenderIl.Emit(OpCodes.Ldc_I4, 2 + index * 2);
                        appenderIl.Emit(OpCodes.Ldelem_Ref);
                        appenderIl.Emit(OpCodes.Ldarg_3);
                        appenderIl.Emit(OpCodes.Ldc_I4_1);
                        appenderIl.Emit(OpCodes.Add);
                        appenderIl.Emit(OpCodes.Ldnull);
                        appenderIl.Emit(OpCodes.Ldc_I4_0);
                        appenderIl.Emit(OpCodes.Ldc_I4_0);
                        appenderIl.Emit(OpCodes.Ldc_I4_0);
                        appenderIl.Emit(OpCodes.Ldc_I4_0);
                        appenderIl.Emit(OpCodes.Call, method);
                        appenderIl.Emit(OpCodes.Brtrue, appended);
                        appenderIl.Emit(OpCodes.Ldc_I4_0);
                        appenderIl.Emit(OpCodes.Ret);
                        appenderIl.MarkLabel(appended);
                        break;
                    }
                }
            }
            EmitAppendChar(appenderIl, '}');
            appenderIl.Emit(OpCodes.Ldc_I4_1);
            appenderIl.Emit(OpCodes.Ret);
        }
    }

    private void EmitAppendShapedPropertyPrefix(ILGenerator il)
    {
        var noPrefix = il.DefineLabel();
        var noComma = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 7);
        il.Emit(OpCodes.Brfalse, noPrefix);
        il.Emit(OpCodes.Ldarg, 8);
        il.Emit(OpCodes.Brfalse, noComma);
        EmitAppendChar(il, ',');
        il.MarkLabel(noComma);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);
        EmitAppendChar(il, ':');
        il.MarkLabel(noPrefix);
    }

    private void EmitAppendChar(ILGenerator il, char value)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
    }

    private void EmitJsonStringifyShapedMethod(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        MethodBuilder appendValue)
    {
        var (registerShape, _) = EmitJsonShapeAssociationHelpers(typeBuilder);
        var prototypesSafe = EmitJsonShapePrototypesSafeHelper(typeBuilder, runtime);
        var canUseShape = EmitCanUseJsonShapeHelper(typeBuilder, runtime);
        var appendShaped = EmitAppendJsonShapedValueHelper(typeBuilder, runtime, appendValue);
        var method = typeBuilder.DefineMethod(
            "JsonStringifyShaped",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Boolean]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        runtime.JsonStringifyShaped = method;

        var il = method.GetILGenerator();
        var fallback = il.DefineLabel();
        var undefinedRoot = il.DefineLabel();
        var cleanupDone = il.DefineLabel();
        var builderLocal = il.DeclareLocal(_types.StringBuilder);
        var resultLocal = il.DeclareLocal(_types.Object);
        var fallbackLocal = il.DeclareLocal(_types.Boolean);

        il.Emit(OpCodes.Call, prototypesSafe);
        il.Emit(OpCodes.Brfalse, fallback);
        var shapeReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, shapeReady);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, canUseShape);
        il.Emit(OpCodes.Brfalse, fallback);
        il.MarkLabel(shapeReady);

        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, builderLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, appendShaped);
        il.Emit(OpCodes.Brfalse, undefinedRoot);
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, resultLocal);
        var resultRegistered = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, resultRegistered);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, registerShape);
        il.MarkLabel(resultRegistered);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.MarkLabel(undefinedRoot);
        var storeUndefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, storeUndefined);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, fallbackLocal);
        il.Emit(OpCodes.Leave, cleanupDone);
        il.MarkLabel(storeUndefined);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Call, _jsonReturnStringBuilderMethod!);
        il.EndExceptionBlock();
        il.MarkLabel(cleanupDone);
        il.Emit(OpCodes.Ldloc, fallbackLocal);
        il.Emit(OpCodes.Brtrue, fallback);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonStringify);
        il.Emit(OpCodes.Ret);
    }
}
