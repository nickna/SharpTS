using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record ClassMethodSignature(TypeInfo.Function Body, List<TypeInfo.TypeParameter>? Parameters);
    private readonly Dictionary<Stmt.Function, ClassMethodSignature> _classMethodSignatures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeInfo.Function, List<TypeInfo.TypeParameter>?> _classMethodImplementationParameters = new(ReferenceEqualityComparer.Instance);

    private TypeInfo BuildClassMethodSignature(Stmt.Function method, bool preparatory = false,
        bool wrapGeneratorReturn = true, bool validateDefaults = true)
    {
        var signatureEnvironment = new TypeEnvironment(_environment);
        using var scope = new EnvironmentScope(this, signatureEnvironment);
        if (!preparatory) ValidateCircularTypeParameterConstraints(method.TypeParams);
        List<TypeInfo.TypeParameter>? typeParameters = method.TypeParams is { Count: > 0 } declarations
            ? BuildGenericTypeParameters(declarations, signatureEnvironment) : null;
        var (parameters, required, rest, names) = BuildFunctionSignature(method.Parameters,
            validateDefaults: !preparatory && validateDefaults, contextName: $"method '{method.Name.Lexeme}'", editorOwner: method,
            inspectDestructuredDefaults: !preparatory,
            deferClassContextDefaults: !preparatory && !validateDefaults);
        for (int i = 0; i < names.Count; i++) signatureEnvironment.Define(names[i], parameters[i]);
        TypeInfo result = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode, method, EditorAnnotationSlot.Return)
            ?? (preparatory ? TypeInfo.Any.Shared : TypeInfo.Inferred.Shared);
        if (wrapGeneratorReturn && method.ReturnType is not null && method.IsGenerator)
            result = method.IsAsync && result is not TypeInfo.AsyncGenerator ? new TypeInfo.AsyncGenerator(result)
                : !method.IsAsync && result is not TypeInfo.Generator ? new TypeInfo.Generator(result) : result;
        var body = new TypeInfo.Function(parameters, result, required, rest,
            ResolveAnnotation(method.ThisType, method.ThisTypeNode, method, EditorAnnotationSlot.This), names);
        _classMethodSignatures[method] = new(body, typeParameters);
        _classMethodImplementationParameters[body] = typeParameters;
        TypeInfo callable = ClassMethodCallable(body, typeParameters);
        _typeMap.SetClassMethodImplementation(method, body, callable);
        RegisterEditorSignature(body, method, method.Name);
        RegisterEditorSignature(callable, method, method.Name);
        return callable;
    }

    private bool CheckClassMethodParameterDefaults(IReadOnlyList<Stmt.Function> methods,
        TypeInfo.MutableClass owner, TypeEnvironment classEnvironment)
    {
        var methodsWithDefaults = methods.Where(method =>
            method.Parameters.Any(parameter => parameter.DefaultValue is not null ||
                parameter.DestructuredProperties?.Any(property => property.DefaultValue is not null) == true) ||
            method.Body?.Any(statement => statement is Stmt.Sequence { ParameterName: not null }) == true).ToList();
        if (methodsWithDefaults.Count == 0) return false;

        owner.ResetFrozenCache();
        var classType = owner.Freeze();
        var previousClass = _currentClass;
        var previousThis = _currentFunctionThisType;
        bool previousStatic = _inStaticMethod;
        bool previousStaticBlock = _inStaticBlock;
        bool previousGlobalThis = _currentArrowCapturesScriptGlobalThis;
        _privateNameEnclosingClasses.Push(previousClass);
        _currentClass = classType;
        bool refined = false;
        try
        {
            // Publish every completed parameter list before checking defaults that can
            // call a later method whose class-dependent pattern types were deferred.
            CheckParameterPhase(validateDefaults: false);
            if (refined)
            {
                owner.ResetFrozenCache();
                classType = owner.Freeze();
                _currentClass = classType;
                _compatibilityCache = null;
                _identityCompatibilityCache = null;
            }
            CheckParameterPhase(validateDefaults: true);
        }
        finally
        {
            _privateNameEnclosingClasses.Pop();
            _currentClass = previousClass;
            _currentFunctionThisType = previousThis;
            _inStaticMethod = previousStatic;
            _inStaticBlock = previousStaticBlock;
            _currentArrowCapturesScriptGlobalThis = previousGlobalThis;
        }
        if (refined)
        {
            owner.ResetFrozenCache();
            _compatibilityCache = null;
            _identityCompatibilityCache = null;
        }
        return refined;

        void CheckParameterPhase(bool validateDefaults)
        {
            foreach (var method in methodsWithDefaults)
            {
                var environment = new TypeEnvironment(classEnvironment);
                _inStaticMethod = method.IsStatic;
                _inStaticBlock = false;
                _currentArrowCapturesScriptGlobalThis = false;
                _currentFunctionThisType = method.IsStatic ? classType : new TypeInfo.Instance(owner);
                environment.Define("this", _currentFunctionThisType);
                if (owner.Superclass is { } superclass) environment.Define("super", superclass);
                using var scope = new EnvironmentScope(this, environment);
                if (!_classMethodSignatures.TryGetValue(method, out var signature))
                {
                    BuildClassMethodSignature(method, wrapGeneratorReturn: method.ComputedKey is null,
                        validateDefaults: false);
                    signature = _classMethodSignatures[method];
                }
                if (signature.Parameters is { } typeParameters && method.TypeParams is { } declarations)
                    for (int i = 0; i < typeParameters.Count; i++)
                        DefineSourceTypeParameter(environment, declarations[i], typeParameters[i]);
                if (signature.Body.ThisType is { } explicitThis)
                {
                    _currentFunctionThisType = explicitThis;
                    environment.Define("this", explicitThis);
                }
                List<TypeInfo> parameters;
                try
                {
                    (parameters, _, _, _) = BuildFunctionSignature(method.Parameters,
                        validateDefaults, contextName: $"method '{method.Name.Lexeme}'", editorOwner: method);
                }
                catch (Exceptions.TypeCheckException error) when (error.Line is null)
                {
                    throw new Exceptions.TypeCheckException(error.Diagnostic with
                    {
                        Location = new SharpTS.Diagnostics.SourceLocation(null, method.Name.Line, 1)
                    });
                }
                for (int i = 0; i < parameters.Count; i++)
                    if (!TypeInfoEqualityComparer.Instance.Equals(signature.Body.ParamTypes[i], parameters[i]))
                    {
                        // Public overload/generic signatures share these parameter lists.
                        signature.Body.ParamTypes[i] = parameters[i];
                        refined = true;
                    }
            }
        }
    }

    private TypeInfo CheckMethodSignatureDefault(Expr expression, bool deferClassContext)
    {
        if (deferClassContext)
        {
            var visitor = new MethodDefaultClassContextVisitor();
            visitor.Visit(expression);
            if (visitor.RequiresContext) return TypeInfo.Any.Shared;
        }
        return CheckDeferredClassDefault(expression);
    }

    private sealed class MethodDefaultClassContextVisitor : Parsing.Visitors.AstVisitorBase
    {
        public bool RequiresContext { get; private set; }
        protected override void VisitThis(Expr.This expression) => RequiresContext = true;
        protected override void VisitSuper(Expr.Super expression) => RequiresContext = true;
        protected override void VisitGetPrivate(Expr.GetPrivate expression) => RequiresContext = true;
        protected override void VisitSetPrivate(Expr.SetPrivate expression) => RequiresContext = true;
        protected override void VisitCallPrivate(Expr.CallPrivate expression) => RequiresContext = true;
        protected override void VisitCompoundSetPrivate(Expr.CompoundSetPrivate expression) => RequiresContext = true;
        protected override void VisitLogicalSetPrivate(Expr.LogicalSetPrivate expression) => RequiresContext = true;
        protected override void VisitPrivateIn(Expr.PrivateIn expression) => RequiresContext = true;
    }

    private static TypeInfo ClassMethodCallable(TypeInfo.Function body, List<TypeInfo.TypeParameter>? parameters) =>
        parameters is { Count: > 0 }
            ? new TypeInfo.GenericFunction(parameters, body.ParamTypes, body.ReturnType, body.RequiredParams,
                body.HasRestParam, body.ThisType, body.ParamNames) : body;

    private TypeInfo.Function ClassMethodBodySignature(Stmt.Function method, TypeEnvironment environment)
    {
        if (!_classMethodSignatures.TryGetValue(method, out var signature))
        {
            BuildClassMethodSignature(method, wrapGeneratorReturn: method.ComputedKey is null);
            signature = _classMethodSignatures[method];
        }
        if (signature.Parameters is { } parameters && method.TypeParams is { } declarations)
            for (int i = 0; i < parameters.Count; i++) DefineSourceTypeParameter(environment, declarations[i], parameters[i]);
        return signature.Body;
    }

    private TypeInfo BuildClassMethodOverload(List<TypeInfo> signatures, TypeInfo implementation)
    {
        var body = ClassMethodImplementation(implementation);
        TypeInfo callable = signatures.All(signature => signature is TypeInfo.Function)
            ? new TypeInfo.OverloadedFunction(signatures.Cast<TypeInfo.Function>().ToList(), body)
            : new TypeInfo.OverloadSet(signatures);
        _typeMap.SetClassMethodImplementation(callable, body);
        RegisterEditorPublicSignatures(callable, signatures);
        return callable;
    }

    private TypeInfo.Function ClassMethodImplementation(TypeInfo callable) =>
        _typeMap.GetClassMethodImplementation(callable) ?? callable switch
        {
            TypeInfo.Function function => function,
            TypeInfo.GenericFunction generic => new TypeInfo.Function(generic.ParamTypes, generic.ReturnType,
                generic.RequiredParams, generic.HasRestParam, generic.ThisType, generic.ParamNames),
            TypeInfo.OverloadedFunction overloaded => overloaded.Implementation,
            _ => throw new InvalidOperationException("Class method implementation signature is unavailable.")
        };

    private TypeInfo UpdateClassMethodReturn(Stmt.Function method, TypeInfo declared, TypeInfo.Function body, TypeInfo result)
    {
        body = body with { ReturnType = result };
        var signature = _classMethodSignatures[method];
        _classMethodSignatures[method] = signature with { Body = body };
        _classMethodImplementationParameters[body] = signature.Parameters;
        TypeInfo callable = declared switch
        {
            TypeInfo.OverloadedFunction overloaded => overloaded with { Implementation = body },
            TypeInfo.OverloadSet => declared,
            _ => ClassMethodCallable(body, signature.Parameters)
        };
        _typeMap.SetClassMethodImplementation(method, body, callable);
        RegisterEditorSignature(body, method, method.Name);
        RegisterEditorSignature(callable, method, method.Name);
        CopyEditorPublicSignatures(declared, callable);
        return callable;
    }

    private TypeInfo WidenInferredClassMemberReturn(IReadOnlyList<Stmt> body, TypeInfo inferred)
    {
        // A single fresh primitive return widens, but a literal union or an
        // explicitly fixed literal remains part of the callable's signature.
        // Use each checked expression's source provenance to distinguish
        // `return 1` / a fresh const alias from `return 1 as const` / `const n: 1`.
        TypeInfo? widened = inferred switch
        {
            TypeInfo.NumberLiteral => TypeInfo.Primitive.Number,
            TypeInfo.StringLiteral => TypeInfo.String.Shared,
            TypeInfo.BooleanLiteral => TypeInfo.Primitive.Boolean,
            TypeInfo.BigIntLiteral => TypeInfo.BigInt.Shared,
            _ => null
        };
        if (widened == null) return inferred;

        List<Expr> returns = [];
        var collector = new ClassMemberReturnCollector(returns);
        foreach (var statement in body) collector.Visit(statement);
        if (returns.Count == 0) return inferred;
        foreach (var expression in returns)
        {
            if (_typeMap.Get(expression) is not { } expressionType ||
                !TypeInfoEqualityComparer.Instance.Equals(
                    WidenClassFieldInitializerType(expression, expressionType), widened))
                return inferred;
        }
        return widened;
    }
}
