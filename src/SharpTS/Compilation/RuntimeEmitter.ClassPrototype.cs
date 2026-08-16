using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the cache for user-class prototype objects. Prototype allocation is
    /// performed by compiler-generated constructors on the user types themselves;
    /// this runtime helper only registers stable identities and wires prototype
    /// inheritance.
    /// </summary>
    private void EmitClassPrototypeSupport(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder cacheField)
    {
        var cacheType = cacheField.FieldType;
        var tryGetValue = _types.GetMethod(
            cacheType, "TryGetValue", [_types.Type, _types.Object.MakeByRefType()])!;
        var setItem = _types.GetMethod(
            cacheType, "set_Item", [_types.Type, _types.Object])!;

        var getMethod = typeBuilder.DefineMethod(
            "GetClassPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type]);
        runtime.GetClassPrototypeMethod = getMethod;

        var registerMethod = typeBuilder.DefineMethod(
            "RegisterClassPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type, _types.Object, _types.Type]);
        runtime.RegisterClassPrototypeMethod = registerMethod;

        // GetClassPrototype(Type): registered user classes return their stable
        // object. A cache miss forces the compiler-generated type initializer,
        // which registers the prototype without reflection or running a user
        // constructor. Other $IHasFields implementers (notably $Object) use the
        // normal Object.prototype fallback.
        {
            var il = getMethod.GetILGenerator();
            var prototypeLocal = il.DeclareLocal(_types.Object);
            var useObjectPrototype = il.DefineLabel();

            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, prototypeLocal);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            var initializeType = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, initializeType);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(initializeType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt,
                typeof(Type).GetProperty(nameof(Type.TypeHandle))!.GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);

            // The generated initializer registers user-class prototypes. If the
            // type was a runtime implementation rather than a user class, the
            // second lookup still misses and falls back to Object.prototype.
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, prototypeLocal);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            il.Emit(OpCodes.Brfalse, useObjectPrototype);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(useObjectPrototype);
            il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
            il.Emit(OpCodes.Ret);
        }

        // RegisterClassPrototype(Type, object, Type?): publish first, then resolve
        // the base. Publishing first keeps recursive prototype resolution stable.
        {
            var il = registerMethod.GetILGenerator();
            var useObjectPrototype = il.DefineLabel();
            var prototypeReady = il.DefineLabel();
            var basePrototypeLocal = il.DeclareLocal(_types.Object);

            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, useObjectPrototype);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, getMethod);
            il.Emit(OpCodes.Stloc, basePrototypeLocal);
            il.Emit(OpCodes.Br, prototypeReady);

            il.MarkLabel(useObjectPrototype);
            il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
            il.Emit(OpCodes.Stloc, basePrototypeLocal);

            il.MarkLabel(prototypeReady);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, basePrototypeLocal);
            il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
            il.Emit(OpCodes.Ret);
        }
    }
}
