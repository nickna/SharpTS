using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakRefMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Shared primitive probe: RuntimeEmitter.WeakValidation.cs
        runtime.ValidateWeakRefTarget = EmitWeakTargetValidator(typeBuilder, "ValidateWeakRefTarget",
            "Runtime Error: Invalid value used as weak reference target. WeakRef target must be an object");

        EmitCreateWeakRef(typeBuilder, runtime);
        EmitWeakRefDeref(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits CreateWeakRef(object target) -> object (WeakReference&lt;object&gt;).
    /// Validates target is not a primitive, then creates a WeakReference.
    /// </summary>
    private void EmitCreateWeakRef(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakRef",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateWeakRef = method;

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();

        // if (target == null) throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // ValidateWeakRefTarget(target)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ValidateWeakRefTarget);

        // new WeakReference<object>(target)
        var weakRefType = _types.WeakReferenceObject;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(weakRefType, [_types.Object])!);
        il.Emit(OpCodes.Ret);

        // null target - throw
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: WeakRef target cannot be null or undefined.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits WeakRefDeref(object weakRef) -> object? (target or null).
    /// </summary>
    private void EmitWeakRefDeref(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakRefDeref",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.WeakRefDeref = method;

        var il = method.GetILGenerator();
        var weakRefType = _types.WeakReferenceObject;
        var targetLocal = il.DeclareLocal(_types.Object);

        var returnNullLabel = il.DefineLabel();

        // if (weakRef is not WeakReference<object> wr) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, weakRefType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // wr.TryGetTarget(out target)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, weakRefType);
        il.Emit(OpCodes.Ldloca, targetLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(weakRefType, "TryGetTarget")!);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // return target;
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ret);

        // return null;
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
