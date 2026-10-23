using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // Per-binding storage names for block-scoped let/const shadows (#766), shared with the analyzer via
    // the analysis. Empty for the common no-shadow case (and for expression-bodied arrows).
    // The rename-then-delegate operator overrides that consume this live on StateMachineExitRoutingEmitter.
    protected override IReadOnlyDictionary<object, string> BlockScopeRenames => _analysis.BlockScopeRenames ?? NoRenames;

    protected override void EmitVariable(Expr.Variable v)
    {
        var checkedReference = v;
        if (TryEmitDefaultParameterTdz(v.Name.Lexeme))
            return;

        // Resolve a block-scoped shadow to its analyzed storage name before lookup (#766/#838).
        // Own display-class and hoisted fields use that name, keeping outer captures separate.
        if (BlockScopeRenames.TryGetValue(v, out var renamed))
            v = v with { Name = RenameToken(v.Name, renamed) };

        string name = v.Name.Lexeme;

        // A private-name reference keeps its declaration identity after an inner
        // loop or block has left same-spelled storage in the flattened local map.
        if (_builder.Arrow.Name is { } selfName && _builder.PrivateSelfField is { } selfField
            && _ctx?.ClosureAnalyzer?.IsReferenceToDeclaration(checkedReference, selfName) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, selfField);
            SetStackUnknown();
            return;
        }
        if (LexicalEnvironmentCellAccess.TryLoad(_il, Ctx, name)) { SetStackUnknown(); return; }
        // Per-iteration cells are authoritative after the loop initializer's
        // local has been copied into a box, matching assignment/store routing.
        if (_resolver!.TryResolveCell(name) is { } cellStorage)
        {
            cellStorage.EmitLoad(_il);
            SetStackUnknown();
            return;
        }
        // An expression's private name precedes outer/module captures, while
        // actual parameters and body declarations keep their ordinary storage.
        if (_resolver!.TryResolvePrivateSelf(name) is { } selfStorage)
        {
            selfStorage.EmitLoad(_il);
            SetStackUnknown();
            return;
        }
        // A declaration-owned IL local shadows surrounding captures. Display-class
        // storage still precedes potentially stale hoisted copies of promoted captures.
        if ((_resolver!.TryResolveLocal(name) ?? TryResolveDisplayClassStorage(name)) is { } displayStorage)
        {
            displayStorage.EmitLoad(_il);
            SetStackUnknown();
            return;
        }

        // Then resolver storage (cells, parameters, hoisted locals, captures, IL locals).
        var stackType = _resolver!.TryLoadVariable(name);
        if (stackType != null)
        {
            SetStackType(stackType.Value);
            return;
        }

        // JavaScript global constants (NaN/Infinity/undefined). The base EmitVariable reaches
        // these through TryEmitGlobalVariable, but this override reimplements resolution and so
        // must check them explicitly — otherwise a bare NaN/Infinity compiled to a null load,
        // e.g. `NaN === NaN` degraded to `null === null` → true (#648). Checked after the
        // resolver so a same-named local/param/capture still shadows the global.
        // Checked external aliases wait for the remaining real capture storage below.
        bool externalClassValue = TryResolveExternalClassValue(checkedReference, out _);
        if (!externalClassValue && TryEmitJsGlobalConstant(name)) return;

        // Check if it's an imported value (from another module) - must check BEFORE Functions
        // because cross-module function references need to go through the import field
        if (_ctx?.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            SetStackType(_ctx.EmitTopLevelStaticLoad(_il, name, topLevelField));
            return;
        }

        // Name-based functions must not steal a checked CLR import from another module.
        if (!externalClassValue &&
            _ctx?.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod) == true)
        {
            _il.Emit(OpCodes.Ldnull);
            Types.EmitLoadMethodInfoViaHandle(_il, funcMethod);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.FunctionConstruction.Constructor);
            SetStackUnknown();
            return;
        }

        // #1222: a top-level BLOCK-scoped shadow is captured BY VALUE into this state
        // machine — read the snapshot field; the entry-DC static field below holds the
        // same-named OUTER (module-level) binding.
        if (TryGetShadowedTopLevelCaptureField(name, out var shadowReadField))
        {
            AsyncArrowStorageAccess.StateMachineField(shadowReadField).EmitLoad(_il);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a captured top-level variable in entry-point display class
        if (_ctx?.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            _ctx.EmitTopLevelLexicalTdzCheck(_il, name);
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Ldfld, entryPointField);
            SetStackUnknown();
            return;
        }

        // Standalone capture (#641): a value the enclosing arrow's frame passed as a leading stub
        // argument, which the stub copied into a field on THIS arrow's state machine. Checked LAST,
        // below the module-level globals above: a top-level variable a standalone arrow closes over
        // is ALSO registered as a standalone capture but must be read LIVE from its static field
        // (the standalone copy is a stale snapshot) — handling it earlier broke compound/logical
        // assignment to such a variable. Reaches here only for genuine enclosing-arrow locals.
        if (_builder.StandaloneCaptureFields.TryGetValue(name, out var standaloneField))
        {
            AsyncArrowStorageAccess.StateMachineField(standaloneField).EmitLoad(_il);
            SetStackUnknown();
            return;
        }

        // A built-in can require value lookup when descriptor observation
        // disables its static-call strategy. Resolve it after every lexical
        // capture so globals such as Promise remain available without hiding
        // a same-named enclosing binding.
        if (TryEmitGlobalVariable(name, checkedReference))
            return;

        // Not found - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        if (BlockScopeRenames.TryGetValue(a, out var renamed))
            a = a with { Name = RenameToken(a.Name, renamed) };

        string name = a.Name.Lexeme;
        EmitExpression(a.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        if (LexicalEnvironmentCellAccess.TryStore(_il, Ctx, name)) { SetStackUnknown(); return; }
        // Assignments keep a previously declared local or parameter in its own
        // storage; only unresolved names can fall through to module storage.
        var storage = _resolver!.TryResolveCell(name)
            ?? _resolver.TryResolveLocal(name)
            ?? TryResolveDisplayClassStorage(name)
            ?? _resolver.TryResolveOwnHoisted(name)
            ?? TryResolveShadowedTopLevelStorage(name);
        if (storage != null)
            storage.EmitStore(_il);
        else if (!TryStoreTopLevelVariable(name))
            _resolver.TryStoreVariable(name);

        SetStackUnknown();
    }

    protected override void EmitStoreVariable(string name) => StoreVariable(name);

    private void StoreVariable(string name)
    {
        if (LexicalEnvironmentCellAccess.TryStore(_il, Ctx, name)) return;
        // Renaming happens at the AST boundary. Cells and live display classes win over any
        // hoisted copy; hoisted parameters/locals and outer captures win over module globals.
        var storage = _resolver!.TryResolveCell(name)
            ?? _resolver.TryResolveLocal(name)
            ?? TryResolveDisplayClassStorage(name)
            ?? _resolver.TryResolveHoistedOrCaptured(name)
            ?? TryResolveShadowedTopLevelStorage(name);
        if (storage != null)
            storage.EmitStore(_il);
        else if (!TryStoreTopLevelVariable(name))
            _resolver.GetOrCreateLocal(name).EmitStore(_il);
    }

    private bool TryStoreTopLevelVariable(string name)
    {
        // The block-scoped standalone shadow has already been resolved by the caller.
        // Other top-level captures must reach live module storage, not standalone snapshots.
        if (_ctx?.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            _ctx.EmitTopLevelLexicalTdzCheck(_il, name);
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, entryPointField);
            return true;
        }

        if (_ctx?.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _ctx.EmitTopLevelLexicalTdzCheck(_il, name);
            _il.Emit(OpCodes.Stsfld, topLevelField);
            return true;
        }
        return false;
    }

    private void LoadVariableForCapture(string name)
    {
        // Capture population keeps declaration-owned locals ahead of outer captures,
        // then checks standalone captures before globals. The capturing-arrow caller
        // separately shares cell/display-class references when live storage is required.
        var storage = _resolver!.TryResolveLocal(name) ?? TryResolveDisplayClassStorage(name)
            ?? _resolver.TryResolveHoistedOrCaptured(name);
        if (storage == null && _builder.StandaloneCaptureFields.TryGetValue(name, out var standaloneField))
            storage = AsyncArrowStorageAccess.StateMachineField(standaloneField);

        if (storage != null)
        {
            storage.EmitLoad(_il);
            SetStackUnknown();
            return;
        }

        // Global function captures need their canonical wrapper (including expandos).
        if (!TryEmitGlobalVariable(name))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    private AsyncArrowStorageAccess? TryResolveShadowedTopLevelStorage(string name)
        => TryGetShadowedTopLevelCaptureField(name, out var field)
            ? AsyncArrowStorageAccess.StateMachineField(field)
            : null;

    /// <summary>
    /// True when <paramref name="name"/> is a top-level BLOCK-scoped shadow of a same-named
    /// module-level binding, captured by value into this standalone arrow's state machine
    /// (#1222). Reads/writes must use the snapshot field: the entry-DC static field holds
    /// the OUTER binding. #1201-lifted bindings are excluded — their home IS the entry-DC
    /// field and must stay live for mutation visibility.
    /// </summary>
    private bool TryGetShadowedTopLevelCaptureField(string name, out FieldBuilder field)
    {
        field = null!;
        return _builder.IsStandalone
            && _builder.StandaloneCaptureFields.TryGetValue(name, out field!)
            && _ctx?.ClosureAnalyzer?.IsDirectTopLevelBlockScopedCapture(_builder.Arrow, name) == true
            && _ctx.LiftedBlockScopedTopLevelVars?.Contains(name) != true;
    }

    private AsyncArrowStorageAccess? TryResolveDisplayClassStorage(string name)
    {
        if (_builder.CapturedLiveStorage.TryGetValue(name, out var declarationCapture))
            return declarationCapture;
        if (_builder.DebugCapturedBindings.TryGetValue(name, out var debugCapture))
            return AsyncArrowStorageAccess.OwnDisplayClassField(debugCapture.Holder, debugCapture.Field);

        if (_builder.StandaloneLiveCaptureFields.TryGetValue(name, out var liveField))
            return AsyncArrowStorageAccess.OwnDisplayClassField(
                _builder.StandaloneCaptureFields[name], liveField);

        // A captured write promoted by the enclosing function uses outer.functionDC.field.
        // The outer plumbing is absent on standalone arrows, which must fall through.
        if (_ctx?.OuterFunctionDCField != null &&
            _builder.OuterStateMachineField != null &&
            _builder.OuterStateMachineType != null &&
            _builder.CapturedFieldMap.TryGetValue(name, out var capturedField) &&
            capturedField.DeclaringType == _ctx.OuterFunctionDCField.DeclaringType &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var outerField) == true)
            return AsyncArrowStorageAccess.OuterDisplayClassField(_builder, _ctx.OuterFunctionDCField, outerField);

        // An arrow's own DC is shared with nested sync arrows that write its locals (#838).
        // Its map stays on the builder so it cannot collide with the enclosing function's map.
        if (_builder.FunctionDCField != null && _builder.FunctionDCFieldMap.TryGetValue(name, out var ownField))
            return AsyncArrowStorageAccess.OwnDisplayClassField(_builder.FunctionDCField, ownField);

        return null;
    }

    // The rename-then-delegate overrides for const declarations, compound/logical assignment, and
    // increment/decrement live on StateMachineExitRoutingEmitter (shared with the iterator family).
}
