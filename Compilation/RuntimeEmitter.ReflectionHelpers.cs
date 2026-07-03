using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Assembly-qualified late-bound name of <see cref="RuntimeTypes"/> — the
    /// default target of the reflection-call idiom. The string form (instead of
    /// a <c>typeof</c> token) is what keeps compiled DLLs standalone; see the
    /// CLAUDE.md "Standalone DLL Constraint" section and StandaloneDllTests.
    /// </summary>
    private const string RuntimeTypesLateBoundName = "SharpTS.Compilation.RuntimeTypes, SharpTS";

    /// <summary>
    /// Emits a <c>new object[count]</c> populated element-by-element, leaving the
    /// array on the IL stack. <paramref name="emitElement"/> receives the element
    /// index and must push exactly one object reference (already boxed). This is
    /// the canonical arg-packing idiom (<c>ldc/newarr/{dup;ldc;…;stelem.ref}×N</c>)
    /// for raw-<see cref="ILGenerator"/> emit sites; expression-driven emitters use
    /// <see cref="ExpressionEmitterBase.EmitArgsArray"/> instead.
    /// </summary>
    private void EmitObjectArray(ILGenerator il, int count, System.Action<int> emitElement)
    {
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Newarr, _types.Object);
        for (int i = 0; i < count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitElement(i);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    /// <summary>
    /// Defines a public static helper method on <paramref name="typeBuilder"/> whose
    /// body calls <c>RuntimeTypes.{methodName}</c> via the late-bound reflection idiom
    /// (<c>Type.GetType("…, SharpTS").GetMethod(name).Invoke(null, args)</c>).
    /// All parameters and the return value are <c>object?</c>. No missing-runtime
    /// guard is emitted — callers reach these paths only for features that record
    /// <see cref="EmittedRuntime.RequireSharpTSRuntime"/>, so SharpTS.dll is co-located.
    /// </summary>
    private MethodBuilder EmitReflectionHelper(TypeBuilder typeBuilder, string methodName, int argCount)
    {
        var paramTypes = new Type[argCount];
        Array.Fill(paramTypes, _types.Object);

        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        EmitReflectionCall(il, RuntimeTypesLateBoundName, methodName, argCount);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits the late-bound SharpTS reflection-call idiom inline:
    /// <c>Type.GetType(typeName).GetMethod(methodName).Invoke(null, new object[argCount] { … })</c>,
    /// leaving the invoke result (an object reference) on the IL stack.
    /// </summary>
    /// <param name="emitArg">
    /// Pushes element <c>i</c> of the argument array (must already be an object
    /// reference — box value types). Defaults to <c>ldarg i</c>.
    /// </param>
    /// <param name="onMissing">
    /// When supplied, a <c>Type.GetType</c> null-check is emitted and this callback
    /// provides the missing-runtime path; it must NOT fall through — end with
    /// <c>ret</c> or <c>throw</c>. When null, no guard is emitted (the site relies on
    /// RequireSharpTSRuntime co-locating SharpTS.dll, and fails with an NRE otherwise).
    /// </param>
    private void EmitReflectionCall(ILGenerator il, string typeName, string methodName, int argCount,
        System.Action<int>? emitArg = null, System.Action? onMissing = null)
    {
        il.Emit(OpCodes.Ldstr, typeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));

        if (onMissing != null)
        {
            var typeLocal = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Stloc, typeLocal);
            var typeOk = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Brtrue, typeOk);
            onMissing();
            il.MarkLabel(typeOk);
            il.Emit(OpCodes.Ldloc, typeLocal);
        }

        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));

        il.Emit(OpCodes.Ldnull); // null target for static method invoke
        EmitObjectArray(il, argCount, emitArg ?? (i => il.Emit(OpCodes.Ldarg, i)));

        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
    }

    /// <summary>
    /// <see cref="EmitReflectionCall"/> for void-returning (or discarded-result)
    /// targets — same idiom followed by a <c>pop</c>; leaves nothing on the stack.
    /// </summary>
    private void EmitReflectionCallVoid(ILGenerator il, string typeName, string methodName, int argCount,
        System.Action<int>? emitArg = null, System.Action? onMissing = null)
    {
        EmitReflectionCall(il, typeName, methodName, argCount, emitArg, onMissing);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits the late-bound construction idiom:
    /// <c>Activator.CreateInstance(Type.GetType(typeName), new object[argCount] { … })</c>,
    /// leaving the new instance on the IL stack. Used where the soft dependency is a
    /// SharpTS runtime TYPE (e.g. SharpTSProxy) rather than a static method.
    /// </summary>
    private void EmitReflectionCreateInstance(ILGenerator il, string typeName, int argCount,
        System.Action<int>? emitArg = null)
    {
        il.Emit(OpCodes.Ldstr, typeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        EmitObjectArray(il, argCount, emitArg ?? (i => il.Emit(OpCodes.Ldarg, i)));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Activator, "CreateInstance", _types.Type, _types.ObjectArray)!);
    }
}
