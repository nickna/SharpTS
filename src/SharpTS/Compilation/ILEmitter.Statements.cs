using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Statement emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private readonly Stack<(string Name, LocalBuilder Key, LocalBuilder Value)> _stableMapEntryBindings = new();

    private Type PromotedObjectValueClrType(TypeInfo? type) => type switch
    {
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => _ctx.Types.Double,
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => _ctx.Types.Boolean,
        TypeInfo.String => _ctx.Types.String,
        _ => throw new InvalidOperationException(
            $"Promoted object field has unsupported static type '{type}'")
    };

    protected override void EmitConditionCheck(Expr condition)
    {
        EmitExpression(condition);
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brfalse/Brtrue
            EnsureBoxed();
            EmitTruthyCheck();
        }
    }

    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        void MirrorScriptVarToGlobal(Action emitValue)
        {
            if (!_ctx.IsScriptTopLevel || !v.IsVar)
                return;

            IL.Emit(OpCodes.Ldstr, v.Name.Lexeme);
            emitValue();
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisSetProperty);
        }

        // Module-level storage (static field / entry-point display class) is only the right
        // target when this declaration IS a module-level binding: we are emitting the module
        // top-level statements (IsModuleTopLevel) and not inside a nested block. Inside a
        // function body the same dictionaries are present for READ access, but a same-named
        // declaration there is a function-local that must shadow — not overwrite — the module
        // binding; falling into this block wrote a function-local through to the module slot
        // and never created the real local, so captured reads saw null and the module var was
        // clobbered (#562). A nested block at top level likewise shadows via a fresh local —
        // EXCEPT a captured block-scoped binding lifted onto the entry-point display class
        // (#1201): that one's home IS the DC field (a closure created inside another closure's
        // body can only reach it there), and its name is declared exactly once in the module,
        // so no shadowing declaration can collide with the name-keyed check.
        if (_ctx.IsModuleTopLevel &&
            (!_ctx.Locals.IsInNestedScope ||
             _ctx.LiftedBlockScopedTopLevelVars?.Contains(v.Name.Lexeme) == true))
        {
            // Check if this is a captured top-level variable - use entry-point display class
            if (_ctx.CapturedTopLevelVars?.Contains(v.Name.Lexeme) == true &&
                _ctx.EntryPointDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var displayField) == true)
            {
                // Load the display class instance
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
                    // No access to display class - fall through to static field path
                    goto checkStaticField;
                }

                if (v.Initializer != null)
                {
                    EmitExpression(v.Initializer);
                    EmitBoxIfNeeded(v.Initializer);
                }
                else if (v.TypeAnnotation == "number")
                {
                    // Typed number without initializer defaults to 0
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                }
                IL.Emit(OpCodes.Stfld, displayField);
                _ctx.EmitMarkTopLevelLexicalInitialized(IL, v.Name.Lexeme);
                MirrorScriptVarToGlobal(() =>
                {
                    if (_ctx.EntryPointDisplayClassLocal != null)
                        IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                    else
                        IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField!);
                    IL.Emit(OpCodes.Ldfld, displayField);
                });
                return;
            }

        checkStaticField:

            // Check if this is a top-level variable - use static fields so all functions can access them
            if (_ctx.TopLevelStaticVars?.TryGetValue(v.Name.Lexeme, out var staticField) == true)
            {
                if (v.Initializer != null)
                {
                    if (!TryEmitNumericEmptyArrayInit(v))
                    {
                        EmitExpression(v.Initializer);
                        EmitBoxIfNeeded(v.Initializer);
                    }
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else if (v.TypeAnnotation == "number")
                {
                    // Typed number without initializer defaults to 0
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                _ctx.EmitMarkTopLevelLexicalInitialized(IL, v.Name.Lexeme);
                MirrorScriptVarToGlobal(() => IL.Emit(OpCodes.Ldsfld, staticField));
                return;
            }
        }

        // Check if this is a function-level captured variable - use function display class
        if (_ctx.CapturedFunctionLocals?.Contains(v.Name.Lexeme) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var funcDisplayField) == true &&
            _ctx.FunctionDisplayClassLocal != null)
        {
            // Store initializer (or default) in function display class field
            IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);

            if (v.Initializer != null)
            {
                if (funcDisplayField.FieldType == _ctx.Types.Double)
                    EmitExpressionAsDouble(v.Initializer);
                else
                {
                    EmitExpression(v.Initializer);
                    EmitBoxIfNeeded(v.Initializer);
                }
            }
            else if (v.TypeAnnotation == "number")
            {
                // Typed number without initializer defaults to 0
                IL.Emit(OpCodes.Ldc_R8, 0.0);
                if (funcDisplayField.FieldType == _ctx.Types.Object)
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            }
            IL.Emit(OpCodes.Stfld, funcDisplayField);
            return;
        }

        // Check if this is an arrow-scope captured variable - use arrow scope display class
        if (_ctx.CapturedArrowLocals?.Contains(v.Name.Lexeme) == true &&
            _ctx.ArrowScopeDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var arrowDisplayField) == true &&
            _ctx.ArrowScopeDisplayClassLocal != null)
        {
            // Store initializer (or default) in arrow scope display class field
            IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EmitBoxIfNeeded(v.Initializer);
            }
            else if (v.TypeAnnotation == "number")
            {
                // Typed number without initializer defaults to 0
                IL.Emit(OpCodes.Ldc_R8, 0.0);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            }
            IL.Emit(OpCodes.Stfld, arrowDisplayField);
            return;
        }

        // A lexical binding captured by a same-scope function declaration is
        // allocated before that function is materialized, so the capture sees
        // the TDZ sentinel rather than CLR null. Initialize that existing slot
        // here and refresh the snapshot fields retained by hoisted functions.
        if (_ctx.Locals.TryGetTag(v.Name.Lexeme, out var lexicalTag)
            && ReferenceEquals(lexicalTag, v.Name)
            && _ctx.Locals.GetCurrentScopeLocal(v.Name.Lexeme) is { } predeclaredLexical)
        {
            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EmitBoxIfNeeded(v.Initializer);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            }
            IL.Emit(OpCodes.Stloc, predeclaredLexical);

            if (_ctx.LexicalCaptureWriteBacks.TryGetValue(v.Name.Lexeme, out var writeBacks))
            {
                foreach (var (displayClass, field) in writeBacks)
                {
                    IL.Emit(OpCodes.Ldloc, displayClass);
                    IL.Emit(OpCodes.Ldloc, predeclaredLexical);
                    IL.Emit(OpCodes.Stfld, field);
                }
            }
            return;
        }

        // Non-escaping direct-call arrow (#858): `const add = (a) => a + i;` whose value is only ever
        // invoked by name. For a CAPTURING arrow, store the bare display-class instance in a typed local
        // (CLR type = the display class) instead of a $TSFunction wrapper; the function-value call fast
        // path then emits a direct `callvirt Invoke` with unboxed typed args, skipping the per-call
        // reflective dispatch. Reached only after the capture branches above, so a name captured by a
        // nested closure (excluded by the analyzer anyway) is never routed here. The call site re-checks
        // the slot's CLR type, so this binding and the direct call stay consistent even across same-named
        // bindings in other scopes.
        if (v.Initializer is Expr.ArrowFunction directCallArrow &&
            _ctx.DirectCallArrowBindings.TryGetValue(v.Name.Lexeme, out var boundArrow) &&
            ReferenceEquals(boundArrow, directCallArrow) &&
            _ctx.DisplayClasses.TryGetValue(directCallArrow, out var directCallDisplayClass))
        {
            var arrowLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, directCallDisplayClass);
            if (EmitCapturingArrowDisplayInstance(directCallArrow, directCallDisplayClass))
            {
                IL.Emit(OpCodes.Stloc, arrowLocal);
                return;
            }
            // Display-instance construction declined (missing ctor) — fall through to the generic
            // path, which re-emits the arrow as a $TSFunction wrapper into a fresh object local.
        }

        // Non-capturing variant of the above (#858 follow-up): `const id = (x) => x + 5; id(37)`. A
        // non-capturing arrow compiles to a STATIC method on $Program (no display class, no instance),
        // so there is no typed slot to key the call site on. Emit NOTHING for the binding (an arrow
        // literal has no observable side effect and the analyzer proved every use is a direct call), and
        // tag the in-scope binding with the arrow node so the call site can recognize it and emit a
        // direct `call` to the static method. The tag is scope-managed by LocalsManager, so a same-named
        // parameter/local elsewhere (no tag) can never hit the fast path. This also removes the per-call
        // $TSFunction wrapper allocation entirely (it was rebuilt every loop iteration before).
        if (v.Initializer is Expr.ArrowFunction staticCallArrow &&
            _ctx.DirectCallArrowBindings.TryGetValue(v.Name.Lexeme, out var staticBoundArrow) &&
            ReferenceEquals(staticBoundArrow, staticCallArrow) &&
            !_ctx.DisplayClasses.ContainsKey(staticCallArrow) &&
            _ctx.ArrowMethods.ContainsKey(staticCallArrow))
        {
            _ctx.Locals.DeclareLocal(v.Name.Lexeme, _ctx.Types.Object, tag: staticCallArrow);
            return;
        }

        // Exact non-escaping class allocation: when analysis proves that the
        // constructor only copies primitive parameters into declared fields and
        // the local is observed solely through constant-key field reads, reuse the
        // generated value-type shape carrier. Evaluate every argument first in
        // source order, then replay the constructor's pure field assignments; no
        // class instance or dynamic property store is allocated.
        if (_ctx.TypeMap != null && v.Initializer is Expr.New scalarNew
            && _ctx.TypeMap.IsScalarReplaceableClassLocal(v.Name, out var scalarInfo)
            && _ctx.TryGetObjectShapeType(scalarInfo.Shape.CanonicalKey) is { } scalarShape)
        {
            var argumentLocals = new LocalBuilder[scalarNew.Arguments.Count];
            for (int index = 0; index < scalarNew.Arguments.Count; index++)
            {
                var kind = scalarInfo.ConstructorParameterKinds[index];
                Type argumentType = kind switch
                {
                    TokenType.TYPE_NUMBER => _ctx.Types.Double,
                    TokenType.TYPE_BOOLEAN => _ctx.Types.Boolean,
                    _ => _ctx.Types.String
                };
                EmitExpression(scalarNew.Arguments[index]);
                EnsureForFieldType(argumentType);
                argumentLocals[index] = IL.DeclareLocal(argumentType);
                IL.Emit(OpCodes.Stloc, argumentLocals[index]);
            }

            var scalarLocal = _ctx.Locals.DeclareLocal(
                v.Name.Lexeme,
                scalarShape.ClrType);
            IL.Emit(OpCodes.Ldloca, scalarLocal);
            IL.Emit(OpCodes.Initobj, scalarShape.ClrType);
            foreach (var initialization in scalarInfo.FieldInitializations)
            {
                IL.Emit(OpCodes.Ldloca, scalarLocal);
                IL.Emit(OpCodes.Ldloc, argumentLocals[initialization.ParameterIndex]);
                IL.Emit(OpCodes.Stfld, scalarShape.FieldBuilders[initialization.FieldName]);
            }
            return;
        }

        // Non-escaping object-literal local (#862): a provably non-escaping `const o = { x: …, y: … }`
        // whose literal has a fixed, statically-known primitive shape is promoted to a generated
        // value-type "shape" struct local with typed fields. Field reads/writes (`o.x`) lower to direct
        // ldfld/stfld — no Dictionary, no string hash, no boxing — and a non-escaping struct local is
        // register-promoted by the JIT. Reached only after the capture branches above (a captured name is
        // excluded by the analyzer). Falls through to the generic Dictionary path if shapes aren't
        // threaded into this context (e.g. async/generator bodies, which never consult the mark).
        if (_ctx.TypeMap != null && v.Initializer is Expr.ObjectLiteral shapeLit
            && _ctx.TypeMap.IsPromotableObjectLocal(v.Name, out var objShape)
            && _ctx.TryGetObjectShapeType(objShape.CanonicalKey) is { } shapeType)
        {
            var structLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, shapeType.ClrType);
            IL.Emit(OpCodes.Ldloca, structLocal);
            IL.Emit(OpCodes.Initobj, shapeType.ClrType);

            if (shapeLit.Properties.Any(p => p.IsSpread))
            {
                // Stable exact object spread (#1505). Snapshot each source field at the precise
                // spread position: an explicit initializer between two spreads may mutate a source,
                // so loading only the final source state would violate CopyDataProperties order.
                // Explicit values also go through temporaries so overwritten initializers still run.
                var finalValues = new Dictionary<string, LocalBuilder>(StringComparer.Ordinal);
                foreach (var prop in shapeLit.Properties)
                {
                    if (prop.IsSpread)
                    {
                        var spreadVar = (Expr.Variable)prop.Value;
                        var source = _ctx.TryGetPromotedObjectLocal(spreadVar.Name.Lexeme)
                            ?? throw new InvalidOperationException(
                                $"Promoted object spread source '{spreadVar.Name.Lexeme}' has no shape local");

                        foreach (var sourceField in source.Shape.Fields)
                        {
                            var sourceBuilder = source.Shape.FieldBuilders[sourceField.Name];
                            var snapshot = IL.DeclareLocal(sourceBuilder.FieldType);
                            IL.Emit(OpCodes.Ldloca, source.Local);
                            IL.Emit(OpCodes.Ldfld, sourceBuilder);
                            IL.Emit(OpCodes.Stloc, snapshot);
                            finalValues[sourceField.Name] = snapshot;
                        }
                        continue;
                    }

                    var fieldName = ((Expr.IdentifierKey)prop.Key!).Name.Lexeme;
                    Type valueType = PromotedObjectValueClrType(_ctx.TypeMap.Get(prop.Value));
                    var value = IL.DeclareLocal(valueType);
                    EmitExpression(prop.Value);
                    EnsureForFieldType(valueType);
                    IL.Emit(OpCodes.Stloc, value);
                    finalValues[fieldName] = value;
                }

                foreach (var field in shapeType.Fields)
                {
                    var fieldBuilder = shapeType.FieldBuilders[field.Name];
                    IL.Emit(OpCodes.Ldloca, structLocal);
                    IL.Emit(OpCodes.Ldloc, finalValues[field.Name]);
                    IL.Emit(OpCodes.Stfld, fieldBuilder);
                }
                return;
            }

            // Evaluate every field initializer in source order (preserving side effects), even a field
            // never read later, and store into its typed struct field.
            foreach (var prop in shapeLit.Properties)
            {
                var fieldName = ((Expr.IdentifierKey)prop.Key!).Name.Lexeme;
                var fb = shapeType.FieldBuilders[fieldName];
                IL.Emit(OpCodes.Ldloca, structLocal);
                EmitExpression(prop.Value);
                EnsureForFieldType(fb.FieldType);
                IL.Emit(OpCodes.Stfld, fb);
            }
            return;
        }

        // Typed-array-local promotion (#857/#860): a provably non-escaping number[]/boolean[]
        // local with an empty-array-literal initializer gets a concrete List<double>/List<bool>
        // slot, so index get/set, .length, and push/pop lower to direct typed ops with no
        // element boxing or per-access isinst dispatch. Reached only AFTER the capture branches
        // above, so a captured local (routed to an object display-class field) is never promoted —
        // and the index/length/push fast paths key off the slot's CLR type via LocalsManager, so a
        // captured name (which has no typed local) can never accidentally hit them.
        if (_ctx.TypeMap != null && _ctx.TypeMap.IsPromotableArrayLocal(v.Name, out var promoElemTok))
        {
            // Initializer kinds the typed slot can hold: an empty array literal `[]` (build a fresh
            // List), or a typed-double `src.map(cb)` (#861 typed-HOF pipeline) when the source is
            // itself a promoted List<double> and the mapper is a typed non-capturing arrow — decided
            // at emit time from the already-declared source slot, so a List<double> result is only
            // ever produced into a List<double> slot (never escaping into $Array context).
            bool emptyInit = v.Initializer is Expr.ArrayLiteral { Elements.Count: 0 } or null;
            System.Reflection.Emit.LocalBuilder hofSrc = null!;
            System.Reflection.Emit.MethodBuilder hofArrow = null!;
            bool hofIsFilter = false;
            bool typedHofInit = promoElemTok == TokenType.TYPE_NUMBER
                && TryResolveTypedDoubleHofInit(v.Initializer, out hofSrc, out hofArrow, out hofIsFilter);

            if (emptyInit || typedHofInit)
            {
                var promoDesc = promoElemTok == TokenType.TYPE_NUMBER ? ArrayElements.Double : ArrayElements.Bool;
                var promoListType = promoDesc.GetListType(_ctx.Types);
                var promoLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, promoListType);
                if (emptyInit)
                {
                    IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(promoListType));
                }
                else
                {
                    // result = ArrayMapDouble/ArrayFilterDouble(src, <typed arrow>) — direct typed
                    // delegate (no boxed adapter), produces a fresh List<double> with no element boxing.
                    IL.Emit(OpCodes.Ldloc, hofSrc);
                    IL.Emit(OpCodes.Ldnull);
                    IL.Emit(OpCodes.Ldftn, hofArrow);
                    if (hofIsFilter)
                    {
                        IL.Emit(OpCodes.Newobj, typeof(Func<double, bool>).GetConstructor([typeof(object), typeof(IntPtr)])!);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayFilterDouble);
                    }
                    else
                    {
                        IL.Emit(OpCodes.Newobj, typeof(Func<double, double>).GetConstructor([typeof(object), typeof(IntPtr)])!);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayMapDouble);
                    }
                }
                IL.Emit(OpCodes.Stloc, promoLocal);
                return;
            }
            // Marked promotable but the initializer won't produce a matching typed list (e.g. the map
            // source isn't itself promoted) — fall through to the generic object-slot path below so the
            // slot type stays consistent with the value the initializer actually produces.
        }

        // Append-only string-accumulator promotion (#857): a provably non-escaping `string` local with
        // a string-literal initializer, used only via `s = s + str`/`s += str` (statement position),
        // `s.length`, and `s.charCodeAt(i)`, is backed by a StringBuilder slot — turning O(n²) repeated
        // String.Concat (each copies the whole accumulator) into amortized-O(1) Append. StringBuilder
        // .Length and the [i] indexer are UTF-16 code units, identical to JS .length/charCodeAt, so those
        // reads need no materialization. Reached only AFTER the capture branches above (the analyzer
        // excludes captured names); the append/length/charCodeAt fast paths key off the slot's CLR type.
        if (_ctx.TypeMap != null && _ctx.TypeMap.IsPromotableStringAccumulator(v.Name)
            && v.Initializer is Expr.Literal { Value: string seedStr })
        {
            var sbLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, _ctx.Types.StringBuilder);
            IL.Emit(OpCodes.Ldstr, seedStr);
            IL.Emit(OpCodes.Newobj, _ctx.Types.StringBuilderStringCtor);
            IL.Emit(OpCodes.Stloc, sbLocal);
            return;
        }

        // Closed-lifetime numeric Map promotion (#1482). The analyzer admits
        // only a fresh, empty Map<number, number> local with direct numeric
        // set/get/has/delete/clear/size uses, so the slot can hold native doubles.
        // EqualityComparer<double>.Default is explicit here: Double.Equals and
        // Double.GetHashCode define NaN equality and signed-zero equivalence,
        // matching SameValueZero for the admitted numeric key domain.
        if (_ctx.TypeMap?.IsPromotableNumericMapLocal(v.Name) == true)
        {
            Type mapType = _ctx.Types.DictionaryDoubleDouble;
            var mapLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, mapType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetProperty(
                _ctx.Types.EqualityComparerOfDouble, "Default").GetMethod!);
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(
                mapType, _ctx.Types.IEqualityComparerOfDouble));
            IL.Emit(OpCodes.Stloc, mapLocal);
            return;
        }

        // Integer loop-counter prototype (#928): the analyzer-identified counter gets a native
        // Int64 slot initialized from its integer literal. Reads materialize a double (resolver),
        // the increment stays native int, and recognized index sites (a[i], a[i±k]) consume the
        // int directly. The analyzer guarantees the counter is non-captured, non-reassigned, and
        // integer-initialized, so this early return safely bypasses the capture/array paths below.
        if (_integerLoopCounterName == v.Name.Lexeme
            && TryGetIntegerCounterInit(v.Initializer, out long counterInit))
        {
            var intLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, _ctx.Types.Int64);
            _ctx.IntegerCounterLocals.Add(v.Name.Lexeme);
            IL.Emit(OpCodes.Ldc_I8, counterInit);
            IL.Emit(OpCodes.Stloc, intLocal);
            return;
        }

        // Determine if this local can use unboxed double type
        Type localType = CanUseUnboxedLocal(v) ? _ctx.Types.Double : _ctx.Types.Object;
        var local = _ctx.Locals.DeclareLocal(v.Name.Lexeme, localType);

        if (v.Initializer != null)
        {
            // Self-referential capture write-back (issue #421): a closure created
            // in the initializer that captures THIS variable (e.g.
            // `const s = make(() => s)`) snapshots the local's value before this
            // assignment, so it sees the stale/previous value (or null on the first
            // loop iteration). Track the captured closures' display-class fields and
            // write the freshly-assigned value back into them after the store —
            // giving the closure the live value while keeping per-iteration
            // fresh-binding (each iteration builds a distinct display class).
            var savedSelfCaptureName = _ctx.SelfCaptureVarName;
            var savedSelfCaptureWriteBacks = _ctx.SelfCaptureWriteBacks;
            _ctx.SelfCaptureVarName = v.Name.Lexeme;
            _ctx.SelfCaptureWriteBacks = [];

            // number[] unboxing: a non-promoted (escaping) `number[]` local with an empty-array
            // initializer is created as a NUMERIC $Array; otherwise emit the initializer normally.
            if (!TryEmitNumericEmptyArrayInit(v))
            {
                if (_ctx.Types.IsDouble(localType))
                    EmitExpressionAsDouble(v.Initializer);
                else
                    EmitExpression(v.Initializer);

                if (_ctx.Types.IsDouble(localType))
                {
                    // Ensure we have an unboxed double on stack
                    EnsureDouble();
                }
                else
                {
                    // Ensure we have a boxed object on stack
                    EmitBoxIfNeeded(v.Initializer);
                }
            }
            IL.Emit(OpCodes.Stloc, local);
            RegisterStableDestructuringSource(v, local);

            foreach (var (dcInstance, field) in _ctx.SelfCaptureWriteBacks)
            {
                IL.Emit(OpCodes.Ldloc, dcInstance);
                IL.Emit(OpCodes.Ldloc, local);
                if (_ctx.Types.IsDouble(localType))
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                IL.Emit(OpCodes.Stfld, field);
            }

            _ctx.SelfCaptureVarName = savedSelfCaptureName;
            _ctx.SelfCaptureWriteBacks = savedSelfCaptureWriteBacks;
        }
        else
        {
            if (_ctx.Types.IsDouble(localType))
            {
                // Initialize to 0.0 for uninitialized number variables
                IL.Emit(OpCodes.Ldc_R8, 0.0);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            }
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Resolves <paramref name="init"/> as a typed-double <c>src.map(cb)</c> or <c>src.filter(cb)</c>
    /// (#861 typed-HOF pipeline): true iff <c>src</c> currently binds to a promoted <c>List&lt;double&gt;</c>
    /// slot and <c>cb</c> is an inline, non-capturing arrow compiled to <c>double(double)</c> (map) or
    /// <c>bool(double)</c> (filter). On success yields the source list local, the arrow's typed static
    /// method (binds directly to <c>Func&lt;double,double&gt;</c>/<c>Func&lt;double,bool&gt;</c>), and
    /// whether it is filter. Decided at emit time (source declared earlier in source order), so a typed
    /// List&lt;double&gt; result is only produced when the source is genuinely typed.
    /// </summary>
    private bool TryResolveTypedDoubleHofInit(
        Expr? init,
        out System.Reflection.Emit.LocalBuilder srcList,
        out System.Reflection.Emit.MethodBuilder arrowMethod,
        out bool isFilter)
    {
        srcList = null!;
        arrowMethod = null!;
        isFilter = false;
        if (init is not Expr.Call { Callee: Expr.Get { Object: Expr.Variable v, Optional: false } get } call)
            return false;
        bool isMap = get.Name.Lexeme == "map";
        isFilter = get.Name.Lexeme == "filter";
        if (!isMap && !isFilter) return false;
        if (call.Arguments.Count != 1 || call.Arguments[0] is not Expr.ArrowFunction af) return false;
        if (_ctx.TryGetPromotedArrayLocal(v.Name.Lexeme) is not { Descriptor.Kind: ArrayElementsKind.Double } prom)
            return false;
        if (_ctx.DisplayClasses.ContainsKey(af)) return false;               // capturing → not directly bindable
        if (!_ctx.ArrowMethods.TryGetValue(af, out var m)) return false;
        var expectedReturn = isFilter ? _ctx.Types.Boolean : _ctx.Types.Double;
        if (m.ReturnType != expectedReturn) return false;
        var ps = m.GetParameters();
        if (ps.Length != 1 || ps[0].ParameterType != _ctx.Types.Double) return false;
        srcList = prom.Local;
        arrowMethod = m;
        return true;
    }

    /// <summary>
    /// Tracks the loop counter variable name for optimized for loops.
    /// When set, the variable can use unboxed double even without explicit type annotation.
    /// </summary>
    private string? _optimizedLoopCounterName;

    /// <summary>
    /// Integer loop-counter prototype (#928): name of the for-loop counter to back with a native
    /// Int64 slot for the current loop, or null. Consulted by EmitVarStatement at the counter's
    /// declaration. Gated by <see cref="ForLoopAnalyzer.IntegerCounterEnabled"/>.
    /// </summary>
    private string? _integerLoopCounterName;

    /// <summary>
    /// Check whether a local variable can use an unboxed double (float64) IL type
    /// instead of the default object. Eligible when:
    ///   1. It is an optimized for-loop counter, OR
    ///   2. It has an explicit ': number' annotation with a numeric initializer, OR
    ///   3. The TypeChecker inferred its initializer type as 'number'.
    /// By the time this method is called, captured variables have already been
    /// routed to display-class fields (always object), so no capture check is needed.
    /// </summary>
    private bool CanUseUnboxedLocal(Stmt.Var v)
    {
        // Stable custom iteration proves its value binding is numeric. For the
        // exact `acc = acc + value` loop shape, that proof also removes the
        // checker's conservative any/undefined flow from the accumulator.
        if (_ctx.TypeMap?.IsStableCustomIteratorNumericAccumulator(v.Name) == true)
            return true;

        // #367: a number-typed local that an `any`/`undefined` value may have (transitively) been
        // assigned must use an object slot — an unboxed double slot would coerce the runtime
        // `undefined` sentinel to NaN at the store. The type checker flags such declarations (and,
        // for `const`, the reused initializer expression) in the TypeMap.
        if (_ctx.TypeMap != null &&
            (_ctx.TypeMap.IsUndefinedReachableNumericLocal(v) ||
             (v.Initializer != null && _ctx.TypeMap.IsUndefinedReachableNumericLocal(v.Initializer))))
            return false;

        // Check if this is an optimized for loop counter
        if (_optimizedLoopCounterName != null && v.Name.Lexeme == _optimizedLoopCounterName)
            return true;

        // Explicit 'number' type annotation
        if (v.TypeAnnotation == "number")
        {
            // If there's an initializer, it must be a known number expression
            if (v.Initializer != null)
            {
                var exprType = _ctx.TypeMap?.Get(v.Initializer);
                if (!IsNumericType(exprType))
                    return false;
            }
            return true;
        }

        // Infer from TypeMap: if the initializer is statically typed as 'number'
        // (including number literal types like '1', '42'), the TypeChecker
        // guarantees all assignments stay 'number'.
        if (v.TypeAnnotation == null && v.Initializer != null && _ctx.TypeMap != null)
        {
            var exprType = _ctx.TypeMap.Get(v.Initializer);
            if (IsNumericType(exprType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the type is a numeric type (number primitive or number literal).
    /// </summary>
    private static bool IsNumericType(TypeSystem.TypeInfo? type) =>
        type is TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            or TypeSystem.TypeInfo.NumberLiteral;

    /// <summary>
    /// Integer loop-counter prototype (#928): extracts an integer-literal initializer (incl. unary
    /// minus, e.g. <c>let i = -1</c>) as a <c>long</c>. Mirrors <c>ForLoopAnalyzer</c>'s eligibility
    /// check; returns false for non-integer or non-literal initializers (defensive — the analyzer
    /// already required an integer literal before the counter name reaches here).
    /// </summary>
    private static bool TryGetIntegerCounterInit(Expr? initializer, out long value)
    {
        value = 0;
        double d;
        if (initializer is Expr.Literal { Value: double lit })
            d = lit;
        else if (initializer is Expr.Unary { Operator.Type: TokenType.MINUS, Right: Expr.Literal { Value: double neg } })
            d = -neg;
        else
            return false;
        if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Truncate(d))
            return false;
        value = (long)d;
        return true;
    }

    /// <summary>
    /// Numeric-mode array creation (number[] unboxing project): a statically-typed
    /// <c>number[]</c> declaration initialized with an empty array literal (<c>[]</c>)
    /// is created as a NUMERIC <c>$Array</c> (unboxed <c>double[]</c> elements-kind)
    /// instead of a boxed one. Escaping <c>number[]</c> arrays (params/fields/
    /// module-level) live on the <c>$Array</c> path — the win is that their index
    /// writes go straight into an unboxed <c>double[]</c> via the <c>SetDouble</c>
    /// fast path, with no per-element boxing. Non-escaping <c>number[]</c> locals are
    /// promoted to <c>List&lt;double&gt;</c> upstream (the IsPromotableArrayLocal
    /// branch) and return before reaching the storage sites that consult this.
    ///
    /// <para>Returns true and leaves a numeric (empty) <c>$Array</c> on the stack
    /// (as <c>object</c>) when applicable; the caller stores it. Returns false
    /// otherwise — the caller emits the initializer normally. Any operation the
    /// numeric fast paths don't cover deopts the array back to boxed on first
    /// touch, so this is purely a representation choice, never a semantic one.</para>
    /// </summary>
    private bool TryEmitNumericEmptyArrayInit(Stmt.Var v)
        => TryEmitNumericEmptyArrayInit(v.TypeAnnotation, v.Initializer);

    /// <summary>
    /// Component overload of <see cref="TryEmitNumericEmptyArrayInit(Stmt.Var)"/> so class
    /// field initializers (<c>arr: number[] = []</c>) can reuse the same numeric-creation hook.
    /// </summary>
    internal bool TryEmitNumericEmptyArrayInit(string? typeAnnotation, Expr? initializer)
    {
        if (typeAnnotation != "number[]") return false;
        if (initializer is not Expr.ArrayLiteral { Elements.Count: 0 }) return false;
        EmitNumericEmptyArray();
        return true;
    }

    /// <summary>
    /// Emits an empty numeric <c>$Array</c> onto the stack: <c>$Runtime.CreateArray(new
    /// object[0])</c> (a fresh empty <c>$Array</c>) followed by <c>MarkNumeric()</c>,
    /// which flips it into unboxed <c>double[]</c> mode. Leaves the <c>$Array</c>
    /// reference on the stack.
    /// </summary>
    private void EmitNumericEmptyArray()
    {
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);            // [$Array] (empty)
        IL.Emit(OpCodes.Dup);                                        // [$Array, $Array]
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayMarkNumeric); // [$Array]
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a for loop with unboxed counter and array hoist optimizations.
    /// </summary>
    protected override void EmitFor(Stmt.For f)
    {
        // Analyze the loop to see if we can use an unboxed counter
        var analysis = ForLoopAnalyzer.Analyze(f, _ctx.ClosureAnalyzer);

        if (analysis.CanUseUnboxedCounter && analysis.VariableName != null)
            _optimizedLoopCounterName = analysis.VariableName;

        // Integer loop-counter prototype (#928): when gated on, back a provably-integer monotonic
        // counter with a native Int64 slot. Save/restore the field so nested loops don't clobber it.
        var savedIntCounterName = _integerLoopCounterName;
        string? activeIntCounter = ForLoopAnalyzer.IntegerCounterEnabled
            ? ForLoopAnalyzer.AnalyzeIntegerCounter(f, _ctx.ClosureAnalyzer)
            : null;
        if (activeIntCounter != null)
            _integerLoopCounterName = activeIntCounter;

        try
        {
            if (activeIntCounter != null
                && TryEmitExactInt32StencilReduction(f, activeIntCounter))
            {
                return;
            }
            if (activeIntCounter != null
                && TryEmitExactInt32FillLoop(f, activeIntCounter))
            {
                return;
            }

            // Inline base.EmitFor to insert array hoist preamble between
            // initializer and loop start.
            _ctx.Locals.EnterScope();

            // Emit initializer (declares loop variable in current scope)
            if (f.Initializer != null)
                EmitStatement(f.Initializer);

            // A tightly-proven `for (let i = 0; i < n; i++) a.push(pureValue)`
            // can reserve its boxed-array storage once. This removes geometric
            // growth (and the 16,384-slot LOH allocation at n=10,000) without
            // evaluating any user expression early; unsupported runtime shapes
            // and unbounded/NaN counts simply retain normal List<T> growth.
            EmitCountedPushReservation(f);
            EmitCountedNumericMapReservation(f);

            // Per-iteration reference cells (#650): for a loop binding that the body
            // both mutates and a closure captures, wrap its initial value in a fresh
            // StrongBox and route all body/condition/increment access through the cell
            // (registered in CellBindingLocals). Closures capture the cell by reference,
            // so they observe end-of-iteration mutations; the copy-forward below gives
            // each iteration its own cell.
            var cellNames = _ctx.ClosureAnalyzer?.GetForLoopCells(f);
            List<(string Name, LocalBuilder? Prior)>? activeCells = null;
            if (cellNames != null && cellNames.Count > 0)
            {
                activeCells = EmitForLoopCellInit(cellNames);
            }

            // Array hoist preamble: emit isinst checks for loop-invariant arrays
            var hoisted = EmitArrayHoistPreamble(f.Body, f.Condition, f.Increment);
            // Typed-array receiver hoist (#928): cast loop-invariant numeric TypedArray receivers once.
            var taHoisted = EmitTypedArrayHoistPreamble(f.Body, f.Condition, f.Increment);

            var builder = _ctx.ILBuilder;
            var startLabel = builder.DefineLabel("for_start");
            var endLabel = builder.DefineLabel("for_end");
            var continueLabel = builder.DefineLabel("for_continue");

            _ctx.EnterLoop(endLabel, continueLabel);

            builder.MarkLabel(startLabel);
            EmitCancellationCheck();

            if (f.Condition != null)
            {
                EmitConditionCheck(f.Condition);
                builder.Emit_Brfalse(endLabel);
            }

            EmitStatement(f.Body);

            builder.MarkLabel(continueLabel);
            // CreatePerIterationEnvironment analog: copy each cell's end-of-body value
            // into a FRESH cell BEFORE the increment, so the closures created this
            // iteration keep the value they observed and the increment acts on the
            // next iteration's binding.
            if (activeCells != null)
                EmitForLoopCellCopyForward(activeCells);

            if (f.Increment != null)
            {
                EmitExpression(f.Increment);
                IL.Emit(OpCodes.Pop);
            }

            builder.Emit_Br(startLabel);

            builder.MarkLabel(endLabel);
            _ctx.ExitLoop();

            // Pop hoisted cache
            if (hoisted) _ctx.HoistedArrayCaches.Pop();
            if (taHoisted) _ctx.HoistedTypedArrayCaches.Pop();

            if (activeCells != null)
                foreach (var (name, prior) in activeCells)
                {
                    if (prior != null) _ctx.CellBindingLocals[name] = prior;
                    else _ctx.CellBindingLocals.Remove(name);
                }

            _ctx.Locals.ExitScope();
        }
        finally
        {
            _optimizedLoopCounterName = null;
            // The counter's Int64 slot/membership is loop-scoped: drop it so any later same-named
            // local in an enclosing scope is not mistaken for an integer counter.
            if (activeIntCounter != null)
                _ctx.IntegerCounterLocals.Remove(activeIntCounter);
            _integerLoopCounterName = savedIntCounterName;
        }
    }

    /// <summary>
    /// Versions the canonical <c>sum = sum + Int32 three-point stencil</c> loop. The hot
    /// version compares the native Int64 counter to a guarded integer bound and keeps the
    /// running sum integral. Fractional/NaN/out-of-range bounds, non-integral accumulators,
    /// negative zero, or a sum that leaves Number's safe-integer range branch to the ordinary
    /// double loop at the exact next iteration.
    /// </summary>
    private bool TryEmitExactInt32StencilReduction(Stmt.For loop, string counterName)
    {
        if (_ctx.ExceptionBlockDepth != 0
            || loop.Initializer is not Stmt.Var counterDeclaration
            || counterDeclaration.Name.Lexeme != counterName
            || !TryGetIntegerCounterInit(counterDeclaration.Initializer, out long initialCounter)
            || initialCounter != 1
            || loop.Increment is not Expr.PostfixIncrement
            {
                Operator.Type: TokenType.PLUS_PLUS,
                Operand: Expr.Variable incrementCounter
            }
            || incrementCounter.Name.Lexeme != counterName
            || loop.Condition is not Expr.Binary
            {
                Operator.Type: TokenType.LESS,
                Left: Expr.Variable conditionCounter,
                Right: Expr.Binary
                {
                    Operator.Type: TokenType.MINUS,
                    Left: Expr.Variable boundVariable,
                    Right: var boundOffset
                }
            }
            || conditionCounter.Name.Lexeme != counterName
            || !TryGetIntLiteralValue(boundOffset, out long offset)
            || offset != 1
            || !_ctx.TryGetParameterType(boundVariable.Name.Lexeme, out var boundParameterType)
            || boundParameterType != _ctx.Types.Double
            || !TryMatchInt32StencilAccumulator(
                loop.Body, counterName, out var accumulatorName, out var receiver)
            || _ctx.Locals.GetLocal(accumulatorName) is not { } accumulatorDouble
            || accumulatorDouble.LocalType != _ctx.Types.Double
            || _ctx.Locals.GetLocal(receiver.Name.Lexeme) == null
            || _ctx.Runtime == null)
        {
            return false;
        }

        var candidates = TypedArrayHoistAnalyzer.AnalyzeFor(
            loop.Body, loop.Condition, loop.Increment, _ctx.TypeMap);
        if (!candidates.TryGetValue(receiver.Name.Lexeme, out var candidate)
            || candidate is not { ElementType: "Int32", CanHoistBacking: true })
        {
            return false;
        }

        _ctx.Locals.EnterScope();
        EmitStatement(loop.Initializer);
        bool typedArrayHoisted = EmitTypedArrayHoistPreamble(
            loop.Body, loop.Condition, loop.Increment);
        if (!typedArrayHoisted
            || !TryGetDirectTypedArrayBacking(receiver, "Int32", out var backing)
            || backing.Layout is not
                { BytesPerElement: 4, Signed: true, IsFloat: false })
        {
            throw new InvalidOperationException(
                "Exact Int32 stencil proof did not produce its direct backing.");
        }

        var counter = _ctx.Locals.GetLocal(counterName)!;
        var boundDouble = IL.DeclareLocal(_ctx.Types.Double);
        var boundInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var accumulatorInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var candidateInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var fastActive = IL.DeclareLocal(_ctx.Types.Boolean);
        var centerIndex = IL.DeclareLocal(_ctx.Types.Int32);
        var centerReference = IL.DeclareLocal(_ctx.Types.Byte.MakeByRefType());

        var slowStart = IL.DefineLabel();
        var fastStart = IL.DefineLabel();
        var fastContinue = IL.DefineLabel();
        var fastOverflow = IL.DefineLabel();
        var end = IL.DefineLabel();

        // The exact body cannot contain control flow, but adopting the loop labels preserves
        // labeled break/continue resolution invariants for the surrounding emitter.
        _ctx.EnterLoop(end, fastContinue);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, fastActive);

        // Hoist `n - 1`: n is a typed parameter and the exact body only assigns the accumulator.
        var loadedBound = _resolver.TryLoadVariable(boundVariable.Name.Lexeme);
        if (loadedBound == null)
            throw new InvalidOperationException("Unable to load Int32 stencil bound.");
        SetStackType(loadedBound.Value);
        EnsureDouble();
        IL.Emit(OpCodes.Ldc_R8, 1d);
        IL.Emit(OpCodes.Sub);
        IL.Emit(OpCodes.Stloc, boundDouble);

        EmitExactIntegerGuard(boundDouble, -2_147_483_648d, 2_147_483_647d, slowStart);
        IL.Emit(OpCodes.Ldloc, boundDouble);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, boundInteger);

        // A safe integer can be accumulated exactly in Int64. Negative zero must remain on
        // the double path because its sign is observable when the loop has no iterations.
        EmitExactIntegerGuard(
            accumulatorDouble,
            -9_007_199_254_740_991d,
            9_007_199_254_740_991d,
            slowStart);
        var accumulatorNotZero = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Ldc_R8, 0d);
        IL.Emit(OpCodes.Bne_Un, accumulatorNotZero);
        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Call, typeof(BitConverter).GetMethod(
            nameof(BitConverter.DoubleToInt64Bits), [_ctx.Types.Double])!);
        IL.Emit(OpCodes.Ldc_I8, 0L);
        IL.Emit(OpCodes.Blt, slowStart);
        IL.MarkLabel(accumulatorNotZero);

        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, accumulatorInteger);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, fastActive);
        IL.Emit(OpCodes.Br, fastStart);

        IL.MarkLabel(fastStart);
        EmitCancellationCheckWithInt64AccumulatorFlush(
            accumulatorDouble, accumulatorInteger);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldloc, boundInteger);
        IL.Emit(OpCodes.Bge, end);

        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, centerIndex);

        var inRange = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, centerIndex);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Sub);
        IL.Emit(OpCodes.Ldloc, backing.LengthLocal);
        IL.Emit(OpCodes.Ldc_I4_2);
        IL.Emit(OpCodes.Sub);
        IL.Emit(OpCodes.Blt_Un, inRange);
        EmitInt64AccumulatorStore(accumulatorDouble, accumulatorInteger);
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(typeof(IndexOutOfRangeException)));
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(inRange);

        IL.Emit(OpCodes.Ldloc, backing.BufferLocal);
        IL.Emit(OpCodes.Call, RuntimeEmitter.GetByteArrayDataReference());
        IL.Emit(OpCodes.Ldloc, centerIndex);
        IL.Emit(OpCodes.Ldc_I4_4);
        IL.Emit(OpCodes.Mul);
        IL.Emit(OpCodes.Call, RuntimeEmitter.UnsafeAddByteOffset());
        IL.Emit(OpCodes.Stloc, centerReference);

        IL.Emit(OpCodes.Ldloc, accumulatorInteger);
        EmitDirectInt32ReadAsInt64(centerReference, -4);
        EmitDirectInt32ReadAsInt64(centerReference, 0);
        IL.Emit(OpCodes.Ldc_I4_2);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Mul);
        IL.Emit(OpCodes.Sub);
        EmitDirectInt32ReadAsInt64(centerReference, 4);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, candidateInteger);

        IL.Emit(OpCodes.Ldloc, candidateInteger);
        IL.Emit(OpCodes.Ldc_I8, -9_007_199_254_740_991L);
        IL.Emit(OpCodes.Blt, fastOverflow);
        IL.Emit(OpCodes.Ldloc, candidateInteger);
        IL.Emit(OpCodes.Ldc_I8, 9_007_199_254_740_991L);
        IL.Emit(OpCodes.Bgt, fastOverflow);
        IL.Emit(OpCodes.Ldloc, candidateInteger);
        IL.Emit(OpCodes.Stloc, accumulatorInteger);

        IL.MarkLabel(fastContinue);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, counter);
        IL.Emit(OpCodes.Br, fastStart);

        // The current iteration's exact integer result is rounded to double once, exactly
        // as Number addition would round it, and the next iteration resumes generic lowering.
        IL.MarkLabel(fastOverflow);
        IL.Emit(OpCodes.Ldloc, candidateInteger);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Stloc, accumulatorDouble);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, fastActive);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, counter);

        IL.MarkLabel(slowStart);
        EmitCancellationCheck();
        EmitConditionCheck(loop.Condition);
        IL.Emit(OpCodes.Brfalse, end);
        EmitStatement(loop.Body);
        EmitExpression(loop.Increment);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Br, slowStart);

        IL.MarkLabel(end);
        // Only the fast path reaches end with a stale double accumulator.
        var finished = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, fastActive);
        IL.Emit(OpCodes.Brfalse, finished);
        EmitInt64AccumulatorStore(accumulatorDouble, accumulatorInteger);
        IL.MarkLabel(finished);

        _ctx.ExitLoop();
        _ctx.HoistedTypedArrayCaches.Pop();
        _ctx.Locals.ExitScope();
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Versions the fill phase of the Int32 kernel. Once the typed parameter bound is
    /// proven to be an Int32 integer, the loop uses a native comparison and writes the
    /// analyzer-proven counter expression directly to the backing buffer. The assignment's
    /// discarded Number result is never materialized.
    /// </summary>
    private bool TryEmitExactInt32FillLoop(Stmt.For loop, string counterName)
    {
        Stmt bodyStatement = loop.Body is Stmt.Block { Statements.Count: 1 } block
            ? block.Statements[0]
            : loop.Body;
        if (_ctx.ExceptionBlockDepth != 0
            || loop.Initializer is not Stmt.Var counterDeclaration
            || counterDeclaration.Name.Lexeme != counterName
            || !TryGetIntegerCounterInit(counterDeclaration.Initializer, out long initialCounter)
            || initialCounter != 0
            || loop.Increment is not Expr.PostfixIncrement
            {
                Operator.Type: TokenType.PLUS_PLUS,
                Operand: Expr.Variable incrementCounter
            }
            || incrementCounter.Name.Lexeme != counterName
            || loop.Condition is not Expr.Binary
            {
                Operator.Type: TokenType.LESS,
                Left: Expr.Variable conditionCounter,
                Right: Expr.Variable boundVariable
            }
            || conditionCounter.Name.Lexeme != counterName
            || !_ctx.TryGetParameterType(boundVariable.Name.Lexeme, out var boundParameterType)
            || boundParameterType != _ctx.Types.Double
            || bodyStatement is not Stmt.Expression
            {
                Expr: Expr.SetIndex
                {
                    Object: Expr.Variable receiver,
                    Index: Expr.Variable index,
                    Value: var value
                }
            }
            || index.Name.Lexeme != counterName
            || _ctx.TypeMap?.Get(receiver) is not TypeInfo.TypedArray { ElementType: "Int32" }
            || !TryAnalyzeInt32CounterExpression(value, counterName, out _)
            || _ctx.Locals.GetLocal(receiver.Name.Lexeme) == null
            || _ctx.Runtime == null)
        {
            return false;
        }

        var candidates = TypedArrayHoistAnalyzer.AnalyzeFor(
            loop.Body, loop.Condition, loop.Increment, _ctx.TypeMap);
        if (!candidates.TryGetValue(receiver.Name.Lexeme, out var candidate)
            || candidate is not { ElementType: "Int32", CanHoistBacking: true })
        {
            return false;
        }

        _ctx.Locals.EnterScope();
        EmitStatement(loop.Initializer);
        bool typedArrayHoisted = EmitTypedArrayHoistPreamble(
            loop.Body, loop.Condition, loop.Increment);
        if (!typedArrayHoisted
            || !TryGetDirectTypedArrayBacking(receiver, "Int32", out var backing)
            || backing.Layout is not
                { BytesPerElement: 4, Signed: true, IsFloat: false })
        {
            throw new InvalidOperationException(
                "Exact Int32 fill proof did not produce its direct backing.");
        }

        var counter = _ctx.Locals.GetLocal(counterName)!;
        var boundDouble = IL.DeclareLocal(_ctx.Types.Double);
        var boundInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var indexInteger = IL.DeclareLocal(_ctx.Types.Int32);
        var nativeValue = IL.DeclareLocal(_ctx.Types.Int32);
        var slowStart = IL.DefineLabel();
        var fastStart = IL.DefineLabel();
        var fastContinue = IL.DefineLabel();
        var end = IL.DefineLabel();

        _ctx.EnterLoop(end, fastContinue);

        var loadedBound = _resolver.TryLoadVariable(boundVariable.Name.Lexeme);
        if (loadedBound == null)
            throw new InvalidOperationException("Unable to load Int32 fill bound.");
        SetStackType(loadedBound.Value);
        EnsureDouble();
        IL.Emit(OpCodes.Stloc, boundDouble);
        EmitExactIntegerGuard(boundDouble, 0d, 2_147_483_647d, slowStart);
        IL.Emit(OpCodes.Ldloc, boundDouble);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, boundInteger);

        IL.MarkLabel(fastStart);
        EmitCancellationCheck();
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldloc, boundInteger);
        IL.Emit(OpCodes.Bge, end);

        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, indexInteger);
        var inRange = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, indexInteger);
        IL.Emit(OpCodes.Ldloc, backing.LengthLocal);
        IL.Emit(OpCodes.Blt_Un, inRange);
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(typeof(IndexOutOfRangeException)));
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(inRange);

        EmitInt32CounterExpression(value, counterName, indexInteger);
        IL.Emit(OpCodes.Stloc, nativeValue);
        EmitDirectTypedArrayElementReference(backing, indexInteger);
        IL.Emit(OpCodes.Ldloc, nativeValue);
        IL.Emit(OpCodes.Unaligned, (byte)1);
        IL.Emit(OpCodes.Stind_I4);

        IL.MarkLabel(fastContinue);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, counter);
        IL.Emit(OpCodes.Br, fastStart);

        IL.MarkLabel(slowStart);
        EmitCancellationCheck();
        EmitConditionCheck(loop.Condition);
        IL.Emit(OpCodes.Brfalse, end);
        EmitStatement(loop.Body);
        EmitExpression(loop.Increment);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Br, slowStart);

        IL.MarkLabel(end);
        _ctx.ExitLoop();
        _ctx.HoistedTypedArrayCaches.Pop();
        _ctx.Locals.ExitScope();
        SetStackUnknown();
        return true;
    }

    private bool TryMatchInt32StencilAccumulator(
        Stmt body,
        string counterName,
        out string accumulatorName,
        out Expr.Variable receiver)
    {
        Stmt statement = body is Stmt.Block { Statements.Count: 1 } block
            ? block.Statements[0]
            : body;
        if (statement is Stmt.Expression
            {
                Expr: Expr.Assign
                {
                    Name: var accumulatorToken,
                    Value: Expr.Binary
                    {
                        Operator.Type: TokenType.PLUS,
                        Left: Expr.Variable accumulatorRead,
                        Right: var stencilExpression
                    }
                }
            }
            && accumulatorRead.Name.Lexeme == accumulatorToken.Lexeme
            && TryMatchExactInt32StencilShape(
                stencilExpression, counterName, out _, out receiver))
        {
            accumulatorName = accumulatorToken.Lexeme;
            return true;
        }

        accumulatorName = "";
        receiver = null!;
        return false;
    }

    private void EmitExactIntegerGuard(
        LocalBuilder value,
        double minimum,
        double maximum,
        Label fallback)
    {
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldc_R8, minimum);
        IL.Emit(OpCodes.Blt_Un, fallback);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldc_R8, maximum);
        IL.Emit(OpCodes.Bgt_Un, fallback);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(
            _ctx.Types.Math, nameof(Math.Truncate), _ctx.Types.Double));
        IL.Emit(OpCodes.Bne_Un, fallback);
    }

    private void EmitInt64AccumulatorStore(LocalBuilder target, LocalBuilder accumulator)
    {
        IL.Emit(OpCodes.Ldloc, accumulator);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Stloc, target);
    }

    private void EmitCancellationCheckWithInt64AccumulatorFlush(
        LocalBuilder target,
        LocalBuilder accumulator)
    {
        if (_ctx.Runtime?.BuildCancellationExceptionMethod == null
            || _ctx.Runtime.CancelRequestedField == null)
        {
            return;
        }

        var notCancelled = IL.DefineLabel();
        IL.Emit(OpCodes.Volatile);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.CancelRequestedField);
        IL.Emit(OpCodes.Brfalse, notCancelled);
        EmitInt64AccumulatorStore(target, accumulator);
        IL.Emit(OpCodes.Call, _ctx.Runtime.BuildCancellationExceptionMethod);
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(notCancelled);
    }

    private void EmitCountedPushReservation(Stmt.For loop)
    {
        if (!CountedPushLoopAnalyzer.TryAnalyze(loop, out var reservation))
            return;

        var arrayLocal = _ctx.Locals.GetLocal(reservation.Array.Name.Lexeme);
        if (arrayLocal is null)
            return;

        var listType = _ctx.Types.ListOfObject;
        var listLocal = IL.DeclareLocal(listType);
        var countLocal = IL.DeclareLocal(_ctx.Types.Double);
        var skipLabel = _ctx.ILBuilder.DefineLabel("counted_push_reserve_skip");

        IL.Emit(OpCodes.Ldloc, arrayLocal);
        if (arrayLocal.LocalType != listType)
            IL.Emit(OpCodes.Isinst, listType);
        IL.Emit(OpCodes.Stloc, listLocal);
        IL.Emit(OpCodes.Ldloc, listLocal);
        _ctx.ILBuilder.Emit_Brfalse(skipLabel);

        SetStackUnknown();
        EmitExpression(reservation.Bound);
        EnsureDouble();
        IL.Emit(OpCodes.Stloc, countLocal);

        // Bound eager allocation to one million elements. The unordered branch
        // form also rejects NaN; ceiling covers finite fractional upper bounds.
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Ldc_R8, 0.0);
        IL.Emit(OpCodes.Blt_Un, skipLabel);
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Ldc_R8, 1_000_000.0);
        IL.Emit(OpCodes.Bgt_Un, skipLabel);

        IL.Emit(OpCodes.Ldloc, listLocal);
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Call, typeof(Math).GetMethod("Ceiling", [_ctx.Types.Double])!);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
            listType,
            "EnsureCapacity",
            [_ctx.Types.Int32])!);
        IL.Emit(OpCodes.Pop);

        _ctx.ILBuilder.MarkLabel(skipLabel);
        SetStackUnknown();
    }

    private void EmitCountedNumericMapReservation(Stmt.For loop)
    {
        if (!CountedNumericMapSetLoopAnalyzer.TryAnalyze(loop, out var reservation)
            || _ctx.TryGetPromotedNumericMapLocal(reservation.Map.Name.Lexeme) is not { } mapLocal)
        {
            return;
        }

        var countLocal = IL.DeclareLocal(_ctx.Types.Double);
        var skipLabel = _ctx.ILBuilder.DefineLabel("counted_numeric_map_reserve_skip");

        SetStackUnknown();
        EmitExpression(reservation.Bound);
        EnsureDouble();
        IL.Emit(OpCodes.Stloc, countLocal);

        // Match the existing counted-array reservation boundary: reject NaN,
        // infinities, negatives, and bounds above one million rather than
        // changing allocation failure timing for unbounded guest input.
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Ldc_R8, 0.0);
        IL.Emit(OpCodes.Blt_Un, skipLabel);
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Ldc_R8, 1_000_000.0);
        IL.Emit(OpCodes.Bgt_Un, skipLabel);

        IL.Emit(OpCodes.Ldloc, mapLocal);
        IL.Emit(OpCodes.Ldloc, countLocal);
        IL.Emit(OpCodes.Call, typeof(Math).GetMethod("Ceiling", [_ctx.Types.Double])!);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
            _ctx.Types.DictionaryDoubleDouble,
            "EnsureCapacity",
            [_ctx.Types.Int32])!);
        IL.Emit(OpCodes.Pop);

        _ctx.ILBuilder.MarkLabel(skipLabel);
        SetStackUnknown();
    }

    protected override void EmitIf(Stmt.If i)
    {
        // Check for dead code elimination optimization
        var branchResult = _ctx.DeadCode?.GetIfResult(i) ?? IfBranchResult.BothReachable;

        switch (branchResult)
        {
            case IfBranchResult.OnlyThenReachable:
                // Condition is always true - emit only then branch
                EmitStatement(i.ThenBranch);
                return;

            case IfBranchResult.OnlyElseReachable:
                // Condition is always false - emit only else branch (or nothing)
                if (i.ElseBranch != null)
                {
                    EmitStatement(i.ElseBranch);
                }
                return;
        }

        // BothReachable: emit both branches with condition check
        var builder = _ctx.ILBuilder;
        var elseLabel = builder.DefineLabel("if_else");
        var endLabel = builder.DefineLabel("if_end");

        EmitConditionCheck(i.Condition);
        builder.Emit_Brfalse(elseLabel);

        EmitStatement(i.ThenBranch);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
        {
            EmitStatement(i.ElseBranch);
        }

        builder.MarkLabel(endLabel);
    }

    // EmitCancellationCheck is inherited from StatementEmitterBase so async/
    // generator state machines also poll the cancellation flag at loop heads
    // (their inherited base loop emitters call it). See issue #74.

    protected override void EmitWhile(Stmt.While w)
    {
        // Array hoist preamble before loop
        var hoisted = EmitArrayHoistPreamble(w.Body, w.Condition, increment: null);
        // Typed-array receiver hoist (#928): cast loop-invariant numeric TypedArray receivers once.
        var taHoisted = EmitTypedArrayHoistPreamble(w.Body, w.Condition, increment: null);

        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("while_start");
        var endLabel = builder.DefineLabel("while_end");

        _ctx.EnterLoop(endLabel, startLabel);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();
        EmitConditionCheck(w.Condition);
        builder.Emit_Brfalse(endLabel);

        EmitStatement(w.Body);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.ExitLoop();

        if (hoisted) _ctx.HoistedArrayCaches.Pop();
        if (taHoisted) _ctx.HoistedTypedArrayCaches.Pop();
    }

    /// <summary>
    /// Emits isinst preamble for loop-invariant array variables.
    /// Returns true if any arrays were hoisted (caller must pop the cache).
    /// </summary>
    private bool EmitArrayHoistPreamble(Stmt body, Expr? condition, Expr? increment)
    {
        var candidates = ArrayHoistAnalyzer.AnalyzeFor(body, condition, increment, _ctx.TypeMap);
        if (candidates.Count == 0) return false;

        // Exclude variables already hoisted by an outer loop, and promoted typed-array
        // locals (#857/#860) — their slot is already a concrete List<T>, so the index
        // fast paths read it directly; hoisting would only emit a dead isinst + local.
        foreach (var name in candidates.Keys.ToList())
        {
            if (_ctx.TryGetHoistedArray(name) != null || _ctx.TryGetPromotedArrayLocal(name) != null)
                candidates.Remove(name);
        }
        if (candidates.Count == 0) return false;

        var cache = new Dictionary<string, HoistedArrayEntry>();

        foreach (var (varName, desc) in candidates)
        {
            // Double-kind candidates that reach the hoist are escaping number[] whose runtime value is a
            // $Array (numeric or boxed) — never a bare List<double> (promoted List<double> locals were
            // filtered out above). Hoist the $Array cast so the loop-body index get/set route through the
            // mode-checked Get(long)/SetDouble fast paths (straight into the unboxed double[] store)
            // instead of isinst-ing List<double> → null → boxed SetIndex per write, which deopts the array
            // to boxed and reintroduces the per-element boxing this project removed (#927 step 1). Bool/
            // Object kinds keep their List<T> hoist ($Array : List<object?> covers Object directly).
            var hoistType = desc.Kind == ArrayElementsKind.Double
                ? _ctx.Runtime!.TSArrayType
                : desc.GetListType(_ctx.Types);
            var typedLocal = IL.DeclareLocal(hoistType);

            // Load array variable, isinst to the hoist type, store in local
            // If the variable holds a different type, typedLocal will be null
            // Use the local directly to avoid stack type tracking complications
            var arrLocal = _ctx.Locals.GetLocal(varName);
            if (arrLocal == null) continue; // Variable not found in locals — skip
            IL.Emit(OpCodes.Ldloc, arrLocal);
            // Array locals are always typed as object — no boxing needed
            IL.Emit(OpCodes.Isinst, hoistType);
            IL.Emit(OpCodes.Stloc, typedLocal);

            cache[varName] = new HoistedArrayEntry(typedLocal, desc);
        }

        _ctx.HoistedArrayCaches.Push(cache);
        return true;
    }

    /// <summary>
    /// Typed-array receiver hoist (#928): for each loop-invariant variable statically typed as a
    /// numeric TypedArray and used as an index receiver, cast it to its concrete <c>$XArray</c> type
    /// ONCE before the loop into a typed local. The element index fast paths then load that local
    /// instead of re-emitting <c>ldloc; castclass $XArray</c> on every access — which, combined with
    /// the native-int counter, is what closes the typed-array kernel gap. Returns true if anything
    /// was hoisted (caller must pop the cache). Gated on the same flag as the int-counter prototype
    /// so the two ship together.
    /// </summary>
    private bool EmitTypedArrayHoistPreamble(Stmt body, Expr? condition, Expr? increment)
    {
        if (!ForLoopAnalyzer.IntegerCounterEnabled || _ctx.Runtime == null) return false;

        var candidates = TypedArrayHoistAnalyzer.AnalyzeFor(body, condition, increment, _ctx.TypeMap);
        if (candidates.Count == 0) return false;

        Dictionary<string, HoistedTypedArrayEntry>? cache = null;
        foreach (var (varName, candidate) in candidates)
        {
            string elementType = candidate.ElementType;
            // Skip variables already hoisted by an outer loop.
            if (_ctx.TryGetHoistedTypedArray(varName) != null) continue;
            // Only numeric typed arrays with unboxed accessors take the fast path (BigInt /
            // Uint8Clamped fall through to boxed GetIndex, so a hoisted local would be dead).
            if (_ctx.Runtime.GetTypedArrayType(elementType) is not { } xArrayType) continue;
            if (!_ctx.Runtime.TypedArrayGetUnboxedByElement.ContainsKey(elementType)) continue;

            var arrLocal = _ctx.Locals.GetLocal(varName);
            if (arrLocal == null) continue; // captured / not a plain local — leave to the per-access path

            // castclass (not isinst) mirrors the per-access fast path exactly: the static type is
            // TypedArray, so a correctly-typed value never throws; null casts to null as before.
            var typedLocal = IL.DeclareLocal(xArrayType);
            IL.Emit(OpCodes.Ldloc, arrLocal);
            IL.Emit(OpCodes.Castclass, xArrayType);
            IL.Emit(OpCodes.Stloc, typedLocal);

            HoistedTypedArrayBacking? backing = null;
            if (candidate.CanHoistBacking
                && TypedArrayElementLayout.TryGet(elementType, out var layout))
            {
                // The whole-program proof guarantees a fresh length-constructed array whose
                // binding and backing identity never escape. Capture all storage facts before the
                // loop so the hot body need not reload fields through GetUnboxed/SetUnboxed.
                var bufferLocal = IL.DeclareLocal(typeof(byte[]));
                IL.Emit(OpCodes.Ldloc, typedLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime.TypedArrayGetBuffer);
                IL.Emit(OpCodes.Stloc, bufferLocal);

                var byteOffsetLocal = IL.DeclareLocal(_ctx.Types.Int32);
                IL.Emit(OpCodes.Ldloc, typedLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime.TypedArrayByteOffsetGetter);
                IL.Emit(OpCodes.Stloc, byteOffsetLocal);

                var lengthLocal = IL.DeclareLocal(_ctx.Types.Int32);
                IL.Emit(OpCodes.Ldloc, typedLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime.TypedArrayLengthGetter);
                IL.Emit(OpCodes.Stloc, lengthLocal);

                backing = new HoistedTypedArrayBacking(
                    bufferLocal, byteOffsetLocal, lengthLocal, layout);
            }

            cache ??= new Dictionary<string, HoistedTypedArrayEntry>();
            cache[varName] = new HoistedTypedArrayEntry(
                typedLocal, xArrayType, elementType, backing);
        }

        if (cache == null) return false;
        _ctx.HoistedTypedArrayCaches.Push(cache);
        return true;
    }

    protected override void EmitDoWhile(Stmt.DoWhile dw)
    {
        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("dowhile_start");
        var endLabel = builder.DefineLabel("dowhile_end");
        var continueLabel = builder.DefineLabel("dowhile_continue");

        _ctx.EnterLoop(endLabel, continueLabel);

        // Body executes at least once
        builder.MarkLabel(startLabel);
        EmitCancellationCheck();
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        builder.MarkLabel(continueLabel);

        // Evaluate condition
        EmitConditionCheck(dw.Condition);
        builder.Emit_Brtrue(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    protected override void EmitForOf(Stmt.ForOf f)
    {
        // A for-of emits several alternative runtime paths (iterator protocol, index-based, …),
        // each registering its own loop scope. Capture any labeled-loop names once up front and
        // hand them to every path, so `continue`/`break <label>` resolve no matter which path runs
        // at runtime (#558 — consuming them in only the first-emitted path left the others bare).
        var labelNames = _ctx.TakePendingLoopLabels();
        _ctx.Locals.EnterScope();
        var builder = _ctx.ILBuilder;

        // Evaluate iterable
        TypeInfo? iterableType = _ctx.TypeMap?.Get(f.Iterable);
        EmitExpression(f.Iterable);

        if (_ctx.TypeMap?.TryGetStableCustomIterator(f, out var stableIterator) == true)
        {
            EmitBoxIfNeeded(f.Iterable);
            var stableIterable = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, stableIterable);
            EmitStableCustomIterator(f, stableIterable, stableIterator, labelNames);
            return;
        }

        if (iterableType is TypeInfo.Map
            && _ctx.TypeMap?.IsStableNumericMapIteration(f) == true)
        {
            // The analyzer proved that the receiver is a fresh, non-escaping
            // Map<number, number> and that the entry binding is observed only
            // through literal [0]/[1] reads.
            EmitBoxIfNeeded(f.Iterable);
            var stableMapIterable = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, stableMapIterable);
            EmitStableNumericMapIteration(f, stableMapIterable, labelNames);
            return;
        }

        // For Map/Set, convert to a List first
        if (iterableType is TypeInfo.Map)
        {
            // Map iteration yields [key, value] entries
            IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
        }
        else if (iterableType is TypeInfo.Set)
        {
            // Set iteration yields values
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
        }

        if (iterableType is TypeInfo.Map or TypeInfo.Set)
        {
            var collectionStartLabel = builder.DefineLabel("forof_collection_start");
            var collectionEndLabel = builder.DefineLabel("forof_collection_end");
            var collectionContinueLabel = builder.DefineLabel("forof_collection_continue");
            _ctx.EnterLoop(collectionEndLabel, collectionContinueLabel, labelNames);
            EmitForOfNormalizedEnumerator(
                f, collectionStartLabel, collectionEndLabel, collectionContinueLabel);
            return;
        }

        // For generators, use IEnumerable-based iteration
        if (iterableType is TypeInfo.Generator generatorType)
        {
            var genStartLabel = builder.DefineLabel("forof_gen_start");
            var genEndLabel = builder.DefineLabel("forof_gen_end");
            var genContinueLabel = builder.DefineLabel("forof_gen_continue");
            _ctx.EnterLoop(genEndLabel, genContinueLabel, labelNames);
            if (IsStableNativeNumberGeneratorCall(f.Iterable, generatorType))
            {
                EmitForOfNativeNumberGenerator(
                    f, genStartLabel, genEndLabel, genContinueLabel);
            }
            else
            {
                EmitForOfEnumerator(f, genStartLabel, genEndLabel, genContinueLabel);
            }
            return;
        }

        // For iterators, normalize to IEnumerator then iterate
        if (iterableType is TypeInfo.Iterator)
        {
            var iterStartLabel = builder.DefineLabel("forof_iter_start");
            var iterEndLabel = builder.DefineLabel("forof_iter_end");
            var iterContinueLabel = builder.DefineLabel("forof_iter_continue");
            _ctx.EnterLoop(iterEndLabel, iterContinueLabel, labelNames);
            EmitForOfNormalizedEnumerator(f, iterStartLabel, iterEndLabel, iterContinueLabel);
            return;
        }

        // Store the iterable for potential iterator protocol check
        var iterableLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, iterableLocal);
        var afterLoopLabel = builder.DefineLabel("forof_after");
        var arrayDesc = ArrayElements.Resolve(iterableType);

        // JavaScript-style `var map = new Map()` can remain `any` in the
        // type map. Select the collection iterator dynamically before the
        // generic protocol/index paths so live deletion semantics still apply.
        // A statically known array takes the direct fast path below; emitting
        // dynamic collection branches before that compile-time early return
        // would leave their shared after-loop target unmarked.
        if (arrayDesc == null && _ctx.RuntimeFeatures?.UsesMap == true)
        {
            var notDynamicMap = builder.DefineLabel("forof_not_dynamic_map");
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryObjectObject);
            builder.Emit_Brfalse(notDynamicMap);
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
            var start = builder.DefineLabel("forof_dynamic_map_start");
            var end = builder.DefineLabel("forof_dynamic_map_end");
            var cont = builder.DefineLabel("forof_dynamic_map_continue");
            _ctx.EnterLoop(end, cont, labelNames);
            EmitForOfNormalizedEnumerator(f, start, end, cont);
            builder.Emit_Br(afterLoopLabel);
            _ctx.Locals.EnterScope();
            builder.MarkLabel(notDynamicMap);
        }
        if (arrayDesc == null && _ctx.RuntimeFeatures?.UsesSet == true)
        {
            var notDynamicSet = builder.DefineLabel("forof_not_dynamic_set");
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Types.HashSetOfObject);
            builder.Emit_Brfalse(notDynamicSet);
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
            var start = builder.DefineLabel("forof_dynamic_set_start");
            var end = builder.DefineLabel("forof_dynamic_set_end");
            var cont = builder.DefineLabel("forof_dynamic_set_continue");
            _ctx.EnterLoop(end, cont, labelNames);
            EmitForOfNormalizedEnumerator(f, start, end, cont);
            builder.Emit_Br(afterLoopLabel);
            _ctx.Locals.EnterScope();
            builder.MarkLabel(notDynamicSet);
        }

        // Phase C: when the iterable's static type is `T[]`, skip the
        // iterator-protocol probe and the per-iter GetLength/GetElement
        // dispatch. Direct `Callvirt list.Count + Callvirt list[i]` loop
        // for the Object-kind common case (any[], string[], etc.) plus
        // $Array wrappers. Typed kinds (number[], boolean[]) currently
        // run through the existing slow path — their runtime
        // representation can be a typed list OR a List<object> depending
        // on construction site, and the slow path already handles both.
        if (arrayDesc != null && arrayDesc.Kind == ArrayElementsKind.Object)
        {
            EmitForOfArrayDirect(f, iterableLocal, arrayDesc, labelNames);
            _ctx.Locals.ExitScope();
            return;
        }

        // Try iterator protocol first: GetIteratorFunction(iterable, Symbol.iterator)
        var iteratorFnLocal = IL.DeclareLocal(_ctx.Types.Object);
        var indexBasedLabel = builder.DefineLabel("forof_index_based");

        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIteratorFunction);
        IL.Emit(OpCodes.Stloc, iteratorFnLocal);

        // If the iterator property is absent, fall back to index-based iteration.
        // Explicit null remains present and fails as non-callable below.
        IL.Emit(OpCodes.Ldloc, iteratorFnLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
        builder.Emit_Brtrue(indexBasedLabel);

        // ===== Iterator protocol path =====
        {
            var iterStartLabel = builder.DefineLabel("forof_iter_start");
            var iterEndLabel = builder.DefineLabel("forof_iter_end");
            var iterContinueLabel = builder.DefineLabel("forof_iter_continue");
            _ctx.EnterLoop(iterEndLabel, iterContinueLabel, labelNames);

            // Call the iterator function to get the iterator object
            // Use InvokeMethodValue to properly bind 'this' to the iterable object
            IL.Emit(OpCodes.Ldloc, iterableLocal);       // receiver (this)
            IL.Emit(OpCodes.Ldloc, iteratorFnLocal);     // method
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);  // args
            IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

            // Store the iterator object
            var iteratorObjLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, iteratorObjLocal);

            // Loop variable
            var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);
            var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
            var closeNeeded = IL.DeclareLocal(_ctx.Types.Boolean);
            var throwing = IL.DeclareLocal(_ctx.Types.Boolean);

            builder.MarkLabel(iterStartLabel);
            EmitCancellationCheck();

            // IteratorStep: errors from next()/done occur before the iteration's
            // close region and therefore do not trigger IteratorClose.
            IL.Emit(OpCodes.Ldloc, iteratorObjLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeIteratorNext);
            IL.Emit(OpCodes.Stloc, resultLocal);
            IL.Emit(OpCodes.Ldloc, resultLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIteratorDone);
            var bodyLabel = builder.DefineLabel("forof_iter_body");
            builder.Emit_Brtrue(iterEndLabel);
            builder.Emit_Br(bodyLabel);

            // done=true is natural exhaustion and must not close the iterator.
            builder.MarkLabel(iterEndLabel);
            builder.Emit_Br(afterLoopLabel);

            builder.MarkLabel(bodyLabel);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Stloc, closeNeeded);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, throwing);

            var escapingTargets = new HashSet<Label>();
            foreach (var loop in _ctx.LoopLabels)
            {
                escapingTargets.Add(loop.BreakLabel);
                escapingTargets.Add(loop.ContinueLabel);
            }

            _ctx.ExceptionBlockDepth++;
            builder.BeginExceptionBlock();

            // IteratorValue and binding/body evaluation are protected: every
            // abrupt exit closes, while a continue of this loop clears the flag.
            IL.Emit(OpCodes.Ldloc, resultLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIteratorValue);
            IL.Emit(OpCodes.Stloc, loopVar);

            _iteratorLoopCompletionScopes.Push(new IteratorLoopCompletionScope(
                closeNeeded, iterContinueLabel, escapingTargets));
            EmitStatement(f.Body);
            _iteratorLoopCompletionScopes.Pop();

            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, closeNeeded);
            builder.Emit_Leave(iterContinueLabel);

            builder.BeginCatchBlock(_ctx.Types.Exception);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Stloc, throwing);
            IL.Emit(OpCodes.Rethrow);

            builder.BeginFinallyBlock();
            var skipClose = builder.DefineLabel("forof_skip_close");
            IL.Emit(OpCodes.Ldloc, closeNeeded);
            builder.Emit_Brfalse(skipClose);
            IL.Emit(OpCodes.Ldloc, iteratorObjLocal);
            IL.Emit(OpCodes.Ldloc, throwing);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.IteratorClose);
            builder.MarkLabel(skipClose);
            builder.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;

            builder.MarkLabel(iterContinueLabel);
            builder.Emit_Br(iterStartLabel);

            // iterEndLabel was emitted above so natural completion can bypass close.
            _ctx.ExitLoop();
        }

        // ===== Index-based fallback (for arrays, strings, etc.) =====
        builder.MarkLabel(indexBasedLabel);
        {
            // Normalize iterable to List<object> via IterateToList so IEnumerable types
            // (e.g. Intl.Segments) are properly materialized before index-based iteration
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
            IL.Emit(OpCodes.Call, _ctx.Runtime!.IterateToList);
            IL.Emit(OpCodes.Stloc, iterableLocal);

            var startLabel = builder.DefineLabel("forof_idx_start");
            var endLabel = builder.DefineLabel("forof_idx_end");
            var continueLabel = builder.DefineLabel("forof_idx_continue");
            _ctx.EnterLoop(endLabel, continueLabel, labelNames);

            // Create index variable
            var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, indexLocal);

            // Loop variable
            var indexLoopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

            builder.MarkLabel(startLabel);
            EmitCancellationCheck();

            // Check if index < length
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
            IL.Emit(OpCodes.Clt);
            builder.Emit_Brfalse(endLabel);

            // Get current element
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
            IL.Emit(OpCodes.Stloc, indexLoopVar);

            // Emit body
            EmitStatement(f.Body);

            builder.MarkLabel(continueLabel);

            // Increment index
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Stloc, indexLocal);

            builder.Emit_Br(startLabel);

            builder.MarkLabel(endLabel);
            _ctx.ExitLoop();
        }

        // Common exit point for both paths
        builder.MarkLabel(afterLoopLabel);
        _ctx.Locals.ExitScope();
    }

    /// <summary>
    /// Direct backing-dictionary loop for the non-escaping numeric Map shape
    /// marked by <see cref="StableMapIterationAnalyzer"/>. Key/value objects are
    /// held in locals and exposed to literal entry-index reads by
    /// <c>TryEmitStableMapEntryIndex</c>; no JavaScript entry array is created.
    /// </summary>
    private void EmitStableNumericMapIteration(
        Stmt.ForOf f,
        LocalBuilder iterableLocal,
        IReadOnlyList<string>? labelNames)
    {
        var builder = _ctx.ILBuilder;
        var dictType = _ctx.Types.DictionaryObjectObject;
        var kvpType = EmitGenerics.MakeGenericType(
            _ctx.Types.KeyValuePairOpen, _ctx.Types.Object, _ctx.Types.Object);
        var enumeratorType = EmitGenerics.MakeGenericType(
            typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(),
            _ctx.Types.Object, _ctx.Types.Object);

        var startLabel = builder.DefineLabel("forof_stable_map_start");
        var endLabel = builder.DefineLabel("forof_stable_map_end");
        var continueLabel = builder.DefineLabel("forof_stable_map_continue");

        var dictLocal = IL.DeclareLocal(dictType);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Castclass, dictType);
        IL.Emit(OpCodes.Stloc, dictLocal);

        var enumeratorLocal = IL.DeclareLocal(enumeratorType);
        var currentLocal = IL.DeclareLocal(kvpType);
        var keyLocal = IL.DeclareLocal(_ctx.Types.Double);
        var valueLocal = IL.DeclareLocal(_ctx.Types.Double);

        IL.Emit(OpCodes.Ldloc, dictLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(dictType, "GetEnumerator")!);
        IL.Emit(OpCodes.Stloc, enumeratorLocal);

        _ctx.EnterLoop(endLabel, continueLabel, labelNames ?? CompilationContext.NoLabels);
        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        IL.Emit(OpCodes.Ldloca, enumeratorLocal);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(enumeratorType, "MoveNext")!);
        builder.Emit_Brfalse(endLabel);

        IL.Emit(OpCodes.Ldloca, enumeratorLocal);
        IL.Emit(OpCodes.Call, _ctx.Types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        IL.Emit(OpCodes.Stloc, currentLocal);

        IL.Emit(OpCodes.Ldloca, currentLocal);
        IL.Emit(OpCodes.Call, _ctx.Types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
        IL.Emit(OpCodes.Stloc, keyLocal);

        IL.Emit(OpCodes.Ldloca, currentLocal);
        IL.Emit(OpCodes.Call, _ctx.Types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
        IL.Emit(OpCodes.Stloc, valueLocal);

        _stableMapEntryBindings.Push((f.Variable.Lexeme, keyLocal, valueLocal));
        try
        {
            EmitStatement(f.Body);
        }
        finally
        {
            _stableMapEntryBindings.Pop();
        }

        builder.MarkLabel(continueLabel);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        IL.Emit(OpCodes.Ldloca, enumeratorLocal);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(enumeratorType, "Dispose")!);
        _ctx.ExitLoop();
        _ctx.Locals.ExitScope();
    }

    private void EmitStableCustomIterator(
        Stmt.ForOf loop,
        LocalBuilder iterableLocal,
        StableCustomIteratorInfo info,
        IReadOnlyList<string>? labelNames)
    {
        _ctx.ArrowMethods.TryGetValue(info.NextMethod, out var nextMethod);
        Type resultType = _ctx.Runtime!.StableNumberIteratorResultType;
        var valueField = _ctx.Runtime.StableNumberIteratorResultValueField;
        var doneField = _ctx.Runtime.StableNumberIteratorResultDoneField;
        if (nextMethod is null ||
            nextMethod.ReturnType != resultType)
        {
            throw new InvalidOperationException(
                $"Stable custom iterator proof did not match emitted result shape " +
                $"(method={nextMethod?.ReturnType}, result={resultType}, " +
                $"value={valueField?.FieldType}, done={doneField?.FieldType}).");
        }

        var builder = _ctx.ILBuilder;

        // GetIteratorFromMethod, once: preserve observable iterator acquisition and this binding.
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.SymbolIterator);
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorFunction);
        var iteratorFunction = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, iteratorFunction);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldloc, iteratorFunction);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);
        var iteratorObject = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, iteratorObject);

        // Get(iterator, "next"), once. The analyzer proved this exact data method cannot
        // be reassigned or observed through an alias, so subsequent steps call it directly.
        IL.Emit(OpCodes.Ldloc, iteratorObject);
        IL.Emit(OpCodes.Ldstr, "next");
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime.TSFunctionType);
        var nextFunction = IL.DeclareLocal(_ctx.Runtime.TSFunctionType);
        IL.Emit(OpCodes.Stloc, nextFunction);

        LocalBuilder? nextTarget = null;
        bool capturing = _ctx.DisplayClasses.TryGetValue(
            info.NextMethod, out var displayClass);
        if (capturing)
        {
            nextTarget = IL.DeclareLocal(displayClass!);
            IL.Emit(OpCodes.Ldloc, nextFunction);
            IL.Emit(OpCodes.Callvirt, _ctx.Runtime.TSFunctionGetTarget);
            IL.Emit(OpCodes.Castclass, displayClass!);
            IL.Emit(OpCodes.Stloc, nextTarget);
        }

        var start = builder.DefineLabel("forof_stable_iterator_start");
        var end = builder.DefineLabel("forof_stable_iterator_end");
        var body = builder.DefineLabel("forof_stable_iterator_body");
        var cont = builder.DefineLabel("forof_stable_iterator_continue");
        var result = IL.DeclareLocal(resultType);
        var loopVar = _ctx.Locals.DeclareLocal(
            loop.Variable.Lexeme, _ctx.Types.Double);
        var closeNeeded = IL.DeclareLocal(_ctx.Types.Boolean);
        var throwing = IL.DeclareLocal(_ctx.Types.Boolean);

        _ctx.EnterLoop(end, cont, labelNames ?? CompilationContext.NoLabels);
        builder.MarkLabel(start);
        EmitCancellationCheck();

        if (nextTarget is not null)
            IL.Emit(OpCodes.Ldloc, nextTarget);
        if (info.NextMethod.HasOwnThis)
            IL.Emit(OpCodes.Ldloc, iteratorObject);
        IL.Emit(capturing ? OpCodes.Callvirt : OpCodes.Call, nextMethod);
        IL.Emit(OpCodes.Stloc, result);
        IL.Emit(OpCodes.Ldloc, result);
        IL.Emit(OpCodes.Ldfld, doneField);
        builder.Emit_Brfalse(body);
        builder.Emit_Br(end);

        builder.MarkLabel(body);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, throwing);

        var escapingTargets = new HashSet<Label>();
        foreach (var enclosing in _ctx.LoopLabels)
        {
            escapingTargets.Add(enclosing.BreakLabel);
            escapingTargets.Add(enclosing.ContinueLabel);
        }

        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();
        IL.Emit(OpCodes.Ldloc, result);
        IL.Emit(OpCodes.Ldfld, valueField);
        IL.Emit(OpCodes.Stloc, loopVar);

        _iteratorLoopCompletionScopes.Push(new IteratorLoopCompletionScope(
            closeNeeded, cont, escapingTargets));
        EmitStatement(loop.Body);
        _iteratorLoopCompletionScopes.Pop();

        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        builder.Emit_Leave(cont);

        builder.BeginCatchBlock(_ctx.Types.Exception);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, throwing);
        IL.Emit(OpCodes.Rethrow);

        builder.BeginFinallyBlock();
        var skipClose = builder.DefineLabel("forof_stable_iterator_skip_close");
        IL.Emit(OpCodes.Ldloc, closeNeeded);
        builder.Emit_Brfalse(skipClose);
        IL.Emit(OpCodes.Ldloc, iteratorObject);
        IL.Emit(OpCodes.Ldloc, throwing);
        IL.Emit(OpCodes.Call, _ctx.Runtime.IteratorClose);
        builder.MarkLabel(skipClose);
        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;

        builder.MarkLabel(cont);
        builder.Emit_Br(start);
        builder.MarkLabel(end);
        _ctx.ExitLoop();
        _ctx.Locals.ExitScope();
    }

    /// <summary>
    /// Phase C fast path for <c>for (const x of arr)</c> when <c>arr</c> is
    /// statically typed as <c>T[]</c>. Emits a direct list-indexed loop
    /// that bypasses the iterator-protocol probe + per-iter
    /// GetLength/GetElement runtime dispatch (each of which does an
    /// isinst chain). Always emits IL — runtime mismatch (e.g. a
    /// <c>$Array</c> wrapper instead of a bare list) is handled in-line
    /// for Object kind, or routed to a fallback iterator-helper for
    /// typed kinds.
    /// </summary>
    private void EmitForOfArrayDirect(Stmt.ForOf f, LocalBuilder iterableLocal, ArrayElementsDescriptor desc, IReadOnlyList<string>? labelNames = null)
    {
        var builder = _ctx.ILBuilder;
        var listType = desc.GetListType(_ctx.Types);
        var listCountGetter = _ctx.Types.GetProperty(listType, "Count").GetGetMethod()!;
        var listIndexerGetter = _ctx.Types.GetProperty(listType, "Item").GetGetMethod()!;

        var startLabel = builder.DefineLabel("forof_arr_start");
        var endLabel = builder.DefineLabel("forof_arr_end");
        var continueLabel = builder.DefineLabel("forof_arr_continue");
        var loopHeadLabel = builder.DefineLabel("forof_arr_loop_head");
        var fallbackLabel = builder.DefineLabel("forof_arr_fallback");

        // Resolve iterable → List<T>. For Object kind, also accept $Array
        // (unwrapped via .Elements). For typed kinds (Double/Bool),
        // only the bare list shape is supported; non-matches go through
        // the runtime-helper fallback.
        var listLocal = IL.DeclareLocal(listType);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Isinst, listType);
        IL.Emit(OpCodes.Stloc, listLocal);
        IL.Emit(OpCodes.Ldloc, listLocal);
        IL.Emit(OpCodes.Brtrue, loopHeadLabel);

        if (desc.Kind == ArrayElementsKind.Object)
        {
            // $Array wrapper → .Elements
            var notTSArrayLabel = builder.DefineLabel("forof_arr_not_tsarr");
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.TSArrayType);
            IL.Emit(OpCodes.Brfalse, notTSArrayLabel);
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
            IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayElementsGetter);
            IL.Emit(OpCodes.Stloc, listLocal);
            IL.Emit(OpCodes.Br, loopHeadLabel);

            builder.MarkLabel(notTSArrayLabel);
            // Last resort: route through IterateToList to materialize.
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
            IL.Emit(OpCodes.Call, _ctx.Runtime!.IterateToList);
            IL.Emit(OpCodes.Stloc, listLocal);
        }
        else
        {
            // Typed kind. The list could be elsewhere wrapped ($Array stores
            // List<object> only, so an `arr: number[]` declared then mutated
            // through a generic path could wind up as List<object>). Skip
            // the fast path in that case by routing through the runtime
            // helper, which will rebox to List<object> — slow but correct.
            builder.MarkLabel(fallbackLabel);
            // Materialize via IterateToList → returns List<object>. Since
            // listLocal is List<T> typed, we can't store there directly.
            // For typed kinds, just bail: emit the existing index-based
            // path inline using the iterable local. Simplest: route to a
            // GetElement-based loop using listLocal=null marker.
            //
            // For the common case (benchmark), the bare-list path above
            // hits, so this branch is cold. Implement only when needed.
            IL.Emit(OpCodes.Ldstr, "for-of: typed-array fast path expected List<T> at runtime");
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(typeof(InvalidOperationException), _ctx.Types.String));
            IL.Emit(OpCodes.Throw);
        }

        // Loop entry: listLocal holds the list.
        builder.MarkLabel(loopHeadLabel);

        _ctx.EnterLoop(endLabel, continueLabel, labelNames ?? CompilationContext.NoLabels);

        // var i = 0
        var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable. Element type comes from the descriptor; for the
        // benchmark's `any[]` case it's _types.Object so the loop var
        // matches the existing slow path's binding.
        var elementType = desc.GetElementType(_ctx.Types);
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, elementType);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        // if (i >= list.Count) goto end
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, listLocal);
        IL.Emit(OpCodes.Callvirt, listCountGetter);
        IL.Emit(OpCodes.Bge, endLabel);

        // loopVar = list[i]; for Object kind, unhole $ArrayHole → $Undefined
        IL.Emit(OpCodes.Ldloc, listLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Callvirt, listIndexerGetter);
        if (desc.Kind == ArrayElementsKind.Object)
        {
            // if (top is $ArrayHole) → $Undefined
            var notHoleLabel = builder.DefineLabel("forof_arr_not_hole");
            var unholedLabel = builder.DefineLabel("forof_arr_unholed");
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.ArrayHoleType);
            IL.Emit(OpCodes.Brfalse, notHoleLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            IL.Emit(OpCodes.Br, unholedLabel);
            builder.MarkLabel(notHoleLabel);
            builder.MarkLabel(unholedLabel);
        }
        IL.Emit(OpCodes.Stloc, loopVar);

        // Body
        EmitStatement(f.Body);

        builder.MarkLabel(continueLabel);

        // i++
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);
        IL.Emit(OpCodes.Br, startLabel);

        builder.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    private void EmitForOfEnumerator(Stmt.ForOf f, Label startLabel, Label endLabel, Label continueLabel)
    {
        var builder = _ctx.ILBuilder;

        // Stack has the emitted generator. Drive it through the same iterator-result
        // protocol as a custom iterator so abrupt loop completion is routed through
        // the shared IteratorClose primitive (including generator.return()).
        var generatorLocal = IL.DeclareLocal(_ctx.Runtime!.GeneratorInterfaceType);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime.GeneratorInterfaceType);
        IL.Emit(OpCodes.Stloc, generatorLocal);

        // Loop variable
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        var closeNeeded = IL.DeclareLocal(_ctx.Types.Boolean);
        var throwing = IL.DeclareLocal(_ctx.Types.Boolean);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        IL.Emit(OpCodes.Ldloc, generatorLocal);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime.GeneratorNextMethod);
        IL.Emit(OpCodes.Stloc, resultLocal);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
        builder.Emit_Brtrue(endLabel);

        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, throwing);

        var escapingTargets = new HashSet<Label>();
        foreach (var loop in _ctx.LoopLabels)
        {
            escapingTargets.Add(loop.BreakLabel);
            escapingTargets.Add(loop.ContinueLabel);
        }

        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();

        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
        IL.Emit(OpCodes.Stloc, loopVar);

        _iteratorLoopCompletionScopes.Push(new IteratorLoopCompletionScope(
            closeNeeded, continueLabel, escapingTargets));
        EmitStatement(f.Body);
        _iteratorLoopCompletionScopes.Pop();

        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        builder.Emit_Leave(continueLabel);

        builder.BeginCatchBlock(_ctx.Types.Exception);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, throwing);
        IL.Emit(OpCodes.Rethrow);

        builder.BeginFinallyBlock();
        var skipClose = builder.DefineLabel("forof_gen_skip_close");
        IL.Emit(OpCodes.Ldloc, closeNeeded);
        builder.Emit_Brfalse(skipClose);
        IL.Emit(OpCodes.Ldloc, generatorLocal);
        IL.Emit(OpCodes.Ldloc, throwing);
        IL.Emit(OpCodes.Call, _ctx.Runtime.IteratorClose);
        builder.MarkLabel(skipClose);
        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;

        builder.MarkLabel(continueLabel);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    private bool IsStableNativeNumberGeneratorCall(
        Expr iterable,
        TypeInfo.Generator generatorType)
    {
        if (generatorType.YieldType is not
            (TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            or TypeInfo.NumberLiteral))
        {
            return false;
        }

        while (true)
        {
            switch (iterable)
            {
                case Expr.Grouping grouping:
                    iterable = grouping.Expression;
                    continue;
                case Expr.TypeAssertion assertion:
                    iterable = assertion.Expression;
                    continue;
                case Expr.Satisfies satisfies:
                    iterable = satisfies.Expression;
                    continue;
                case Expr.NonNullAssertion nonNull:
                    iterable = nonNull.Expression;
                    continue;
            }
            break;
        }

        if (iterable is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Variable callee
            } call
            || call.Arguments.Any(argument => argument is Expr.Spread))
        {
            return false;
        }

        string name = callee.Name.Lexeme;
        string resolvedName = _ctx.ResolveFunctionName(name);
        if (_ctx.StableNativeNumberGeneratorFunctions?.Contains(resolvedName) != true
            || !_ctx.Functions.ContainsKey(resolvedName)
            || _ctx.TopLevelStaticVars?.ContainsKey(name) == true)
        {
            return false;
        }

        // Match the ordinary direct-call resolver's conservative shadowing
        // boundary. The optimized loop is valid only when evaluating the call
        // above necessarily created the exact emitted state machine.
        return !_ctx.TryGetParameter(name, out _)
            && !_ctx.CellBindingLocals.ContainsKey(name)
            && !_ctx.Locals.HasLocal(name)
            && _ctx.CapturedFunctionLocals?.Contains(name) != true
            && _ctx.CapturedArrowLocals?.Contains(name) != true
            && _ctx.ParentArrowCapturedLocals?.Contains(name) != true
            && _ctx.ExtraArrowScopeBindings?.ContainsKey(name) != true
            && _ctx.CapturedFields?.ContainsKey(name) != true;
    }

    private void EmitForOfNativeNumberGenerator(
        Stmt.ForOf f,
        Label startLabel,
        Label endLabel,
        Label continueLabel)
    {
        var builder = _ctx.ILBuilder;
        var generatorLocal = IL.DeclareLocal(
            _ctx.Runtime!.NativeNumberGeneratorInterfaceType);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime.NativeNumberGeneratorInterfaceType);
        IL.Emit(OpCodes.Stloc, generatorLocal);

        var loopVar = _ctx.Locals.DeclareLocal(
            f.Variable.Lexeme, _ctx.Types.Double);
        var closeNeeded = IL.DeclareLocal(_ctx.Types.Boolean);
        var throwing = IL.DeclareLocal(_ctx.Types.Boolean);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        IL.Emit(OpCodes.Ldloc, generatorLocal);
        IL.Emit(OpCodes.Callvirt,
            _ctx.Runtime.NativeNumberGeneratorMoveNextMethod);
        builder.Emit_Brfalse(endLabel);

        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, throwing);

        var escapingTargets = new HashSet<Label>();
        foreach (var loop in _ctx.LoopLabels)
        {
            escapingTargets.Add(loop.BreakLabel);
            escapingTargets.Add(loop.ContinueLabel);
        }

        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();

        IL.Emit(OpCodes.Ldloc, generatorLocal);
        IL.Emit(OpCodes.Callvirt,
            _ctx.Runtime.NativeNumberGeneratorCurrentMethod);
        IL.Emit(OpCodes.Stloc, loopVar);

        _iteratorLoopCompletionScopes.Push(new IteratorLoopCompletionScope(
            closeNeeded, continueLabel, escapingTargets));
        EmitStatement(f.Body);
        _iteratorLoopCompletionScopes.Pop();

        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, closeNeeded);
        builder.Emit_Leave(continueLabel);

        builder.BeginCatchBlock(_ctx.Types.Exception);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, throwing);
        IL.Emit(OpCodes.Rethrow);

        builder.BeginFinallyBlock();
        var skipClose = builder.DefineLabel("forof_native_gen_skip_close");
        IL.Emit(OpCodes.Ldloc, closeNeeded);
        builder.Emit_Brfalse(skipClose);
        IL.Emit(OpCodes.Ldloc, generatorLocal);
        IL.Emit(OpCodes.Ldloc, throwing);
        IL.Emit(OpCodes.Call, _ctx.Runtime.IteratorClose);
        builder.MarkLabel(skipClose);
        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;

        builder.MarkLabel(continueLabel);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    /// <summary>
    /// Emits for...of using NormalizeToEnumerator for iterator types.
    /// Unlike EmitForOfEnumerator (which casts to IEnumerable), this handles
    /// IEnumerator sources (like lazy iterator helpers and array values) correctly.
    /// </summary>
    private void EmitForOfNormalizedEnumerator(Stmt.ForOf f, Label startLabel, Label endLabel, Label continueLabel)
    {
        var builder = _ctx.ILBuilder;
        var moveNext = _ctx.Types.GetMethod(_ctx.Types.IEnumerator, "MoveNext");
        var current = _ctx.Types.GetProperty(_ctx.Types.IEnumerator, "Current")!.GetGetMethod()!;

        // Stack has the iterator source — normalize to IEnumerator<object>
        EmitBoxIfNeeded(f.Iterable);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.NormalizeToEnumerator);

        var enumLocal = IL.DeclareLocal(_ctx.Types.IEnumerator);
        IL.Emit(OpCodes.Stloc, enumLocal);

        // Loop variable
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        builder.Emit_Brfalse(endLabel);

        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, current);
        IL.Emit(OpCodes.Stloc, loopVar);

        EmitStatement(f.Body);

        builder.MarkLabel(continueLabel);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    protected override void EmitForIn(Stmt.ForIn f)
    {
        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("forin_start");
        var endLabel = builder.DefineLabel("forin_end");
        var continueLabel = builder.DefineLabel("forin_continue");

        _ctx.EnterLoop(endLabel, continueLabel);
        _ctx.Locals.EnterScope();

        // Evaluate object and get keys
        EmitExpression(f.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetKeys);
        var keysLocal = IL.DeclareLocal(_ctx.Types.ListOfObject);
        IL.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

        builder.MarkLabel(startLabel);
        EmitCancellationCheck();

        // Check if index < keys.Count
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        builder.Emit_Brfalse(endLabel);

        // Get current key: keys[index]
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        builder.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    protected override void EmitBlock(Stmt.Block b)
    {
        _ctx.Locals.EnterScope();

        _ctx.PredeclareCapturedLexicalLocals?.Invoke(IL, _ctx, b.Statements);
        InitializeCapturedLexicalTdzBindings(b.Statements);

        // Class declarations have lexical block bindings but their CLR Types
        // are defined ahead of method emission. Predeclare an undefined local
        // for TDZ behavior; the declaration statement installs its Type token.
        PredeclareBlockScopedClassLocals(b.Statements);

        // Check if block contains using declarations
        var usingResources = new List<LocalBuilder>();
        bool hasUsing = b.Statements.Any(s => s is Stmt.Using);

        if (hasUsing)
        {
            // Emit block with try/finally for disposal
            EmitBlockWithUsing(b.Statements, usingResources);
        }
        else
        {
            // Simple block without using declarations
            foreach (var stmt in b.Statements)
            {
                EmitStatement(stmt);
            }
        }

        _ctx.Locals.ExitScope();
    }

    private void PredeclareBlockScopedClassLocals(IEnumerable<Stmt> statements)
    {
        foreach (var classStmt in statements.OfType<Stmt.Class>())
        {
            if (_ctx.BlockScopedClassBuilders?.ContainsKey(classStmt) != true)
                continue;

            var local = _ctx.Locals.DeclareLocal(
                classStmt.Name.Lexeme, _ctx.Types.Object, classStmt);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Initializes captured let/const slots for this statement-list scope to
    /// the dedicated TDZ sentinel before any hoisted or textual function can
    /// observe them. Declaration emission later replaces the sentinel.
    /// </summary>
    internal void InitializeCapturedLexicalTdzBindings(IEnumerable<Stmt> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
            CollectDirectLexicalNames(statement, names);

        foreach (var name in names)
        {
            if (_ctx.CapturedFunctionLocals?.Contains(name) == true
                && _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var functionField) == true
                && _ctx.FunctionDisplayClassLocal != null)
            {
                // A value-type slot is emitted only after proving initialization
                // precedes creation of its sole iterator closure, so it cannot be
                // observed in TDZ and cannot hold the sentinel.
                if (functionField.FieldType.IsValueType)
                    continue;
                IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.LexicalUninitializedInstance);
                IL.Emit(OpCodes.Stfld, functionField);
                continue;
            }

            if (_ctx.CapturedArrowLocals?.Contains(name) == true
                && _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowField) == true
                && _ctx.ArrowScopeDisplayClassLocal != null)
            {
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.LexicalUninitializedInstance);
                IL.Emit(OpCodes.Stfld, arrowField);
            }
        }

        static void CollectDirectLexicalNames(Stmt statement, HashSet<string> names)
        {
            switch (statement)
            {
                case Stmt.Const constant:
                    names.Add(constant.Name.Lexeme);
                    break;
                case Stmt.Var { IsVar: false } lexical:
                    names.Add(lexical.Name.Lexeme);
                    break;
                case Stmt.Sequence sequence:
                    foreach (var nested in sequence.Statements)
                        CollectDirectLexicalNames(nested, names);
                    break;
            }
        }
    }

    private void EmitBlockScopedClassDeclaration(Stmt.Class classStmt)
    {
        var scopedClasses = _ctx.BlockScopedClassBuilders;
        TypeBuilder? builder = null;
        bool isBlockScoped = scopedClasses != null
            && scopedClasses.TryGetValue(classStmt, out builder);
        if (!isBlockScoped)
        {
            string qualifiedName = _ctx.GetQualifiedClassName(classStmt.Name.Lexeme);
            if (!_ctx.Classes.TryGetValue(qualifiedName, out var topLevelBuilder))
                return;
            builder = topLevelBuilder;
        }

        EmitClassHeritageExpression(classStmt.SuperclassExpr, classStmt.Name.Lexeme);

        // ECMAScript evaluates static elements and computed method/accessor
        // keys at the class declaration's exact source position. CLR type
        // initializers are lazy, so force the emitted .cctor here rather than
        // in an entry-point pre-pass (which ran every class too early) or at
        // first later use (which ran block-scoped classes too late).
        // Prototype creation is itself eager class-definition work and lives in
        // the emitted .cctor alongside static fields/blocks/computed keys. Force
        // every class definition, including classes whose only members are methods
        // or private instance fields, so `C.prototype` exists immediately.
        IL.Emit(OpCodes.Ldtoken, builder!);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(
            _ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
        IL.Emit(OpCodes.Call, _ctx.Runtime!.RunClassDefinitionMethod);

        // Top-level classes are lexical declarations, so they may also be present
        // in BlockScopedClassBuilders. Regardless of that implementation detail,
        // a captured module binding must be published to the entry-point display
        // class at its declaration position.
        if (_ctx.IsModuleTopLevel
            && _ctx.CapturedTopLevelVars?.Contains(classStmt.Name.Lexeme) == true
            && _ctx.EntryPointDisplayClassFields?.TryGetValue(classStmt.Name.Lexeme, out var displayField) == true)
        {
            if (_ctx.EntryPointDisplayClassLocal != null)
                IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
            else if (_ctx.EntryPointDisplayClassStaticField != null)
                IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            else
                goto skipCapturedClassStore;

            IL.Emit(OpCodes.Ldtoken, builder!);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(
                _ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            IL.Emit(OpCodes.Stfld, displayField);
        }
    skipCapturedClassStore:

        if (!isBlockScoped)
            return;

        if (!_ctx.Locals.TryGetTag(classStmt.Name.Lexeme, out var tag)
            || !ReferenceEquals(tag, classStmt))
            return;

        var local = _ctx.Locals.GetLocal(classStmt.Name.Lexeme)!;
        IL.Emit(OpCodes.Ldtoken, builder!);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
        IL.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Emits a block that contains using declarations with proper try/finally disposal.
    /// </summary>
    private void EmitBlockWithUsing(List<Stmt> statements, List<LocalBuilder> usingResources)
    {
        // Find the first using declaration index
        int firstUsingIndex = statements.FindIndex(s => s is Stmt.Using);

        // Emit statements before the first using
        for (int i = 0; i < firstUsingIndex; i++)
        {
            EmitStatement(statements[i]);
        }

        // Now emit using declarations and remaining statements in a try/finally
        IL.BeginExceptionBlock();

        for (int i = firstUsingIndex; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (stmt is Stmt.Using usingStmt)
            {
                // Process using declaration - store resources for disposal
                foreach (var binding in usingStmt.Bindings)
                {
                    // Evaluate the initializer
                    EmitExpression(binding.Initializer);
                    EnsureBoxed();

                    // Store in a local variable for later disposal
                    LocalBuilder resourceLocal;
                    if (binding.Name != null)
                    {
                        resourceLocal = _ctx.Locals.DeclareLocal(binding.Name.Lexeme, _ctx.Types.Object);
                    }
                    else
                    {
                        // Anonymous using - still need to track for disposal
                        resourceLocal = IL.DeclareLocal(_ctx.Types.Object);
                    }
                    IL.Emit(OpCodes.Stloc, resourceLocal);
                    usingResources.Add(resourceLocal);
                }
            }
            else
            {
                EmitStatement(stmt);
            }
        }

        // Finally block - dispose resources in reverse order
        IL.BeginFinallyBlock();
        EmitUsingDisposal(usingResources);
        IL.EndExceptionBlock();
    }

    /// <summary>
    /// Emits disposal code for using declaration resources.
    /// Disposes in reverse order (LIFO).
    /// </summary>
    private void EmitUsingDisposal(List<LocalBuilder> resources)
    {
        // Dispose in reverse order
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            var resourceLocal = resources[i];

            // Load the resource
            IL.Emit(OpCodes.Ldloc, resourceLocal);

            // Load Symbol.dispose
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolDispose);

            // Call $Runtime.DisposeResource(resource, Symbol.dispose)
            IL.Emit(OpCodes.Call, _ctx.Runtime!.DisposeResource);
        }
    }

    /// <summary>
    /// Emits a list of statements with proper handling for 'using' declarations.
    /// If using declarations are present, wraps the statements in try/finally for disposal.
    /// </summary>
    public void EmitStatements(List<Stmt> statements)
    {
        InitializeCapturedLexicalTdzBindings(statements);

        // Function/state-machine bodies are emitted as a raw statement list,
        // not through EmitBlock. Their direct class declarations still need
        // lexical locals so references after the declaration resolve to the
        // installed Type value.
        PredeclareBlockScopedClassLocals(statements);

        // Check if any statement is a using declaration
        bool hasUsing = statements.Any(s => s is Stmt.Using);

        if (hasUsing)
        {
            var usingResources = new List<LocalBuilder>();

            // Find the first using declaration index
            int firstUsingIndex = statements.FindIndex(s => s is Stmt.Using);

            // Emit statements before the first using
            for (int i = 0; i < firstUsingIndex; i++)
            {
                EmitStatement(statements[i]);
            }

            // Now emit using declarations and remaining statements in a try/finally
            // Use the builder for exception block tracking and validation
            var builder = _ctx.ILBuilder;
            _ctx.ExceptionBlockDepth++;
            builder.BeginExceptionBlock();

            for (int i = firstUsingIndex; i < statements.Count; i++)
            {
                var stmt = statements[i];
                if (stmt is Stmt.Using usingStmt)
                {
                    // Process using declaration - store resources for disposal
                    foreach (var binding in usingStmt.Bindings)
                    {
                        // Evaluate the initializer
                        EmitExpression(binding.Initializer);
                        EnsureBoxed();

                        // Store in a local variable for later disposal
                        LocalBuilder resourceLocal;
                        if (binding.Name != null)
                        {
                            resourceLocal = _ctx.Locals.DeclareLocal(binding.Name.Lexeme, _ctx.Types.Object);
                        }
                        else
                        {
                            // Anonymous using - still need to track for disposal
                            resourceLocal = IL.DeclareLocal(_ctx.Types.Object);
                        }
                        IL.Emit(OpCodes.Stloc, resourceLocal);
                        usingResources.Add(resourceLocal);
                    }
                }
                else
                {
                    EmitStatement(stmt);
                }
            }

            // Finally block - dispose resources in reverse order
            builder.BeginFinallyBlock();
            EmitUsingDisposal(usingResources);
            builder.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;
        }
        else
        {
            // No using declarations - emit normally
            foreach (var stmt in statements)
            {
                EmitStatement(stmt);
            }
        }
    }

    protected override void EmitReturn(Stmt.Return r)
    {
        // Get the current method's return type (defaults to object if not set)
        var returnType = _ctx.CurrentMethodReturnType ?? _ctx.Types.Object;

        if (r.Value != null)
        {
            if (TryEmitStableIteratorResultReturn(returnType, r.Value))
                goto emit_return;

            if (_ctx.Types.IsDouble(returnType) && r.Value is Expr.GetIndex)
                EmitExpressionAsDouble(r.Value);
            else
                EmitExpression(r.Value);
            // Only box if return type is object; otherwise use typed value directly
            if (returnType == _ctx.Types.Object)
            {
                EmitBoxIfNeeded(r.Value);
            }
            else if (returnType == typeof(void))
            {
                // Void method (most commonly a constructor) — discard the value to keep
                // the stack balanced on ret. JS constructors can return a replacement
                // object (spec: if the returned value is an object, it replaces `this`),
                // but .NET ctors must return void with an empty stack. We fall back to
                // ignoring the return value, matching the common case where ctors return
                // primitives or `undefined`; spec-compliant object substitution would
                // require rewriting the `new` call site and is out of scope here.
                // Required for real npm packages that use this idiom (semver's Comparator
                // ctor does `return comp` when the argument is already a Comparator).
                EmitBoxIfNeeded(r.Value);
                IL.Emit(OpCodes.Pop);
            }
            else if (_ctx.Types.IsDouble(returnType))
            {
                // Ensure we have an unboxed double for : number return type
                if (_stackType != StackType.Double)
                {
                    EmitUnboxToDouble();
                }
            }
            else if (_ctx.Types.IsBoolean(returnType))
            {
                // Ensure we have an unboxed bool for : boolean return type
                if (_stackType != StackType.Boolean)
                {
                    // Convert to boolean: double -> i4, or object -> unbox to double -> i4
                    if (_stackType == StackType.Double)
                    {
                        IL.Emit(OpCodes.Conv_I4);
                    }
                    else
                    {
                        EmitUnboxToDouble();
                        IL.Emit(OpCodes.Conv_I4);
                    }
                }
            }
            // Note: `string` return types are never emitted as a narrow `string` slot —
            // ParameterTypeResolver.ResolveReturnType maps them to object because a string
            // slot cannot carry the `$Undefined` sentinel an inferred-string body can
            // produce (see #318, which removed the #275 castclass that lived here).
            // For other narrow types (double/bool handled above), the value is already correct.
        }
        else
        {
            // Return undefined (null) or appropriate default
            if (returnType == typeof(void))
            {
                // Void functions: no value on stack; ret takes nothing.
            }
            else if (returnType == _ctx.Types.Object)
            {
                // ECMA-262: a bare `return;` completes with undefined, not null. Emit the
                // $Undefined sentinel for untyped object returns — mirrors EmitDefaultReturnValue
                // (the off-the-end path) and the interpreter's VisitReturn — so a plain function
                // returning no value is `undefined`. `return null;` (the r.Value != null branch
                // above) still yields null. #563
                EmitUndefinedConstant();
            }
            else if (!returnType.IsValueType)
            {
                // Specific reference-typed returns keep their null default (matches
                // EmitDefaultReturnValue): the checker treats an explicit `T | null` return as null.
                IL.Emit(OpCodes.Ldnull);
            }
            else if (_ctx.Types.IsDouble(returnType))
            {
                IL.Emit(OpCodes.Ldc_R8, 0.0);
            }
            else if (_ctx.Types.IsBoolean(returnType))
            {
                IL.Emit(OpCodes.Ldc_I4_0);
            }
            else
            {
                // For other value types, emit default
                var local = IL.DeclareLocal(returnType);
                IL.Emit(OpCodes.Ldloca, local);
                IL.Emit(OpCodes.Initobj, returnType);
                IL.Emit(OpCodes.Ldloc, local);
            }
        }

    emit_return:
        if (_abruptCompletionScopes.TryPeek(out var completion))
        {
            if (_ctx.ReturnValueLocal == null && !_ctx.HasDeferredVoidReturn)
            {
                _ctx.ReturnLabel = _ctx.ILBuilder.DefineLabel("deferred_return");
                if (returnType == typeof(void))
                    _ctx.HasDeferredVoidReturn = true;
                else
                    _ctx.ReturnValueLocal = IL.DeclareLocal(returnType);
            }
            if (returnType != typeof(void))
                IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal!);
            IL.Emit(OpCodes.Ldc_I4_1); // return completion
            IL.Emit(OpCodes.Stloc, completion.Kind);
            _ctx.ILBuilder.Emit_Leave(completion.RunFinally);
        }
        else if (_ctx.ExceptionBlockDepth > 0)
        {
            // Inside exception block: store value and leave
            // Use builder for Leave validation (ensures we're inside exception block)
            var builder = _ctx.ILBuilder;
            if (_ctx.ReturnValueLocal == null && !_ctx.HasDeferredVoidReturn)
            {
                _ctx.ReturnLabel = builder.DefineLabel("deferred_return");
                if (returnType == typeof(void))
                    _ctx.HasDeferredVoidReturn = true;
                else
                    _ctx.ReturnValueLocal = IL.DeclareLocal(returnType);
            }
            if (returnType != typeof(void))
                IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal!);
            builder.Emit_Leave(_ctx.ReturnLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ret);
        }

        // Reset stack type after return consumes the value. Without this,
        // _stackType remains stale (e.g., Double from 'return 0') and dead code
        // emitted after the return (like the 'br endLabel' in EmitIf) preserves
        // the stale type. When the branch target is reached and new code emits
        // EmitBoxIfNeeded, it sees StackType.Double and incorrectly boxes the
        // next value (e.g., an array reference) as a Double.
        SetStackUnknown();
    }

    private bool TryEmitStableIteratorResultReturn(Type returnType, Expr expression)
    {
        if (_ctx.Runtime?.StableNumberIteratorResultType != returnType)
            return false;

        while (true)
        {
            switch (expression)
            {
                case Expr.Grouping grouping:
                    expression = grouping.Expression;
                    continue;
                case Expr.TypeAssertion assertion:
                    expression = assertion.Expression;
                    continue;
                case Expr.Satisfies satisfies:
                    expression = satisfies.Expression;
                    continue;
                case Expr.NonNullAssertion nonNull:
                    expression = nonNull.Expression;
                    continue;
            }
            break;
        }

        if (expression is not Expr.ObjectLiteral
            {
                Properties:
                [
                    { IsSpread: false, Kind: Expr.ObjectPropertyKind.Value, Key: var valueKey, Value: var value },
                    { IsSpread: false, Kind: Expr.ObjectPropertyKind.Value, Key: var doneKey, Value: var done }
                ]
            } ||
            GetPropertyKeyString(valueKey!) != "value" ||
            GetPropertyKeyString(doneKey!) != "done")
            return false;

        EmitExpressionAsDouble(value);
        EmitExpression(done);
        EnsureBoolean();
        IL.Emit(OpCodes.Newobj, _ctx.Runtime.StableNumberIteratorResultCtor);
        SetStackUnknown();
        return true;
    }

    protected override void EmitBreak(Stmt.Break b)
    {
        var loop = b.Label != null
            ? FindLabeledLoop(b.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.BreakLabel);
    }

    protected override void EmitContinue(Stmt.Continue c)
    {
        var loop = c.Label != null
            ? FindLabeledLoop(c.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.ContinueLabel);
    }

    protected override void EmitLabeledStatement(Stmt.LabeledStatement labeledStmt)
    {
        // Look through a chain of labels (a: b: …) to whatever they ultimately wrap.
        var inner = UnwrapLabelChain(labeledStmt, out var chainLabels);

        if (IsLabelableLoop(inner))
        {
            // Direct (or chained) loop: park EVERY label in the chain so the inner loop attaches them
            // all to its OWN break/continue targets (a for-loop's increment, a while's condition, …).
            // Marking a continue label here — ahead of a for's initializer — would re-run it forever,
            // and the outer label of a chain used to fall into exactly that path (#558/#580).
            foreach (var label in chainLabels)
                _ctx.AddPendingLoopLabel(label);
            try
            {
                EmitStatement(inner);
            }
            finally
            {
                // The loop's EnterLoop drains the parked labels; clear any it somehow didn't.
                _ctx.ClearPendingLoopLabels();
            }
            return;
        }

        // Non-loop labeled statement (a block, etc.). Mark the continue target before the statement
        // (harmless for a block) and keep one wrapper scope per label by recursing through the chain;
        // only `break <label>` is meaningful here.
        string labelName = labeledStmt.Label.Lexeme;
        var builder = _ctx.ILBuilder;
        var breakLabel = builder.DefineLabel($"labeled_{labelName}_break");
        var continueLabel = builder.DefineLabel($"labeled_{labelName}_continue");
        builder.MarkLabel(continueLabel);
        _ctx.EnterLoop(breakLabel, continueLabel, labelName);
        try
        {
            EmitStatement(labeledStmt.Statement);
        }
        finally
        {
            _ctx.ExitLoop();
        }
        builder.MarkLabel(breakLabel);
    }

    protected override void EmitSwitch(Stmt.Switch s)
    {
        // Check for exhaustive switch optimization
        var switchAnalysis = _ctx.DeadCode?.GetSwitchResult(s);
        bool skipDefault = switchAnalysis?.DefaultIsUnreachable == true;

        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("switch_end");
        var defaultLabel = builder.DefineLabel("switch_default");
        var caseLabels = s.Cases.Select((_, i) => builder.DefineLabel($"switch_case_{i}")).ToList();

        // Evaluate subject once
        EmitExpression(s.Subject);
        var subjectLocal = IL.DeclareLocal(_ctx.Types.Object);
        EmitBoxIfNeeded(s.Subject);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EmitBoxIfNeeded(s.Cases[i].Value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Equals);
            builder.Emit_Brtrue(caseLabels[i]);
        }

        // Jump to default or end (skip default if unreachable)
        if (skipDefault || s.DefaultBody == null)
        {
            builder.Emit_Br(endLabel);
        }
        else
        {
            builder.Emit_Br(defaultLabel);
        }

        // Register the switch end as the current break target so nested breaks
        // (inside blocks, if/else, try/catch, etc.) exit the switch. Preserve the
        // outer loop's continue target so `continue;` still propagates outward.
        var outerContinue = CurrentLoop?.ContinueLabel ?? endLabel;
        EnterLoop(endLabel, outerContinue);
        try
        {
            // Emit case bodies
            for (int i = 0; i < s.Cases.Count; i++)
            {
                builder.MarkLabel(caseLabels[i]);
                foreach (var stmt in s.Cases[i].Body)
                {
                    if (stmt is Stmt.Break breakStmt)
                    {
                        if (breakStmt.Label != null)
                        {
                            // Labeled break - find and jump to the labeled target
                            EmitBreak(breakStmt);
                        }
                        else
                        {
                            // Unlabeled break - exits switch only
                            builder.Emit_Br(endLabel);
                        }
                    }
                    else
                    {
                        EmitStatement(stmt);
                    }
                }
                // Fall through if no break
            }

            // Default case (skip if unreachable)
            if (s.DefaultBody != null && !skipDefault)
            {
                builder.MarkLabel(defaultLabel);
                foreach (var stmt in s.DefaultBody)
                {
                    if (stmt is Stmt.Break breakStmt)
                    {
                        if (breakStmt.Label != null)
                        {
                            // Labeled break - find and jump to the labeled target
                            EmitBreak(breakStmt);
                        }
                        else
                        {
                            // Unlabeled break - exits switch only
                            builder.Emit_Br(endLabel);
                        }
                    }
                    else
                    {
                        EmitStatement(stmt);
                    }
                }
            }
        }
        finally
        {
            ExitLoop();
        }

        builder.MarkLabel(endLabel);
    }

    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        if (t.FinallyBlock != null)
        {
            EmitTryCatchFinally(t);
            return;
        }

        if (t.CatchBlock == null)
            throw new InvalidOperationException("A try statement requires a catch or finally block.");

        // Keep a real CLR handler for exceptions crossing calls/reflection, but
        // route syntactically local guest throws directly to the JavaScript catch
        // body. CLR unwind dominates sparse local-throw time even after removing
        // wrapper metadata; a typed value local preserves the same catch identity
        // without allocating or unwinding.
        var builder = _ctx.ILBuilder;
        var catchValue = IL.DeclareLocal(_ctx.Types.Object);
        var catchBody = builder.DefineLabel("js_local_catch");
        var afterCatch = builder.DefineLabel("js_local_catch_done");

        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();

        _localThrowScopes.Push(new LocalThrowScope(
            catchValue,
            catchBody,
            _abruptCompletionScopes.Count,
            _iteratorLoopCompletionScopes.Count));
        try
        {
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);
        }
        finally
        {
            _localThrowScopes.Pop();
        }
        builder.Emit_Leave(afterCatch);

        builder.BeginCatchBlock(_ctx.Types.Exception);
        if (t.CatchParam != null)
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
            IL.Emit(OpCodes.Stloc, catchValue);
        }
        else
        {
            IL.Emit(OpCodes.Pop);
        }
        builder.Emit_Leave(catchBody);

        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;

        builder.MarkLabel(catchBody);
        _ctx.Locals.EnterScope();
        try
        {
            if (t.CatchParam != null)
                _ctx.Locals.RegisterLocal(t.CatchParam.Lexeme, catchValue);
            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }
        finally
        {
            _ctx.Locals.ExitScope();
        }
        builder.MarkLabel(afterCatch);
    }

    /// <summary>
    /// Lowers JavaScript try/catch/finally through an explicit Completion record.
    /// CLR finally handlers cannot transfer control, while ECMAScript finally may
    /// replace an in-flight return, break, continue, or throw. Routing the pending
    /// completion through ordinary IL after the protected region preserves that
    /// distinction and composes for nested finally blocks.
    /// </summary>
    private void EmitTryCatchFinally(Stmt.TryCatch t)
    {
        var builder = _ctx.ILBuilder;
        var runFinally = builder.DefineLabel("js_finally");
        var afterFinally = builder.DefineLabel("js_finally_done");
        var kind = IL.DeclareLocal(_ctx.Types.Int32); // 0 normal, 1 return, 2 throw, 3+ branch
        var exception = IL.DeclareLocal(_ctx.Types.Exception);

        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, kind);

        var enclosingTargets = new HashSet<Label>();
        foreach (var loop in _ctx.LoopLabels)
        {
            enclosingTargets.Add(loop.BreakLabel);
            enclosingTargets.Add(loop.ContinueLabel);
        }

        var completion = new AbruptCompletionScope
        {
            RunFinally = runFinally,
            Kind = kind,
            Exception = exception,
            EnclosingTargets = enclosingTargets
        };
        _abruptCompletionScopes.Push(completion);

        // Outer catch converts any exception escaping either the try body or
        // the JavaScript catch clause into a pending throw completion.
        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();

        if (t.CatchBlock != null)
        {
            _ctx.ExceptionBlockDepth++;
            builder.BeginExceptionBlock();
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);

            builder.BeginCatchBlock(_ctx.Types.Exception);
            _ctx.Locals.EnterScope();
            try
            {
                if (t.CatchParam != null)
                {
                    var exLocal = _ctx.Locals.DeclareLocal(t.CatchParam.Lexeme, _ctx.Types.Object);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                    IL.Emit(OpCodes.Stloc, exLocal);
                }
                else
                {
                    IL.Emit(OpCodes.Pop);
                }
                foreach (var stmt in t.CatchBlock)
                    EmitStatement(stmt);
            }
            finally
            {
                _ctx.Locals.ExitScope();
            }
            builder.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;
        }
        else
        {
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);
        }

        builder.Emit_Leave(runFinally);
        builder.BeginCatchBlock(_ctx.Types.Exception);
        IL.Emit(OpCodes.Stloc, exception);
        IL.Emit(OpCodes.Ldc_I4_2);
        IL.Emit(OpCodes.Stloc, kind);
        builder.Emit_Leave(runFinally);
        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;

        _abruptCompletionScopes.Pop();
        builder.MarkLabel(runFinally);
        foreach (var stmt in t.FinallyBlock!)
            EmitStatement(stmt);

        // A normal finally preserves and resumes the pending completion.
        IL.Emit(OpCodes.Ldloc, kind);
        builder.Emit_Brfalse(afterFinally);

        if (_ctx.ReturnValueLocal != null)
        {
            var notReturn = builder.DefineLabel("js_finally_not_return");
            IL.Emit(OpCodes.Ldloc, kind);
            IL.Emit(OpCodes.Ldc_I4_1);
            builder.Emit_Bne_Un(notReturn);
            IL.Emit(OpCodes.Ldloc, _ctx.ReturnValueLocal);
            if (_abruptCompletionScopes.TryPeek(out var outerCompletion))
            {
                IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, outerCompletion.Kind);
                builder.Emit_Leave(outerCompletion.RunFinally);
            }
            else if (_ctx.ExceptionBlockDepth > 0)
            {
                builder.Emit_Leave(_ctx.ReturnLabel);
            }
            else
            {
                IL.Emit(OpCodes.Ret);
            }
            builder.MarkLabel(notReturn);
        }
        var notThrow = builder.DefineLabel("js_finally_not_throw");
        IL.Emit(OpCodes.Ldloc, kind);
        IL.Emit(OpCodes.Ldc_I4_2);
        builder.Emit_Bne_Un(notThrow);
        IL.Emit(OpCodes.Ldloc, exception);
        IL.Emit(OpCodes.Throw);

        builder.MarkLabel(notThrow);
        foreach (var (target, code) in completion.BranchCodes)
        {
            var next = builder.DefineLabel("js_finally_next_branch");
            IL.Emit(OpCodes.Ldloc, kind);
            IL.Emit(OpCodes.Ldc_I4, code);
            builder.Emit_Bne_Un(next);
            EmitBranchToLabel(target);
            builder.MarkLabel(next);
        }

        builder.MarkLabel(afterFinally);
    }

    protected override void EmitThrow(Stmt.Throw t)
    {
        if (_localThrowScopes.TryPeek(out var localThrow) &&
            localThrow.AbruptCompletionDepth == _abruptCompletionScopes.Count &&
            localThrow.IteratorCompletionDepth == _iteratorLoopCompletionScopes.Count)
        {
            EmitExpression(t.Value);
            EmitBoxIfNeeded(t.Value);
            IL.Emit(OpCodes.Stloc, localThrow.Value);
            SetStackUnknown();
            _ctx.ILBuilder.Emit_Leave(localThrow.CatchBody);
            return;
        }

        EmitExpression(t.Value);
        EmitBoxIfNeeded(t.Value);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateException);
        IL.Emit(OpCodes.Throw);
    }

}
