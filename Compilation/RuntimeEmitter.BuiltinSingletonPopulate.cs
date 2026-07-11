using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
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

    private void DefineMathSingletonPopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.MathSingletonPopulateMethod = typeBuilder.DefineMethod(
            "_MathSingletonPopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void DefineJsonSingletonPopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.JsonSingletonPopulateMethod = typeBuilder.DefineMethod(
            "_JsonSingletonPopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitMathSingletonPopulate(EmittedRuntime runtime) =>
        EmitBuiltinSingletonPopulate(
            runtime.MathSingletonPopulateMethod,
            runtime.MathSingletonField,
            runtime,
            MathStaticEmitter.EnumerateValueFormMethods(runtime));

    private void EmitJsonSingletonPopulate(EmittedRuntime runtime) =>
        EmitBuiltinSingletonPopulate(
            runtime.JsonSingletonPopulateMethod,
            runtime.JsonSingletonField,
            runtime,
            JSONStaticEmitter.EnumerateValueFormMethods(runtime));

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
        EmittedRuntime runtime,
        IEnumerable<(string Name, MethodInfo? Backing, int Length)> methods)
    {
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);
        var getMethodFromHandle = _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
            _types.RuntimeMethodHandle, _types.RuntimeTypeHandle);

        EmitPrototypePopulateGuard(il, singletonField);

        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        var fnLocal = il.DeclareLocal(_types.Object);
        foreach (var (jsName, backing, jsLength) in methods)
        {
            if (backing is null) continue;
            // $TSFunction.GetOrCreate(MethodInfo, name, length) — cached identity
            // so `m.max === Math.max` (same instance the value-form static
            // emitter hands out).
            il.Emit(OpCodes.Ldtoken, backing);
            il.Emit(OpCodes.Ldtoken, backing.DeclaringType!);
            il.Emit(OpCodes.Call, getMethodFromHandle);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            il.Emit(OpCodes.Stloc, fnLocal);
            // Fast-path dict store (covers `m.max`) + non-enumerable descriptor
            // (so `Object.keys(Math)` / for-in don't surface the methods).
            il.Emit(OpCodes.Ldsfld, singletonField);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Callvirt, setItem);
            EmitInstallNonEnumerable(il, runtime, singletonField, descLocal, jsName,
                () => il.Emit(OpCodes.Ldloc, fnLocal));
        }

        il.Emit(OpCodes.Ret);
    }
}
