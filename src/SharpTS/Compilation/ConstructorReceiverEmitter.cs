using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal static class ConstructorReceiverEmitter
{
    internal static void CloseNominalSuperTarget(ILGenerator il, CompilationContext context)
    {
        // A guest generic constructor keeps its open Type identity. Its factory
        // must execute through the checked closed owner, but only while that
        // exact nominal constructor is still the selected live parent.
        if (context.CurrentClassBuilder?.BaseType is not { IsConstructedGenericType: true } parent
            || parent.ContainsGenericParameters
            || parent.GetGenericTypeDefinition() is not TypeBuilder definition
            || !context.Classes.Values.Any(owner => ReferenceEquals(owner, definition))) return;

        var selected = il.DeclareLocal(context.Types.Object);
        var ready = il.DefineLabel();
        il.Emit(OpCodes.Stloc, selected);
        il.Emit(OpCodes.Ldloc, selected);
        il.Emit(OpCodes.Ldtoken, definition);
        il.Emit(OpCodes.Call, context.Types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Bne_Un, ready);
        il.Emit(OpCodes.Ldtoken, parent);
        il.Emit(OpCodes.Call, context.Types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stloc, selected);
        il.MarkLabel(ready);
        il.Emit(OpCodes.Ldloc, selected);
    }

    internal static void ReadCell(ILGenerator il, CompilationContext context)
    {
        il.Emit(OpCodes.Castclass, context.Types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        var initialized = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, context.Runtime!.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brfalse, initialized);
        il.Emit(OpCodes.Pop);
        GuestErrorEmitter.ThrowError(il, context.Runtime.Errors.CreateException,
            context.Runtime.Errors.ReferenceErrorConstructor,
            "Must call super constructor before accessing this");
        il.MarkLabel(initialized);
    }
}
