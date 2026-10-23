using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed class ForwardClassDeclaration(
        Stmt.Class declaration, TypeEnvironment environment, int declarationId,
        ParsedModule? module, string? filePath, SourceDocument? standaloneSourceDocument)
    {
        public Stmt.Class Declaration { get; } = declaration;
        public TypeEnvironment Environment { get; } = environment;
        public TypeInfo.MutableClass Shape { get; } = new(declaration.Name.Lexeme, declarationId);
        public ParsedModule? Module { get; } = module;
        public string? FilePath { get; } = filePath;
        public SourceDocument? StandaloneSourceDocument { get; } = standaloneSourceDocument;
        public TypeInfo? Constructor { get; set; }
        public bool IsResolving { get; set; }
        public Dictionary<BindingSymbol, bool>? DeclarationFlow { get; set; }
    }

    // The binding, rather than the spelling at the use site, owns the forward declaration. In
    // particular a parameter/local class named Later must not select an outer Later's constructor.
    private readonly Dictionary<BindingSymbol, ForwardClassDeclaration> _forwardClassDeclarations =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeInfo.MutableClass, ForwardClassDeclaration> _forwardClassTypeShadows =
        new(ReferenceEqualityComparer.Instance);
    private int _forwardClassSignatureDepth;
    private Token? _recoverableClassHeritageRead;
    private readonly Stack<Dictionary<BindingSymbol, bool>?> _deferredClassDefaultFlows = new();
    private readonly Dictionary<Expr.ArrowFunction, Dictionary<BindingSymbol, bool>> _immediateClassCallables =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Dictionary<BindingSymbol, bool>, Dictionary<BindingSymbol, bool>> _immediateClassFlows =
        new(ReferenceEqualityComparer.Instance);

    private BindingSymbol RegisterForwardClassDeclaration(Stmt.Class declaration)
    {
        BindingSymbol symbol = RegisterValueDeclaration(declaration.Name);
        if (!declaration.IsDeclare)
        {
            if (!_forwardClassDeclarations.TryGetValue(symbol, out var forward) ||
                !ReferenceEquals(forward.Environment, _environment))
                _forwardClassDeclarations[symbol] = forward = new(declaration, _environment, -symbol.Id,
                    _currentModule, _filePath, _standaloneSourceDocument);
            _definiteAssignmentStack.TryPeek(out var declarationFlow);
            forward.DeclarationFlow = declarationFlow;
            // Shadow an existing outer type with this declaration's shell, so
            // annotations and deferred constructor reads select the same class.
            // Preserve existing unresolved annotation and generic handling.
            if (declaration.TypeParams is not { Count: > 0 } &&
                !_environment.IsTypeDefinedLocally(declaration.Name.Lexeme) &&
                _environment.GetTypeBinding(declaration.Name.Lexeme) is not null)
            {
                _environment.DefineType(declaration.Name.Lexeme, forward.Shape);
                _forwardClassTypeShadows[forward.Shape] = forward;
            }
        }
        return symbol;
    }

    private void CheckClassValueDeclarationOrder(Token use, BindingSymbol symbol)
    {
        if (ReferenceEquals(use, _recoverableClassHeritageRead) ||
            !_forwardClassDeclarations.TryGetValue(symbol, out var forward))
            return;

        if (IsClassValueReadBeforeDeclaration(use, forward))
            throw new TypeCheckException($"Class '{use.Lexeme}' used before its declaration.",
                use.Line, tsCode: "TS2449");
    }

    private bool IsClassValueReadBeforeDeclaration(Token use, ForwardClassDeclaration forward)
    {
        // Offsets belong to one parsed source. Scripts can share a flow frame,
        // and imported aliases can retain a binding from another source.
        if (!ReferenceEquals(forward.Module, _currentModule) ||
            forward.Module is null &&
            !ReferenceEquals(forward.StandaloneSourceDocument, _standaloneSourceDocument))
            return false;

        Token declaration = forward.Declaration.Name;
        // REPL submissions can combine separately parsed ASTs without documents.
        // A retained document/module remains authoritative for re-lexed fragments.
        if (forward.Module is null && forward.StandaloneSourceDocument is null &&
            use.SourceIdentity is { } useSource && declaration.SourceIdentity is { } declarationSource &&
            !ReferenceEquals(useSource, declarationSource))
            return false;

        if (!_definiteAssignmentStack.TryPeek(out var currentFlow)) return false;

        while (!ReferenceEquals(forward.DeclarationFlow, currentFlow))
        {
            if (_deferredClassDefaultFlows.Contains(currentFlow) ||
                !_immediateClassFlows.TryGetValue(currentFlow, out currentFlow))
                return false;
        }
        if (_deferredClassDefaultFlows.Contains(currentFlow)) return false;

        // Instance initializers run when their owner is constructed. Class
        // declarations inside a nested callable still have their own eager flow.
        for (var scope = _deferredInitializerFlowBoundary; scope != null; scope = scope.Enclosing)
            if (ReferenceEquals(scope, forward.Environment))
                return false;

        // Synthetic AST tokens have no source offset; their diagnostic line
        // does not establish declaration order in the surrounding parsed source.
        return use.Start >= 0 && declaration.Start >= 0 && use.Start < declaration.Start;
    }

    private TypeInfo CheckDeferredClassDefault(Expr expression, TypeInfo? contextualType = null)
    {
        _definiteAssignmentStack.TryPeek(out var currentFlow);
        _deferredClassDefaultFlows.Push(currentFlow);
        try
        {
            return contextualType is null ? CheckExpr(expression) : CheckExprWithContext(expression, contextualType);
        }
        finally { _deferredClassDefaultFlows.Pop(); }
    }

    private TypeInfo CheckClassOrderCallTarget(Expr callee)
    {
        Expr target = callee;
        while (target is Expr.Grouping grouping) target = grouping.Expression;
        if (target is not Expr.ArrowFunction arrow ||
            !_definiteAssignmentStack.TryPeek(out var callerFlow))
            return CheckExpr(callee);

        _immediateClassCallables.TryGetValue(arrow, out var previous);
        _immediateClassCallables[arrow] = callerFlow;
        try { return CheckExpr(callee); }
        finally
        {
            if (previous is null) _immediateClassCallables.Remove(arrow);
            else _immediateClassCallables[arrow] = previous;
        }
    }

    private TypeInfo ResolveForwardClassConstructor(Token use, TypeInfo declared)
    {
        // Only deferred bodies (or an enclosing constructor signature being resolved for one)
        // need a forward value type. Direct reads and heritage retain their source-order checks.
        if (declared is not TypeInfo.Any ||
            _currentFunctionReturnType is null && _forwardClassSignatureDepth == 0 ||
            _environment.GetValueBinding(use.Lexeme) is not { } binding ||
            !_forwardClassDeclarations.TryGetValue(binding, out var forward))
            return declared;

        return ResolveForwardClassSignature(forward);
    }

    private void EnsureForwardClassTypeSignature(TypeInfo declared)
    {
        if (declared is TypeInfo.MutableClass shape &&
            _forwardClassTypeShadows.TryGetValue(shape, out var forward))
            ResolveForwardClassSignature(forward);
    }

    private TypeInfo ResolveForwardClassSignature(ForwardClassDeclaration forward)
    {
        if (forward.Constructor is { } completed) return completed;
        if (forward.IsResolving) return forward.Shape.Freeze();

        forward.IsResolving = true;
        _forwardClassSignatureDepth++;
        ParsedModule? previousModule = _currentModule;
        string? previousFilePath = _filePath;
        SourceDocument? previousStandaloneSourceDocument = _standaloneSourceDocument;
        try
        {
            _currentModule = forward.Module;
            _filePath = forward.FilePath;
            _standaloneSourceDocument = forward.StandaloneSourceDocument;
            Stmt.Class declaration = forward.Declaration;
            var shape = forward.Shape;
            using var declarationScope = new EnvironmentScope(this, forward.Environment);
            // Reuse this shell in the authoritative class pass. Instances in an early signature
            // retain the declaration's identity and follow its final frozen instance shape.
            forward.Environment.DefineType(declaration.Name.Lexeme, shape);
            var (signatureEnvironment, typeParameters, superclass) = CreateClassTypeEnvironment(declaration);
            shape.Superclass = superclass;
            shape.IsAbstract = declaration.IsAbstract;
            RegisterSourceClassMembers(declaration, shape);
            signatureEnvironment.Define(declaration.Name.Lexeme, shape);
            signatureEnvironment.DefineType(declaration.Name.Lexeme, shape);
            signatureEnvironment.Define("this", new TypeInfo.Instance(shape));
            using (new EnvironmentScope(this, signatureEnvironment))
            {
                CollectForwardClassSignatures(declaration, shape);
            }
            shape.ResetFrozenCache();
            return forward.Constructor = typeParameters is { Count: > 0 }
                ? shape.FreezeGeneric(typeParameters)
                : shape.Freeze();
        }
        finally
        {
            _forwardClassSignatureDepth--;
            forward.IsResolving = false;
            _currentModule = previousModule;
            _filePath = previousFilePath;
            _standaloneSourceDocument = previousStandaloneSourceDocument;
        }
    }

    // Collect only declaration signatures. Decorators, computed-key expressions, default values,
    // initializers and bodies are checked once, at the declaration's normal source position.
    private void CollectForwardClassSignatures(Stmt.Class declaration, TypeInfo.MutableClass shape)
    {
        CollectClassIndexSignatures(declaration, shape);
        var methodGroups = declaration.Methods.Select(method => (
                Method: method,
                Name: method.ComputedKey is null ? method.Name.Lexeme : TryGetWellKnownSymbolMemberName(method.ComputedKey)))
            .Where(candidate => candidate.Name is not null)
            .GroupBy(candidate => (Name: candidate.Name!, candidate.Method.IsStatic, candidate.Method.IsPrivate));
        foreach (var group in methodGroups)
        {
            string name = group.Key.Name;
            var methodsInGroup = group.Select(candidate => candidate.Method).ToList();
            var method = methodsInGroup.FirstOrDefault(candidate => candidate.Body is not null) ?? methodsInGroup[0];
            var signature = BuildForwardClassMethodSignature(method);
            var methods = method.IsPrivate
                ? method.IsStatic ? shape.StaticPrivateMethods : shape.PrivateMethods
                : method.IsStatic ? shape.StaticMethods : shape.Methods;
            var overloads = methodsInGroup.Where(candidate => candidate.Body is null && !candidate.IsAbstract)
                .Select(BuildForwardClassMethodSignature).ToList();
            methods[name] = overloads.Count > 0 ? BuildClassMethodOverload(overloads, signature) : signature;
            (method.IsStatic ? shape.StaticMethodAccess : shape.MethodAccess)[name] = method.Access;

            if (method.Name.Lexeme == "constructor")
                foreach (var parameter in method.Parameters.Where(parameter => parameter.IsParameterProperty))
                    shape.FieldTypes[parameter.Name.Lexeme] = ResolveAnnotation(parameter.Type, parameter.TypeAnnotationNode, parameter)
                        ?? TypeInfo.Any.Shared;
        }
        foreach (var field in declaration.Fields)
        {
            string? name = field.ComputedKey is null ? field.Name.Lexeme : TryGetWellKnownSymbolMemberName(field.ComputedKey);
            if (name is null) continue;
            TypeInfo type = ResolveAnnotation(field.TypeAnnotation, field.TypeAnnotationNode, field)
                ?? (field.Initializer is Expr.Literal literal ? WidenLiteralType(GetLiteralType(literal.Value)) : TypeInfo.Any.Shared);
            var fields = field.IsPrivate
                ? field.IsStatic ? shape.StaticPrivateFields : shape.PrivateFields
                : field.IsStatic ? shape.StaticProperties : shape.FieldTypes;
            fields[name] = type;
            (field.IsStatic ? shape.StaticFieldAccess : shape.FieldAccess)[name] = field.Access;
            if (field.IsReadonly) (field.IsStatic ? shape.StaticReadonlyFields : shape.ReadonlyFields).Add(name);
        }
        foreach (var accessor in declaration.Accessors ?? [])
        {
            string? name = accessor.ComputedKey is null ? accessor.Name.Lexeme : TryGetWellKnownSymbolMemberName(accessor.ComputedKey);
            if (name is null) continue;
            PublishAccessorReadAccess(shape, accessor, name);
            if (accessor.Kind.Type == TokenType.GET)
                (accessor.IsStatic ? shape.StaticGetters : shape.Getters)[name] =
                    ResolveAnnotation(accessor.ReturnType, accessor.ReturnTypeNode, accessor, EditorAnnotationSlot.Return) ?? TypeInfo.Any.Shared;
            else
                (accessor.IsStatic ? shape.StaticSetters : shape.Setters)[name] = accessor.SetterParam is { } parameter
                    ? ResolveAnnotation(parameter.Type, parameter.TypeAnnotationNode, parameter) ?? TypeInfo.Any.Shared
                    : TypeInfo.Any.Shared;
        }
        PublishStaticAccessorTypes(shape);
        foreach (var accessor in declaration.AutoAccessors ?? [])
        {
            string name = accessor.Name.Lexeme;
            TypeInfo type = ResolveAnnotation(accessor.TypeAnnotation, accessor.TypeAnnotationNode, accessor)
                ?? (accessor.Initializer is Expr.Literal literal ? WidenLiteralType(GetLiteralType(literal.Value)) : TypeInfo.Any.Shared);
            (accessor.IsStatic ? shape.StaticGetters : shape.Getters)[name] = type;
            if (!accessor.IsReadonly)
                (accessor.IsStatic ? shape.StaticSetters : shape.Setters)[name] = type;
            (accessor.IsStatic ? shape.StaticFieldAccess : shape.FieldAccess)[name] = accessor.Access;
            shape.AutoAccessors.Add(name);
        }
        PublishStaticAccessorTypes(shape);
    }

    private TypeInfo BuildForwardClassMethodSignature(Stmt.Function method) =>
        BuildClassMethodSignature(method, preparatory: true, wrapGeneratorReturn: method.ComputedKey is null);
}
