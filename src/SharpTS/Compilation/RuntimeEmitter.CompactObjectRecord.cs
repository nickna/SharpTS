using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCompactObjectRecordInterface(
        ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var interfaceBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$ICompactObjectRecord",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null);
        runtime.CompactObjectRecordInterface = interfaceBuilder.CreateType()!;
    }

    /// <summary>
    /// Emits one exact CLR reference type per small plain-record shape.  The
    /// object contains only its value slots; a shared weak table holds the
    /// canonical dictionary only after a dynamic write or observation asks for
    /// it.  A type-wide bit makes the untouched typed-read path a single branch
    /// followed by ldfld while retaining full IHasFields mutation semantics.
    /// </summary>
    private void EmitCompactObjectRecordClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        int ordinal = 0;
        foreach (var pair in _features.CompactObjectRecordShapes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string fingerprint = pair.Key;
            JsonSerializationShape.Record shape = pair.Value;
            if (shape.Fields.Count is < 1 or > 4)
                continue;

            var typeBuilder = EmitTypeDefinitions.DefineType(
                moduleBuilder,
                $"$CompactObjectRecord{ordinal++}",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed |
                TypeAttributes.BeforeFieldInit,
                _types.Object);
            EmitTypeDefinitions.AddInterfaceImplementation(typeBuilder, runtime.IHasFieldsInterface);
            EmitTypeDefinitions.AddInterfaceImplementation(
                typeBuilder, runtime.CompactObjectRecordInterface);
            runtime.CompactObjectRecordTypes.Add(fingerprint, typeBuilder);

            var valueFields = shape.Fields.Select((_, index) =>
                typeBuilder.DefineField($"_v{index}", _types.Object, FieldAttributes.Assembly)).ToArray();
            Type weakTableType = _types.MakeGenericType(
                _types.ConditionalWeakTableOpen, _types.Object, _types.DictionaryStringObject);
            var materializedTable = typeBuilder.DefineField(
                "_materialized", weakTableType, FieldAttributes.Private | FieldAttributes.Static);
            var anyMaterialized = typeBuilder.DefineField(
                "_anyMaterialized", _types.Boolean, FieldAttributes.Assembly | FieldAttributes.Static);
            runtime.CompactObjectRecordAnyMaterializedFields.Add(fingerprint, anyMaterialized);
            for (int index = 0; index < valueFields.Length; index++)
                runtime.CompactObjectRecordValueFields.Add((fingerprint, index), valueFields[index]);

            var ctor = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Enumerable.Repeat(_types.Object, valueFields.Length).ToArray());
            runtime.CompactObjectRecordCtors.Add(fingerprint, ctor);
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            for (int index = 0; index < valueFields.Length; index++)
            {
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Ldarg, index + 1);
                ctorIl.Emit(OpCodes.Stfld, valueFields[index]);
            }
            ctorIl.Emit(OpCodes.Ret);

            MethodInfo tableTryGet = _types.GetMethod(
                weakTableType, "TryGetValue", _types.Object,
                _types.DictionaryStringObject.MakeByRefType());
            MethodInfo tableAdd = _types.GetMethod(
                weakTableType, "Add", _types.Object, _types.DictionaryStringObject);
            MethodInfo dictSet = _types.GetMethod(
                _types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
            MethodInfo dictTryGet = _types.GetMethod(
                _types.DictionaryStringObject, "TryGetValue", _types.String,
                _types.Object.MakeByRefType());
            MethodInfo dictContains = _types.GetMethod(
                _types.DictionaryStringObject, "ContainsKey", _types.String);
            MethodInfo stringEquals = _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String);

            var ensure = typeBuilder.DefineMethod(
                "EnsureMaterialized", MethodAttributes.Private | MethodAttributes.HideBySig,
                _types.DictionaryStringObject, Type.EmptyTypes);
            EmitEnsure(ensure.GetILGenerator());

            var fieldsGetter = typeBuilder.DefineMethod(
                "get_Fields",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName |
                MethodAttributes.HideBySig,
                _types.DictionaryStringObject, Type.EmptyTypes);
            var fieldsIl = fieldsGetter.GetILGenerator();
            fieldsIl.Emit(OpCodes.Ldarg_0);
            fieldsIl.Emit(OpCodes.Call, ensure);
            fieldsIl.Emit(OpCodes.Ret);
            typeBuilder.DefineMethodOverride(fieldsGetter, runtime.IHasFieldsFieldsGetter);

            var getProperty = typeBuilder.DefineMethod(
                "GetProperty", MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.HideBySig, _types.Object, [_types.String]);
            EmitGetProperty(getProperty.GetILGenerator());
            typeBuilder.DefineMethodOverride(getProperty, runtime.IHasFieldsGetProperty);

            var setProperty = typeBuilder.DefineMethod(
                "SetProperty", MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.HideBySig, _types.Void, [_types.String, _types.Object]);
            var setIl = setProperty.GetILGenerator();
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Call, ensure);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Ldarg_2);
            setIl.Emit(OpCodes.Callvirt, dictSet);
            setIl.Emit(OpCodes.Ret);
            typeBuilder.DefineMethodOverride(setProperty, runtime.IHasFieldsSetProperty);

            var hasProperty = typeBuilder.DefineMethod(
                "HasProperty", MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.HideBySig, _types.Boolean, [_types.String]);
            EmitHasProperty(hasProperty.GetILGenerator());
            typeBuilder.DefineMethodOverride(hasProperty, runtime.IHasFieldsHasProperty);

            typeBuilder.CreateType();

            void EmitEnsure(ILGenerator il)
            {
                var result = il.DeclareLocal(_types.DictionaryStringObject);
                var tableReady = il.DefineLabel();
                var create = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, materializedTable);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brtrue_S, tableReady);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(weakTableType, Type.EmptyTypes)!);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stsfld, materializedTable);
                il.MarkLabel(tableReady);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, result);
                il.Emit(OpCodes.Callvirt, tableTryGet);
                il.Emit(OpCodes.Brfalse_S, create);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(create);
                il.Emit(OpCodes.Ldc_I4, valueFields.Length);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    _types.DictionaryStringObject, [_types.Int32])!);
                il.Emit(OpCodes.Stloc, result);
                for (int index = 0; index < valueFields.Length; index++)
                {
                    il.Emit(OpCodes.Ldloc, result);
                    il.Emit(OpCodes.Ldstr, shape.Fields[index].Key);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, valueFields[index]);
                    il.Emit(OpCodes.Callvirt, dictSet);
                }
                il.Emit(OpCodes.Ldsfld, materializedTable);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Callvirt, tableAdd);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stsfld, anyMaterialized);
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Ret);
            }

            void EmitGetProperty(ILGenerator il)
            {
                var dictionary = il.DeclareLocal(_types.DictionaryStringObject);
                var value = il.DeclareLocal(_types.Object);
                var compact = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, anyMaterialized);
                il.Emit(OpCodes.Brfalse_S, compact);
                il.Emit(OpCodes.Ldsfld, materializedTable);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, dictionary);
                il.Emit(OpCodes.Callvirt, tableTryGet);
                il.Emit(OpCodes.Brfalse_S, compact);
                il.Emit(OpCodes.Ldloc, dictionary);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, value);
                il.Emit(OpCodes.Callvirt, dictTryGet);
                var missing = il.DefineLabel();
                il.Emit(OpCodes.Brfalse_S, missing);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(missing);
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(compact);
                for (int index = 0; index < valueFields.Length; index++)
                {
                    var next = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldstr, shape.Fields[index].Key);
                    il.Emit(OpCodes.Call, stringEquals);
                    il.Emit(OpCodes.Brfalse_S, next);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, valueFields[index]);
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(next);
                }
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                il.Emit(OpCodes.Ret);
            }

            void EmitHasProperty(ILGenerator il)
            {
                var dictionary = il.DeclareLocal(_types.DictionaryStringObject);
                var compact = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, anyMaterialized);
                il.Emit(OpCodes.Brfalse_S, compact);
                il.Emit(OpCodes.Ldsfld, materializedTable);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, dictionary);
                il.Emit(OpCodes.Callvirt, tableTryGet);
                il.Emit(OpCodes.Brfalse_S, compact);
                il.Emit(OpCodes.Ldloc, dictionary);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, dictContains);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(compact);
                foreach (var field in shape.Fields)
                {
                    var next = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldstr, field.Key);
                    il.Emit(OpCodes.Call, stringEquals);
                    il.Emit(OpCodes.Brfalse_S, next);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(next);
                }
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
            }
        }
    }
}
