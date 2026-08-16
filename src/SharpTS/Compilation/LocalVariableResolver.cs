using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Variable resolver for standard IL emission contexts (non-state-machine).
/// Handles locals, parameters, and captured variables from display classes.
/// </summary>
/// <remarks>
/// Resolution order:
/// 1. Parameters (via CompilationContext.TryGetParameter)
/// 2. Local variables (via CompilationContext.Locals)
/// 3. Captured fields (via CompilationContext.CapturedFields)
///
/// Does NOT handle: functions, namespaces, classes, Math (caller handles these as fallback).
/// </remarks>
public class LocalVariableResolver : IVariableResolver
{
    private readonly ILGenerator _il;
    private readonly CompilationContext _ctx;
    private readonly TypeProvider _types;

    /// <summary>
    /// Creates a new resolver for standard IL emission variable access.
    /// </summary>
    /// <param name="il">The IL generator for emitting instructions</param>
    /// <param name="ctx">The compilation context with locals, parameters, and captured fields</param>
    /// <param name="types">The type provider for type checking</param>
    public LocalVariableResolver(ILGenerator il, CompilationContext ctx, TypeProvider types)
    {
        _il = il;
        _ctx = ctx;
        _types = types;
    }

    /// <inheritdoc />
    public StackType? TryLoadVariable(string name)
    {
        // 0. Per-iteration loop-binding cell (#650): in the loop body / condition /
        //    increment the binding is accessed through its StrongBox so closures that
        //    captured the cell observe end-of-iteration mutations. Takes precedence
        //    over the (now dead) plain local left by the initializer.
        if (_ctx.CellBindingLocals.TryGetValue(name, out var loadCell))
        {
            _il.Emit(OpCodes.Ldloc, loadCell);
            _il.Emit(OpCodes.Ldfld, _types.StrongBoxOfObjectValueField);
            return StackType.Unknown;
        }

        // 1. Parameters
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            _il.Emit(OpCodes.Ldarg, argIndex);
            // Check if we have type information for this parameter
            if (_ctx.TryGetParameterType(name, out var paramType) && paramType != null)
            {
                var stackType = MapTypeToStackType(paramType);
                // Box union types immediately so that StackType.Unknown correctly means "boxed object"
                if (stackType == StackType.Unknown && UnionTypeHelper.IsUnionType(paramType))
                {
                    _il.Emit(OpCodes.Box, paramType);
                }
                return stackType;
            }
            return StackType.Unknown; // Fallback for untyped parameters
        }

        // 2. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage.
        // #838: in an arrow body, a captured write-shadow's DC field is keyed by its renamed storage
        // (<name>__bsN), not the bare name — remap before the lookup so the arrow's `name` reaches the
        // shadow's field rather than the outer same-named binding's field.
        var dcName = _ctx.CurrentArrowFunctionDCFieldRenames is { } funcDCRenames &&
                     funcDCRenames.TryGetValue(name, out var renamedStorage)
            ? renamedStorage : name;
        if (_ctx.CapturedFunctionLocals?.Contains(dcName) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(dcName, out var funcDCField) == true)
        {
            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                _il.Emit(OpCodes.Ldfld, funcDCField);
                return StackType.Unknown;
            }

