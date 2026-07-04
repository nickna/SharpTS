using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // Per-binding storage names for block-scoped let/const shadows (#766), shared with the analyzer via
    // the analysis. Empty for the common no-shadow case (and for expression-bodied arrows).
    private static readonly IReadOnlyDictionary<object, string> NoRenames = new Dictionary<object, string>();
    private IReadOnlyDictionary<object, string> BlockScopeRenames => _analysis.BlockScopeRenames ?? NoRenames;

    private static Token RenameToken(Token original, string lexeme) =>
        new(original.Type, lexeme, original.Literal, original.Line, original.Start);

    protected override void EmitVariable(Expr.Variable v)
    {
        // Resolve a shadowing block-scoped binding to its own storage before resolution (#766); a renamed
        // binding is never a captured/DC name, so the capture checks below correctly fall through.
        if (BlockScopeRenames.TryGetValue(v, out var renamed))
            v = v with { Name = RenameToken(v.Name, renamed) };

        string name = v.Name.Lexeme;

        // A capture promoted into the enclosing function's display class (#625) is read through
        // `outer.functionDC.field`, NOT the boxed state-machine field. Checked before the resolver:
        // the variable may still have a (now-unused) hoisted SM field that the resolver would load
        // a stale value from.
        if (TryGetOuterFunctionDCField(name, out var dcReadField))
        {
            EmitLoadOuterFunctionDC();
            _il.Emit(OpCodes.Ldfld, dcReadField);
            SetStackUnknown();
            return;
        }

        // Follow-up to #838: a local of THIS async arrow that a nested sync arrow writes lives in the
        // arrow's own function DC (reference type), so read it through `this.<>__functionDC.field`.
        if (TryGetOwnFunctionDCField(name, out var ownReadField))
        {
            EmitLoadOwnFunctionDC();
            _il.Emit(OpCodes.Ldfld, ownReadField);
            SetStackUnknown();
            return;
        }

        // Try resolver first (params, locals, hoisted, captured)
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
        if (TryEmitJsGlobalConstant(name)) return;

        // Check if it's an imported value (from another module) - must check BEFORE Functions
        // because cross-module function references need to go through the import field
        if (_ctx?.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a global function
        if (_ctx?.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod) == true)
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // #1222: a top-level BLOCK-scoped shadow is captured BY VALUE into this state
        // machine — read the snapshot field; the entry-DC static field below holds the
        // same-named OUTER (module-level) binding.
        if (TryGetShadowedTopLevelCaptureField(name, out var shadowReadField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, shadowReadField);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a captured top-level variable in entry-point display class
        if (_ctx?.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
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
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, standaloneField);
            SetStackUnknown();
            return;
        }

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

        // Per-iteration loop-binding cell (#650/#817): write through the StrongBox.
        if (_ctx != null && _ctx.CellBindingLocals.TryGetValue(name, out var assignCell))
        {
            var cellTmp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, cellTmp);
            _il.Emit(OpCodes.Ldloc, assignCell);
            _il.Emit(OpCodes.Ldloc, cellTmp);
            _il.Emit(OpCodes.Stfld, _types.StrongBoxOfObjectValueField);
            SetStackUnknown();
            return;
        }

        // A capture promoted into the enclosing function's display class (#625) is written through
        // `outer.functionDC.field = value` (the DC is a reference type, so the store is verifiable),
        // not by mutating the boxed value-type state machine in place. Checked first.
        if (TryGetOuterFunctionDCField(name, out var dcAssignField))
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);     // consume the duplicated value
            EmitLoadOuterFunctionDC();
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, dcAssignField);
            SetStackUnknown();                 // remaining copy is the assignment's value
            return;
        }

        // Follow-up to #838: write a DC-resident local through `this.<>__functionDC.field = value`.
        if (TryGetOwnFunctionDCField(name, out var ownAssignField))
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);     // consume the duplicated value
            EmitLoadOwnFunctionDC();
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, ownAssignField);
            SetStackUnknown();                 // remaining copy is the assignment's value
            return;
        }

        // #1222: write the shadow's by-value snapshot field, never the same-named outer
        // module binding's entry-DC home. (Lost on exit like every standalone-capture
        // write — matching the by-value semantics of #641/#684.)
        if (TryGetShadowedTopLevelCaptureField(name, out var shadowAssignField))
        {
            var shadowTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, shadowTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, shadowTemp);
            _il.Emit(OpCodes.Stfld, shadowAssignField);
            SetStackUnknown();
            return;
        }

        // Check if it's a captured top-level variable in entry-point display class
        if (_ctx?.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, entryPointField);
            SetStackUnknown();
            return;
        }

        // Check if it's a non-captured top-level variable
        if (_ctx?.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Stsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Use resolver to store (consumes one copy, leaves one on stack as return value)
        _resolver!.TryStoreVariable(name);

        SetStackUnknown();
    }

    protected override void EmitStoreVariable(string name) => StoreVariable(name);

    private void StoreVariable(string name)
    {
        // Per-iteration loop-binding cell (#650/#817): write through the StrongBox.
        if (_ctx != null && _ctx.CellBindingLocals.TryGetValue(name, out var cell))
        {
            var t = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, t);
            _il.Emit(OpCodes.Ldloc, cell);
            _il.Emit(OpCodes.Ldloc, t);
            _il.Emit(OpCodes.Stfld, _types.StrongBoxOfObjectValueField);
            return;
        }

        // A capture promoted into the enclosing function's display class (#625) is stored through
        // `outer.functionDC.field`. Checked first so it wins over any (now-unused) hoisted SM field.
        if (TryGetOuterFunctionDCField(name, out var dcStoreField))
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            EmitLoadOuterFunctionDC();
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, dcStoreField);
            return;
        }

        // Follow-up to #838: store a DC-resident local through `this.<>__functionDC.field` (covers the
        // body's own `let r = …` declaration, which routes through EmitStoreVariable). Checked first so it
        // wins over any (now-unused) hoisted SM field.
        if (TryGetOwnFunctionDCField(name, out var ownStoreField))
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            EmitLoadOwnFunctionDC();
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, ownStoreField);
            return;
        }

        // Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, paramField);
            return;
        }

        // Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, localField);
            return;
        }

        // Check if it's captured from outer scope - store back to outer
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            // Store value to outer state machine's field through the boxed reference
            // Stack has: value
            // We need to: store to temp, get outer ptr, load temp, store to field
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);

            // Get pointer to the boxed outer state machine
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);

            // Check if this is a transitive capture (needs extra indirection through parent's outer)
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                // First unbox to parent, then load parent's outer reference
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            }

            // Load value and store to field
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, outerField);
            return;
        }

        // #1222: write the shadow's by-value snapshot field, never the same-named outer
        // module binding's entry-DC home (mirror of the EmitAssign branch above).
        if (TryGetShadowedTopLevelCaptureField(name, out var shadowStoreField))
        {
            var shadowTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, shadowTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, shadowTemp);
            _il.Emit(OpCodes.Stfld, shadowStoreField);
            return;
        }

        // Check if it's a captured top-level variable in entry-point display class
        if (_ctx?.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, entryPointField);
            return;
        }

        // Check if it's a non-captured top-level variable
        if (_ctx?.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Stsfld, topLevelField);
            return;
        }

        // Non-hoisted local variable - use IL local
        // Create or get the local
        if (!_locals.TryGetValue(name, out var local))
        {
            local = _il.DeclareLocal(typeof(object));
            _locals[name] = local;
        }
        _il.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Loads a variable value for populating a capture in a non-async arrow's display class.
    /// This is similar to the EmitVariable override but designed for capture population.
    /// </summary>
    private void LoadVariableForCapture(string name)
    {
        // Check if it's a parameter of this async arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            SetStackUnknown();
            return;
        }

        // Check if it's a hoisted local of this async arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            SetStackUnknown();
            return;
        }

        // Check if it's captured from outer scope (parent async function/arrow)
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);

            // Check if this is a transitive capture
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            }

            _il.Emit(OpCodes.Ldfld, outerField);
            SetStackUnknown();
            return;
        }

        // Check for non-hoisted local variable
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            SetStackUnknown();
            return;
        }

        // Transitive standalone capture: a variable THIS (enclosing) standalone arrow itself
        // captured (stored in its own state-machine field) and a nested standalone arrow now
        // re-captures. Without this the value would fall through to null one level too deep.
        // Checked after the arrow's own params/locals so a same-named local still shadows it.
        if (_builder.StandaloneCaptureFields.TryGetValue(name, out var enclosingCaptureField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, enclosingCaptureField);
            SetStackUnknown();
            return;
        }

        // Handle 'this' capture - in async arrows, 'this' is captured from outer scope
        if (name == "this" && _builder.IsCaptured("this") && _builder.CapturedFieldMap.TryGetValue("this", out var thisField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            _il.Emit(OpCodes.Ldfld, thisField);
            SetStackUnknown();
            return;
        }

        // Fallback: null
        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

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

    /// <summary>
    /// True when <paramref name="name"/> is a captured variable the enclosing async function placed
    /// in its (reference-type) display class (#625). Such a variable must be read/written through
    /// <c>outer.functionDC.field</c> rather than the boxed state-machine field. Requires the outer
    /// reference plumbing — present only for non-standalone arrows nested directly in an async
    /// function — so standalone/top-level async arrows fall through to the existing paths.
    /// </summary>
    private bool TryGetOuterFunctionDCField(string name, out FieldBuilder dcField)
    {
        dcField = null!;
        return _ctx?.OuterFunctionDCField != null
            && _builder.OuterStateMachineField != null
            && _builder.OuterStateMachineType != null
            && _ctx.FunctionDisplayClassFields?.TryGetValue(name, out dcField!) == true;
    }

    /// <summary>
    /// Pushes the enclosing async function's display-class instance: <c>outer.functionDC</c>.
    /// Reading the DC reference field through the <c>unbox</c>'d (readonly) outer pointer is
    /// verifiable; the resulting reference is an ordinary class instance, so the caller's
    /// subsequent <c>ldfld</c>/<c>stfld</c> on it verifies (unlike storing into the boxed struct).
    /// </summary>
    private void EmitLoadOuterFunctionDC()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
        _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
        _il.Emit(OpCodes.Ldfld, _ctx!.OuterFunctionDCField!);
    }

    // Follow-up to #838: this arrow's OWN function display class (a reference type held on its state
    // machine), shared with a nested sync arrow that writes one of the arrow's locals. The field map is
    // on the builder so it never collides with the OuterFunctionDCField relay above.
    private bool TryGetOwnFunctionDCField(string name, out FieldBuilder dcField)
    {
        dcField = null!;
        return _builder.FunctionDCField != null
            && _builder.FunctionDCFieldMap.TryGetValue(name, out dcField!);
    }

    // Pushes `this.<>__functionDC` (a class reference); the caller's ldfld/stfld on it is verifiable.
    private void EmitLoadOwnFunctionDC()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.FunctionDCField!);
    }

    // Const declarations, compound/logical assignment, and increment/decrement reach the variable
    // through the operator node's name token (or the increment operand). Rewriting that token to the
    // shadowing binding's storage name before delegating to the base routes both the read and the
    // write to the right field/local (#766). The base re-enters EmitVarDeclaration / EmitVariable /
    // EmitStoreVariable (all of which resolve by name here), so the rename flows through consistently.

    protected override void EmitConstDeclaration(Stmt.Const c)
    {
        if (BlockScopeRenames.TryGetValue(c, out var renamed))
            c = c with { Name = RenameToken(c.Name, renamed) };
        base.EmitConstDeclaration(c);
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        if (BlockScopeRenames.TryGetValue(ca, out var renamed))
            ca = ca with { Name = RenameToken(ca.Name, renamed) };
        base.EmitCompoundAssign(ca);
    }

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        if (BlockScopeRenames.TryGetValue(la, out var renamed))
            la = la with { Name = RenameToken(la.Name, renamed) };
        base.EmitLogicalAssign(la);
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v && BlockScopeRenames.TryGetValue(v, out var renamed))
            pi = pi with { Operand = v with { Name = RenameToken(v.Name, renamed) } };
        base.EmitPrefixIncrement(pi);
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v && BlockScopeRenames.TryGetValue(v, out var renamed))
            poi = poi with { Operand = v with { Name = RenameToken(v.Name, renamed) } };
        base.EmitPostfixIncrement(poi);
    }
}
