using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Metadata for a private, non-escaping primitive array queue.</summary>
public sealed record ArrayQueueTypeInfo(
    TypeBuilder Type, ConstructorBuilder Constructor, ArrayElementsDescriptor Elements,
    MethodBuilder Count, MethodBuilder Push, MethodBuilder Unshift,
    MethodBuilder Shift, MethodBuilder Get, MethodBuilder Set,
    MethodBuilder? ShiftNumber, MethodBuilder? GetNumber, MethodBuilder Reserve);

public partial class RuntimeEmitter
{
    /// <summary>
    /// Two typed stacks implement the admitted queue operations. Front is in reverse
    /// order; back is in insertion order. Only when front is empty does shift reverse
    /// and swap back into front. Each appended element moves at most once, giving
    /// amortized O(1) push/shift/unshift and O(1) indexed access without changing the
    /// representation used by ordinary promoted array loops. Only queues admitting
    /// indexed writes use nullable slots to preserve holes; dense queues keep native
    /// double/bool storage. All dependencies are BCL types.
    /// </summary>
    private ArrayQueueTypeInfo EmitArrayQueue(ModuleBuilder module, EmittedRuntime runtime,
        ArrayElementsDescriptor elements, bool holes = false)
    {
        var element = elements.GetElementType(_types);
        var slot = holes ? _types.MakeNullable(element) : element;
        var list = _types.MakeGenericType(_types.ListOpen, slot);
        var type = EmitTypeDefinitions.DefineType(module, "$ArrayQueue" + elements.Kind + (holes ? "WithHoles" : ""),
            TypeAttributes.NotPublic | TypeAttributes.Sealed, _types.Object);
        var front = type.DefineField("_front", list, FieldAttributes.Private);
        var back = type.DefineField("_back", list, FieldAttributes.Private);
        var listCount = _types.GetMethodNoParams(list, "get_Count");
        var listGet = _types.GetMethod(list, "get_Item", _types.Int32);
        var listAdd = _types.GetMethod(list, "Add", slot);
        var slotCtor = holes ? _types.GetConstructor(slot, element) : null;
        var ctor = type.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        foreach (var field in new[] { front, back })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(list));
            il.Emit(OpCodes.Stfld, field);
        }
        il.Emit(OpCodes.Ret);

        MethodBuilder Method(string name, Type result, params Type[] args) =>
            type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.HideBySig, result, args);
        void LoadList(ILGenerator body, FieldBuilder field)
        {
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, field);
        }
        var count = Method("Count", _types.Int32);
        il = count.GetILGenerator();
        LoadList(il, front); il.Emit(OpCodes.Callvirt, listCount);
        LoadList(il, back); il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);

        MethodBuilder Append(string name, FieldBuilder field)
        {
            var method = Method(name, _types.Void, element);
            var body = method.GetILGenerator();
            LoadList(body, field);
            body.Emit(OpCodes.Ldarg_1);
            if (holes) body.Emit(OpCodes.Newobj, slotCtor!);
            body.Emit(OpCodes.Callvirt, listAdd);
            body.Emit(OpCodes.Ret);
            return method;
        }
        var push = Append("Push", back);
        var unshift = Append("Unshift", front);
        var reserve = Method("EnsureCapacity", _types.Int32, _types.Int32);
        il = reserve.GetILGenerator();
        LoadList(il, back); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(list, "EnsureCapacity", _types.Int32));
        il.Emit(OpCodes.Ret);

        var take = Method("Take", slot);
        il = take.GetILGenerator();
        var ready = il.DefineLabel();
        var empty = il.DefineLabel();
        var temp = il.DeclareLocal(list);
        var index = il.DeclareLocal(_types.Int32);
        var value = il.DeclareLocal(slot);
        LoadList(il, front); il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Brtrue, ready);
        LoadList(il, back); il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Brfalse, empty);
        LoadList(il, back); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(list, "Reverse"));
        LoadList(il, front); il.Emit(OpCodes.Stloc, temp);
        il.Emit(OpCodes.Ldarg_0); LoadList(il, back); il.Emit(OpCodes.Stfld, front);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, temp); il.Emit(OpCodes.Stfld, back);
        il.MarkLabel(ready);
        LoadList(il, front); il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, index);
        LoadList(il, front); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Callvirt, listGet);
        il.Emit(OpCodes.Stloc, value);
        LoadList(il, front); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(list, "RemoveAt", _types.Int32));
        il.MarkLabel(empty);
        il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ret);

        var read = Method("Read", slot, _types.Int32);
        il = read.GetILGenerator();
        var inBack = il.DefineLabel();
        empty = il.DefineLabel();
        value = il.DeclareLocal(slot);
        var frontCount = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, count);
        il.Emit(OpCodes.Bge_Un, empty);
        LoadList(il, front); il.Emit(OpCodes.Callvirt, listCount); il.Emit(OpCodes.Stloc, frontCount);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, frontCount); il.Emit(OpCodes.Bge, inBack);
        LoadList(il, front); il.Emit(OpCodes.Ldloc, frontCount); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, listGet); il.Emit(OpCodes.Ret);
        il.MarkLabel(inBack);
        LoadList(il, back); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, frontCount);
        il.Emit(OpCodes.Sub); il.Emit(OpCodes.Callvirt, listGet); il.Emit(OpCodes.Ret);
        il.MarkLabel(empty); il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ret);

        MethodBuilder ConvertResult(string name, MethodBuilder source, bool numeric, params Type[] args)
        {
            var method = Method(name, numeric ? _types.Double : _types.Object, args);
            var body = method.GetILGenerator();
            var local = body.DeclareLocal(slot);
            var present = body.DefineLabel();
            var missing = body.DefineLabel();
            if (!holes)
            {
                if (args.Length != 0) body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, count);
                body.Emit(args.Length != 0 ? OpCodes.Bge_Un : OpCodes.Brfalse, missing);
            }
            body.Emit(OpCodes.Ldarg_0);
            if (args.Length != 0) body.Emit(OpCodes.Ldarg_1);
            body.Emit(OpCodes.Call, source); body.Emit(OpCodes.Stloc, local);
            if (holes)
            {
                body.Emit(OpCodes.Ldloca, local);
                body.Emit(OpCodes.Call, _types.GetMethodNoParams(slot, "get_HasValue"));
                body.Emit(OpCodes.Brtrue, present);
            }
            else body.Emit(OpCodes.Br, present);
            body.MarkLabel(missing);
            if (numeric) body.Emit(OpCodes.Ldc_R8, double.NaN);
            else body.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            body.Emit(OpCodes.Ret);
            body.MarkLabel(present);
            if (holes)
            {
                body.Emit(OpCodes.Ldloca, local);
                body.Emit(OpCodes.Call, _types.GetMethodNoParams(slot, "GetValueOrDefault"));
            }
            else body.Emit(OpCodes.Ldloc, local);
            if (!numeric) body.Emit(OpCodes.Box, element);
            body.Emit(OpCodes.Ret);
            return method;
        }
        var shift = ConvertResult("Shift", take, false);
        var get = ConvertResult("Get", read, false, _types.Int32);
        var shiftNumber = elements.Kind == ArrayElementsKind.Double ? ConvertResult("ShiftNumber", take, true) : null;
        var getNumber = elements.Kind == ArrayElementsKind.Double ? ConvertResult("GetNumber", read, true, _types.Int32) : null;

        var set = Method("Set", _types.Void, _types.Int32, element);
        il = set.GetILGenerator();
        frontCount = il.DeclareLocal(_types.Int32);
        var hole = il.DeclareLocal(slot);
        inBack = il.DefineLabel();
        var extend = il.DefineLabel();
        var store = il.DefineLabel();
        LoadList(il, front); il.Emit(OpCodes.Callvirt, listCount); il.Emit(OpCodes.Stloc, frontCount);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, frontCount); il.Emit(OpCodes.Bge, inBack);
        LoadList(il, front); il.Emit(OpCodes.Ldloc, frontCount); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldarg_2);
        if (holes) il.Emit(OpCodes.Newobj, slotCtor!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(list, "set_Item", _types.Int32, slot));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(inBack);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloc, frontCount); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(extend);
        LoadList(il, back); il.Emit(OpCodes.Callvirt, listCount); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bgt, store);
        LoadList(il, back); il.Emit(OpCodes.Ldloc, hole); il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Br, extend);
        il.MarkLabel(store);
        LoadList(il, back); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        if (holes) il.Emit(OpCodes.Newobj, slotCtor!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(list, "set_Item", _types.Int32, slot));
        il.Emit(OpCodes.Ret);
        type.CreateType();
        return new(type, ctor, elements, count, push, unshift, shift, get, set, shiftNumber, getNumber, reserve);
    }
}
