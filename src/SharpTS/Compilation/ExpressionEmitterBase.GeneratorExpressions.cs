using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private void EmitGeneratorExpression(Expr.ArrowFunction source)
    {
        if (!Ctx.GeneratorExpressions.TryGetValue(source, out var info))
            throw new CompileException("Generator expression has no compiled lexical environment.");

        if (!TryEmitCapturingArrowDisplayInstance(source))
            EmitCapturingArrowDisplayViaHooks(source, info.TargetConstructor, generatorExpression: true);
        var target = IL.DeclareLocal(info.TargetType);
        IL.Emit(OpCodes.Stloc, target);

        // Relay actual enclosing storage owners, not a value read from their fields.
        foreach (var binding in info.Captures.Where(c => c.Value.TargetPath.Count > 1)
                     .GroupBy(c => c.Value.TargetPath[0]).Select(group => group.First()))
        {
            var field = binding.Value.TargetPath[0];
            IL.Emit(OpCodes.Ldloc, target);
            if (!TryEmitGeneratorCaptureOwner(field.FieldType, binding.Key))
                throw new CompileException($"Generator capture owner '{field.Name}' is unavailable.");
            IL.Emit(OpCodes.Stfld, field);
        }

        IL.Emit(OpCodes.Ldloc, target);
        Types.EmitLoadMethodInfoViaHandle(IL, info.Kickoff);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.FunctionConstruction.Constructor);
        var function = IL.DeclareLocal(Ctx.Runtime.FunctionValues.Type);
        IL.Emit(OpCodes.Stloc, function);

        // Each evaluation owns its function property identity and initial prototype.
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Stfld, Ctx.Runtime.FunctionValues.PropertyIdentityField);
        IL.Emit(OpCodes.Ldloc, target);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Stfld, info.FunctionValueField);
        if (source.Name is { } name && Ctx.DisplayClassFields[source].TryGetValue(name.Lexeme, out var self))
        {
            IL.Emit(OpCodes.Ldloc, target);
            IL.Emit(OpCodes.Ldloc, function);
            IL.Emit(OpCodes.Stfld, self);
        }
        IL.Emit(OpCodes.Ldloc, function);
        SetStackUnknown();
    }

    protected virtual bool TryEmitGeneratorCaptureOwner(Type owner, string capturedName)
    {
        foreach (var local in new[] { Ctx.FunctionDisplayClassLocal, Ctx.ArrowScopeDisplayClassLocal,
                     Ctx.EntryPointDisplayClassLocal })
            if (local?.LocalType == owner) { IL.Emit(OpCodes.Ldloc, local); return true; }
        IEnumerable<FieldBuilder?> fields = new[] { Ctx.CurrentArrowFunctionDCField,
            Ctx.CurrentArrowScopeDCField, Ctx.CurrentArrowEntryPointDCField, GetFunctionDCField() };
        if (Ctx.CurrentArrowScopeDCExtraFields is { } extras) fields = fields.Concat(extras.Values);
        foreach (var field in fields)
            if (field?.FieldType == owner)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, field);
                return true;
            }
        if (Ctx.EntryPointDisplayClassStaticField?.FieldType == owner)
        {
            IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            return true;
        }
        if (TryEmitRetainedGeneratorCaptureOwner(owner)) return true;
        return TryEmitClassDefinitionEnvironment(owner);
    }

    private bool TryEmitRetainedGeneratorCaptureOwner(Type owner)
    {
        if (Ctx.CurrentGeneratorExpression is { } enclosing)
            foreach (var capture in enclosing.Captures.Values)
                for (int index = 0; index < capture.TargetPath.Count - 1; index++)
                    if (capture.TargetPath[index].FieldType == owner)
                    {
                        IL.Emit(OpCodes.Ldarg_0);
                        IL.Emit(OpCodes.Ldfld, enclosing.EnvironmentField);
                        for (int step = 0; step <= index; step++) IL.Emit(OpCodes.Ldfld, capture.TargetPath[step]);
                        return true;
                    }
        return false;
    }
}
