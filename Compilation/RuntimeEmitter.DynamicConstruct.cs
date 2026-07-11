using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits ConstructDynamicValue: (object ctor, object[] args) → object.
    ///
    /// The runtime construct path for `new x(...)` when the callee is a VALUE
    /// rather than a compile-time-resolvable class name — e.g. an aliased
    /// built-in (`const C = ReadableStream; new C(...)`), a member of a
    /// namespace singleton (`const I = Intl; new I.NumberFormat(...)`), or any
    /// expression callee (`new (getCtor())()`). Mirrors ReflectConstruct's
    /// target dispatch (#224):
    ///   - System.Type → Activator.CreateInstance(type, args)
    ///   - plain object / null / undefined → TypeError "not a constructor"
    ///   - everything else ($TSFunction etc.) → NewOnFunction, the shared JS
    ///     `new` protocol (fresh `this`, return-object-wins, IsConstructor
    ///     policy) — same routing ILEmitter's fallback construction uses.
    /// Must be emitted AFTER EmitNewOnFunction so runtime.NewOnFunction is set.
    /// </summary>
    private void EmitConstructDynamicValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConstructDynamicValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        runtime.ConstructDynamicValue = method;

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();

        // null / $Undefined → TypeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // System.Type (built-in or user class reference) → Activator.CreateInstance(type, args)
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Activator), "CreateInstance", _types.Type, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTypeLabel);

        // Plain objects have no [[Construct]] → TypeError. (Namespace
        // singletons like AbortSignal/Math land here: `new AbortSignal()`
        // must throw per WebIDL "Illegal constructor".)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // Callable values ($TSFunction and wrappers) → NewOnFunction(ctor, args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NewOnFunction);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Value is not a constructor");
    }
}
