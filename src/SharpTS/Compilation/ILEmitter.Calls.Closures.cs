using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Arrow function and closure emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Iterator-helper fast path: emit <paramref name="af"/> as a typed
    /// delegate of <paramref name="delegateType"/> (Func&lt;,&gt; / Func&lt;,,&gt;
    /// etc.) without going through <c>$TSFunction</c>. Handles both
    /// non-capturing arrows (<c>ldftn</c> on the static method) and
    /// capturing arrows (allocate + populate display class, then <c>ldftn</c>
    /// on the instance Invoke method). Returns false on unsupported shape:
    /// named function expression with self-ref, async, generator, missing
    /// arrow method, missing display ctor.
    /// </summary>
    protected override bool TryEmitArrowAsDelegate(Expr.ArrowFunction af, Type delegateType)
    {
        if (af.IsAsync || af.IsGenerator) return false;
        // Self-reference (named function expression) needs the wrapper to
        // be the captured value, not the bare display instance. Skip.
        if (af.Name != null) return false;
        if (!_ctx.ArrowMethods.TryGetValue(af, out var method)) return false;

        var delegateCtor = Types.TryGetConstructor(delegateType, typeof(object), typeof(IntPtr));
        if (delegateCtor == null) return false;

        if (_ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // Capturing path: build display instance, then bind delegate to
            // instance + Invoke method.
            if (!EmitCapturingArrowDisplayInstance(af, displayClass))
                return false;
            // Stack: [displayInstance]
            IL.Emit(OpCodes.Ldftn, method);
            IL.Emit(OpCodes.Newobj, delegateCtor);
            SetStackUnknown();
            return true;
        }

        // Non-capturing path: target = null, ldftn on the static method.
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, method);
        IL.Emit(OpCodes.Newobj, delegateCtor);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// #861 L3: builds a capturing arrow's display-class instance on the stack so the array-HOF
    /// boxed-adapter path can bind an instance adapter to it. Returns false if the arrow has no
    /// registered display class / constructor.
    /// </summary>
    protected override bool TryEmitCapturingArrowDisplayInstance(Expr.ArrowFunction af)
    {
        if (!_ctx.DisplayClasses.TryGetValue(af, out var displayClass))
            return false;
        if (!EmitCapturingArrowDisplayInstance(af, displayClass))
            return false;
        SetStackUnknown();
        return true;
    }

    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check if this is an async arrow function with a state machine
        if (af.IsAsync && _ctx.AsyncArrowBuilders?.TryGetValue(af, out var arrowBuilder) == true)
        {
            // Async arrow with its own state machine - emit a callable for the stub method
            EmitAsyncArrowFunction(af, arrowBuilder);
            SetStackUnknown();
            return;
        }

        // Get the method for this arrow function
        if (!_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found (shouldn't happen with proper collection)
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // Capturing arrow: create display class instance and populate fields
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            // Non-capturing arrow: create TSFunction wrapping static method
            EmitNonCapturingArrowFunction(af, method);
        }

        // $TSFunction is a reference type, mark stack as unknown (not a value type)
        SetStackUnknown();
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af, AsyncArrowStateMachineBuilder arrowBuilder)
    {
        var stubMethod = arrowBuilder.StubMethod;

        // For standalone async arrows with captures, TSFunction's "static method with target"
        // mechanism prepends the single target to the args. Pack EVERY captured value into one
        // object[] and pass it as that target; the stub unpacks each into its own field. One
        // array slot for any capture count is what makes multi-capture work — the old per-capture
        // arg layout only lined up at exactly one capture (#641/#684).
        if (arrowBuilder.IsStandalone && arrowBuilder.StandaloneCaptureFields.Count > 0)
        {
            // Ordinal ordering must match the stub's capture-unpacking order (AsyncArrowStateMachineBuilder).
            var captureOrder = arrowBuilder.StandaloneCaptureFields.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

            IL.Emit(OpCodes.Ldc_I4, captureOrder.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
            for (int i = 0; i < captureOrder.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                var captureName = captureOrder[i];
                if (captureName == "this")
                {
                    IL.Emit(OpCodes.Ldarg_0);
                }
                else
                {
                    EmitVariable(new Expr.Variable(new Parsing.Token(Parsing.TokenType.IDENTIFIER, captureName, null, 0)));
                    EnsureBoxed();
                }
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // No captures: new TSFunction(null, stubMethod)
            IL.Emit(OpCodes.Ldnull);
        }

        // Load method info for the stub method
        IL.Emit(OpCodes.Ldtoken, stubMethod);
        IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Create TSFunction
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method:
        // new TSFunction(null, method)

        // Push null (no target)
        IL.Emit(OpCodes.Ldnull);

        // Get MethodInfo from the method builder using reflection
        // We need to load the method as a MethodInfo at runtime
        // Use Type.GetMethod or RuntimeMethodHandle

        // Load the method as a runtime handle and convert to MethodInfo
        IL.Emit(OpCodes.Ldtoken, method);

        // For static methods on a non-generic type:
        IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    /// <summary>
    /// Emits IL that allocates the display class instance for a capturing
    /// arrow and populates its fields (special DC chains + ordinary captures).
    /// On exit, the display-instance reference is the only thing left on the
    /// stack — caller decides what to do with it next ($TSFunction wrap, or
    /// for the iterator-helper fast path, an <c>ldftn</c> + Func ctor).
    ///
    /// Self-reference handling (named function expressions where the arrow's
    /// own name is captured into a field) is intentionally NOT done here —
    /// that field is populated AFTER the wrapping callable is created so it
    /// can hold the wrapper reference, not the bare display instance. Caller
    /// is responsible for that follow-up if needed; the iterator-helper fast
    /// path declines arrows where this matters.
    /// </summary>
    /// <returns>true if the display instance was emitted; false on
    /// unsupported configuration (the only current case is the constructor
    /// not being tracked, which is internally inconsistent — slow-path caller
    /// emits Ldnull and bails).</returns>
    private bool EmitCapturingArrowDisplayInstance(Expr.ArrowFunction af, TypeBuilder displayClass)
    {
        if (!_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
            return false;

        IL.Emit(OpCodes.Newobj, displayCtor);
        EmitDisplayInstanceFieldPopulation(af, displayClass);
        return true;
    }

    /// <summary>
    /// Populates the display-class instance currently on top of the stack
    /// with all special-field DC chains and ordinary captured variables.
    /// Stack on entry/exit: <c>[displayInstance]</c>.
    /// </summary>
    private void EmitDisplayInstanceFieldPopulation(Expr.ArrowFunction af, TypeBuilder displayClass)
    {
        // Populate $entryPointDC field if this arrow captures top-level variables
        if (_ctx.ArrowEntryPointDCFields?.TryGetValue(af, out var entryPointDCField) == true)
        {
            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // In entry point method - use local variable
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // In arrow body - get from parent arrow's $entryPointDC field
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldarg_0); // Load parent display class
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Fallback to static field
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
        }

        // Populate $functionDC field if this arrow captures function-level variables
        if (_ctx.ArrowFunctionDCFields?.TryGetValue(af, out var functionDCField) == true)
        {
            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // In function body - use local variable
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                IL.Emit(OpCodes.Stfld, functionDCField);
            }
            else if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // In arrow body - get from parent arrow's $functionDC field
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldarg_0); // Load parent display class
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                IL.Emit(OpCodes.Stfld, functionDCField);
            }
        }

        // Populate $arrowDC field if this arrow captures arrow-scope variables.
        // When both a scope-DC local (this context's own DC) and a chained
        // reference field are available, pick by DC TYPE: the new arrow may
        // reference a GRANDPARENT scope DC that only the chain field carries.
        if (_ctx.ArrowScopeDCFields?.TryGetValue(af, out var arrowScopeDCField) == true)
        {
            bool localMatches = _ctx.ArrowScopeDisplayClassLocal != null &&
                _ctx.ArrowScopeDisplayClassLocal.LocalType == arrowScopeDCField.FieldType;
            bool chainMatches = _ctx.CurrentArrowScopeDCField != null &&
                _ctx.CurrentArrowScopeDCField.FieldType == arrowScopeDCField.FieldType;

            if (localMatches || (_ctx.ArrowScopeDisplayClassLocal != null && !chainMatches))
            {
                // In parent arrow body - use local variable
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal!);
                IL.Emit(OpCodes.Stfld, arrowScopeDCField);
            }
            else if (chainMatches)
            {
                // In nested closure body - chain through the $arrowDC /
                // $arrowScopeDC reference field
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField!);
                IL.Emit(OpCodes.Stfld, arrowScopeDCField);
            }
            else if (!EmitScopeDCRefFromExtras(arrowScopeDCField) && _ctx.CurrentArrowScopeDCField != null)
            {
                // Legacy fallback: no type-matching source available — keep the
                // historical chain-through behavior.
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
                IL.Emit(OpCodes.Stfld, arrowScopeDCField);
            }
        }

        // EXTRA ancestor scope DC references (multi-source captures): populate
        // each one from whatever this context can reach by DC type.
        if (_ctx.ArrowScopeDCExtraFieldsByArrow?.TryGetValue(af, out var extraScopeRefFields) == true)
        {
            foreach (var extraRefField in extraScopeRefFields.Values)
            {
                if (_ctx.ArrowScopeDisplayClassLocal != null &&
                    _ctx.ArrowScopeDisplayClassLocal.LocalType == extraRefField.FieldType)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
                    IL.Emit(OpCodes.Stfld, extraRefField);
                }
                else if (_ctx.CurrentArrowScopeDCField != null &&
                         _ctx.CurrentArrowScopeDCField.FieldType == extraRefField.FieldType)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
                    IL.Emit(OpCodes.Stfld, extraRefField);
                }
                else
                {
                    EmitScopeDCRefFromExtras(extraRefField);
                }
            }
        }

        // No captured-variable field map: special DC fields above are the
        // only state on the instance. Caller's wrapping step still needs to
        // run; just return with [instance] on the stack.
        if (!_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
            return;

        // Self-reference field (named function expression) is left empty
        // here — it must hold the wrapping callable, not the bare display
        // instance. The caller (EmitCapturingArrowFunction) populates it
        // after building the $TSFunction.
        string? selfRefSkipName = af.Name?.Lexeme;

        foreach (var (capturedVar, field) in fieldMap)
        {
            if (selfRefSkipName != null && capturedVar == selfRefSkipName) continue;

            // Self-referential capture (issue #421): this arrow captures the
            // variable currently being declared, whose initializer creates this
            // arrow. The field is about to be populated with a snapshot of the
            // local taken BEFORE the assignment — the stale/previous value. Stash
            // the display-class instance so the declaration emitter can write the
            // post-initializer value back into this field. Per-iteration freshness
            // is preserved: each iteration builds a distinct DC instance.
            if (_ctx.SelfCaptureVarName != null && capturedVar == _ctx.SelfCaptureVarName
                && _ctx.SelfCaptureWriteBacks != null)
            {
                var dcInstanceLocal = IL.DeclareLocal(displayClass);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, dcInstanceLocal);
                _ctx.SelfCaptureWriteBacks.Add((dcInstanceLocal, field));
            }

            IL.Emit(OpCodes.Dup); // Keep display class on stack

            // Load the captured variable's current value
            if (capturedVar == "this")
            {
                // `this` is lexically captured from the enclosing non-arrow scope.
                // Snapshot it via the shared resolver so the capture matches a bare
                // `this` read in this same enclosing body — every context shape is
                // handled identically:
                //   - parent arrow body that captured `this` → load from the $DisplayClass field
                //   - object-method-shorthand arrow (`__this`) → the __this parameter
                //   - direct class-method body                → Ldarg_0
                //   - static-constructor context              → the class Type
                //   - plain function invoked via `new`        → thread-local set by NewOnFunction
                // The previous hand-rolled subset here only handled the first two
                // shapes and fell back to Ldnull, so an arrow nested in a plain
                // `function` used as a constructor captured `this == null` (#399).
                // Routing through LoadThis() also picks up the minimatch
                // `.map(s => s.map(ss => this.parse(ss)))` chain (case 1 fires for the
                // parent arrow; class-method arg0 for the outermost).
                _resolver.LoadThis();
            }
            else if (_ctx.CellBindingLocals.TryGetValue(capturedVar, out var cellLocal))
            {
                // Per-iteration cell capture (#650): snapshot the StrongBox REFERENCE
                // (not its value) so the closure shares this iteration's cell and
                // observes end-of-iteration mutations. The cell local is repointed to a
                // fresh box each iteration, so closures in distinct iterations capture
                // distinct cells. The field stays object-typed; only the closure body
                // dereferences Value (see LocalVariableResolver / CellCapturedFieldNames).
                IL.Emit(OpCodes.Ldloc, cellLocal);
            }
            else if (_ctx.TryGetParameter(capturedVar, out var argIndex))
            {
                IL.Emit(OpCodes.Ldarg, argIndex);
                // If parameter is typed (value type), box it for object field storage
                if (_ctx.TryGetParameterType(capturedVar, out var paramType) && paramType != null && paramType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, paramType);
                }
            }
            else if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue(capturedVar, out var capturedField))
            {
                // Variable is captured from outer closure
                IL.Emit(OpCodes.Ldarg_0); // this (display class)
                IL.Emit(OpCodes.Ldfld, capturedField);
            }
            else if (_ctx.CapturedArrowLocals?.Contains(capturedVar) == true &&
                     _ctx.ArrowScopeDisplayClassFields?.TryGetValue(capturedVar, out var ownScopeField) == true &&
                     _ctx.ArrowScopeDisplayClassLocal != null)
            {
                // Variable lives on the creating scope's arrow scope DC (e.g. a
                // hoisted inner function declaration stored there). Without this
                // branch the populate falls through to Locals and emits null.
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
                IL.Emit(OpCodes.Ldfld, ownScopeField);
            }
            else if (_ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(capturedVar, out var parentScopeField) == true &&
                     _ctx.CurrentArrowScopeDCField != null)
            {
                // Variable lives on an ancestor arrow's scope DC reachable through
                // the creating closure's $arrowDC/$arrowScopeDC reference. Arises
                // when the new arrow's single $arrowDC slot is bound to a different
                // source scope, so this variable became a copy-field. Keyed on the
                // fields map alone (not the creating closure's own capture set) —
                // the variable belongs to the NEW closure's captures, which the
                // creating closure may not share.
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
                IL.Emit(OpCodes.Ldfld, parentScopeField);
            }
            else if (_ctx.CapturedTopLevelVars?.Contains(capturedVar) == true &&
                     _ctx.EntryPointDisplayClassFields?.ContainsKey(capturedVar) == true &&
                     _ctx.ClosureAnalyzer?.IsDirectTopLevelBlockScopedCapture(af, capturedVar) == true &&
                     _ctx.Locals.GetNestedScopeLocal(capturedVar) is { } shadowLocal)
            {
                // #1222: the creating scope's block-scoped local lexically shadows the
                // same-named module-level binding whose home is the entry-DC field
                // handled below. Snapshot the shadow like any local capture — the DC
                // branch would misroute the capture to the OUTER binding. (#1201-lifted
                // bindings never take this path: their declarations write the DC field
                // directly and declare no local, so GetNestedScopeLocal returns null.)
                IL.Emit(OpCodes.Ldloc, shadowLocal);
                if (shadowLocal.LocalType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, shadowLocal.LocalType);
                }
            }
            else if (_ctx.CapturedTopLevelVars?.Contains(capturedVar) == true &&
                     _ctx.EntryPointDisplayClassFields?.TryGetValue(capturedVar, out var entryPointField) == true)
            {
                // Variable is a captured top-level var in entry-point display class
                if (_ctx.EntryPointDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                }
                else if (_ctx.EntryPointDisplayClassStaticField != null)
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Ldfld, entryPointField);
            }
            else if (_ctx.TopLevelStaticVars != null && _ctx.TopLevelStaticVars.TryGetValue(capturedVar, out var topLevelField))
            {
                // Variable is a top-level static var
                IL.Emit(OpCodes.Ldsfld, topLevelField);
            }
            else if (_ctx.Classes.TryGetValue(_ctx.ResolveClassName(capturedVar), out var capturedClass))
            {
                // A top-level class declaration is represented by its emitted Type.
                // Closure analysis can still classify the lexical reference as an
                // ordinary capture; seed that capture with the class value instead
                // of leaving its display-class field null. Block-scoped classes use
                // a real local and therefore continue through the local branch below.
                IL.Emit(OpCodes.Ldtoken, capturedClass);
                IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(
                    _ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            }
            else if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(capturedVar), out var capturedFuncMethod))
            {
                // Variable references an outer function declaration. Use the
                // MethodInfo-keyed canonical wrapper, exactly like a normal global
                // function-value read. Constructing a fresh $TSFunction here loses
                // expando state attached earlier to the function object — notably
                // test262's assert.sameValue / assert.throwsAsync helpers — so the
                // captured closure observes a different, property-less function.
                IL.Emit(OpCodes.Ldtoken, capturedFuncMethod);
                if (_ctx.ProgramType != null)
                {
                    IL.Emit(OpCodes.Ldtoken, _ctx.ProgramType);
                    IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
                }
                IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
                int arity = _ctx.GetFunctionLength(capturedFuncMethod);
                IL.Emit(OpCodes.Ldstr, _ctx.GetFunctionName(capturedFuncMethod, capturedVar));
                IL.Emit(OpCodes.Ldc_I4, arity);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.TSFunctionGetOrCreate);
            }
            else
            {
                var local = _ctx.Locals.GetLocal(capturedVar);
                if (local != null)
                {
                    IL.Emit(OpCodes.Ldloc, local);
                    // The capture field is object-typed, so a value-type local (e.g. a
                    // number stored in an unboxed `double` slot via CanUseUnboxedLocal)
                    // must be boxed before Stfld. The captured-parameter path above
                    // already does this; omitting it here left a float64 where the
                    // verifier expects a reference, corrupting the stack (#431).
                    if (local.LocalType.IsValueType)
                    {
                        IL.Emit(OpCodes.Box, local.LocalType);
                    }
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull); // Variable not found
                }
            }

            IL.Emit(OpCodes.Stfld, field);
        }
    }

    /// <summary>
    /// With a new display instance on top of the stack, tries to populate
    /// <paramref name="targetField"/> (a scope-DC reference field) from one of
    /// the current closure's own EXTRA ancestor-scope reference fields, matched
    /// by DC type. Returns false when no extra reference matches.
    /// </summary>
    private bool EmitScopeDCRefFromExtras(FieldBuilder targetField)
    {
        if (_ctx.CurrentArrowScopeDCExtraFields == null)
            return false;
        foreach (var ownRef in _ctx.CurrentArrowScopeDCExtraFields.Values)
        {
            if (ownRef.FieldType != targetField.FieldType)
                continue;
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, ownRef);
            IL.Emit(OpCodes.Stfld, targetField);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Slow-path emission for a capturing arrow: build display instance,
    /// then wrap as <c>$TSFunction</c> for the legacy callable dispatch.
    /// Iterator-helper fast paths bypass this and go through
    /// <see cref="EmitCapturingArrowDisplayInstance"/> directly to build a
    /// typed delegate instead.
    /// </summary>
    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        if (!EmitCapturingArrowDisplayInstance(af, displayClass))
        {
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        // For named function expressions, the self-reference field needs the
        // wrapping $TSFunction (not the bare display instance), so stash a
        // ref to the instance for use after the wrap.
        string? selfRefName = af.Name?.Lexeme;
        FieldBuilder? selfRefField = null;
        LocalBuilder? displayClassLocal = null;
        if (selfRefName != null
            && _ctx.DisplayClassFields.TryGetValue(af, out var fieldMap)
            && fieldMap.TryGetValue(selfRefName, out var srf))
        {
            selfRefField = srf;
            displayClassLocal = IL.DeclareLocal(displayClass);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, displayClassLocal);
        }

        // Wrap: new $TSFunction(displayInstance, method)
        IL.Emit(OpCodes.Ldtoken, method);
        IL.Emit(OpCodes.Ldtoken, displayClass);
        IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);

        if (selfRefField != null && displayClassLocal != null)
        {
            var tsFuncLocal = IL.DeclareLocal(_ctx.Runtime!.TSFunctionType);
            IL.Emit(OpCodes.Stloc, tsFuncLocal);
            IL.Emit(OpCodes.Ldloc, displayClassLocal);
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
            IL.Emit(OpCodes.Stfld, selfRefField);
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
        }
    }
}
