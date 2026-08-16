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

        // Follow-up to #838: if this sync arrow routes a captured write through THIS async arrow's own
        // function DC, share the reference: dc.$functionDC = this.<>__functionDC. The arrow body then
        // reads/writes that local through $functionDC (verifiable) instead of a by-value snapshot.
        if (_ctx.ArrowFunctionDCFields?.TryGetValue(af, out var functionDCField) == true &&
            _builder.FunctionDCField != null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.FunctionDCField);
            _il.Emit(OpCodes.Stfld, functionDCField);
        }

        // Get captured variables field mapping
        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            Types.EmitLoadMethodInfoViaHandle(_il, method);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Populate captured fields from async arrow state machine context
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup); // Keep display class instance on stack

            // Per-iteration cell capture (#650): snapshot the StrongBox REFERENCE, not its
            // value (LoadVariableForCapture would deref the cell). The closure body derefs.
            if (_ctx.CellBindingLocals.TryGetValue(capturedVar, out var cellLocal))
                _il.Emit(OpCodes.Ldloc, cellLocal);
            else
                // #767: pivot a captured nested-block shadow to its renamed storage (identity otherwise).
                LoadVariableForCapture(PivotCaptureSource(_analysis.BlockScopeCaptureRenames, af, capturedVar));

            _il.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance
        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowInAsyncArrow(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method: new TSFunction(null, method)
        _il.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(_il, method);
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

        // A nested async-arrow expression inside a top-level (standalone) async arrow — e.g. an
        // immediately-invoked `(async () => 9)()` — is registered as its own standalone builder
        // whose stub takes only its own parameters (and captures): there is no enclosing state
        // machine to thread. Emit it as a plain TSFunction over that stub with a NULL target,
        // exactly like a non-capturing arrow value. Threading the enclosing arrow's boxed state
        // machine here instead (the nested-in-async-function mechanism) would prepend it as the
        // stub's arg0, clobbering the first real parameter. (#615)
        if (nestedBuilder.IsStandalone)
        {
            // A capturing standalone nested arrow's stub takes every captured value packed into a
            // single object[]. $TSFunction's "static method with target" mechanism prepends the
            // target before the call args, so we pass that object[] AS the target, each element read
            // from THIS (enclosing) arrow's frame. Mirrors the module-level standalone path in
            // ILEmitter.EmitAsyncArrowFunction. #641 enabled the single read-only capture; #684
            // generalized it to any number of read-only captures via the shared object[] slot.
            var captureOrder = nestedBuilder.StandaloneCaptureFields.Keys
                .OrderBy(k => k, System.StringComparer.Ordinal).ToList();

            if (captureOrder.Count > 0)
            {
                // A standalone arrow captures BY VALUE (the values are copied into its own state
                // machine). A write to a capture therefore cannot propagate back to the enclosing
                // binding, so reject it instead of silently dropping the write (#684/#682). The
                // verifiable shared-cell fix is the same class as #625/#673 and tracked there.
                var written = CapturedWriteAnalysis.CollectImmediateWrites(af);
                written.IntersectWith(captureOrder);
                if (written.Count > 0)
                {
                    throw new CompileException(
                        "A nested async arrow inside a top-level async arrow that WRITES a captured " +
                        $"variable ({string.Join(", ", written.OrderBy(n => n, System.StringComparer.Ordinal))}) " +
                        "is not yet supported in compiled mode (#684/#682): the standalone arrow captures " +
                        "by value, so the write would be lost. Hoist it to a named async function, or run " +
                        "in interpreted mode.");
                }

                // Pack all captures into the single object[] target slot, in the same ordinal order
                // the stub unpacks them.
                _il.Emit(OpCodes.Ldc_I4, captureOrder.Count);
                _il.Emit(OpCodes.Newarr, _types.Object);
                for (int i = 0; i < captureOrder.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    LoadVariableForCapture(captureOrder[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }

            Types.EmitLoadMethodInfoViaHandle(_il, nestedBuilder.StubMethod);
            _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Non-standalone nested arrow (nested inside an async function): the nested arrow's stub
        // expects (outer state machine boxed, params...), so thread the enclosing arrow's boxed
        // self-reference as the "outer" target.
        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            throw new CompileException(
                "Async arrow with nested arrows does not have SelfBoxedField set.");
        }

        // Load the stub method for the nested arrow
        Types.EmitLoadMethodInfoViaHandle(_il, nestedBuilder.StubMethod);

        // Create TSFunction(target: self boxed, method: stub)
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);

        SetStackUnknown();
    }
}
