using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ArrayBindingInputs(
        CollectionConsumerInputs Iteration, EmittedArrayStorageRuntime Arrays,
        FieldInfo Undefined, MethodInfo CancellationCheck);

    private void EmitArrayBinding(TypeBuilder type, EmittedArrayOperationsRuntime arrays, ArrayBindingInputs inputs)
    {
        var iteration = inputs.Iteration;
        var start = type.DefineMethod("ArrayBindingStart", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        var step = type.DefineMethod("ArrayBindingNext", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.ObjectArray, _types.Boolean]);
        var close = type.DefineMethod("ArrayBindingClose", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.ObjectArray, _types.Boolean]);
        var closeScope = type.DefineMethod("ArrayBindingCloseScope", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.ListOfObject, _types.Boolean]);
        var dispatch = type.DefineMethod("ArrayBinding", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.ObjectArray]);
        arrays.Binding = dispatch;

        // Cursor slots: iterator, captured next, completed, native indexed source, index.
        // These are private CLR arrays; no guest object or prototype can intercept them.
        var il = start.GetILGenerator();
        var factory = il.DeclareLocal(_types.Object);
        var cursor = il.DeclareLocal(_types.ObjectArray);
        var iterator = il.DeclareLocal(_types.Object);
        var hasIteratorClaim = il.DeclareLocal(_types.Boolean);
        var acquired = il.DefineLabel();
        var fallback = il.DefineLabel();
        var invalid = il.DefineLabel();
        var register = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, cursor);
        StoreSlot(il, cursor, 2, () => EmitBoolean(il, false));
        // Snapshot a native carrier's guest claim before invoking its getter:
        // deleting the descriptor during lookup must not restore native fallback.
        var owner = il.DeclareLocal(_types.Object);
        var storage = il.DeclareLocal(_types.DictionaryObjectObject);
        var inspectSource = il.DefineLabel();
        var inspect = il.DefineLabel();
        var inspectStorage = il.DefineLabel();
        var parent = il.DefineLabel();
        var claimed = il.DefineLabel();
        var lookup = il.DefineLabel();
        foreach (var nativeType in new[] { iteration.TypedArrays?.BaseType, iteration.Buffer?.Type,
                     iteration.MapIteratorConstructor is null ? null : _types.DictionaryObjectObject,
                     iteration.SetIteratorConstructor is null ? null : _types.HashSetOfObject })
        {
            if (nativeType is null) continue;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, nativeType);
            il.Emit(OpCodes.Brtrue, inspectSource);
        }
        il.Emit(OpCodes.Br, lookup);
        il.MarkLabel(inspectSource);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, owner);
        il.MarkLabel(inspect);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Brfalse, lookup);
        // A proxy owns the ordinary lookup; do not invoke extra descriptor or
        // prototype traps while deciding whether an internal bridge applies.
        EmitProxyTypeCheck(il, () => il.Emit(OpCodes.Ldloc, owner), claimed, inspectStorage);
        il.MarkLabel(inspectStorage);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Call, iteration.Symbols.TryGetStorage);
        il.Emit(OpCodes.Stloc, storage);
        il.Emit(OpCodes.Ldloc, storage);
        il.Emit(OpCodes.Brfalse, parent);
        il.Emit(OpCodes.Ldloc, storage);
        il.Emit(OpCodes.Ldsfld, iteration.Symbols.Iterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brtrue, claimed);
        il.MarkLabel(parent);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Call, iteration.Prototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, owner);
        il.Emit(OpCodes.Br, inspect);
        il.MarkLabel(claimed);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasIteratorClaim);
        il.MarkLabel(lookup);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, iteration.Symbols.Iterator);
        il.Emit(OpCodes.Call, iteration.ObjectRead.Index);
        il.Emit(OpCodes.Stloc, factory);
        il.Emit(OpCodes.Ldloc, factory);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, factory);
        il.Emit(OpCodes.Isinst, iteration.UndefinedType);
        il.Emit(OpCodes.Brtrue, fallback);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, factory);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, iteration.Invocation.Method);
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Br, acquired);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldloc, hasIteratorClaim);
        il.Emit(OpCodes.Brtrue, invalid);
        // Native typed-array/buffer carriers retain indexed state rather than
        // snapshotting their elements before a default can mutate them.
        foreach (var nativeType in new[] { iteration.TypedArrays?.BaseType, iteration.Buffer?.Type })
        {
            if (nativeType is null) continue;
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, nativeType);
            il.Emit(OpCodes.Brfalse, next);
            StoreSlot(il, cursor, 3, () => il.Emit(OpCodes.Ldarg_1));
            StoreSlot(il, cursor, 4, () => { il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Box, _types.Double); });
            il.Emit(OpCodes.Br, register);
            il.MarkLabel(next);
        }
        if (iteration.MapIteratorConstructor is { } mapCtor)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.DictionaryObjectObject, "Keys"));
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newobj, mapCtor);
            il.Emit(OpCodes.Stloc, iterator);
            il.Emit(OpCodes.Br, acquired);
            il.MarkLabel(next);
        }
        if (iteration.SetIteratorConstructor is { } setCtor)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
            il.Emit(OpCodes.Newobj, setCtor);
            il.Emit(OpCodes.Stloc, iterator);
            il.Emit(OpCodes.Br, acquired);
            il.MarkLabel(next);
        }
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowError(il, iteration.Errors.CreateException,
            iteration.Errors.TypeErrorConstructor, "source is not iterable");
        il.MarkLabel(acquired);
        StoreSlot(il, cursor, 0, () => { il.Emit(OpCodes.Ldloc, iterator); il.Emit(OpCodes.Call, iteration.Records.RequireObject); });
        StoreSlot(il, cursor, 1, () => { il.Emit(OpCodes.Ldloc, iterator); il.Emit(OpCodes.Call, iteration.Records.NextMethod); });
        il.MarkLabel(register);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, cursor);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, cursor);
        il.Emit(OpCodes.Ret);

        il = step.GetILGenerator();
        var finished = il.DefineLabel();
        var yielded = il.DefineLabel();
        var guestStep = il.DefineLabel();
        var result = il.DeclareLocal(_types.Object);
        var value = il.DeclareLocal(_types.Object);
        LoadArgSlot(il, 2);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, finished);
        SetArgSlot(il, 2, () => EmitBoolean(il, true));
        il.Emit(OpCodes.Call, inputs.CancellationCheck);
        LoadArgSlot(il, 3);
        il.Emit(OpCodes.Brfalse, guestStep);
        LoadArgSlot(il, 4);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        LoadArgSlot(il, 3);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, iteration.ObjectRead.Property);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Bge, finished);
        LoadArgSlot(il, 3);
        LoadArgSlot(il, 4);
        il.Emit(OpCodes.Call, iteration.ObjectRead.Index);
        il.Emit(OpCodes.Stloc, value);
        SetArgSlot(il, 4, () => { LoadArgSlot(il, 4); il.Emit(OpCodes.Unbox_Any, _types.Double); il.Emit(OpCodes.Ldc_R8, 1d); il.Emit(OpCodes.Add); il.Emit(OpCodes.Box, _types.Double); });
        il.Emit(OpCodes.Br, yielded);
        il.MarkLabel(guestStep);
        LoadArgSlot(il, 0);
        LoadArgSlot(il, 1);
        il.Emit(OpCodes.Call, iteration.Records.InvokeNext);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Call, iteration.Protocol.Done);
        il.Emit(OpCodes.Brtrue, finished);
        il.Emit(OpCodes.Ldsfld, inputs.Undefined);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, yielded);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Call, iteration.Protocol.Value);
        il.Emit(OpCodes.Stloc, value);
        il.MarkLabel(yielded);
        SetArgSlot(il, 2, () => EmitBoolean(il, false));
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(finished);
        il.Emit(OpCodes.Ldsfld, inputs.Undefined);
        il.Emit(OpCodes.Ret);

        il = close.GetILGenerator();
        var closed = il.DefineLabel();
        LoadArgSlot(il, 2);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, closed);
        SetArgSlot(il, 2, () => EmitBoolean(il, true));
        LoadArgSlot(il, 3);
        il.Emit(OpCodes.Brtrue, closed);
        LoadArgSlot(il, 0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, iteration.Protocol.Close);
        il.MarkLabel(closed);
        il.Emit(OpCodes.Ret);

        il = closeScope.GetILGenerator();
        il.BeginExceptionBlock();
        EmitCloseScopeLoop(() => il.Emit(OpCodes.Ldarg_1));
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        // Once a close replaces a return with a throw, remaining outer cursors
        // close with throw precedence without replacing that first failure.
        EmitCloseScopeLoop(() => il.Emit(OpCodes.Ldc_I4_1));
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ret);

        il = dispatch.GetILGenerator();
        var operation = il.DeclareLocal(_types.String);
        LoadArgSlot(il, 0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, operation);
        var returnUndefined = il.DefineLabel();
        EmitOperation("scope", () => { il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Ret); });
        EmitOperation("start", () => { LoadArgSlot(il, 1); LoadArgSlot(il, 2); il.Emit(OpCodes.Call, start); il.Emit(OpCodes.Ret); });
        EmitOperation("next", () => { LoadArgSlot(il, 1); il.Emit(OpCodes.Castclass, _types.ObjectArray); LoadArgSlot(il, 3); il.Emit(OpCodes.Unbox_Any, _types.Boolean); il.Emit(OpCodes.Call, step); il.Emit(OpCodes.Ret); });
        EmitOperation("close", () => { LoadArgSlot(il, 1); il.Emit(OpCodes.Castclass, _types.ObjectArray); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, close); il.Emit(OpCodes.Br, returnUndefined); });
        EmitOperation("rest", () =>
        {
            var rest = il.DeclareLocal(_types.ListOfObject);
            var state = il.DeclareLocal(_types.ObjectArray);
            var item = il.DeclareLocal(_types.Object);
            var loop = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Stloc, rest);
            LoadArgSlot(il, 1);
            il.Emit(OpCodes.Castclass, _types.ObjectArray);
            il.Emit(OpCodes.Stloc, state);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, step);
            il.Emit(OpCodes.Stloc, item);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldloc, rest);
            il.Emit(OpCodes.Ldloc, item);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldloc, rest);
            il.Emit(OpCodes.Newobj, inputs.Arrays.Ctor);
            il.Emit(OpCodes.Ret);
        });
        EmitOperation("abort", () => EmitCloseScope(true));
        EmitOperation("finish", () => EmitCloseScope(false));
        il.MarkLabel(returnUndefined);
        il.Emit(OpCodes.Ldsfld, inputs.Undefined);
        il.Emit(OpCodes.Ret);

        void EmitCloseScope(bool preserveThrow)
        {
            LoadArgSlot(il, 1);
            il.Emit(OpCodes.Castclass, _types.ListOfObject);
            il.Emit(preserveThrow ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, closeScope);
            il.Emit(OpCodes.Br, returnUndefined);
        }
        void EmitCloseScopeLoop(Action emitPreserveThrow)
        {
            var index = il.DeclareLocal(_types.Int32);
            var loop = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
            il.Emit(OpCodes.Stloc, index);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
            il.Emit(OpCodes.Castclass, _types.ObjectArray);
            emitPreserveThrow();
            il.Emit(OpCodes.Call, close);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(done);
        }
        void EmitOperation(string name, Action body)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, operation);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, next);
            body();
            il.MarkLabel(next);
        }
        void EmitBoolean(ILGenerator body, bool value)
        {
            body.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            body.Emit(OpCodes.Box, _types.Boolean);
        }
        static void LoadArgSlot(ILGenerator body, int index)
        {
            body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldc_I4, index); body.Emit(OpCodes.Ldelem_Ref);
        }
        static void SetArgSlot(ILGenerator body, int index, Action emit)
        {
            body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldc_I4, index); emit(); body.Emit(OpCodes.Stelem_Ref);
        }
        static void StoreSlot(ILGenerator body, LocalBuilder local, int index, Action emit)
        {
            body.Emit(OpCodes.Ldloc, local); body.Emit(OpCodes.Ldc_I4, index); emit(); body.Emit(OpCodes.Stelem_Ref);
        }
    }
}
