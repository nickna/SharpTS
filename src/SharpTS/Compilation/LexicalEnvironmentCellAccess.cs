using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal static class LexicalEnvironmentCellAccess
{
    internal static bool HasOwnAccess(CompilationContext ctx, string name) => ctx.HasLoopBodyBindingAccess(name);

    internal static bool TryLoad(ILGenerator il, CompilationContext ctx, string name)
    {
        if (!HasOwnAccess(ctx, name)) return false;
        if (ctx.LoopBodyCellShadows.ContainsKey(name))
        {
            if (ctx.LoopBodyCellShadowFields.TryGetValue(name, out var shadow))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, shadow);
                il.Emit(OpCodes.Ldfld, LexicalCellStorage.ForBinding(ctx, name).ValueField);
            }
            else il.Emit(OpCodes.Ldloc, ctx.Locals.GetLocal(name)!);
        }
        else
        {
            EmitOwnReference(il, ctx, name);
            var storage = LexicalCellStorage.ForBinding(ctx, name);
            il.Emit(OpCodes.Castclass, storage.ReferenceType);
            il.Emit(OpCodes.Ldfld, storage.ValueField);
        }
        string sourceName = ctx.LoopBodyCellDeclarations.GetValueOrDefault(name)?.Lexeme
            ?? ctx.LoopBodyCellShadows.GetValueOrDefault(name)?.Lexeme ?? name;
        ctx.EmitLexicalTdzValueCheck(il, sourceName);
        return true;
    }

    internal static bool TryStore(ILGenerator il, CompilationContext ctx, string name)
    {
        if (!HasOwnAccess(ctx, name)) return false;
        var value = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, value);
        TryLoad(il, ctx, name);
        il.Emit(OpCodes.Pop);
        if (ctx.LoopBodyCellShadows.ContainsKey(name))
        {
            if (ctx.LoopBodyCellShadowFields.TryGetValue(name, out var shadow))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, shadow);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Stfld, LexicalCellStorage.ForBinding(ctx, name).ValueField);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Stloc, ctx.Locals.GetLocal(name)!);
            }
        }
        else
        {
            EmitOwnReference(il, ctx, name);
            var storage = LexicalCellStorage.ForBinding(ctx, name);
            il.Emit(OpCodes.Castclass, storage.ReferenceType);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stfld, storage.ValueField);
        }
        return true;
    }

    private static void EmitOwnReference(ILGenerator il, CompilationContext ctx, string name)
    {
        if (ctx.LoopBodyCellFields.TryGetValue(name, out var field))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
        }
        else if (ctx.CellBindingLocals.TryGetValue(name, out var local)) il.Emit(OpCodes.Ldloc, local);
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctx.CapturedFields![name]);
        }
    }

    internal static bool TryEmitCaptureReference(ILGenerator il, CompilationContext ctx, object closure, string name)
    {
        if (ctx.ClosureAnalyzer?.TryGetLoopBodyCellCapture(closure, name, out var declaration) != true) return false;
        if (ctx.LexicalCellStorageNames.TryGetValue(declaration, out var storage)
            && ctx.LoopBodyCellDeclarations.TryGetValue(storage, out var active) && ReferenceEquals(active, declaration))
            EmitOwnReference(il, ctx, storage);
        else if (ctx.LoopBodyCellCaptures?.TryGetValue(name, out var captured) == true
            && ReferenceEquals(captured, declaration) && ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
        }
        else if (ctx.CurrentGeneratorExpression is { } generator
            && generator.Captures.TryGetValue(name, out var capture) && capture.IsCell
            && ctx.ClosureAnalyzer.TryGetLoopBodyCellCapture(generator.Source, name, out var inherited)
            && ReferenceEquals(inherited, declaration))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, generator.EnvironmentField);
            foreach (var step in capture.TargetPath) il.Emit(OpCodes.Ldfld, step);
        }
        else throw new CompileException($"Live lexical cell for capture '{name}' is unavailable.");
        return true;
    }
}
