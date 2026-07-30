using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the <c>$Runtime</c> static field and helper methods that track .NET event
    /// subscriptions created via <c>@DotNetType addEventListener</c>. Needed so
    /// <c>removeEventListener</c> can find the Delegate instance that was added for a
    /// given (owner, eventName, tsFunction) triple.
    /// </summary>
    /// <remarks>
    /// Storage is a <c>List&lt;object?[]&gt;</c> with each entry a 4-tuple
    /// <c>[owner, name, fn, delegate]</c>, protected by a lock on the list. Linear scan
    /// at add/remove; typical event-listener counts are small enough that this is
    /// simpler than emitting a hash-based dictionary without collapsing into SharpTS
    /// type dependencies.
    /// </remarks>
    internal void EmitEventSubscriptionHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime, ILGenerator cctorIL)
    {
        // Static field: List<object?[]> _eventSubscriptions
        var listType = _types.MakeGenericType(_types.ListOpen, _types.ObjectArray);
        var field = typeBuilder.DefineField(
            "_eventSubscriptions",
            listType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.EventSubscriptionsField = field;

        // cctor: _eventSubscriptions = new List<object?[]>();
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(listType));
        cctorIL.Emit(OpCodes.Stsfld, field);

        runtime.AddEventSubscription = EmitAddEventSubscription(typeBuilder, runtime, listType);
        runtime.RemoveEventSubscription = EmitRemoveEventSubscription(typeBuilder, runtime, listType);
    }

    /// <summary>
    /// Emits <c>bool AddEventSubscription(object owner, string name, object fn, Delegate d)</c>.
    /// Returns false if an entry already exists for (owner, name, fn) — idempotent, matches
    /// the interpreter's <c>DotNetEventBinder</c> semantics.
    /// </summary>
    private MethodBuilder EmitAddEventSubscription(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "AddEventSubscription",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String, _types.Object, typeof(Delegate)]);

        var il = method.GetILGenerator();

        var listLocal = il.DeclareLocal(listType);
        var lockTakenLocal = il.DeclareLocal(_types.Boolean);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var entryLocal = il.DeclareLocal(_types.ObjectArray);

        // list = _eventSubscriptions
        il.Emit(OpCodes.Ldsfld, runtime.EventSubscriptionsField);
        il.Emit(OpCodes.Stloc, listLocal);

        // Monitor.Enter(list, ref lockTaken)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lockTakenLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloca, lockTakenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(System.Threading.Monitor), "Enter", _types.Object, _types.Boolean.MakeByRefType()));

        // for (int i = 0; i < list.Count; i++) { ... }
        var listCountGetter = _types.GetPropertyGetter(listType, "Count");
        var listGetItem = _types.GetMethod(listType, "get_Item")!;

        var loopTop = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var loopContinue = il.DefineLabel();
        var foundDuplicate = il.DefineLabel();
        var insertNewEntry = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // entry = list[i]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, listGetItem);
        il.Emit(OpCodes.Stloc, entryLocal);

        // if (ReferenceEquals(entry[0], owner) && (string)entry[1] == name && ReferenceEquals(entry[2], fn)) found
        // entry[0] == owner
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Bne_Un, loopContinue);

        // (string)entry[1] == name
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, loopContinue);

        // entry[2] == fn
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Bne_Un, loopContinue);

        // Match — return false (duplicate).
        il.Emit(OpCodes.Br, foundDuplicate);

        il.MarkLabel(loopContinue);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, insertNewEntry);

        il.MarkLabel(foundDuplicate);
        // Return false — but first fall through to finally (Leave).
        il.Emit(OpCodes.Ldc_I4_0);
        var retLabel = il.DefineLabel();
        var resultLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, retLabel);

        il.MarkLabel(insertNewEntry);
        // list.Add(new object?[4] { owner, name, fn, d })
        il.Emit(OpCodes.Ldloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stelem_Ref);

        var listAdd = _types.GetMethod(listType, "Add", [_types.ObjectArray])!;
        il.Emit(OpCodes.Callvirt, listAdd);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, retLabel);

        il.BeginFinallyBlock();
        // if (lockTaken) Monitor.Exit(list)
        il.Emit(OpCodes.Ldloc, lockTakenLocal);
        var skipExit = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipExit);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(System.Threading.Monitor), "Exit", _types.Object));
        il.MarkLabel(skipExit);
        il.Emit(OpCodes.Endfinally);
        il.EndExceptionBlock();

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits <c>Delegate? RemoveEventSubscription(object owner, string name, object fn)</c>.
    /// Returns the previously-registered Delegate for (owner, name, fn) and removes the entry,
    /// or null if none was registered.
    /// </summary>
    private MethodBuilder EmitRemoveEventSubscription(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "RemoveEventSubscription",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Delegate),
            [_types.Object, _types.String, _types.Object]);

        var il = method.GetILGenerator();

        var listLocal = il.DeclareLocal(listType);
        var lockTakenLocal = il.DeclareLocal(_types.Boolean);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var entryLocal = il.DeclareLocal(_types.ObjectArray);
        var resultLocal = il.DeclareLocal(typeof(Delegate));

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldsfld, runtime.EventSubscriptionsField);
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lockTakenLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloca, lockTakenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(System.Threading.Monitor), "Enter", _types.Object, _types.Boolean.MakeByRefType()));

        var listCountGetter = _types.GetPropertyGetter(listType, "Count");
        var listGetItem = _types.GetMethod(listType, "get_Item")!;
        var listRemoveAt = _types.GetMethod(listType, "RemoveAt", [_types.Int32])!;

        var retLabel = il.DefineLabel();
        var loopTop = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var loopContinue = il.DefineLabel();
        var foundMatch = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, listGetItem);
        il.Emit(OpCodes.Stloc, entryLocal);

        // Same match test as AddEventSubscription.
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Bne_Un, loopContinue);

        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, loopContinue);

        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Bne_Un, loopContinue);

        il.Emit(OpCodes.Br, foundMatch);

        il.MarkLabel(loopContinue);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(foundMatch);
        // result = (Delegate)entry[3]; list.RemoveAt(i);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(Delegate));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, listRemoveAt);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Leave, retLabel);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, lockTakenLocal);
        var skipExit = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipExit);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(System.Threading.Monitor), "Exit", _types.Object));
        il.MarkLabel(skipExit);
        il.Emit(OpCodes.Endfinally);
        il.EndExceptionBlock();

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
