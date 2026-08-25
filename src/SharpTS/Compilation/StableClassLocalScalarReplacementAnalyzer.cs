using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds deliberately narrow exact class allocations that are observable only as
/// primitive field values. Eligible constructors contain nothing except copies
/// from primitive parameters into declared instance fields, so the allocation can
/// use the generated value-type shape carrier already shared by object-local
/// promotion. Any identity, alias, call, capture, dynamic property, prototype, or
/// constructor-side-effect observation retains the ordinary class allocation.
/// </summary>
internal static class StableClassLocalScalarReplacementAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        ClosureAnalyzer? closures,
        RuntimeFeatureSet? features)
    {
        if (typeMap is null
            || features?.UsesDynamicPropertyDescriptors != false
            || features.UsesObjectIntegrityMutation
            || features.UsesClassPrototypeMutation)
            return;

        var declarations = new DeclarationCollector();
        foreach (var statement in program)
            declarations.Visit(statement);

        var eligibleClasses = new Dictionary<string, ClassCandidate>(StringComparer.Ordinal);
        foreach (var (name, classes) in declarations.Classes)
        {
            if (classes.Count != 1 || declarations.ExportedNames.Contains(name))
                continue;
            if (TryBuildClassCandidate(classes[0], out var candidate))
                eligibleClasses[name] = candidate;
        }
        if (eligibleClasses.Count == 0)
            return;

        var visitor = new UsageVisitor(typeMap, eligibleClasses);
        foreach (var statement in program)
            visitor.Visit(statement);

        foreach (var candidate in visitor.Locals.Values)
        {
            if (visitor.DeclarationCounts.GetValueOrDefault(candidate.Key) != 1
                || visitor.DisqualifiedLocals.Contains(candidate.Key)
                || visitor.UnsafeClasses.Contains(candidate.Class.Statement)
                || closures?.IsVariableCaptured(candidate.Key.Name) == true)
                continue;

            typeMap.MarkScalarReplaceableClassLocal(
                candidate.NameToken,
                candidate.Class.Info);
        }
    }

    private static bool TryBuildClassCandidate(
        Stmt.Class statement,
        out ClassCandidate candidate)
    {
        candidate = null!;
        if (statement.SuperclassExpr is not null
            || statement.TypeParams is { Count: > 0 }
            || statement.IsAbstract
            || statement.IsDeclare
            || statement.Decorators is { Count: > 0 }
            || statement.IndexSignatures is { Count: > 0 }
            || statement.Accessors is { Count: > 0 }
            || statement.AutoAccessors is { Count: > 0 }
            || statement.Methods.Any(method =>
                method.IsPrivate || method.ComputedKey is not null
                || method.Decorators is { Count: > 0 }))
            return false;

        var fields = new List<ObjectShapeField>();
        var fieldKinds = new Dictionary<string, TokenType>(StringComparer.Ordinal);
        foreach (var field in statement.Fields.Where(field => !field.IsStatic))
        {
            if (field.IsPrivate || field.IsOptional || field.IsDeclare
                || field.ComputedKey is not null || field.Initializer is not null
                || field.Decorators is { Count: > 0 }
                || ClassifyKind(field.TypeAnnotation) is not { } kind
                || !fieldKinds.TryAdd(field.Name.Lexeme, kind))
                return false;
            fields.Add(new ObjectShapeField(field.Name.Lexeme, kind));
        }
        if (fields.Count == 0)
            return false;

        var constructors = statement.Methods
            .Where(method => !method.IsStatic && method.Name.Lexeme == "constructor")
            .ToList();
        if (constructors.Count != 1 || constructors[0].Body is null)
            return false;

        var constructor = constructors[0];
        var parameterKinds = new List<TokenType>(constructor.Parameters.Count);
        var parameterIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < constructor.Parameters.Count; index++)
        {
            var parameter = constructor.Parameters[index];
            if (parameter.DefaultValue is not null || parameter.IsRest
                || parameter.IsOptional || parameter.IsParameterProperty
                || parameter.Decorators is { Count: > 0 }
                || ClassifyKind(parameter.Type) is not { } kind
                || !parameterIndexes.TryAdd(parameter.Name.Lexeme, index))
                return false;
            parameterKinds.Add(kind);
        }

        var initializations = new List<ClassScalarFieldInitialization>(fields.Count);
        var initializedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bodyStatement in constructor.Body!)
        {
            if (bodyStatement is not Stmt.Expression
                {
                    Expr: Expr.Set
                    {
                        Object: Expr.This,
                        Name: var fieldName,
                        Value: Expr.Variable { Name: var parameterName }
                    }
                }
                || !fieldKinds.TryGetValue(fieldName.Lexeme, out var fieldKind)
                || !parameterIndexes.TryGetValue(parameterName.Lexeme, out int parameterIndex)
                || parameterKinds[parameterIndex] != fieldKind
                || !initializedFields.Add(fieldName.Lexeme))
                return false;

            initializations.Add(new ClassScalarFieldInitialization(
                fieldName.Lexeme,
                parameterIndex));
        }
        if (initializedFields.Count != fields.Count)
            return false;

        string key = string.Join(";", fields.Select(field => $"{field.Name}:{field.Kind}"));
        var shape = new ObjectShapeInfo(key, fields);
        candidate = new ClassCandidate(
            statement,
            new ClassScalarReplacementInfo(shape, parameterKinds, initializations),
            fieldKinds);
        return true;
    }

    private static TokenType? ClassifyKind(string? annotation) => annotation switch
    {
        "number" => TokenType.TYPE_NUMBER,
        "boolean" => TokenType.TYPE_BOOLEAN,
        "string" => TokenType.TYPE_STRING,
        _ => null
    };

    private static TokenType? ClassifyKind(TypeInfo? type) => type switch
    {
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => TokenType.TYPE_NUMBER,
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => TokenType.TYPE_BOOLEAN,
        TypeInfo.NumberLiteral => TokenType.TYPE_NUMBER,
        TypeInfo.BooleanLiteral => TokenType.TYPE_BOOLEAN,
        TypeInfo.String or TypeInfo.StringLiteral => TokenType.TYPE_STRING,
        _ => null
    };

    private sealed class DeclarationCollector : AstVisitorBase
    {
        private int _functionDepth;
        private int _namespaceDepth;

        public Dictionary<string, List<Stmt.Class>> Classes { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> ExportedNames { get; } = new(StringComparer.Ordinal);

        protected override void VisitFunction(Stmt.Function statement)
        {
            _functionDepth++;
            base.VisitFunction(statement);
            _functionDepth--;
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            _functionDepth++;
            base.VisitArrowFunction(expression);
            _functionDepth--;
        }

        protected override void VisitNamespace(Stmt.Namespace statement)
        {
            _namespaceDepth++;
            base.VisitNamespace(statement);
            _namespaceDepth--;
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            if (_functionDepth == 0 && _namespaceDepth == 0)
            {
                if (!Classes.TryGetValue(statement.Name.Lexeme, out var declarations))
                    Classes[statement.Name.Lexeme] = declarations = [];
                declarations.Add(statement);
            }
            base.VisitClass(statement);
        }

        protected override void VisitExport(Stmt.Export statement)
        {
            if (statement.Declaration is Stmt.Class exportedClass)
                ExportedNames.Add(exportedClass.Name.Lexeme);
            if (statement.NamedExports is not null && statement.FromModulePath is null)
            {
                foreach (var specifier in statement.NamedExports.Where(item => !item.IsTypeOnly))
                    ExportedNames.Add(specifier.LocalName.Lexeme);
            }
            base.VisitExport(statement);
        }
    }

    private sealed class UsageVisitor(
        TypeMap typeMap,
        Dictionary<string, ClassCandidate> eligibleClasses) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly Dictionary<string, ClassCandidate> _eligibleClasses = eligibleClasses;
        private readonly Dictionary<int, int> _scopeParents = [];
        private int _scope;
        private int _nextScope;

        public Dictionary<(int Scope, string Name), LocalCandidate> Locals { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public HashSet<(int Scope, string Name)> DisqualifiedLocals { get; } = [];
        public HashSet<Stmt.Class> UnsafeClasses { get; } =
            new(ReferenceEqualityComparer.Instance);

        protected override void VisitFunction(Stmt.Function statement)
        {
            if (_eligibleClasses.TryGetValue(statement.Name.Lexeme, out var namedClass)
                && statement.Name.Lexeme != "constructor")
                UnsafeClasses.Add(namedClass.Statement);
            InScope(statement.Parameters, () => base.VisitFunction(statement));
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(expression.Parameters, () => base.VisitArrowFunction(expression));

        private void InScope(IReadOnlyList<Stmt.Parameter> parameters, Action visit)
        {
            int saved = _scope;
            _scope = ++_nextScope;
            _scopeParents[_scope] = saved;
            foreach (var parameter in parameters)
            {
                if (_eligibleClasses.TryGetValue(parameter.Name.Lexeme, out var namedClass))
                    UnsafeClasses.Add(namedClass.Statement);
            }
            visit();
            _scope = saved;
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            if (_eligibleClasses.TryGetValue(statement.Name.Lexeme, out var candidate)
                && !ReferenceEquals(candidate.Statement, statement))
                UnsafeClasses.Add(candidate.Statement);
            base.VisitClass(statement);
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            RecordDeclaration(statement.Name.Lexeme);
            if (statement.Initializer is not null)
                Visit(statement.Initializer);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            var key = RecordDeclaration(statement.Name.Lexeme);
            if (TryBindLocal(statement.Name, statement.Initializer, key))
                return;
            Visit(statement.Initializer);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam is not null)
                RecordDeclaration(statement.CatchParam.Lexeme);
            base.VisitTryCatch(statement);
        }

        private (int Scope, string Name) RecordDeclaration(string name)
        {
            var key = (_scope, name);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;
            if (_eligibleClasses.TryGetValue(name, out var namedClass))
                UnsafeClasses.Add(namedClass.Statement);
            return key;
        }

        private bool TryBindLocal(
            Token nameToken,
            Expr initializer,
            (int Scope, string Name) key)
        {
            if (initializer is not Expr.New
                {
                    Callee: Expr.Variable classVariable
                } construction
                || !_eligibleClasses.TryGetValue(classVariable.Name.Lexeme, out var classCandidate)
                || construction.Arguments.Count != classCandidate.Info.ConstructorParameterKinds.Count
                || !HasExactClassType(initializer, classVariable.Name.Lexeme))
                return false;

            for (int index = 0; index < construction.Arguments.Count; index++)
            {
                if (ClassifyKind(_typeMap.Get(construction.Arguments[index]))
                    != classCandidate.Info.ConstructorParameterKinds[index])
                    return false;
            }

            Locals[key] = new LocalCandidate(key, nameToken, classCandidate);
            foreach (var argument in construction.Arguments)
                Visit(argument);
            return true;
        }

        protected override void VisitNew(Expr.New expression)
        {
            if (expression.Callee is Expr.Variable classVariable
                && _eligibleClasses.ContainsKey(classVariable.Name.Lexeme))
            {
                foreach (var argument in expression.Arguments)
                    Visit(argument);
                return;
            }
            base.VisitNew(expression);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (expression.Object is Expr.Variable variable
                && TryFindLocal(variable.Name.Lexeme, out var local))
            {
                if (!expression.Optional
                    && local.Key.Scope == _scope
                    && local.Class.FieldKinds.ContainsKey(expression.Name.Lexeme)
                    && HasExactClassType(expression.Object, local.Class.Statement.Name.Lexeme))
                    return;
                Disqualify(local);
            }
            base.VisitGet(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            DisqualifyPropertyMutationOperand(expression.Operand);
            base.VisitDelete(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            DisqualifyPropertyMutationOperand(expression.Operand);
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            DisqualifyPropertyMutationOperand(expression.Operand);
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (TryFindLocal(expression.Name.Lexeme, out var local))
            {
                Disqualify(local);
                return;
            }
            if (_eligibleClasses.TryGetValue(expression.Name.Lexeme, out var classCandidate))
                UnsafeClasses.Add(classCandidate.Statement);
        }

        private bool TryFindLocal(string name, out LocalCandidate local)
        {
            int scope = _scope;
            while (true)
            {
                if (Locals.TryGetValue((scope, name), out local))
                    return true;
                if (!_scopeParents.TryGetValue(scope, out scope))
                    break;
            }
            local = default;
            return false;
        }

        private void Disqualify(LocalCandidate local) =>
            DisqualifiedLocals.Add(local.Key);

        private void DisqualifyPropertyMutationOperand(Expr expression)
        {
            if (expression is Expr.Get { Object: Expr.Variable variable }
                && TryFindLocal(variable.Name.Lexeme, out var local))
                Disqualify(local);
        }

        private bool HasExactClassType(Expr expression, string className) =>
            _typeMap.Get(expression) is TypeInfo.Instance instance
            && instance.ResolvedClassType switch
            {
                TypeInfo.Class type => type.Name == className,
                TypeInfo.MutableClass type => type.Name == className,
                _ => false
            };
    }

    private sealed record ClassCandidate(
        Stmt.Class Statement,
        ClassScalarReplacementInfo Info,
        IReadOnlyDictionary<string, TokenType> FieldKinds);

    private readonly record struct LocalCandidate(
        (int Scope, string Name) Key,
        Token NameToken,
        ClassCandidate Class);
}
