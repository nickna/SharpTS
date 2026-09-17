using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Immutable inputs shared by singleton installation; no peer metadata is retained by Math.
    private readonly record struct BuiltinSingletonInputs(
        Type DescriptorType, PrototypeDescriptorInputs Descriptors, MethodInfo WritableSetter,
        MethodInfo FunctionGetOrCreate, MethodInfo GetSymbolDict, FieldInfo ToStringTag);

    private static BuiltinSingletonInputs GetBuiltinSingletonInputs(EmittedRuntime runtime) => new(
        runtime.DescriptorStorage.DescriptorType,
        new PrototypeDescriptorInputs(runtime.DescriptorStorage.DescriptorConstructor,
            runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
            runtime.DescriptorStorage.DefineProperty),
        runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!, runtime.FunctionConstruction.GetOrCreate,
        runtime.Symbols.GetStorage, runtime.Symbols.ToStringTag);

    // Math.* / JSON.* are normally intercepted at compile time by the dedicated
    // static emitters (MathStaticEmitter / JSONStaticEmitter) before the
    // receiver is evaluated as a value. When the singleton is used as a *value*
    // (`const m = Math; m.max(1, 2)` or `globalThis.Math.max`), dispatch falls
    // through to the runtime `_mathSingleton` / `_jsonSingleton` dictionaries.
    // Those dicts were created empty and never populated, so the lookup returned
    // undefined. These populate steps fill them with $TSFunction wrappers — the
    // same identity-cached wrappers the value-form static emitters hand out — so
    // value-form access matches the bare syntactic form. Mirrors
    // EmitArrayPrototypePopulate / EmitObjectPrototypePopulate. See issue #276.

    private void DefineMathSingletonPopulateShell(TypeBuilder typeBuilder, EmittedMathRuntime math)
    {
        math.SingletonPopulateMethod = typeBuilder.DefineMethod(
            "_MathSingletonPopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void DefineJsonSingletonPopulateShell(TypeBuilder typeBuilder, EmittedJsonRuntime json)
    {
        json.SingletonPopulateMethod = typeBuilder.DefineMethod(
            "_JsonSingletonPopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void DefineReflectSingletonPopulateShell(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace)
    {
        reflectNamespace.SingletonPopulateMethod = typeBuilder.DefineMethod(
            "_ReflectSingletonPopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitMathSingletonPopulate(EmittedMathRuntime math, BuiltinSingletonInputs inputs) =>
        EmitBuiltinSingletonPopulate(
            math.SingletonPopulateMethod,
            math.SingletonField,
            inputs,
            MathStaticEmitter.EnumerateValueFormMethods(math),
            "Math");

    private void EmitJsonSingletonPopulate(EmittedJsonRuntime json, BuiltinSingletonInputs inputs) =>
        EmitBuiltinSingletonPopulate(
            json.SingletonPopulateMethod,
            json.SingletonField,
            inputs,
            JSONStaticEmitter.EnumerateValueFormMethods(json.Implementation),
            "JSON");

    private void EmitReflectSingletonPopulate(EmittedReflectNamespace reflectNamespace, BuiltinSingletonInputs inputs) =>
        EmitBuiltinSingletonPopulate(
            reflectNamespace.SingletonPopulateMethod,
            reflectNamespace.SingletonField,
            inputs,
            ReflectStaticEmitter.EnumerateValueFormMethods(reflectNamespace),
            "Reflect");

    /// <summary>
    /// Fills a built-in singleton dictionary with identity-cached $TSFunction
    /// wrappers (one per value-form method) plus a non-enumerable PDS descriptor
    /// for each, matching ECMA-262 §17 built-in attributes. Idempotent: bails if
    /// the dict already has entries. Entries whose backing MethodBuilder is null
    /// (e.g. JSON helpers when the program doesn't use JSON) are skipped, so the
    /// body is always valid IL regardless of feature gating.
    /// </summary>
    private void EmitBuiltinSingletonPopulate(
        MethodBuilder method,
        FieldBuilder singletonField,
        BuiltinSingletonInputs inputs,
        IEnumerable<(string Name, MethodInfo? Backing, int Length)> methods,
        string toStringTag)
    {
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, singletonField);

        var descLocal = il.DeclareLocal(inputs.DescriptorType);

        var fnLocal = il.DeclareLocal(_types.Object);
        foreach (var (jsName, backing, jsLength) in methods)
        {
            if (backing is null) continue;
            // $TSFunction.GetOrCreate(MethodInfo, name, length) — cached identity
            // so `m.max === Math.max` (same instance the value-form static
            // emitter hands out).
            _types.EmitLoadMethodInfo(il, backing);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Call, inputs.FunctionGetOrCreate);
            il.Emit(OpCodes.Stloc, fnLocal);
            // Fast-path dict store (covers `m.max`) + non-enumerable descriptor
            // (so `Object.keys(Math)` / for-in don't surface the methods).
            il.Emit(OpCodes.Ldsfld, singletonField);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Callvirt, setItem);
            EmitInstallNonEnumerableDescriptor(il, inputs.Descriptors, singletonField, descLocal, jsName,
                () => il.Emit(OpCodes.Ldloc, fnLocal));
        }

        // Install the intrinsic @@toStringTag as a real symbol-keyed data
        // descriptor so assignment and deletion observe W:F/E:F/C:T.
        il.Emit(OpCodes.Newobj, inputs.Descriptors.Ctor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldstr, toStringTag);
        il.Emit(OpCodes.Callvirt, inputs.Descriptors.ValueSetter);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, inputs.WritableSetter);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, inputs.Descriptors.EnumerableSetter);
        il.Emit(OpCodes.Ldsfld, singletonField);
        il.Emit(OpCodes.Call, inputs.GetSymbolDict);
        il.Emit(OpCodes.Ldsfld, inputs.ToStringTag);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        il.Emit(OpCodes.Ret);
    }
}
