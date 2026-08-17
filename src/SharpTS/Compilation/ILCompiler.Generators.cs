using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Generator function compilation for the IL compiler.
/// Handles the definition and emission of generator state machines.
/// </summary>
public partial class ILCompiler
{
    // Maps an instance generator method's AST node to the function-display-class key registered for
    // it during DefineClass (#724). EmitGeneratorMethodBody reads this to wire the state machine's
    // function DC at emit time. Keyed by AST identity so the Phase-4 registration and the later
    // Phase-7 emission agree without reconstructing a string from the emitted type name.
    private readonly Dictionary<Stmt.Function, string> _generatorMethodFunctionDCKeys =
        new(ReferenceEqualityComparer.Instance);

    // The async-generator (`async *m()`) analogue of the above (#725), consumed by
    // EmitAsyncGeneratorMethodBody. Kept separate so each emit path looks up only its own kind.
    private readonly Dictionary<Stmt.Function, string> _asyncGeneratorMethodFunctionDCKeys =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Defines a generator function and its state machine.
    /// </summary>
    private void DefineGeneratorFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Module-qualify the stub/registry keys (#418) so two modules that each declare a
        // same-named generator function don't clobber each other. Single-file compilation
        // returns the simple name unchanged. The readable state-machine type name keeps the
        // simple name (the builder's counter already disambiguates `<name>d__N`).
        string qualifiedName = GetDefinitionContext().GetQualifiedFunctionName(funcName);

        // Analyze the generator function for yield points and hoisted variables
        var analysis = _generators.Analyzer.Analyze(funcStmt);

        // #775: a free-function generator binds its own dynamic `this` (it is a `function*`, never an
        // arrow). When its body uses `this`, the stub captures the active dynamic receiver — the
        // thread-local `$TSFunction._currentFunctionThis`, set by InvokeWithThis for an `o.gen()` /
        // `.call(recv)` value-call — into the state machine's <>4__this field at creation time, since
        // the thread-local is gone by the time MoveNext runs lazily. A plain direct `g()` call leaves it
        // at the globalThis sentinel (sloppy `this`). This mirrors how a non-generator `function(){ this.x }`
        // declaration threads `this`, so direct calls keep their signature (no synthetic `__this` param).
        bool hasDynamicThis = analysis.UsesThis;

        // Create the state machine builder
        var smBuilder = new GeneratorStateMachineBuilder(_moduleBuilder, _types, _generators.StateMachineCounter++);
        smBuilder.DefineStateMachine(funcName, analysis, isInstanceMethod: false, runtime: _runtime, hasDynamicThis: hasDynamicThis);

        _generators.StateMachines[qualifiedName] = smBuilder;
        _generators.Functions[qualifiedName] = funcStmt;

        // Record the AST node so PropagateFunctionDCRequirements can resolve arrows nested in this
        // generator back to its qualified name, and lift captured-and-mutated locals into a shared
        // function display class so a write inside an arrow/callback reaches the generator (#674).
        _closures.FunctionAstNodes[qualifiedName] = funcStmt;
        DefineGeneratorFunctionDisplayClass(funcStmt, qualifiedName, smBuilder);

        // Define the stub method that creates and returns the state machine.
        // A trailing rest parameter is typed List<object> so the indirect
        // ($TSFunction.Invoke) call path packs trailing args into it (#426).
        var paramTypes = BuildStateMachineStubParamTypes(funcStmt);
        var methodBuilder = _programType.DefineMethod(
            qualifiedName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IEnumerableOfObject,  // Generator returns IEnumerable<object>
            paramTypes
        );
        RegisterStateMachine(
            methodBuilder,
            smBuilder.StateMachineType,
            smBuilder.MoveNextMethod,
            EmittedStateMachineKind.Iterator,
            smBuilder.CurrentGetMethod,
            smBuilder.NonGenericCurrentGetMethod,
            smBuilder.ResetMethod,
            smBuilder.DisposeMethod,
            smBuilder.GetEnumeratorMethod,
            smBuilder.NonGenericGetEnumeratorMethod,
            smBuilder.NextMethod,
            smBuilder.ReturnMethod,
            smBuilder.ThrowMethod);

        _functions.Builders[qualifiedName] = methodBuilder;

        // #925: a generator function used as a value (imported cross-module, stored, passed as a
        // callback → $TSFunction.Invoke) must pad omitted trailing optional args with the `undefined`
        // sentinel, not CLR null — matching plain functions, arrows, and class methods. The stub's
        // params are all `object` slots, so the sentinel flows into the state-machine fields and the
        // MoveNext default prologue / `typeof` / `=== undefined` all answer correctly.
        MarkPadsUndefined(methodBuilder);

        // Track rest parameter info (keyed by the qualified name so ResolveFunctionName-based
        // call-site lookups in ExpressionEmitterBase find it).
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functions.RestParams[qualifiedName] = (restIndex, regularCount);
        }
    }

    /// <summary>
    /// Lifts a generator's captured-AND-mutated locals into a function-level display class so an
    /// arrow/callback inside the generator body that writes such a variable shares storage with the
    /// generator instead of snapshotting it by value (#674). Read-only captures keep the existing
    /// by-value snapshot path. Mirrors the sync/async function-DC wiring, restricted to the write
    /// case the generator state machine could not previously honour. No-op when the generator has no
    /// write-captures (the common case), leaving fully-standalone output unchanged.
    /// </summary>
    private void DefineGeneratorFunctionDisplayClass(
        Stmt.Function funcStmt, string qualifiedName, GeneratorStateMachineBuilder smBuilder)
    {
        var mutatedCaptured = ComputeMutatedCapturedGeneratorVars(funcStmt);
        if (mutatedCaptured.Count == 0)
            return;

        RegisterFunctionDisplayClass(qualifiedName, mutatedCaptured);
        if (_closures.FunctionDisplayClasses.TryGetValue(qualifiedName, out var funcDC))
            smBuilder.DefineFunctionDisplayClassField(funcDC);
    }

    /// <summary>
    /// Returns the generator's own locals/parameters that need shared reference storage in the function
    /// display class: those both captured AND written by an inner arrow (#674), plus (#945) those a
    /// HOISTED lambda-lifted nested generator forwards read-only — those must be read LIVE through the DC
    /// rather than a stale by-value snapshot taken above the captured local's assignment. Per-iteration
    /// <c>for (let…)</c> bindings are excluded — each iteration owns its binding (#649), so closures must
    /// snapshot them per iteration rather than share one function-DC cell.
    /// </summary>
    private HashSet<string> ComputeMutatedCapturedGeneratorVars(Stmt.Function funcStmt)
    {
        var capturedLocals = _closures.Analyzer.GetCapturedLocals(funcStmt);
        if (capturedLocals.Count == 0)
            return [];

        var result = new HashSet<string>(capturedLocals);
        result.IntersectWith(CollectGeneratorArrowCapturedWrites(funcStmt));

        // #945: a read-only capture forwarded by a HOISTED lambda-lifted nested generator (the sync
        // forwarding arrow NestedFunctionLifter marks IsLiftedForwarder) must also live in the DC, so the
        // hoisted forwarder reads it live at call time. Marked forwarders only appear in free/module/
        // nested-in-function generator bodies (class-method enclosers decline → unmarked), so generator
        // METHODS are unaffected. Unioned BEFORE the empty-set short-circuit and the per-iteration
        // exclusion below, which still strips any per-iteration loop binding a forwarder happens to read.
        var forwardedReads = CollectLiftedForwarderCapturedReads(funcStmt);
        forwardedReads.IntersectWith(capturedLocals);
        result.UnionWith(forwardedReads);

        if (result.Count == 0)
            return [];

        var perIteration = _closures.Analyzer.GetPerIterationLoopBindings(funcStmt);
        if (perIteration.Count > 0)
            result.ExceptWith(perIteration);
        // #838: a write-captured nested-block shadow gets its own renamed DC field so it does not collide
        // with the outer same-named binding on a single name-keyed cell.
        ApplyWriteCaptureRenames(result, GeneratorBlockScopeRenamer.Compute(funcStmt));
        return result;
    }

    /// <summary>
    /// Makes a name-keyed mutated-captured set rename-aware (#838). For every write-captured block-scope
    /// shadow the renamer disambiguated (arrow → source name → renamed storage), adds the renamed storage
    /// as its own display-class field (ADDITIVE — the original name stays so an un-renamed outer capture
    /// of the same name keeps its own field) and records the per-arrow source → storage remap so the
    /// arrow body's read/write of the capture is redirected to the renamed field at emit time. No-op when
    /// nothing was write-captured-and-renamed (the common case), leaving the DC field set unchanged.
    /// </summary>
    private void ApplyWriteCaptureRenames(HashSet<string> mutatedCaptured, BlockScopeRenameResult renames)
    {
        if (renames.WriteCaptureRenames.Count == 0)
            return;
        foreach (var (arrowNode, names) in renames.WriteCaptureRenames)
        {
            if (arrowNode is not Expr.ArrowFunction arrow)
                continue;
            Dictionary<string, string>? perArrow = null;
            foreach (var (name, storage) in names)
            {
                // Only lift names that are genuinely captured-and-mutated here; a renamed shadow whose
                // outer name is not in the set was not a generator capture and needs no DC field.
                if (!mutatedCaptured.Contains(name))
                    continue;
                mutatedCaptured.Add(storage);
                (perArrow ??= [])[name] = storage;
            }
            if (perArrow == null)
                continue;
            if (_closures.ArrowFunctionDCFieldRenames.TryGetValue(arrow, out var existing))
                foreach (var (k, v) in perArrow) existing[k] = v;
            else
                _closures.ArrowFunctionDCFieldRenames[arrow] = perArrow;
        }
    }

    /// <summary>
    /// Unions, over every arrow lexically inside the generator body (nested arrows included), the
    /// names the arrow assigns within its own scope that it also captures from an enclosing scope.
    /// Intersected by the caller with the generator's own captured locals to identify mutated
    /// generator captures.
    /// </summary>
    private HashSet<string> CollectGeneratorArrowCapturedWrites(Stmt.Function funcStmt)
    {
        var collector = new ArrowCollector();
        if (funcStmt.Body != null)
            foreach (var stmt in funcStmt.Body)
                collector.Visit(stmt);

        var writes = new HashSet<string>();
        foreach (var arrow in collector.Arrows)
        {
            // Only sync arrows share the generator's function DC — async arrows capture through
            // their own boxed state machine, the same scope the compile-time guard covers (#674).
            if (arrow.IsAsync)
                continue;
            var arrowWrites = CapturedWriteAnalysis.CollectImmediateWrites(arrow);
            arrowWrites.IntersectWith(_closures.Analyzer.GetCaptures(arrow));
            writes.UnionWith(arrowWrites);
        }
        return writes;
    }

    /// <summary>
    /// Unions the captures of every lifted forwarding arrow (#945) lexically inside the generator body.
    /// A forwarder is the sync, non-generator arrow <see cref="NestedFunctionLifter"/> substitutes for a
    /// capturing nested generator when it hoists the binding into a generator encloser's body; it only
    /// READS its forwarded captures, so it is invisible to the write-based
    /// <see cref="CollectGeneratorArrowCapturedWrites"/>. The caller intersects the result with the
    /// generator's own captured locals, so a forwarder nested in a deeper function contributes nothing.
    /// </summary>
    private HashSet<string> CollectLiftedForwarderCapturedReads(Stmt.Function funcStmt)
    {
        var collector = new ArrowCollector();
        if (funcStmt.Body != null)
            foreach (var stmt in funcStmt.Body)
                collector.Visit(stmt);

        var reads = new HashSet<string>();
        foreach (var arrow in collector.Arrows)
            if (arrow.IsLiftedForwarder)
                reads.UnionWith(_closures.Analyzer.GetCaptures(arrow));
        return reads;
    }

    /// <summary>Collects every arrow function in a subtree, descending into nested arrows.</summary>
    private sealed class ArrowCollector : Parsing.Visitors.AstVisitorBase
    {
        public readonly List<Expr.ArrowFunction> Arrows = [];
        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            Arrows.Add(expr);
            base.VisitArrowFunction(expr); // descend to find nested arrows
        }
    }

    /// <summary>
    /// Phase-4 registration (called from <see cref="DefineClass"/>): for each SYNC instance generator
    /// method whose body contains an arrow that WRITES a variable captured from the method scope,
    /// registers a function-level display class — the instance-method analogue of the free-function
    /// wiring <see cref="DefineGeneratorFunctionDisplayClass"/> does for <c>function*</c> declarations
    /// (#724/#674). This must run before Phase 5 so <see cref="PropagateFunctionDCRequirements"/> can
    /// resolve the nested arrow back to the method (via <c>FunctionAstNodes</c>) and route its write
    /// through <c>$functionDC</c> instead of a by-value snapshot. <see cref="EmitGeneratorMethodBody"/>
    /// later consumes the recorded key to wire the state machine's function DC. No-op for methods with
    /// no such write-capture, leaving their state machines fully standalone. Covers both sync (#724)
    /// and async (#725) instance generator methods; static and plain methods use other paths.
    /// </summary>
    private void RegisterGeneratorMethodFunctionDisplayClasses(Stmt.Class classStmt, string qualifiedClassName) =>
        RegisterGeneratorMethodFunctionDisplayClasses(classStmt.Methods, qualifiedClassName);

    /// <summary>
    /// Shared core of <see cref="RegisterGeneratorMethodFunctionDisplayClasses(Stmt.Class, string)"/>, reused
    /// for class EXPRESSIONS (#789). <c>Expr.ClassExpr.Methods</c> is the same <c>List&lt;Stmt.Function&gt;</c>
    /// element type as <c>Stmt.Class.Methods</c>, so both class kinds register their generator-method function
    /// display classes through this single pass.
    /// </summary>
    private void RegisterGeneratorMethodFunctionDisplayClasses(IReadOnlyList<Stmt.Function> methods, string qualifiedClassName)
    {
        foreach (var method in methods)
        {
            if (method.Body == null || !method.IsGenerator)
                continue;

            // Generator methods need a function display class for every captured local, not only
            // write-captures.  Their body runs later in MoveNext, so a by-value arrow snapshot can
            // otherwise observe the enclosing/global binding instead of the method-local binding.
            var capturedLocals = new HashSet<string>(_closures.Analyzer.GetCapturedLocals(method));
            capturedLocals.ExceptWith(_closures.Analyzer.GetPerIterationLoopBindings(method));
            ApplyWriteCaptureRenames(capturedLocals, GeneratorBlockScopeRenamer.Compute(method));
            if (capturedLocals.Count == 0)
                continue;

            // Method names with bodies are unique within a class (overload signatures have no body),
            // and the qualified class name disambiguates across modules/namespaces — so the "::" key is
            // unique and disjoint from free-function registry keys (which never contain "::").
            string key = $"{qualifiedClassName}::{method.Name.Lexeme}";
            _closures.FunctionAstNodes[key] = method;
            RegisterFunctionDisplayClass(key, capturedLocals);
            (method.IsAsync ? _asyncGeneratorMethodFunctionDCKeys : _generatorMethodFunctionDCKeys)[method] = key;
        }
    }

    /// <summary>
    /// Emits all generator state machine bodies.
    /// Called after all functions have been defined.
    /// </summary>
    private void EmitGeneratorStateMachineBodies()
    {
        var savedPath = _modules.CurrentPath;
        var savedNamespacePath = _currentNamespacePath;
        foreach (var (funcName, smBuilder) in _generators.StateMachines)
        {
            if (_functionDefinitionModule.TryGetValue(funcName, out var fnModule))
            {
                _modules.CurrentPath = NormalizeToEmissionPath(fnModule);
            }
            // Restore the enclosing namespace (null for non-namespace functions) so the
            // MoveNext body resolves namespace-level var/let/const by bare name (#567).
            _currentNamespacePath = _functionDefinitionNamespace.GetValueOrDefault(funcName);
            var funcStmt = _generators.Functions[funcName];
            var methodBuilder = _functions.Builders[funcName];

            // Emit the stub method body (creates and returns the state machine)
            EmitIteratorFreeFunctionStub(methodBuilder, smBuilder, funcStmt, funcName);

            // Emit the MoveNext method body
            EmitGeneratorMoveNextBody(smBuilder, funcStmt, funcName);

            // Finalize the state machine type
            smBuilder.CreateType();
        }
        _modules.CurrentPath = savedPath;
        _currentNamespacePath = savedNamespacePath;
    }

    // EmitGeneratorStubMethod (free function), EmitGeneratorInstanceStubMethod, and
    // EmitGeneratorStaticStubMethod were folded into the shared EmitIteratorFreeFunctionStub /
    // EmitIteratorMethodStub (ILCompiler.IteratorStubs.cs, #1126) — byte-identical with the async
    // generator's three copies.

    /// <summary>
    /// With the state machine instance on the stack, news up the function display class (#674),
    /// stores it into the state machine's <c>&lt;&gt;__functionDC</c> field, and copies any
    /// captured-and-mutated parameters into it. Leaves the state machine reference on the stack
    /// (net stack effect zero). No-op when the generator has no function DC. Takes the raw
    /// <c>&lt;&gt;__functionDC</c> field so the sync (#674/#724) and async (#725) generator state
    /// machine builders — which share no common interface — can both reuse it.
    /// </summary>
    private void EmitGeneratorFunctionDCInit(
        ILGenerator il,
        FieldBuilder? functionDCField,
        Stmt.Function funcStmt,
        string qualifiedName,
        int paramOffset,
        System.Reflection.ParameterInfo[]? paramTypes = null)
    {
        if (functionDCField == null ||
            !_closures.FunctionDisplayClassCtors.TryGetValue(qualifiedName, out var dcCtor))
            return;

        il.Emit(OpCodes.Dup);                       // [sm, sm]
        il.Emit(OpCodes.Newobj, dcCtor);            // [sm, sm, dc]
        il.Emit(OpCodes.Stfld, functionDCField);    // [sm]

        if (!_closures.FunctionDisplayClassFields.TryGetValue(qualifiedName, out var dcFields))
            return;

        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            var paramName = funcStmt.Parameters[i].Name.Lexeme;
            if (!dcFields.TryGetValue(paramName, out var dcField))
                continue;
            il.Emit(OpCodes.Dup);                   // [sm, sm]
            il.Emit(OpCodes.Ldfld, functionDCField); // [sm, dc]
            il.Emit(OpCodes.Ldarg, i + paramOffset);           // [sm, dc, arg]
            // The DC field is object-typed; box a value-type parameter. Free-function stubs pass
            // null here (their params are already object slots); instance-method stubs pass the
            // method's actual IL parameters (methodBuilder.GetParameters()) so value types are boxed
            // before the store (#724) — and a private method's all-`object` slots are left unboxed.
            if (paramTypes != null && i < paramTypes.Length && paramTypes[i].ParameterType.IsValueType)
                il.Emit(OpCodes.Box, paramTypes[i].ParameterType);
            il.Emit(OpCodes.Stfld, dcField);        // [sm]
        }
    }

    /// <summary>
    /// Emits the MoveNext method body for a generator state machine.
    /// Uses GeneratorMoveNextEmitter to handle full generator body with yield expressions.
    /// </summary>
    private void EmitGeneratorMoveNextBody(GeneratorStateMachineBuilder smBuilder, Stmt.Function funcStmt, string qualifiedName)
    {
        var analysis = _generators.Analyzer.Analyze(funcStmt);

        // Create a compilation context for the state machine
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextMethod);
        // Check for function-level "use strict" directive
        ctx.IsStrictMode = _isStrictMode || Parsing.DirectivePrologue.HasUseStrict(funcStmt.Body);
        // Captured outer variables are read live (by reference) rather than snapshotted (#541).
        // These mirror the async-generator MoveNext context so reads/writes of top-level
        // variables go straight to their backing storage instead of a stale state-machine field.
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Per-arrow $entryPointDC field map so a capturing arrow nested in the generator body
        // gets the entry-point display class threaded in (#732). Without this the arrow's
        // $entryPointDC stays null and reading a captured top-level var NREs.
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;

        // Route reads/writes of captured-and-mutated locals through the shared function display
        // class (#674) and let capturing arrows thread it in. Only set when this generator has a
        // function DC; otherwise the existing by-value snapshot path is used unchanged.
        if (_closures.FunctionDisplayClassFields.TryGetValue(qualifiedName, out var funcDCFields))
        {
            ctx.FunctionDisplayClassFields = funcDCFields;
            ctx.CapturedFunctionLocals = [.. funcDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        }

        // Use the new emitter for full generator body emission
        var emitter = new GeneratorMoveNextEmitter(smBuilder, analysis, _types);
        emitter.EmitMoveNext(funcStmt.Body, ctx);
    }

    /// <summary>
    /// Emits the body of an instance generator method using a state machine.
    /// Called for class methods marked with IsGenerator = true.
    /// </summary>
    private void EmitGeneratorMethodBody(MethodBuilder methodBuilder, Stmt.Function method, FieldInfo? fieldsField,
        bool isInstanceMethod = true, string? currentClassName = null)
    {
        // Analyze generator function to determine yield points and hoisted variables
        var analysis = _generators.Analyzer.Analyze(method);

        // Build state machine type. A static generator method (#692) has no `this`/instance fields, so it
        // is set up like a free function (isInstanceMethod: false, static stub). The type name uses the
        // MethodBuilder's (mangled) name so a private generator's `#p` lexeme doesn't put a `#` in it (#720).
        var smBuilder = new GeneratorStateMachineBuilder(_moduleBuilder, _types, _generators.StateMachineCounter++);
        smBuilder.DefineStateMachine(
            $"{methodBuilder.DeclaringType!.Name}_{methodBuilder.Name}",
            analysis,
            isInstanceMethod: isInstanceMethod,
            runtime: _runtime
        );
        RegisterStateMachine(
            methodBuilder,
            smBuilder.StateMachineType,
            smBuilder.MoveNextMethod,
            EmittedStateMachineKind.Iterator,
            smBuilder.CurrentGetMethod,
            smBuilder.NonGenericCurrentGetMethod,
            smBuilder.ResetMethod,
            smBuilder.DisposeMethod,
            smBuilder.GetEnumeratorMethod,
            smBuilder.NonGenericGetEnumeratorMethod,
            smBuilder.NextMethod,
            smBuilder.ReturnMethod,
            smBuilder.ThrowMethod);

        // #724: wire the function display class registered for this method in DefineClass so an arrow
        // that WRITES a captured method local shares storage with the generator (mirrors the free-
        // function EmitGeneratorFunctionDCInit path). The field must be defined before the stub seeds
        // captured params into it and before CreateType() finalizes the state machine.
        string? methodDCKey = _generatorMethodFunctionDCKeys.GetValueOrDefault(method);
        if (methodDCKey != null && _closures.FunctionDisplayClasses.TryGetValue(methodDCKey, out var methodFuncDC))
            smBuilder.DefineFunctionDisplayClassField(methodFuncDC);

        // Emit stub method body (creates state machine and returns it). A static generator method
        // (#692) has no function-DC write-capture support (it is not registered in
        // RegisterGeneratorMethodFunctionDisplayClasses, so methodDCKey is null) and a write-capture
        // inside one still fail-fasts safely via the CapturedWriteAnalysis guard.
        EmitIteratorMethodStub(
            methodBuilder,
            smBuilder,
            method,
            isInstanceMethod,
            methodDCKey,
            fieldsField,
            currentClassName);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextMethod);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = isInstanceMethod;
        ctx.IsStrictMode = true;
        // ES2022 Private Class Elements support for generator methods (a private generator threads
        // its QUALIFIED class name so nested private member access resolves under modules — #720).
        ctx.CurrentClassName = currentClassName ?? methodBuilder.DeclaringType?.Name;
        ctx.CurrentClassBuilder = methodBuilder.DeclaringType as TypeBuilder;
        // Captured outer variables are read live (by reference), not snapshotted (#541).
        // TopLevelStaticVars covers module-level vars that aren't in the entry-point display class.
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Per-arrow $entryPointDC field map so a capturing arrow nested in this instance
        // generator method's body gets the entry-point display class threaded in (#732).
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;

        // #724: route reads/writes of captured-and-mutated method locals through the shared function
        // display class so the arrow's write and the generator body observe the same storage. Only set
        // when this method has a function DC; otherwise the by-value snapshot path is used unchanged.
        if (methodDCKey != null && _closures.FunctionDisplayClassFields.TryGetValue(methodDCKey, out var methodDCFields))
        {
            ctx.FunctionDisplayClassFields = methodDCFields;
            ctx.CapturedFunctionLocals = [.. methodDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        }

        // Emit MoveNext body
        var moveNextEmitter = new GeneratorMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNext(method.Body, ctx);

        // Finalize the state machine type
        smBuilder.CreateType();
    }

    // EmitGeneratorInstanceStubMethod / EmitGeneratorStaticStubMethod were folded into the shared
    // EmitIteratorMethodStub (ILCompiler.IteratorStubs.cs, #1126).
}
