using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

internal static class StandaloneAsyncCaptureEmitter
{
    internal static void EmitReference(ILGenerator il, CompilationContext ctx,
        FieldBuilder binding, string name, AsyncArrowStateMachineBuilder? caller = null)
        => EmitOwnerReference(il, ctx, binding.DeclaringType!, name, caller);

    internal static void EmitOwnerReference(ILGenerator il, CompilationContext ctx,
        Type owner, string name, AsyncArrowStateMachineBuilder? caller = null)
    {
        if (!TryEmitOwnerReference(il, ctx, owner, name, caller))
            throw new CompileException($"Shared storage for async capture '{name}' is unavailable.");
    }

    internal static bool TryEmitOwnerReference(ILGenerator il, CompilationContext ctx,
        Type owner, string name, AsyncArrowStateMachineBuilder? caller = null)
    {
        foreach (var local in new[] { ctx.FunctionDisplayClassLocal, ctx.ArrowScopeDisplayClassLocal })
        {
            if (local?.LocalType != owner) continue;
            il.Emit(OpCodes.Ldloc, local);
            return true;
        }

        IEnumerable<FieldBuilder?> references = new[]
        {
            ctx.CurrentArrowFunctionDCField, ctx.CurrentArrowScopeDCField,
            caller?.FunctionDCField
        };
        if (ctx.CurrentArrowScopeDCExtraFields is { } extra)
            references = references.Concat(extra.Values);
        if (caller is not null)
            references = references.Concat(caller.StandaloneCaptureFields.Values);
        foreach (var reference in references)
        {
            if (reference?.FieldType != owner) continue;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, reference);
            return true;
        }
        if (caller?.OuterStateMachineField is not null
            && ctx.OuterFunctionDCField?.FieldType == owner)
        {
            AsyncArrowStorageAccess.CapturedField(caller, name, ctx.OuterFunctionDCField).EmitLoad(il);
            return true;
        }
        return false;
    }
}
