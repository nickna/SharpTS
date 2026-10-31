using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct AbortControllerConstructorInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedErrorRuntime Errors,
        EmittedFunctionAttributesRuntime FunctionAttributes,
        EmittedFunctionValueRuntime FunctionValues,
        MethodBuilder GetOrCreateFunction,
        EmittedObjectPrototypeRuntime ObjectPrototypes,
        NativeConstructedBodyLookup? ConstructedBodies);

    private void EmitAbortControllerConstructor(TypeBuilder runtimeType,
        EmittedAbortRuntime abort, AbortControllerConstructorInputs inputs)
    {
        // Keep the constructor entry separate from non-constructible $Runtime
        // helpers. Its new-target core reuses the ordinary runtime-valued new
        // protocol, including bound construction and Reflect.construct.
        var carrier = ((ModuleBuilder)runtimeType.Module).DefineType("$AbortControllerConstructor",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var entry = carrier.DefineMethod("AbortController",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
        var construct = carrier.DefineMethod("Construct",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        construct.DefineParameter(1, ParameterAttributes.None, "__newTarget");
        entry.SetCustomAttribute(inputs.FunctionAttributes.NewTargetCoreCtor,
            CustomAttributeEncoder.Encode(inputs.FunctionAttributes.NewTargetCoreCtor, construct.Name));
        GuestErrorEmitter.ThrowError(entry.GetILGenerator(), inputs.Errors.CreateException,
            inputs.Errors.TypeErrorConstructor, "Class constructor AbortController cannot be invoked without 'new'");

        var il = construct.GetILGenerator();
        var controller = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Call, abort.CreateController);
        il.Emit(OpCodes.Stloc, controller);
        // NewOnFunction already read newTarget.prototype and linked its fresh
        // receiver. Preserve that selected link on the native dictionary result
        // without reading an observable prototype getter a second time.
        il.Emit(OpCodes.Ldloc, controller);
        il.Emit(OpCodes.Ldsfld, inputs.FunctionValues.CurrentThisField);
        il.Emit(OpCodes.Call, inputs.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
        il.Emit(OpCodes.Ldloc, controller);
        il.Emit(OpCodes.Ret);
        inputs.ConstructedBodies?.RecordIntrinsicPair(entry, construct);
        carrier.CreateType();

        il = abort.GetControllerConstructor.GetILGenerator();
        var cached = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, abort.ControllerConstructorField);
        il.Emit(OpCodes.Brtrue, cached);
        il.Emit(OpCodes.Ldtoken, entry);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Ldstr, "AbortController");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, inputs.GetOrCreateFunction);
        il.Emit(OpCodes.Stsfld, abort.ControllerConstructorField);
        il.MarkLabel(cached);
        il.Emit(OpCodes.Ldsfld, abort.ControllerConstructorField);
        il.Emit(OpCodes.Ret);
    }
}
