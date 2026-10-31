using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private HashSet<string> GetGeneratorExpressionAncestorCaptures(Stmt.Function owner)
    {
        var collector = new GeneratorExpressionAncestorCaptureCollector(_closures.Analyzer, owner);
        if (owner.Body is not null)
            foreach (var statement in owner.Body) collector.Visit(statement);
        return collector.Names;
    }

    private sealed class GeneratorExpressionAncestorCaptureCollector(ClosureAnalyzer analyzer, object owner) : AstVisitorBase
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.IsGenerator)
            {
                var cells = analyzer.GetClosureCellFields(expression);
                foreach (var name in analyzer.GetCaptures(expression))
                    if (!cells.Contains(name) && ReferenceEquals(analyzer.GetCaptureSource(expression, name), owner))
                        Names.Add(name);
            }
            base.VisitArrowFunction(expression);
        }
    }

    private void DefineGeneratorExpressionStateMachines()
    {
        foreach (var (source, captures) in _collectedArrows)
        {
            if (!source.IsGenerator) continue;
            var target = _closures.DisplayClasses[source];
            var kickoff = _closures.ArrowMethods[source];
            string stateName = $"<>GeneratorExpression{_closures.GeneratorExpressions.Count}";
            var name = source.Name ?? new Token(TokenType.IDENTIFIER,
                stateName, null, 0);
            var shape = new Stmt.Function(name, source.TypeParams, source.ThisType,
                source.Parameters, source.BlockBody, source.ReturnType,
                IsAsync: source.IsAsync, IsGenerator: true, HasDynamicThis: source.HasOwnThis,
                ThisTypeNode: source.ThisTypeNode, ReturnTypeNode: source.ReturnTypeNode,
                RuntimeName: source.RuntimeName ?? source.Name?.Lexeme ?? "");

            StateMachineBuilderBase machine;
            if (source.IsAsync)
            {
                var analysis = _asyncGenerators.Analyzer.Analyze(shape, EmitDebugSymbols,
                    outerCaptureNames: _closures.Analyzer.GetCaptures(source));
                var builder = new AsyncGeneratorStateMachineBuilder(
                    _moduleBuilder, _types, _asyncGenerators.StateMachineCounter++);
                ConfigureDebugStateMachineOwner(builder, target, kickoff.Name);
                ConfigureDebugHoistedBindings(builder, shape.Parameters, shape.Body, analysis.BlockScopeRenames);
                builder.DefineStateMachine(stateName, analysis, runtime: _runtime, hasDynamicThis: true);
                RegisterStateMachine(kickoff, builder.StateMachineType, builder.MoveNextAsyncMethod,
                    EmittedStateMachineKind.AsyncIterator, builder.CurrentGetMethod,
                    builder.DisposeAsyncMethod, builder.GetAsyncEnumeratorMethod,
                    builder.NextMethod, builder.ReturnMethod, builder.ThrowMethod);
                machine = builder;
            }
            else
            {
                var analysis = _generators.Analyzer.Analyze(shape, EmitDebugSymbols,
                    outerCaptureNames: _closures.Analyzer.GetCaptures(source));
                var builder = new GeneratorStateMachineBuilder(
                    _moduleBuilder, _types, _generators.StateMachineCounter++);
                ConfigureDebugStateMachineOwner(builder, target, kickoff.Name);
                ConfigureDebugHoistedBindings(builder, shape.Parameters, shape.Body, analysis.BlockScopeRenames);
                builder.DefineStateMachine(stateName, analysis, runtime: _runtime, hasDynamicThis: true);
                RegisterStateMachine(kickoff, builder.StateMachineType, builder.MoveNextMethod,
                    EmittedStateMachineKind.Iterator, builder.CurrentGetMethod,
                    builder.NonGenericCurrentGetMethod, builder.ResetMethod,
                    builder.DisposeMethod, builder.GetEnumeratorMethod,
                    builder.NonGenericGetEnumeratorMethod, builder.NextMethod,
                    builder.ReturnMethod, builder.ThrowMethod);
                machine = builder;
            }

            if (_closures.ArrowScopeDisplayClasses.TryGetValue(source, out var ownScope))
            {
                if (machine is GeneratorStateMachineBuilder sync) sync.DefineFunctionDisplayClassField(ownScope);
                else ((AsyncGeneratorStateMachineBuilder)machine).DefineFunctionDisplayClassField(ownScope);
            }

            var environment = machine.StateMachineType.DefineField(
                "<>__expressionEnvironment", target, FieldAttributes.Public);
            var functionValue = target.DefineField("<>__generatorFunction", _types.Object, FieldAttributes.Public);
            var paths = GetGeneratorExpressionCaptures(source, captures);
            _closures.GeneratorExpressions.Add(source, new(source, shape, target,
                _closures.DisplayClassConstructors[source], kickoff, machine,
                environment, functionValue, paths));
            if (ReferencesArgumentsIdentifier(shape.Body, shape.Parameters))
            {
                _functions.MethodsCapturingArguments.Add(kickoff);
                if (_runtime.FunctionAttributes.CapturesArgumentsCtor != null)
                    kickoff.SetCustomAttribute(_runtime.FunctionAttributes.CapturesArgumentsCtor, CustomAttributeEncoder.EmptyBlob);
            }
        }
    }

    private IReadOnlyDictionary<string, GeneratorExpressionCapture> GetGeneratorExpressionCaptures(
        Expr.ArrowFunction source, IEnumerable<string> names)
    {
        Dictionary<string, GeneratorExpressionCapture> paths = new(StringComparer.Ordinal);
        Dictionary<Type, FieldBuilder> additionalOwners = [];
        var fields = _closures.DisplayClassFields[source];
        var cells = _closures.Analyzer.GetClosureCellFields(source);
        var liveFields = GetStandaloneLiveCaptureFields(source, names);
        foreach (var name in names)
        {
            if (cells.Contains(name) && fields.TryGetValue(name, out var cell))
            {
                paths[name] = new([cell], true);
                continue;
            }
            if (source.Name?.Lexeme == name && fields.TryGetValue(name, out var self))
            {
                paths[name] = new([self], false);
                continue;
            }

            object? owner = _closures.Analyzer.GetCaptureSource(source, name);
            if (owner != null)
            {
                if (_closures.ArrowScopeDCFields.TryGetValue(source, out var arrowReference)
                    && _closures.ArrowScopeDCSource.TryGetValue(source, out var arrowOwner)
                    && ReferenceEquals(owner, arrowOwner)
                    && _closures.ArrowScopeDisplayClassFields.TryGetValue(owner, out var arrowFields)
                    && arrowFields.TryGetValue(name, out var arrowField))
                {
                    paths[name] = new([arrowReference, arrowField], false);
                    continue;
                }
                if (_arrowScopeDCExtraFields.TryGetValue(source, out var extraOwners)
                    && extraOwners.TryGetValue(owner, out var extraReference)
                    && _closures.ArrowScopeDisplayClassFields.TryGetValue(owner, out var extraFields)
                    && extraFields.TryGetValue(name, out var extraField))
                {
                    paths[name] = new([extraReference, extraField], false);
                    continue;
                }
                if (_closures.ArrowFunctionDCFields.TryGetValue(source, out var functionReference))
                {
                    foreach (var (key, callable) in _closures.FunctionAstNodes)
                    {
                        if (ReferenceEquals(owner, callable)
                            && _closures.FunctionDisplayClassFields.TryGetValue(key, out var functionFields)
                            && functionFields.TryGetValue(name, out var functionField)
                            && functionReference.FieldType == functionField.DeclaringType)
                        {
                            paths[name] = new([functionReference, functionField], false);
                            break;
                        }
                    }
                    if (paths.ContainsKey(name)) continue;
                }
                if (liveFields.TryGetValue(name, out var liveField)
                    && liveField.DeclaringType is { } liveOwner)
                {
                    // A nested async arrow can relay its own scope DC while a
                    // different capture belongs to a more distant callable.
                    // Carry that actual owner independently of the primary DC.
                    if (!additionalOwners.TryGetValue(liveOwner, out var reference))
                    {
                        reference = _closures.DisplayClasses[source].DefineField(
                            $"<>__captureOwner{additionalOwners.Count}", liveOwner, FieldAttributes.Public);
                        additionalOwners.Add(liveOwner, reference);
                    }
                    paths[name] = new([reference, liveField], false);
                    continue;
                }
            }
            if (name is CompilationContext.SuperOwnerCaptureName or CompilationContext.NewTargetCaptureName or "this"
                && fields.TryGetValue(name, out var metadata))
            {
                paths[name] = new([metadata], false);
                continue;
            }
            // Module bindings retain their existing live resolution. A local
            // fallback slot would only hold a snapshot, so require its planned
            // declaration cell or authoritative owner path instead.
            if (owner != null)
                throw new CompileException($"Generator expression capture '{name}' has no live storage path.");
        }
        return paths;
    }

    private void EmitGeneratorExpressionBodies()
    {
        var savedPath = _modules.CurrentPath;
        var savedNamespace = _currentNamespacePath;
        try
        {
            foreach (var info in _closures.GeneratorExpressions.Values)
            {
                _modules.CurrentPath = NormalizeToEmissionPath(_arrowToModule.GetValueOrDefault(info.Source));
                _currentNamespacePath = _arrowToNamespace.GetValueOrDefault(info.Source);
                EmitIteratorExpressionStub(info);

                if (info.StateMachine is GeneratorStateMachineBuilder sync)
                {
                    var context = CreateGeneratorExpressionBodyContext(info, sync.MoveNextMethod);
                    var analysis = _generators.Analyzer.Analyze(info.FunctionShape, EmitDebugSymbols,
                        outerCaptureNames: _closures.Analyzer.GetCaptures(info.Source));
                    new GeneratorMoveNextEmitter(sync, analysis, _types).EmitMoveNext(info.Source.BlockBody ?? [], context);
                }
                else
                {
                    var async = (AsyncGeneratorStateMachineBuilder)info.StateMachine;
                    var context = CreateGeneratorExpressionBodyContext(info, async.MoveNextAsyncMethod);
                    var analysis = _asyncGenerators.Analyzer.Analyze(info.FunctionShape, EmitDebugSymbols,
                        outerCaptureNames: _closures.Analyzer.GetCaptures(info.Source));
                    new AsyncGeneratorMoveNextEmitter(async, analysis, _types).EmitMoveNextAsync(info.Source.BlockBody ?? [], context);
                }
                info.StateMachine.CreateType();
            }
        }
        finally
        {
            _modules.CurrentPath = savedPath;
            _currentNamespacePath = savedNamespace;
        }
    }

    private CompilationContext CreateGeneratorExpressionBodyContext(GeneratorExpressionCompilation info, MethodBuilder method)
    {
        var context = CreateModuleMemberContext(method.GetILGenerator(), method);
        ApplyIteratorParameterCaptureCells(context, (IIteratorStateMachineBuilder)info.StateMachine);
        context.CurrentGeneratorExpression = info;
        context.IsStrictMode = _strictArrows.Contains(info.Source) || _isStrictMode
            || BodyDeclaresUseStrict(info.Source.BlockBody);
        context.CurrentClassName = _async.ArrowEnclosingClassNames.GetValueOrDefault(info.Source);
        if (context.CurrentClassName != null)
        {
            context.CurrentClassBuilder = _classes.Builders.GetValueOrDefault(context.CurrentClassName);
            if (context.CurrentClassBuilder == null)
            {
                context.CurrentClassExpr = _classExprs.Names.FirstOrDefault(entry => entry.Value == context.CurrentClassName).Key;
                if (context.CurrentClassExpr != null)
                    context.CurrentClassBuilder = _classExprs.Builders.GetValueOrDefault(context.CurrentClassExpr);
            }
        }
        context.IsStaticSuperContext = _arrowsInStaticClassContext.Contains(info.Source);
        context.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields;
        context.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields;
        context.ArrowScopeDCFields = _closures.ArrowScopeDCFields;
        context.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields;
        context.AsyncArrowBuilders = _async.ArrowBuilders;
        ApplyCapturedTopLevelVariableAccess(context);
        ApplyCommonJsModuleAccess(context);
        ApplyInnerFunctionSupport(context);
        if (_closures.ArrowScopeDisplayClassFields.TryGetValue(info.Source, out var ownFields))
        {
            context.FunctionDisplayClassFields = ownFields;
            context.CapturedFunctionLocals = [.. ownFields.Keys];
        }
        return context;
    }
}
