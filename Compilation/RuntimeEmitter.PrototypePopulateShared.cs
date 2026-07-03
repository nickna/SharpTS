using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Shared IL idioms for the <c>*PrototypePopulate</c> emitters. Each
    /// prototype singleton (Array/Boolean/Error/Function/Number/Object/
    /// Promise/String + the NativeError subclasses) fills its dictionary
    /// with the same three sequences, parameterized only by the prototype
    /// field: an idempotency guard, a non-enumerable PDS descriptor install,
    /// and the $TSFunction wrapper wiring. RegExp's populate keeps its own
    /// local helpers — its slots need symbol-keyed entries, getter-only
    /// accessor descriptors, and data methods with an embedded "prototype"
    /// stub, none of which fit this scaffold.
    /// </summary>
    /// <remarks>
    /// Emits the idempotency guard: if the prototype dictionary already has
    /// entries, return early. The cctor calls each populate once, but a
    /// future static-init reordering shouldn't double-fill.
    /// </remarks>
    private void EmitPrototypePopulateGuard(ILGenerator il, FieldBuilder protoField)
    {
        var doFillLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, doFillLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(doFillLabel);
    }

    /// <summary>
    /// Emits a non-enumerable PDS data-descriptor install for
    /// <c>protoField[jsName]</c> (built-in §17 attrs: W:T, E:F, C:T) so
    /// <c>gOPD(proto, name).enumerable === false</c> per spec.
    /// <paramref name="emitValue"/> pushes the descriptor's value.
    /// </summary>
    private void EmitInstallNonEnumerable(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder protoField, LocalBuilder descLocal, string jsName, System.Action emitValue)
    {
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        emitValue();
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, jsName);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits <c>protoField["constructor"] = &lt;value&gt;</c> — the fast-path
    /// dict store followed by the non-enumerable PDS descriptor per
    /// ECMA-262 §17 (built-in constructor property is W:T, E:F, C:T).
    /// </summary>
    private void EmitInstallConstructor(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder protoField, LocalBuilder descLocal, MethodInfo setItem, System.Action emitValue)
    {
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, "constructor");
        emitValue();
        il.Emit(OpCodes.Callvirt, setItem);
        EmitInstallNonEnumerable(il, runtime, protoField, descLocal, "constructor", emitValue);
    }

    /// <summary>
    /// Wires one prototype method: wraps <paramref name="helper"/> in a
    /// $TSFunction (via TSFunctionCtorWithCache for spec-correct .name and
    /// .length), stores it in the prototype dictionary (fast-read path), and
    /// installs the non-enumerable PDS descriptor for gOPD / Object.keys /
    /// for-in.
    /// </summary>
    /// <param name="nameThisParam">
    /// When true, names the helper's first parameter <c>"__this"</c> so
    /// $TSFunction.InvokeWithThis prepends the call-site receiver. Object and
    /// Promise pass false — their helpers define the parameter name at their
    /// own emit site.
    /// </param>
    private void EmitWirePrototypeMethod(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder protoField, LocalBuilder descLocal, MethodInfo setItem,
        string jsName, MethodBuilder? helper, int jsLength, bool nameThisParam = true)
    {
        if (helper is null) return;
        if (nameThisParam)
        {
            try { helper.DefineParameter(1, ParameterAttributes.None, "__this"); }
            catch { /* already named — ignore */ }
        }
        var wrapperLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull); // target — helpers take the receiver as __this
        il.Emit(OpCodes.Ldtoken, helper);
        il.Emit(OpCodes.Ldtoken, helper.DeclaringType!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
            _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Ldstr, jsName);
        il.Emit(OpCodes.Ldc_I4, jsLength);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Stloc, wrapperLocal);
        // Fast-path dict store
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, jsName);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Callvirt, setItem);
        // Non-enumerable PDS descriptor
        EmitInstallNonEnumerable(il, runtime, protoField, descLocal, jsName,
            () => il.Emit(OpCodes.Ldloc, wrapperLocal));
    }
}
