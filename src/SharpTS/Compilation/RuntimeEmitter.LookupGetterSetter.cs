using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct LookupAccessorHelpersInputs(
        TypeBuilder BoundTSFunctionType,
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedErrorRuntime Errors,
        EmittedObjectDescriptorRuntime ObjectDescriptors,
        EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedStringCoercionRuntime StringCoercion,
        TypeBuilder TSFunctionType,
        FieldInfo UndefinedInstance,
        Type UndefinedType
    );

    private readonly record struct AccessorHelperInputs(
        TypeBuilder BoundTSFunctionType,
        EmittedErrorRuntime Errors,
        EmittedObjectDescriptorRuntime ObjectDescriptors,
        TypeBuilder TSFunctionType,
        FieldInfo UndefinedInstance
    );

    private readonly record struct LookupAccessorHelperInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedErrorRuntime Errors,
        EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedStringCoercionRuntime StringCoercion,
        FieldInfo UndefinedInstance,
        Type UndefinedType
    );

    /// <summary>
    /// Emits <c>$Runtime.LookupGetterHelper(object __this, object key)</c> and
    /// <c>$Runtime.LookupSetterHelper(object __this, object key)</c> backing
    /// <c>Object.prototype.__lookupGetter__</c> / <c>__lookupSetter__</c>
    /// (ECMA-262 §B.2.2.4 / §B.2.2.5). Walks the prototype chain calling
    /// <see cref="EmittedDescriptorStorageRuntime.GetPropertyDescriptor"/> at each level; returns
    /// the descriptor's [[Get]]/[[Set]] slot when an accessor descriptor is found,
    /// undefined when a data descriptor is found, undefined when the chain is
    /// exhausted.
    /// </summary>
    private void EmitLookupAccessorHelpers(
        TypeBuilder typeBuilder,
        EmittedObjectOwnPropertiesRuntime objectOwnProperties,
        LookupAccessorHelpersInputs inputs
    )
    {
        objectOwnProperties.LookupGetter = EmitLookupAccessorHelper(
            typeBuilder,
            objectOwnProperties,
            new LookupAccessorHelperInputs(
                inputs.DescriptorStorage,
                inputs.Errors,
                inputs.ObjectPrototypes,
                inputs.StringCoercion,
                inputs.UndefinedInstance,
                inputs.UndefinedType
            ),
            isGetter: true
        );
        objectOwnProperties.LookupSetter = EmitLookupAccessorHelper(
            typeBuilder,
            objectOwnProperties,
            new LookupAccessorHelperInputs(
                inputs.DescriptorStorage,
                inputs.Errors,
                inputs.ObjectPrototypes,
                inputs.StringCoercion,
                inputs.UndefinedInstance,
                inputs.UndefinedType
            ),
            isGetter: false
        );
        objectOwnProperties.DefineGetter = EmitDefineAccessorHelper(
            typeBuilder,
            new AccessorHelperInputs(
                inputs.BoundTSFunctionType,
                inputs.Errors,
                inputs.ObjectDescriptors,
                inputs.TSFunctionType,
                inputs.UndefinedInstance
            ),
            isGetter: true
        );
        objectOwnProperties.DefineSetter = EmitDefineAccessorHelper(
            typeBuilder,
            new AccessorHelperInputs(
                inputs.BoundTSFunctionType,
                inputs.Errors,
                inputs.ObjectDescriptors,
                inputs.TSFunctionType,
                inputs.UndefinedInstance
            ),
            isGetter: false
        );
    }

    /// <summary>
    /// Emits <c>$Runtime.DefineGetterHelper / DefineSetterHelper(object __this, object key, object fn)</c>
    /// backing <c>Object.prototype.__defineGetter__/__defineSetter__</c>
    /// (ECMA-262 §B.2.2.2 / §B.2.2.3). Validates the function arg is callable,
    /// builds a configurable+enumerable accessor descriptor, and forwards to
    /// <see cref="EmittedObjectDescriptorRuntime.DefineProperty"/>.
    /// </summary>
    private MethodBuilder EmitDefineAccessorHelper(TypeBuilder typeBuilder, AccessorHelperInputs inputs, bool isGetter)
    {
        var name = isGetter ? "DefineGetterHelper" : "DefineSetterHelper";
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "key");
        method.DefineParameter(3, ParameterAttributes.None, "fn");

        var il = method.GetILGenerator();
        var descDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        // ECMA-262 §B.2.2.2 step 2 / §B.2.2.3 step 2: IsCallable(fn) === false → TypeError.
        // Strict callable check: $TSFunction / $BoundTSFunction / Type (constructor) /
        // MethodInfo (raw helper). Everything else (string/number/null/undefined/
        // plain object) triggers the throw.
        var isCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, inputs.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, inputs.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.MethodInfo);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        // Not callable — throw TypeError.
        il.Emit(OpCodes.Ldstr, isGetter
            ? "Object.prototype.__defineGetter__: callback must be callable"
            : "Object.prototype.__defineSetter__: callback must be callable");
        GuestErrorEmitter.ThrowErrorFromStack(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor);
        il.MarkLabel(isCallableLabel);

        // desc = new Dictionary<string, object>();
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descDictLocal);
        // desc[isGetter ? "get" : "set"] = fn;
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, isGetter ? "get" : "set");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, setItem);
        // desc["configurable"] = true; desc["enumerable"] = true.
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        // ObjectDefineProperty(__this, key, desc); return undefined.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Call, inputs.ObjectDescriptors.DefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitLookupAccessorHelper(
        TypeBuilder typeBuilder,
        EmittedObjectOwnPropertiesRuntime objectOwnProperties,
        LookupAccessorHelperInputs inputs,
        bool isGetter
    )
    {
        var name = isGetter ? "LookupGetterHelper" : "LookupSetterHelper";
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "key");

        var il = method.GetILGenerator();
        var keyLocal = il.DeclareLocal(_types.String);
        var oLocal = il.DeclareLocal(_types.Object);
        var descLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        var returnUndefinedLabel = il.DefineLabel();
        var throwThisLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var advanceProtoLabel = il.DefineLabel();
        var hasSlotLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwThisLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwThisLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, oLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Brfalse, returnUndefinedLabel);
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnUndefinedLabel);

        // desc = PDSGetPropertyDescriptor(O, key)
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);

        var noDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, noDescLabel);

        // PDS desc found. If isGetter: return desc.Getter ?? undefined. Else Setter.
        var slot = isGetter ? inputs.DescriptorStorage.DescriptorGetter : inputs.DescriptorStorage.DescriptorSetter;
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, slot.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasSlotLabel);
        // null slot — data descriptor on this level, return undefined per spec.
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasSlotLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noDescLabel);
        // No PDS descriptor. If a non-accessor data property exists on this
        // level (dict key, etc.), spec says return undefined. Otherwise walk up.
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, objectOwnProperties.HasOwnProperty);
        il.Emit(OpCodes.Brfalse, advanceProtoLabel);
        // Has data property at this level → return undefined.
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(advanceProtoLabel);
        // O = ObjectGetPrototypeOf(O). When the dispatch returns null (top of
        // chain), the loop-start null check returns undefined.
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Call, inputs.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, oLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(throwThisLabel);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor,
            isGetter
                ? "Object.prototype.__lookupGetter__ called on null or undefined"
                : "Object.prototype.__lookupSetter__ called on null or undefined");

        il.MarkLabel(returnUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
