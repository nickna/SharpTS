using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase.
    // EmitLiteral is inherited from the base too: it leaves value-type literals (number/boolean)
    // UNBOXED with a tracked StackType (Double/Boolean), the convention the shared expression
    // framework — EnsureBoxed, EmitConversionForParameter — is built around. A previous override
    // here eagerly boxed numbers/booleans and set StackType=Unknown; that desynced the literal
    // fast-path in EmitConversionForParameter (which assumes an unboxed double for a `double`
    // parameter), so a call to a function with a numeric parameter stored a boxed object into a
    // double IL slot and failed IL verification (#441). The base storage paths here
    // (EmitVarDeclaration, EmitReturn, …) already EnsureBoxed before storing, so no eager boxing
    // is needed.

    protected override void EmitThis()
    {
        // Standalone async function expression: `this` is bound dynamically at
        // call time (fn.call/apply/bind) and snapshotted into OwnThisField by the
        // stub — read it rather than a lexical capture.
        if (_builder.IsStandalone && _builder.Arrow.HasOwnThis && _builder.OwnThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OwnThisField);
            SetStackUnknown();
            return;
        }

        // Load 'this' from outer state machine if captured
        if (_builder.Captures.Contains("this"))
        {
            // Get outer state machine's ThisField (non-standalone path)
            if (_ctx?.AsyncArrowOuterBuilders?.TryGetValue(_builder.Arrow, out var outerBuilder) == true &&
                outerBuilder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, outerBuilder.ThisField);
                SetStackUnknown();
                return;
            }

            // Standalone async arrow: 'this' captured as a standalone field in this state machine
            if (_builder.IsStandalone && _builder.StandaloneCaptureFields.TryGetValue("this", out var thisField))
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, thisField);
                SetStackUnknown();
                return;
            }
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Load hoisted 'this' from outer state machine if captured
        if (_builder.Captures.Contains("this"))
        {
            if (_ctx?.AsyncArrowOuterBuilders?.TryGetValue(_builder.Arrow, out var outerBuilder) == true &&
                outerBuilder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, outerBuilder.ThisField);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        _il.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetSuperMethod);
        SetStackUnknown();
    }
}
