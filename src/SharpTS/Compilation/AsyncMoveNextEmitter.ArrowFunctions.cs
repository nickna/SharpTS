using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override string ResolveCaptureSourceName(Expr.ArrowFunction af, string capturedVar) =>
        PivotCaptureSource(_analysis.BlockScopeCaptureRenames, af, capturedVar);

    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        if (af.IsAsync)
        {
            EmitAsyncArrowFunction(af);
            return;
        }

        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            EmitNonCapturingArrowFunction(af, method);
        }
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af)
    {
        if (_ctx?.AsyncArrowBuilders == null ||
            !_ctx.AsyncArrowBuilders.TryGetValue(af, out var arrowBuilder))
        {
            throw new CompileException(
                "Async arrow function not registered with state machine builder.");
        }

        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldobj, _builder.StateMachineType);
            _il.Emit(OpCodes.Box, _builder.StateMachineType);
        }

        Types.EmitLoadMethodInfoViaHandle(_il, arrowBuilder.StubMethod);
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.FunctionConstruction.Constructor);
        SetStackUnknown();
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        if (_ctx!.DisplayClassConstructors.TryGetValue(af, out var constructor))
            EmitCapturingArrowViaHooks(af, method, constructor);
        else EmitNullConstant();
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        _il.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.FunctionConstruction.Constructor);
        SetStackUnknown();
    }
}
