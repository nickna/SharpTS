using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Shared emit idioms for the property/index set/delete runtime methods
    /// (#1131). The strict variants used to hand-mirror the non-strict
    /// dispatch ladders ("mirror the non-strict branch" comments); the
    /// genuinely shared segments live here and the Delete pairs are emitted
    /// from single <c>*Core(bool strict)</c> methods so the ladders exist
    /// once.
    /// </summary>
    /// <remarks>
    /// Emits <c>throw CreateException(new $TypeError(message))</c>.
    /// </remarks>
    private void EmitThrowTypeError(ILGenerator il, EmittedRuntime runtime, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
    }

    /// <summary>
    /// Emits <c>throw CreateException(new $TypeError(prefix + name + suffix))</c>
    /// where <c>name</c> is the property-name argument at slot 1.
    /// </summary>
    private void EmitThrowTypeErrorWithName(ILGenerator il, EmittedRuntime runtime, string prefix, string suffix)
    {
        il.Emit(OpCodes.Ldstr, prefix);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, suffix);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
    }

    /// <summary>
    /// Emits the globalThis/global sentinel redirect shared by SetProperty and
    /// SetPropertyStrict (#271): when the receiver (arg 0) is the sentinel,
    /// <c>root.foo = v</c> stores into the shared global-properties dictionary
    /// (visible to subsequent GlobalThisGetProperty reads) and returns.
    /// Mirrors the syntactic <c>globalThis.foo = v</c> path.
    /// </summary>
    private void EmitGlobalThisSetRedirect(ILGenerator il, EmittedRuntime runtime)
    {
        var notGlobalThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.GlobalThisSetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notGlobalThisLabel);
    }

    /// <summary>
    /// Emits the $CJSModule set-property branch body shared by SetProperty and
    /// SetPropertyStrict: only <c>"exports"</c> is writable
    /// (<c>module.exports = X</c>); writes to other module properties are
    /// silently ignored (spec behavior). Caller marks the branch label.
    /// </summary>
    private void EmitCjsModuleExportsSetBranch(ILGenerator il, EmittedRuntime runtime)
    {
        var notExportsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "exports");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notExportsLabel);
        // module.exports = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.CjsModuleType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CjsModuleExportsSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notExportsLabel);
        // Silently ignore writes to other module properties
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the "store as fresh PDS data descriptor" tail shared by the
    /// SetProperty family: <c>PDSDefineProperty(obj, name, new descriptor {
    /// Value = value })</c> with the bool result popped. Receiver/name/value
    /// are args 0/1/2. Caller emits the trailing <c>Ret</c>.
    /// </summary>
    private void EmitDefineDataDescriptorFromValue(ILGenerator il, EmittedRuntime runtime)
    {
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits the "invoke PDS accessor setter and return" tail shared by the
    /// SetProperty family: <c>InvokeMethodValue(obj, setter, [value]); return;</c>
    /// with the receiver at arg 0 and the value at arg 2.
    /// </summary>
    private void EmitInvokePdsSetterWithValueAndReturn(ILGenerator il, EmittedRuntime runtime, LocalBuilder setterLocal)
    {
        il.Emit(OpCodes.Ldarg_0);  // receiver (obj)
        il.Emit(OpCodes.Ldloc, setterLocal);  // function (setter)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);  // value
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);  // Discard return value
        il.Emit(OpCodes.Ret);
    }
}
