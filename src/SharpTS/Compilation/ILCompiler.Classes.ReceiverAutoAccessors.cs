using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<Stmt.AutoAccessor, (TypeBuilder Owner, FieldBuilder Storage, MethodBuilder Getter, MethodBuilder? Setter)>
        _receiverAutoAccessors = new(ReferenceEqualityComparer.Instance);

    private void DefineReceiverAutoAccessor(TypeBuilder owner, Stmt.AutoAccessor source, FieldBuilder backing)
    {
        bool erased = owner.IsGenericTypeDefinition;
        MethodBuilder DefineRawBridge(bool setter)
        {
            // A nonvirtual instance entry on the declaring owner can legally
            // access its private backing slot and retains this exact facet.
            var bridge = owner.DefineMethod("$AutoRaw_" + _receiverAutoAccessors.Count + (setter ? "Set" : "Get"),
                erased ? MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot
                    : MethodAttributes.Public | MethodAttributes.HideBySig,
                setter ? _types.Void : erased ? _types.Object : backing.FieldType,
                setter ? [erased ? _types.Object : backing.FieldType] : Type.EmptyTypes);
            var bridgeIL = bridge.GetILGenerator();
            bridgeIL.Emit(OpCodes.Ldarg_0);
            if (setter)
            {
                bridgeIL.Emit(OpCodes.Ldarg_1);
                if (erased) EmitBoxedFieldConversion(bridgeIL, backing.FieldType);
                bridgeIL.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(backing));
            }
            else
            {
                bridgeIL.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(backing));
                if (erased && (backing.FieldType.IsValueType || backing.FieldType.IsGenericParameter))
                    bridgeIL.Emit(OpCodes.Box, backing.FieldType);
            }
            bridgeIL.Emit(OpCodes.Ret);
            return bridge;
        }
        var getterImplementation = DefineRawBridge(false);
        var setterImplementation = source.IsReadonly ? null : DefineRawBridge(true);
        Type rawReceiverType = owner;
        MethodInfo rawGetter = getterImplementation;
        MethodInfo? rawSetter = setterImplementation;
        Type rawValueType = backing.FieldType;
        if (erased)
        {
            // Every closed CLR instantiation implements this one erased owner
            // contract. Its method bodies retain the actual declaring T slots.
            var contract = _moduleBuilder.DefineType("$AutoOwner_" + _receiverAutoAccessors.Count,
                TypeAttributes.NotPublic | TypeAttributes.Interface | TypeAttributes.Abstract);
            const MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Abstract
                | MethodAttributes.Virtual | MethodAttributes.NewSlot;
            var getterDeclaration = contract.DefineMethod("Get", attributes, _types.Object, Type.EmptyTypes);
            var setterDeclaration = setterImplementation == null ? null
                : contract.DefineMethod("Set", attributes, _types.Void, [_types.Object]);
            EmitTypeDefinitions.AddInterfaceImplementation(owner, contract);
            owner.DefineMethodOverride(getterImplementation, getterDeclaration);
            if (setterDeclaration != null) owner.DefineMethodOverride(setterImplementation!, setterDeclaration);
            contract.CreateType();
            rawReceiverType = contract;
            // These exact declarations are the emitted interface slots. Retain
            // their builders instead of reflecting over the finalized owner.
            rawGetter = getterDeclaration;
            rawSetter = setterDeclaration;
            rawValueType = _types.Object;
        }
        var tableType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>>);
        var storage = _programType.DefineField("$AutoStorage_" + _receiverAutoAccessors.Count,
            tableType, FieldAttributes.Public | FieldAttributes.Static);
        MethodBuilder Define(bool setter)
        {
            var method = _programType.DefineMethod("$AutoReceiver_" + _receiverAutoAccessors.Count + (setter ? "Set" : "Get"),
                MethodAttributes.Public | MethodAttributes.Static, _types.Object,
                setter ? [_types.Object, _types.Object] : [_types.Object]);
            method.DefineParameter(1, ParameterAttributes.None, "__this");
            MarkExpectsThis(method); MarkNonConstructible(method); MarkPadsUndefined(method);
            MarkFunctionName(method, $"{(setter ? "set" : "get")} {source.Name.Lexeme}");
            var il = method.GetILGenerator();
            var slots = il.DeclareLocal(_types.DictionaryStringObject);
            var invalid = il.DefineLabel(); var present = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, storage); il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldsfld, storage); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, slots);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(tableType, "TryGetValue", _types.Object, _types.DictionaryStringObject.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, invalid);
            il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldstr, source.Name.Lexeme);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brtrue, present);
            il.MarkLabel(invalid);
            GuestErrorEmitter.ThrowTypeError(il, _runtime, "Cannot access auto-accessor on an object whose class did not declare it");
            il.MarkLabel(present);
            var external = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, rawReceiverType); il.Emit(OpCodes.Brfalse, external);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, rawReceiverType);
            if (setter)
            {
                il.Emit(OpCodes.Ldarg_1); EmitBoxedFieldConversion(il, rawValueType);
                il.Emit(OpCodes.Callvirt, rawSetter!);
                il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            }
            else
            {
                il.Emit(OpCodes.Callvirt, rawGetter);
                if (rawValueType.IsValueType || rawValueType.IsGenericParameter) il.Emit(OpCodes.Box, rawValueType);
            }
            il.Emit(OpCodes.Ret);
            il.MarkLabel(external);
            il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldstr, source.Name.Lexeme);
            if (setter)
            {
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            }
            else il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
            il.Emit(OpCodes.Ret);
            return method;
        }
        var getter = Define(false);
        var setter = source.IsReadonly ? null : Define(true);
        _receiverAutoAccessors.Add(source, (owner, storage, getter, setter));
    }

    private void EmitReceiverAutoAccessorInitializer(CompilationContext context, Stmt.AutoAccessor source, string className)
    {
        var metadata = _receiverAutoAccessors[source];
        var il = context.IL;
        var emitter = new ILEmitter(context);
        var value = il.DeclareLocal(_types.Object);
        bool previousNewTarget = context.SuppressNewTarget;
        context.SuppressNewTarget = true;
        if (source.Initializer == null) il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
        else { emitter.EmitExpression(source.Initializer); emitter.EmitBoxIfNeeded(source.Initializer); }
        context.SuppressNewTarget = previousNewTarget;
        il.Emit(OpCodes.Stloc, value);
        var ready = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, metadata.Storage); il.Emit(OpCodes.Brtrue, ready);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(metadata.Storage.FieldType));
        il.Emit(OpCodes.Stsfld, metadata.Storage);
        il.MarkLabel(ready);
        var slots = il.DeclareLocal(_types.DictionaryStringObject);
        var existing = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, metadata.Storage); il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver!); il.Emit(OpCodes.Ldloca, slots);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(metadata.Storage.FieldType, "TryGetValue", _types.Object, _types.DictionaryStringObject.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, existing);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject)); il.Emit(OpCodes.Stloc, slots);
        il.Emit(OpCodes.Ldsfld, metadata.Storage); il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver!); il.Emit(OpCodes.Ldloc, slots);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(metadata.Storage.FieldType, "Add", _types.Object, _types.DictionaryStringObject));
        il.MarkLabel(existing);
        var fresh = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldstr, source.Name.Lexeme);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String)); il.Emit(OpCodes.Brfalse, fresh);
        GuestErrorEmitter.ThrowTypeError(il, _runtime, "Cannot initialize auto-accessor twice on the same object");
        il.MarkLabel(fresh);
        il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldstr, source.Name.Lexeme); il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        // Preserve the existing CLR backing default when no typed initializer
        // exists. The actual-object slot still records guest undefined.
        if (source.Initializer == null) return;
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver!); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Bne_Un, done);
        var backing = _typedInterop.PropertyBackingFields[className][NamingConventions.ToPascalCase(source.Name.Lexeme)];
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, value); EmitBoxedFieldConversion(il, backing.FieldType);
        il.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(backing));
        il.MarkLabel(done);
    }

    private void EmitReceiverAutoAccessorDescriptors(ILGenerator il, TypeBuilder owner,
        Stmt.AutoAccessor? selected = null, Type? descriptorOwner = null)
    {
        foreach (var (source, metadata) in _receiverAutoAccessors.Where(entry => ReferenceEquals(entry.Value.Owner, owner)
            && (selected == null || ReferenceEquals(entry.Key, selected))))
        {
            Type self = descriptorOwner ?? (owner.IsGenericTypeDefinition ? EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()) : owner);
            il.Emit(OpCodes.Ldtoken, self); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Get);
            il.Emit(OpCodes.Ldstr, source.Name.Lexeme);
            il.Emit(OpCodes.Newobj, _runtime.DescriptorStorage.DescriptorConstructor);
            void EmitCallable(MethodBuilder method, PropertyInfo property)
            {
                il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldnull); EmitMethodInfoLiteral(il, method, _programType);
                il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor); il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
            }
            EmitCallable(metadata.Getter, _runtime.DescriptorStorage.DescriptorGetter);
            if (metadata.Setter != null) EmitCallable(metadata.Setter, _runtime.DescriptorStorage.DescriptorSetter);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DefineProperty); il.Emit(OpCodes.Pop);
        }
    }
}
