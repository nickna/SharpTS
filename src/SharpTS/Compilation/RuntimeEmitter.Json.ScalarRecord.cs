using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a compact ordinary-record carrier for closed scalar object
    /// literals. Reads stay slot-backed; any consumer asking for Fields or any
    /// write lazily creates the canonical Dictionary representation. This keeps
    /// general object semantics on the existing paths while JSON can consume an
    /// untouched exact-shape record without per-record dictionary storage.
    /// </summary>
    private void EmitJsonScalarRecordClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$JsonScalarRecord",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed |
            TypeAttributes.BeforeFieldInit,
            _types.Object);
        runtime.JsonScalarRecordType = typeBuilder;
        EmitTypeDefinitions.AddInterfaceImplementation(typeBuilder, runtime.IHasFieldsInterface);

        var shapeField = typeBuilder.DefineField("_shape", _types.Object, FieldAttributes.Private);
        var valuesField = typeBuilder.DefineField("_values", _types.ObjectArray, FieldAttributes.Private);
        var inlineValueFields = Enumerable.Range(0, 4)
            .Select(index => typeBuilder.DefineField(
                $"_v{index}", _types.Object, FieldAttributes.Private))
            .ToArray();
        var materializedField = typeBuilder.DefineField(
            "_materialized", _types.DictionaryStringObject, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.ObjectArray]);
        runtime.JsonScalarRecordCtor = ctor;
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, shapeField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, valuesField);
        ctorIl.Emit(OpCodes.Ret);

        for (int arity = 1; arity <= inlineValueFields.Length; arity++)
        {
            var parameterTypes = new Type[arity + 1];
            parameterTypes[0] = _types.Object;
            Array.Fill(parameterTypes, _types.Object, 1, arity);
            var inlineCtor = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                parameterTypes);
            runtime.JsonScalarRecordInlineCtors.Add(arity, inlineCtor);
            var inlineIl = inlineCtor.GetILGenerator();
            inlineIl.Emit(OpCodes.Ldarg_0);
            inlineIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            inlineIl.Emit(OpCodes.Ldarg_0);
            inlineIl.Emit(OpCodes.Ldarg_1);
            inlineIl.Emit(OpCodes.Stfld, shapeField);
            for (int index = 0; index < arity; index++)
            {
                inlineIl.Emit(OpCodes.Ldarg_0);
                inlineIl.Emit(OpCodes.Ldarg, index + 2);
                inlineIl.Emit(OpCodes.Stfld, inlineValueFields[index]);
            }
            inlineIl.Emit(OpCodes.Ret);
        }

        runtime.JsonScalarRecordShapeGetter = EmitSimpleGetter(
            "get_Shape", _types.Object, shapeField);
        runtime.JsonScalarRecordValuesGetter = EmitSimpleGetter(
            "get_Values", _types.ObjectArray, valuesField);

        var getValue = typeBuilder.DefineMethod(
            "GetValue",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Int32]);
        getValue.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        runtime.JsonScalarRecordGetValue = getValue;
        var getValueIl = getValue.GetILGenerator();
        var inline = getValueIl.DefineLabel();
        getValueIl.Emit(OpCodes.Ldarg_0);
        getValueIl.Emit(OpCodes.Ldfld, valuesField);
        getValueIl.Emit(OpCodes.Dup);
        getValueIl.Emit(OpCodes.Brfalse, inline);
        getValueIl.Emit(OpCodes.Ldarg_1);
        getValueIl.Emit(OpCodes.Ldelem_Ref);
        getValueIl.Emit(OpCodes.Ret);
        getValueIl.MarkLabel(inline);
        getValueIl.Emit(OpCodes.Pop);
        var valueLabels = inlineValueFields.Select(_ => getValueIl.DefineLabel()).ToArray();
        getValueIl.Emit(OpCodes.Ldarg_1);
        getValueIl.Emit(OpCodes.Switch, valueLabels);
        getValueIl.Emit(OpCodes.Ldnull);
        getValueIl.Emit(OpCodes.Ret);
        for (int index = 0; index < inlineValueFields.Length; index++)
        {
            getValueIl.MarkLabel(valueLabels[index]);
            getValueIl.Emit(OpCodes.Ldarg_0);
            getValueIl.Emit(OpCodes.Ldfld, inlineValueFields[index]);
            getValueIl.Emit(OpCodes.Ret);
        }

        var isMaterialized = typeBuilder.DefineMethod(
            "get_IsMaterialized",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes);
        runtime.JsonScalarRecordIsMaterializedGetter = isMaterialized;
        var materializedIl = isMaterialized.GetILGenerator();
        materializedIl.Emit(OpCodes.Ldarg_0);
        materializedIl.Emit(OpCodes.Ldfld, materializedField);
        materializedIl.Emit(OpCodes.Ldnull);
        materializedIl.Emit(OpCodes.Cgt_Un);
        materializedIl.Emit(OpCodes.Ret);

        var ensure = typeBuilder.DefineMethod(
            "EnsureMaterialized",
            MethodAttributes.Private,
            _types.DictionaryStringObject,
            Type.EmptyTypes);
        EmitEnsureMaterialized(ensure.GetILGenerator(), shapeField, getValue, materializedField);

        var fieldsGetter = typeBuilder.DefineMethod(
            "get_Fields",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName |
            MethodAttributes.HideBySig,
            _types.DictionaryStringObject,
            Type.EmptyTypes);
        var fieldsIl = fieldsGetter.GetILGenerator();
        fieldsIl.Emit(OpCodes.Ldarg_0);
        fieldsIl.Emit(OpCodes.Call, ensure);
        fieldsIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(fieldsGetter, runtime.IHasFieldsFieldsGetter);

        var getProperty = typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Object,
            [_types.String]);
        getProperty.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        EmitScalarGetProperty(
            getProperty.GetILGenerator(), shapeField, getValue, materializedField);
        typeBuilder.DefineMethodOverride(getProperty, runtime.IHasFieldsGetProperty);

        var setProperty = typeBuilder.DefineMethod(
            "SetProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            [_types.String, _types.Object]);
        var setIl = setProperty.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Call, ensure);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Ldarg_2);
        setIl.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);
        setIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(setProperty, runtime.IHasFieldsSetProperty);

        var hasProperty = typeBuilder.DefineMethod(
            "HasProperty",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            [_types.String]);
        EmitScalarHasProperty(
            hasProperty.GetILGenerator(), shapeField, materializedField);
        typeBuilder.DefineMethodOverride(hasProperty, runtime.IHasFieldsHasProperty);

        typeBuilder.CreateType();

        MethodBuilder EmitSimpleGetter(string name, Type returnType, FieldBuilder field)
        {
            var getter = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                returnType,
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return getter;
        }
    }

    private void EmitEnsureMaterialized(
        ILGenerator il,
        FieldBuilder shapeField,
        MethodBuilder getValue,
        FieldBuilder materializedField)
    {
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var valueIndexLocal = il.DeclareLocal(_types.Int32);
        var build = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, materializedField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, build);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(build);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, shapeField);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(
            _types.DictionaryStringObject, [_types.Int32])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, valueIndexLocal);

        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Call, getValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, valueIndexLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stfld, materializedField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitScalarGetProperty(
        ILGenerator il,
        FieldBuilder shapeField,
        MethodBuilder getValue,
        FieldBuilder materializedField)
    {
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var valueIndexLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Object);
        var compact = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, materializedField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, compact);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        var materializedMiss = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, materializedMiss);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(materializedMiss);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(compact);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, shapeField);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, valueIndexLocal);
        var loop = il.DefineLabel();
        var miss = il.DefineLabel();
        var next = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, miss);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Call, getValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, valueIndexLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(miss);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitScalarHasProperty(
        ILGenerator il,
        FieldBuilder shapeField,
        FieldBuilder materializedField)
    {
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var compact = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, materializedField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, compact);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(compact);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, shapeField);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        var loop = il.DefineLabel();
        var miss = il.DefineLabel();
        var next = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, miss);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(miss);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
