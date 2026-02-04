using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check if it's an async arrow (nested async arrow)
        if (af.IsAsync)
        {
            EmitNestedAsyncArrow(af);
            return;
        }

        // Get the method for this arrow function (pre-compiled)
        if (_ctx?.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            EmitCapturingArrowInAsyncArrow(af, method, displayClass);
        }
        else
        {
            EmitNonCapturingArrowInAsyncArrow(af, method);
        }
    }

    private void EmitCapturingArrowInAsyncArrow(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Create display class instance
        _il.Emit(OpCodes.Newobj, displayCtor);

        // Get captured variables field mapping
        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            _il.Emit(OpCodes.Ldtoken, method);
            _il.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Populate captured fields from async arrow state machine context
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup); // Keep display class instance on stack

            // Load the captured variable using the same logic as LoadVariable
            LoadVariableForCapture(capturedVar);

            _il.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowInAsyncArrow(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method: new TSFunction(null, method)
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNestedAsyncArrow(Expr.ArrowFunction af)
    {
        // Get the nested async arrow's state machine builder
        if (_ctx?.AsyncArrowBuilders == null ||
            !_ctx.AsyncArrowBuilders.TryGetValue(af, out var nestedBuilder))
        {
            throw new CompileException(
                "Nested async arrow function not registered with state machine builder.");
        }

        // For nested arrows, we need to pass the current arrow's boxed state machine
        // as the "outer" reference for the nested arrow.
        // The nested arrow's stub expects (outer state machine boxed, params...)

        // Load the current arrow's self-boxed reference
        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            // Fallback: this shouldn't happen if hasNestedAsyncArrows was set correctly
            throw new CompileException(
                "Async arrow with nested arrows does not have SelfBoxedField set.");
        }

        // Load the stub method for the nested arrow
        _il.Emit(OpCodes.Ldtoken, nestedBuilder.StubMethod);
        _il.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Create TSFunction(target: self boxed, method: stub)
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);

        SetStackUnknown();
    }
}