            if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // Access from arrow body - go through $functionDC field.
                // For arrows inside async arrows, $functionDC might be null at runtime
                // (not populated by the intermediary). Emit a null check and fall through
                // to CapturedFields if null.
                if (_ctx.CapturedFields?.ContainsKey(name) == true)
                {
                    // Emit: if (this.$functionDC != null) use DC, else use own field
                    var fallbackLabel = _il.DefineLabel();
                    var doneLabel = _il.DefineLabel();
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Brfalse, fallbackLabel);
                    // DC is non-null — load from it
                    _il.Emit(OpCodes.Ldfld, funcDCField);
                    _il.Emit(OpCodes.Br, doneLabel);
                    // Fallback — load from own captured field
                    _il.MarkLabel(fallbackLabel);
                    _il.Emit(OpCodes.Pop); // discard null DC
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, _ctx.CapturedFields[name]);
                    _il.MarkLabel(doneLabel);
                }
                else
                {
                    // No fallback field — assume DC is populated
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                    _il.Emit(OpCodes.Ldfld, funcDCField);
                }
                return StackType.Unknown;
            }

            // No DC access available — fall through to other checks
        }

        // 2b. Arrow scope display class fields — CURRENT arrow's own DC (direct
        // access via the arrow's DC local).
        if (_ctx.CapturedArrowLocals?.Contains(name) == true &&
            _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowDCField) == true &&
            _ctx.ArrowScopeDisplayClassLocal != null)
        {
            _il.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
            _il.Emit(OpCodes.Ldfld, arrowDCField);
            return StackType.Unknown;
        }

        // 2c. Arrow scope display class fields — PARENT arrow's DC, reachable
        // through the current arrow's `$arrowDC` field. Kept separate from the
        // own-DC slots above so an arrow that *both* owns a DC (for its own
        // locals captured by inner arrows) AND captures values from a parent
        // arrow's DC can dispatch each variable to the correct path.
        if (_ctx.ParentArrowCapturedLocals?.Contains(name) == true &&
            _ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(name, out var parentArrowDCField) == true &&
            _ctx.CurrentArrowScopeDCField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);                              // this
            _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField); // this.$arrowDC (parent's DC)
            _il.Emit(OpCodes.Ldfld, parentArrowDCField);            // parent.<name>
            return StackType.Unknown;
        }

        // 2d. EXTRA ancestor arrow scope DCs — per-name bindings for captures
        // whose source scope is referenced by a non-primary $arrowDC{n} /
        // $arrowScopeDC{n} field (closures capturing from multiple ancestor
        // arrow scopes). Analyzer-confirmed entries only, so shadowing-safe.
        if (_ctx.ExtraArrowScopeBindings?.TryGetValue(name, out var extraBinding) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, extraBinding.RefField);
            _il.Emit(OpCodes.Ldfld, extraBinding.VarField);
            return StackType.Unknown;
        }

        // (Removed: the old CapturedArrowLocals + $arrowDC chain path. With
        // ParentArrowCapturedLocals now the dedicated parent slot, the old
        // path was emitting broken Ldarg_0 sequences in non-display-class
        // method contexts (object-literal methods) where Ldarg_0 is `__this`
        // rather than a display class instance.)

        // 3. Locals (with type awareness)
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(name);
            _il.Emit(OpCodes.Ldloc, local);
            // Integer loop-counter prototype (#928): a counter backed by a native Int64 slot reads
            // back as a double everywhere it is observed as a value (condition compares, arithmetic,
            // boxing, calls) — exact for any terminating loop (≤ 2^53). The increment and recognized
            // index sites bypass this generic load with their own native-int IL.
            if (localType == _types.Int64 && _ctx.IntegerCounterLocals.Contains(name))
            {
                _il.Emit(OpCodes.Conv_R8);
                return StackType.Double;
            }
            return MapTypeToStackType(localType);
        }

        // 4. Captured fields (closure)
        if (_ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            // Per-iteration cell capture (#650): the field holds a StrongBox shared
            // with the loop body; dereference Value to read the live binding.
            if (_ctx.CellCapturedFieldNames?.Contains(name) == true)
            {
                // The field is object-typed but holds a StrongBox at runtime.
                _il.Emit(OpCodes.Castclass, _types.StrongBoxOfObject);
                _il.Emit(OpCodes.Ldfld, _types.StrongBoxOfObjectValueField);
                return StackType.Unknown;
            }
            return MapTypeToStackType(field.FieldType);
        }

        // 5. Entry-point display class fields (captured top-level vars)
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true)
        {
            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                _il.Emit(OpCodes.Ldfld, entryPointField);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField); // Load entry-point display class
                _il.Emit(OpCodes.Ldfld, entryPointField); // Load the variable field
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                _il.Emit(OpCodes.Ldfld, entryPointField);
            }
            else
            {
                // Fallback - shouldn't happen
                return null;
            }
            return StackType.Unknown;
        }

        // 6. Top-level static vars (non-captured)
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, topLevelField);
            return StackType.Unknown;
        }

        return null; // Caller handles fallback (Math, classes, functions, namespaces)
    }

    /// <inheritdoc />
    public bool HasVariable(string name)
    {
        if (_ctx.CellBindingLocals.ContainsKey(name)) return true;
        if (_ctx.TryGetParameter(name, out _)) return true;
        if (_ctx.CapturedFunctionLocals?.Contains(name) == true &&
            _ctx.FunctionDisplayClassFields?.ContainsKey(name) == true) return true;
        if (_ctx.CapturedArrowLocals?.Contains(name) == true &&
            _ctx.ArrowScopeDisplayClassFields?.ContainsKey(name) == true) return true;
        if (_ctx.ParentArrowCapturedLocals?.Contains(name) == true &&
            _ctx.ParentArrowScopeDisplayClassFields?.ContainsKey(name) == true) return true;
        if (_ctx.ExtraArrowScopeBindings?.ContainsKey(name) == true) return true;
        if (_ctx.Locals.HasLocal(name)) return true;
        if (_ctx.CapturedFields?.ContainsKey(name) == true) return true;
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.ContainsKey(name) == true) return true;
        if (_ctx.TopLevelStaticVars?.ContainsKey(name) == true) return true;
        return false;
    }

    /// <inheritdoc />
    public bool TryStoreVariable(string name)
    {
        // 0. Per-iteration loop-binding cell (#650): write through the StrongBox so the
        //    mutation is visible to closures that captured this iteration's cell.
        if (_ctx.CellBindingLocals.TryGetValue(name, out var storeCell))
        {
            var cellTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, cellTemp);
            _il.Emit(OpCodes.Ldloc, storeCell);
            _il.Emit(OpCodes.Ldloc, cellTemp);
            _il.Emit(OpCodes.Stfld, _types.StrongBoxOfObjectValueField);
            return true;
        }

        // 1. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage.
        // #838: remap a captured write-shadow's name to its renamed DC storage key in an arrow body
        // (mirror of the load path), so the write lands on the shadow's field, not the outer binding's.
        var dcName = _ctx.CurrentArrowFunctionDCFieldRenames is { } funcDCRenames &&
                     funcDCRenames.TryGetValue(name, out var renamedStorage)
            ? renamedStorage : name;
        if (_ctx.CapturedFunctionLocals?.Contains(dcName) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(dcName, out var funcDCField) == true)
        {
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);

            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, funcDCField);
                // Captured PARAMETER: also sync the arg slot — reads resolve
                // parameters before the function DC (see 1b's mirror comment).
                if (_ctx.TryGetParameter(name, out var funcParamSync))
                {
                    _il.Emit(OpCodes.Ldloc, temp);
                    _ctx.EmitConvertForParamSlot(_il, name);
                    _il.Emit(OpCodes.Starg, funcParamSync);
                }
                return true;
            }

            if (_ctx.CurrentArrowFunctionDCField != null)
            {
                if (_ctx.CapturedFields?.ContainsKey(name) == true)
                {
                    // Emit: if (this.$functionDC != null) store to DC, else store to own field
                    var fallbackLabel = _il.DefineLabel();
                    var doneLabel = _il.DefineLabel();
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Brfalse, fallbackLabel);
                    // DC is non-null — store to it
                    _il.Emit(OpCodes.Ldloc, temp);
                    _il.Emit(OpCodes.Stfld, funcDCField);
                    _il.Emit(OpCodes.Br, doneLabel);
                    // Fallback — store to own captured field
                    _il.MarkLabel(fallbackLabel);
                    _il.Emit(OpCodes.Pop); // discard null DC
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldloc, temp);
                    _il.Emit(OpCodes.Stfld, _ctx.CapturedFields[name]);
                    _il.MarkLabel(doneLabel);
                }
                else
                {
                    // No fallback field — assume DC is populated
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                    _il.Emit(OpCodes.Ldloc, temp);
                    _il.Emit(OpCodes.Stfld, funcDCField);
                }
                return true;
            }

            // No DC access available — restore value to stack and fall through
            _il.Emit(OpCodes.Ldloc, temp);
        }

        // 1b. Arrow scope display class fields (captured arrow-local vars) —
        // CURRENT arrow's own DC (direct access via the arrow's DC local).
        if (_ctx.CapturedArrowLocals?.Contains(name) == true &&
            _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowDCFieldStore) == true &&
            _ctx.ArrowScopeDisplayClassLocal != null)
        {
            var tempArrow = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, tempArrow);
            _il.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
            _il.Emit(OpCodes.Ldloc, tempArrow);
            _il.Emit(OpCodes.Stfld, arrowDCFieldStore);
            // Captured PARAMETER: also sync the arg slot. Reads resolve
            // parameters before the scope DC, so a DC-only store would leave
            // later same-body reads seeing the stale argument (lodash:
            // `context = context || root; ... context.Date` read the original
            // undefined arg after the reassignment).
            if (_ctx.TryGetParameter(name, out var arrowParamSync))
            {
                _il.Emit(OpCodes.Ldloc, tempArrow);
                _ctx.EmitConvertForParamSlot(_il, name);
                _il.Emit(OpCodes.Starg, arrowParamSync);
            }
            return true;
        }

        // 1c. Arrow scope display class fields — PARENT arrow's DC, reachable
        // through `$arrowDC`. Mirror of read path 2c. Also covers the old
        // "no own DC, chain through parent" case where the field was tracked
        // in ArrowScopeDisplayClassFields before the parent/own slot split.
        {
            FieldBuilder? storeField = null;
            if (_ctx.ParentArrowCapturedLocals?.Contains(name) == true &&
                _ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(name, out var parentField) == true)
            {
                storeField = parentField;
            }
            else if (_ctx.CapturedArrowLocals?.Contains(name) == true &&
                     _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var ownChainField) == true &&
                     _ctx.ArrowScopeDisplayClassLocal == null)
            {
                storeField = ownChainField;
            }
            if (storeField != null && _ctx.CurrentArrowScopeDCField != null)
            {
                var tempArrow = _il.DeclareLocal(_types.Object);
                _il.Emit(OpCodes.Stloc, tempArrow);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
                _il.Emit(OpCodes.Ldloc, tempArrow);
                _il.Emit(OpCodes.Stfld, storeField);
                return true;
            }
        }

        // 1d. EXTRA ancestor arrow scope DCs — mirror of read path 2d.
        if (_ctx.ExtraArrowScopeBindings?.TryGetValue(name, out var extraStoreBinding) == true)
        {
            var tempExtra = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, tempExtra);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, extraStoreBinding.RefField);
            _il.Emit(OpCodes.Ldloc, tempExtra);
            _il.Emit(OpCodes.Stfld, extraStoreBinding.VarField);
            return true;
        }

        // 2. Locals
        if (_ctx.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
            return true;
        }

        // 3. Parameters
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            _il.Emit(OpCodes.Starg, argIndex);
            return true;
        }

        // 4. Captured fields (auto-detect value/reference type)
        if (_ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            // Use temp local pattern for storing to fields
            // This works for both value and reference type display classes
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            // Per-iteration cell capture (#650): the field holds a shared StrongBox —
            // write through Value so the loop body and sibling closures see the update.
            if (_ctx.CellCapturedFieldNames?.Contains(name) == true)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, field);
                _il.Emit(OpCodes.Castclass, _types.StrongBoxOfObject);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, _types.StrongBoxOfObjectValueField);
                return true;
            }
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
            return true;
        }

        // 5. Entry-point display class fields (captured top-level vars)
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true)
        {
            // Use temp local pattern for storing to fields
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);

            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField); // Load entry-point display class
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            }
            else
            {
                // Fallback - shouldn't happen
                return false;
            }

            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, entryPointField);
            return true;
        }

        // 6. Top-level static vars (non-captured)
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Stsfld, topLevelField);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void LoadThis()
    {
        // 1. Captured this (closure)
        if (_ctx.CapturedFields?.TryGetValue("this", out var thisField) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, thisField);
            return;
        }

        // 2. __this parameter (object method shorthand, function-expression receiver)
        if (_ctx.TryGetParameter("__this", out var thisArgIndex))
        {
            _il.Emit(OpCodes.Ldarg, thisArgIndex);
            // A nullish __this denotes a sloppy-mode bare call with no receiver
            // (e.g. an IIFE `(function(){ this.x = … })()`, or a callback passed
            // to Array.prototype.filter without a thisArg — ArrayEmitter stages the
            // missing thisArg as the JS `undefined` sentinel). Per ECMA-262
            // OrdinaryCallBindThis, a non-strict callee's `this` then resolves to
            // the global object; strict callees keep `this === undefined`. Mirror
            // cases 5/6 (and InvokeWithThis's "null in our model" convention) so
            // `this.x` routes through GlobalThis instead of throwing on null.
            // (Test262 Array filter/some 15.4.4.{20,17}-5-1, call 11.2.3-3_8.)
            EmitCoerceSloppyThisToGlobal();
            return;
        }

        // 3. Instance method — but only when arg0 IS the JS `this`. For inner function
        //    declarations emitted onto a display class, arg0 is the display-class self,
        //    not the user's `this`; fall through to the thread-local path in that case.
        if (_ctx.IsInstanceMethod && !_ctx.IsInnerFunctionOnDisplayClass)
        {
            _il.Emit(OpCodes.Ldarg_0);
            return;
        }

        // 4. Static constructor context - 'this' is the class type
        if (_ctx.IsStaticConstructorContext && _ctx.CurrentClassBuilder != null)
        {
            // Load the Type object for the current class
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            return;
        }

        // 5. Thread-local `this` set by $Runtime.NewOnFunction (or other call paths
        //    that prep a thisArg for a method whose signature has no __this param).
        //    Falls back to the globalThis sentinel when no such this is active: the
        //    sentinel denotes sloppy-mode `this` (= globalThis) so that a subsequent
        //    `this.x` read routes through GlobalThisGetProperty and — crucially for
        //    #735/#733 — value-position JS null stays distinct from a sloppy `this`.
        if (_ctx.Runtime?.CurrentFunctionThisField != null)
        {
            _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.CurrentFunctionThisField);
            EmitCoerceSloppyThisToGlobal();
            return;
        }

        // 6. Static context with an emitted runtime: top-level / entry point with no
        //    active this binding. Resolve to the globalThis sentinel (sloppy `this`)
        //    so `this.x` works and value-null remains distinguishable. Reference-
        //    assembly mode (no runtime) falls back to bare null as before.
        if (_ctx.Runtime != null)
        {
            _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.GlobalThisSingletonField);
            return;
        }

        _il.Emit(OpCodes.Ldnull);
    }

    /// <summary>
    /// Coerces a nullish <c>this</c> on the evaluation stack to the globalThis
    /// sentinel per ECMA-262 §10.2.1.2 OrdinaryCallBindThis. A CLR <c>null</c>
    /// (the compiler's sloppy "no receiver" model value) ALWAYS resolves to
    /// globalThis — preserving the long-standing cases 5/6 behavior. The JS
    /// <c>undefined</c> sentinel additionally resolves to globalThis ONLY when the
    /// enclosing function is non-strict; strict functions keep
    /// <c>this === undefined</c> (matching the interpreter's
    /// <c>functionStrict ? SharpTSUndefined.Instance : SharpTSGlobalThis.Instance</c>
    /// binding). No-op in reference-assembly mode (no emitted runtime).
    /// Stack: [this] → [this-or-globalThis].
    /// </summary>
    private void EmitCoerceSloppyThisToGlobal()
    {
        if (_ctx.Runtime?.GlobalThisSingletonField == null) return;

        var keep = _il.DefineLabel();
        var useGlobal = _il.DefineLabel();

        // null → globalThis (always).
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, useGlobal);

        // undefined → globalThis (non-strict callees only).
        if (!_ctx.IsStrictMode && _ctx.Runtime.UndefinedType != null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Isinst, _ctx.Runtime.UndefinedType);
            _il.Emit(OpCodes.Brtrue, useGlobal);
        }

        _il.Emit(OpCodes.Br, keep);

        _il.MarkLabel(useGlobal);
        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.GlobalThisSingletonField);

        _il.MarkLabel(keep);
    }

    private StackType MapTypeToStackType(Type? type)
    {
        if (type == null) return StackType.Unknown;
        if (_types.IsDouble(type)) return StackType.Double;
        if (_types.IsBoolean(type)) return StackType.Boolean;
        if (_types.IsString(type)) return StackType.String;
        return StackType.Unknown;
    }
}
