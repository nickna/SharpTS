using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Async generator function compilation for the IL compiler.
/// Handles the definition and emission of async generator state machines.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines an async generator function and its state machine.
    /// </summary>
    private void DefineAsyncGeneratorFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Module-qualify the stub/registry keys (#418) so two modules that each declare a
        // same-named async-generator function don't clobber each other. Single-file
        // compilation returns the simple name unchanged. The readable state-machine type name
        // keeps the simple name (the builder's counter already disambiguates `<name>d__N`).
        string qualifiedName = GetDefinitionContext().GetQualifiedFunctionName(funcName);

        // Analyze the async generator function for yield/await points and hoisted variables
        var analysis = _asyncGenerators.Analyzer.Analyze(funcStmt);

        // #775: a free-function async generator binds its own dynamic `this`; when its body uses `this`
        // the stub captures the active dynamic receiver into <>4__this at creation time (see
        // EmitGeneratorStubMethod for the sync rationale).
        bool hasDynamicThis = analysis.UsesThis;

        // Create the state machine builder
        var smBuilder = new AsyncGeneratorStateMachineBuilder(_moduleBuilder, _types, _asyncGenerators.StateMachineCounter++);
        smBuilder.DefineStateMachine(funcName, analysis, isInstanceMethod: false, runtime: _runtime, hasDynamicThis: hasDynamicThis);

        _asyncGenerators.StateMachines[qualifiedName] = smBuilder;
        _asyncGenerators.Functions[qualifiedName] = funcStmt;

        // Record the AST node so PropagateFunctionDCRequirements can resolve arrows nested in this
        // async generator back to its qualified name, and lift captured-and-mutated locals into a
        // shared function display class so a sync arrow/callback that writes one reaches the generator
        // instead of snapshotting it by value (#725). Mirrors DefineGeneratorFunction (#674).
        _closures.FunctionAstNodes[qualifiedName] = funcStmt;
        DefineAsyncGeneratorFunctionDisplayClass(funcStmt, qualifiedName, smBuilder);

        // Define the stub method that creates and returns the state machine.
        // A trailing rest parameter is typed List<object> so the indirect
        // ($TSFunction.Invoke) call path packs trailing args into it (#426).
        var paramTypes = BuildStateMachineStubParamTypes(funcStmt);
        var methodBuilder = _programType.DefineMethod(
            qualifiedName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IAsyncEnumerableOfObject,  // Async generator returns IAsyncEnumerable<object>
            paramTypes
        );
        RegisterStateMachine(
            methodBuilder,
            smBuilder.StateMachineType,
            smBuilder.MoveNextAsyncMethod,
            EmittedStateMachineKind.AsyncIterator,
            smBuilder.CurrentGetMethod,
            smBuilder.DisposeAsyncMethod,
            smBuilder.GetAsyncEnumeratorMethod,
            smBuilder.NextMethod,
            smBuilder.ReturnMethod,
            smBuilder.ThrowMethod);

        _functions.Builders[qualifiedName] = methodBuilder;

        // #925: an async generator used as a value (imported cross-module, stored, passed as a
        // callback → $TSFunction.Invoke) must pad omitted trailing optional args with the `undefined`
        // sentinel, not CLR null — matching the other function kinds (see DefineGeneratorFunction).
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
    /// Lifts an async generator's captured-AND-mutated locals into a function-level display class so a
    /// sync arrow/callback inside the body that writes such a variable shares storage with the generator
    /// instead of snapshotting it by value (#725). The async-generator state machine is a reference type,
    /// so the DC field persists across awaits and yields. No-op when there is no write-capture, leaving
    /// fully-standalone output unchanged. Mirrors <see cref="DefineGeneratorFunctionDisplayClass"/>.
    /// </summary>
    private void DefineAsyncGeneratorFunctionDisplayClass(
        Stmt.Function funcStmt, string qualifiedName, AsyncGeneratorStateMachineBuilder smBuilder)
    {
        var mutatedCaptured = ComputeMutatedCapturedGeneratorVars(funcStmt);
        if (mutatedCaptured.Count == 0)
            return;

        RegisterFunctionDisplayClass(qualifiedName, mutatedCaptured);
        if (_closures.FunctionDisplayClasses.TryGetValue(qualifiedName, out var funcDC))
            smBuilder.DefineFunctionDisplayClassField(funcDC);
    }

    /// <summary>
    /// Emits all async generator state machine bodies.
    /// Called after all functions have been defined.
    /// </summary>
    private void EmitAsyncGeneratorStateMachineBodies()
    {
        var savedPath = _modules.CurrentPath;
        var savedNamespacePath = _currentNamespacePath;
        foreach (var (funcName, smBuilder) in _asyncGenerators.StateMachines)
        {
            if (_functionDefinitionModule.TryGetValue(funcName, out var fnModule))
            {
                _modules.CurrentPath = NormalizeToEmissionPath(fnModule);
            }
            // Restore the enclosing namespace (null for non-namespace functions) so the
            // MoveNext body resolves namespace-level var/let/const by bare name (#567).
            _currentNamespacePath = _functionDefinitionNamespace.GetValueOrDefault(funcName);
            var funcStmt = _asyncGenerators.Functions[funcName];
            var methodBuilder = _functions.Builders[funcName];

            // Emit the stub method body (creates and returns the state machine)
            EmitIteratorFreeFunctionStub(methodBuilder, smBuilder, funcStmt, funcName);

            // Emit the MoveNextAsync method body
            EmitAsyncGeneratorMoveNextAsyncBody(smBuilder, funcStmt, funcName);

            // Finalize the state machine type
            smBuilder.CreateType();
        }
        _modules.CurrentPath = savedPath;
        _currentNamespacePath = savedNamespacePath;
    }

    // EmitAsyncGeneratorStubMethod (free function), EmitAsyncGeneratorInstanceStubMethod, and
    // EmitAsyncGeneratorStaticStubMethod were folded into the shared EmitIteratorFreeFunctionStub /
    // EmitIteratorMethodStub (ILCompiler.IteratorStubs.cs, #1126) — byte-identical with the sync
    // generator's three copies.

    /// <summary>
    /// Emits the MoveNextAsync method body for an async generator state machine.
    /// Uses AsyncGeneratorMoveNextEmitter to handle full generator body with yield and await expressions.
    /// </summary>
    private void EmitAsyncGeneratorMoveNextAsyncBody(AsyncGeneratorStateMachineBuilder smBuilder, Stmt.Function funcStmt, string qualifiedName)
    {
        var analysis = _asyncGenerators.Analyzer.Analyze(funcStmt);

        // Create a compilation context for the state machine
        var il = smBuilder.MoveNextAsyncMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextAsyncMethod);
        // Captured top-level variables (entry-point display class)
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Per-arrow $entryPointDC field map so a capturing arrow nested in the async generator body
        // gets the entry-point display class threaded in (#725 / async analog of #732).
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;

        // Route reads/writes of captured-and-mutated locals through the shared function display class
        // (#725) and let capturing arrows thread it in. Only set when this async generator has a
        // function DC; otherwise the existing by-value snapshot path is used unchanged.
        if (_closures.FunctionDisplayClassFields.TryGetValue(qualifiedName, out var funcDCFields))
        {
            ctx.FunctionDisplayClassFields = funcDCFields;
            ctx.CapturedFunctionLocals = [.. funcDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        }

        // Use the new emitter for full async generator body emission
        var emitter = new AsyncGeneratorMoveNextEmitter(smBuilder, analysis, _types);
        emitter.EmitMoveNextAsync(funcStmt.Body, ctx);
    }

    /// <summary>
    /// Emits the body of an instance async generator method using a state machine.
    /// Called for class methods marked with both IsAsync and IsGenerator = true.
    /// </summary>
    private void EmitAsyncGeneratorMethodBody(MethodBuilder methodBuilder, Stmt.Function method, FieldInfo? fieldsField,
        bool isInstanceMethod = true, string? currentClassName = null)
    {
        // Analyze async generator function to determine yield/await points and hoisted variables
        var analysis = _asyncGenerators.Analyzer.Analyze(method);

        // Build state machine type. A static async generator method (#778) has no `this`/instance fields,
        // so it is set up like a free function (isInstanceMethod: false, static stub). Use the
        // MethodBuilder's (mangled) name rather than method.Name.Lexeme so a private async generator's
        // `#p` lexeme doesn't put a `#` in the name.
        var smBuilder = new AsyncGeneratorStateMachineBuilder(_moduleBuilder, _types, _asyncGenerators.StateMachineCounter++);
        smBuilder.DefineStateMachine(
            $"{methodBuilder.DeclaringType!.Name}_{methodBuilder.Name}",
            analysis,
            isInstanceMethod: isInstanceMethod,
            runtime: _runtime
        );
        RegisterStateMachine(
            methodBuilder,
            smBuilder.StateMachineType,
            smBuilder.MoveNextAsyncMethod,
            EmittedStateMachineKind.AsyncIterator,
            smBuilder.CurrentGetMethod,
            smBuilder.DisposeAsyncMethod,
            smBuilder.GetAsyncEnumeratorMethod,
            smBuilder.NextMethod,
            smBuilder.ReturnMethod,
            smBuilder.ThrowMethod);

        // #725: wire the function display class registered for this async generator method in
        // DefineClass so a sync arrow that writes a captured method local shares storage with the
        // generator. The field must be defined before the stub seeds captured params into it and
        // before CreateType() finalizes the state machine.
        string? methodDCKey = _asyncGeneratorMethodFunctionDCKeys.GetValueOrDefault(method);
        if (methodDCKey != null && _closures.FunctionDisplayClasses.TryGetValue(methodDCKey, out var methodFuncDC))
            smBuilder.DefineFunctionDisplayClassField(methodFuncDC);

        // Emit stub method body (creates state machine and returns it). A static async generator method
        // (#778) has no `this` and no function-DC write-capture support (it is not registered in
        // RegisterGeneratorMethodFunctionDisplayClasses, so methodDCKey is null), mirroring the sync
        // static generator (#692).
        EmitIteratorMethodStub(
            methodBuilder,
            smBuilder,
            method,
            isInstanceMethod,
            isInstanceMethod ? methodDCKey : null,
            fieldsField,
            currentClassName);

        // Create context for MoveNextAsync emission
        var il = smBuilder.MoveNextAsyncMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextAsyncMethod);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = isInstanceMethod;
        ctx.IsStrictMode = _isStrictMode || Parsing.DirectivePrologue.HasUseStrict(method.Body);
        // ES2022 Private Class Elements support for async generator methods (a private async
        // generator threads its QUALIFIED class name so nested private member access resolves — #720).
        ctx.CurrentClassName = currentClassName ?? methodBuilder.DeclaringType?.Name;
        ctx.CurrentClassBuilder = methodBuilder.DeclaringType as TypeBuilder;
        // Entry-point display class for captured top-level variables
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Per-arrow $entryPointDC field map so a capturing arrow nested in this instance async
        // generator method's body gets the entry-point display class threaded in (#725).
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;

        // #725: route reads/writes of captured-and-mutated method locals through the shared function
        // display class so the arrow's write and the generator body observe the same storage. Only set
        // when this method has a function DC; otherwise the by-value snapshot path is used unchanged.
        if (methodDCKey != null && _closures.FunctionDisplayClassFields.TryGetValue(methodDCKey, out var methodDCFields))
        {
            ctx.FunctionDisplayClassFields = methodDCFields;
            ctx.CapturedFunctionLocals = [.. methodDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        }

        // Emit MoveNextAsync body
        var moveNextEmitter = new AsyncGeneratorMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNextAsync(method.Body, ctx);

        // Finalize the state machine type
        smBuilder.CreateType();
    }

}
