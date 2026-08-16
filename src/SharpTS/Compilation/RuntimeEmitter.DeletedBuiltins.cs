using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits two helpers backing the <see cref="EmittedRuntime.DeletedBuiltinsField"/>
    /// per-instance set. <see cref="EmittedRuntime.MarkBuiltinDeletedMethod"/>
    /// records a deletion (lazily creating the per-object HashSet);
    /// <see cref="EmittedRuntime.IsBuiltinDeletedMethod"/> consults it. Used
    /// for ECMA-262 §17 configurable-name/length semantics on $TSFunction.
    /// </summary>
    private void EmitDeletedBuiltinsHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitMarkBuiltinDeleted(typeBuilder, runtime);
        EmitIsBuiltinDeleted(typeBuilder, runtime);
    }

    private void EmitMarkBuiltinDeleted(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MarkBuiltinDeleted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String]);
        runtime.MarkBuiltinDeletedMethod = method;
        method.DefineParameter(1, ParameterAttributes.None, "obj");
        method.DefineParameter(2, ParameterAttributes.None, "name");

        var il = method.GetILGenerator();
        var hashSetType = typeof(HashSet<string>);

        // if (obj == null) return;
        var bodyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, bodyLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(bodyLabel);

        // var existing = null; _deletedBuiltins.TryGetValue(obj, out existing);
        var existingLocal = il.DeclareLocal(_types.Object);
        var setLocal = il.DeclareLocal(hashSetType);
        il.Emit(OpCodes.Ldsfld, runtime.DeletedBuiltinsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue",
            _types.Object, _types.Object.MakeByRefType()));

        var hasSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasSetLabel);

        // No existing entry — create a fresh HashSet<string> and add it to the table.
        il.Emit(OpCodes.Newobj, hashSetType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, setLocal);
        il.Emit(OpCodes.Ldsfld, runtime.DeletedBuiltinsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "Add",
            _types.Object, _types.Object));
        var afterLookupLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterLookupLabel);

        il.MarkLabel(hasSetLabel);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, hashSetType);
        il.Emit(OpCodes.Stloc, setLocal);
        il.MarkLabel(afterLookupLabel);

        // setLocal.Add(name) — return value (bool) is discarded.
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, hashSetType.GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIsBuiltinDeleted(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsBuiltinDeleted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]);
        runtime.IsBuiltinDeletedMethod = method;
        method.DefineParameter(1, ParameterAttributes.None, "obj");
        method.DefineParameter(2, ParameterAttributes.None, "name");

        var il = method.GetILGenerator();
        var hashSetType = typeof(HashSet<string>);

        // if (obj == null) return false;
        var bodyLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, bodyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(bodyLabel);

        // var existing = null; if (!_deletedBuiltins.TryGetValue(obj, out existing)) return false;
        var existingLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.DeletedBuiltinsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue",
            _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return ((HashSet<string>)existing).Contains(name);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, hashSetType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, hashSetType.GetMethod("Contains", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
