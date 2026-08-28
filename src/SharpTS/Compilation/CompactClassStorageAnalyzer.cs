using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds deliberately narrow user classes whose instances never escape exact, typed local use.
/// Those classes can move their rarely-used dynamic-property dictionary to weak side storage,
/// avoiding an otherwise permanent reference slot on every instance. Any value-position class
/// use, non-local construction, dynamic/indexed access, assertion, export, inheritance, or local
/// escape keeps the ordinary per-instance storage layout.
/// </summary>
internal static class CompactClassStorageAnalyzer
{
    public static HashSet<Stmt.Class> Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        RuntimeFeatureSet? features)
    {
        if (typeMap is null
            || features?.UsesDynamicPropertyDescriptors != false
            || features.UsesObjectIntegrityMutation
            || features.UsesClassPrototypeMutation)
            return [];

        var declarations = new DeclarationCollector();
        foreach (var statement in program)
            declarations.Visit(statement);

        var eligibleByName = declarations.Classes
            .Where(pair => pair.Value.Count == 1)
            .Select(pair => pair.Value[0])
            .Where(IsLayoutCandidate)
            .Where(statement => !declarations.ExportedNames.Contains(statement.Name.Lexeme))
            .ToDictionary(statement => statement.Name.Lexeme, StringComparer.Ordinal);
        if (eligibleByName.Count == 0)
            return [];

        var visitor = new UsageVisitor(typeMap, eligibleByName);
        foreach (var statement in program)
            visitor.Visit(statement);

        foreach (var candidate in visitor.Locals.Values)
        {
            if (visitor.DeclarationCounts.GetValueOrDefault(candidate.Key) != 1
                || visitor.DisqualifiedLocals.Contains(candidate.Key))
                visitor.UnsafeClasses.Add(candidate.ClassStatement);
        }

        return new HashSet<Stmt.Class>(
            eligibleByName.Values
                .Where(statement => visitor.AllocationCounts.GetValueOrDefault(statement) > 0)
                .Where(statement => !visitor.UnsafeClasses.Contains(statement)),
            ReferenceEqualityComparer.Instance);
    }

    private static bool IsLayoutCandidate(Stmt.Class statement) =>
        statement.SuperclassExpr is null
        && statement.TypeParams is null or { Count: 0 }
        && !statement.IsAbstract
        && !statement.IsDeclare
        && statement.Decorators is null or { Count: 0 }
        && statement.IndexSignatures is null or { Count: 0 }
        && statement.AutoAccessors is null or { Count: 0 }
        && statement.Fields.All(field =>
            !field.IsPrivate && field.ComputedKey is null
            && (field.Decorators is null or { Count: 0 }))
        && statement.Methods.All(method =>
            !method.IsPrivate && method.ComputedKey is null
            && (method.Decorators is null or { Count: 0 }))
        && (statement.Accessors?.All(accessor =>
            accessor.ComputedKey is null
            && (accessor.Decorators is null or { Count: 0 })) ?? true);

    private sealed class DeclarationCollector : AstVisitorBase
    {
        private int _namespaceDepth;

        public Dictionary<string, List<Stmt.Class>> Classes { get; } =
            new(StringComparer.Ordinal);
        public HashSet<string> ExportedNames { get; } = new(StringComparer.Ordinal);

        protected override void VisitNamespace(Stmt.Namespace statement)
        {
            _namespaceDepth++;
            base.VisitNamespace(statement);
            _namespaceDepth--;
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            if (_namespaceDepth == 0)
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
            if (statement.NamedExports != null && statement.FromModulePath is null)
            {
                foreach (var specifier in statement.NamedExports.Where(item => !item.IsTypeOnly))
                    ExportedNames.Add(specifier.LocalName.Lexeme);
            }
            base.VisitExport(statement);
        }
    }

    private sealed class UsageVisitor(
        TypeMap typeMap,
        Dictionary<string, Stmt.Class> eligibleByName) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly Dictionary<string, Stmt.Class> _eligibleByName = eligibleByName;
        private readonly Dictionary<int, int> _scopeParents = [];
        private int _scope;
        private int _nextScope;

        public Dictionary<(int Scope, string Name), LocalCandidate> Locals { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public HashSet<(int Scope, string Name)> DisqualifiedLocals { get; } = [];
        public Dictionary<Stmt.Class, int> AllocationCounts { get; } =
            new(ReferenceEqualityComparer.Instance);
        public HashSet<Stmt.Class> UnsafeClasses { get; } =
            new(ReferenceEqualityComparer.Instance);

        protected override void VisitFunction(Stmt.Function statement) =>
            InScope(() => base.VisitFunction(statement));

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(() => base.VisitArrowFunction(expression));

        protected override void VisitClass(Stmt.Class statement)
        {
            // AstVisitorBase intentionally visits only the class body. A class
            // reference in an extends clause is nevertheless a value use, and
            // selecting that base for weak side storage would give its derived
            // instances two incompatible dynamic-property stores.
            if (statement.SuperclassExpr is not null)
                Visit(statement.SuperclassExpr);
            base.VisitClass(statement);
        }

        private void InScope(Action visit)
        {
            int saved = _scope;
            _scope = ++_nextScope;
            _scopeParents[_scope] = saved;
            visit();
            _scope = saved;
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            var key = RecordDeclaration(statement.Name.Lexeme);
            if (TryBindLocal(statement.Initializer, key))
                return;
            if (statement.Initializer != null)
                Visit(statement.Initializer);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            var key = RecordDeclaration(statement.Name.Lexeme);
            if (!TryBindLocal(statement.Initializer, key))
                Visit(statement.Initializer);
        }

        private (int Scope, string Name) RecordDeclaration(string name)
        {
            var key = (_scope, name);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;
            return key;
        }

        private bool TryBindLocal(Expr? initializer, (int Scope, string Name) key)
        {
            if (!TryGetDirectNew(initializer, out var construction, out var classStatement)
                || !HasExactClassType(initializer!, classStatement.Name.Lexeme))
                return false;

            Locals[key] = new LocalCandidate(key, classStatement);
            AllocationCounts[classStatement] = AllocationCounts.GetValueOrDefault(classStatement) + 1;
            foreach (var argument in construction.Arguments)
                Visit(argument);
            return true;
        }

        protected override void VisitNew(Expr.New expression)
        {
            if (expression.Callee is Expr.Variable variable
                && _eligibleByName.TryGetValue(variable.Name.Lexeme, out var classStatement))
            {
                AllocationCounts[classStatement] = AllocationCounts.GetValueOrDefault(classStatement) + 1;
                UnsafeClasses.Add(classStatement);
            }
            foreach (var argument in expression.Arguments)
                Visit(argument);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (TryAllowDeclaredRead(expression.Object, expression.Name.Lexeme, expression.Optional))
                return;
            base.VisitGet(expression);
        }

        protected override void VisitSet(Expr.Set expression)
        {
            if (TryAllowDeclaredWrite(expression.Object, expression.Name.Lexeme))
            {
                Visit(expression.Value);
                return;
            }
            base.VisitSet(expression);
        }

        protected override void VisitCompoundSet(Expr.CompoundSet expression)
        {
            if (TryAllowDeclaredWrite(expression.Object, expression.Name.Lexeme))
            {
                Visit(expression.Value);
                return;
            }
            base.VisitCompoundSet(expression);
        }

        protected override void VisitLogicalSet(Expr.LogicalSet expression)
        {
            if (TryAllowDeclaredWrite(expression.Object, expression.Name.Lexeme))
            {
                Visit(expression.Value);
                return;
            }
            base.VisitLogicalSet(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            // Even a declared delete changes the object's dynamic own-property semantics.
            if (expression.Operand is Expr.Get { Object: Expr.Variable variable }
                && TryFindLocal(variable.Name.Lexeme, out var local))
                Disqualify(local);
            base.VisitDelete(expression);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (TryFindLocal(expression.Name.Lexeme, out var local))
                Disqualify(local);
            base.VisitAssign(expression);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (TryFindLocal(expression.Name.Lexeme, out var local))
                Disqualify(local);
            else if (_eligibleByName.TryGetValue(expression.Name.Lexeme, out var classStatement))
                UnsafeClasses.Add(classStatement);
        }

        private bool TryAllowDeclaredRead(Expr receiver, string memberName, bool optional)
        {
            if (optional || receiver is not Expr.Variable variable
                || !TryFindLocal(variable.Name.Lexeme, out var local)
                || !HasExactClassType(receiver, local.ClassStatement.Name.Lexeme))
                return false;
            if (local.Key.Scope != _scope)
            {
                Disqualify(local);
                return false;
            }

            var statement = local.ClassStatement;
            bool declared = statement.Fields.Any(field =>
                    !field.IsStatic && field.Name.Lexeme == memberName)
                || statement.Methods.Any(method =>
                    !method.IsStatic && method.Name.Lexeme == memberName)
                || (statement.Accessors?.Any(accessor =>
                    !accessor.IsStatic && accessor.Name.Lexeme == memberName) ?? false);
            if (!declared)
                Disqualify(local);
            return declared;
        }

        private bool TryAllowDeclaredWrite(Expr receiver, string memberName)
        {
            if (receiver is not Expr.Variable variable
                || !TryFindLocal(variable.Name.Lexeme, out var local)
                || !HasExactClassType(receiver, local.ClassStatement.Name.Lexeme))
                return false;
            if (local.Key.Scope != _scope)
            {
                Disqualify(local);
                return false;
            }

            var statement = local.ClassStatement;
            bool declared = statement.Fields.Any(field =>
                    !field.IsStatic && !field.IsReadonly && field.Name.Lexeme == memberName)
                || (statement.Accessors?.Any(accessor =>
                    !accessor.IsStatic && accessor.Kind.Type == TokenType.SET
                    && accessor.Name.Lexeme == memberName) ?? false);
            if (!declared)
                Disqualify(local);
            return declared;
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

        private void Disqualify(LocalCandidate local)
        {
            DisqualifiedLocals.Add(local.Key);
            UnsafeClasses.Add(local.ClassStatement);
        }

        private bool TryGetDirectNew(
            Expr? expression,
            out Expr.New construction,
            out Stmt.Class classStatement)
        {
            expression = Unwrap(expression);
            if (expression is Expr.New { Callee: Expr.Variable variable } direct
                && _eligibleByName.TryGetValue(variable.Name.Lexeme, out var foundClass))
            {
                construction = direct;
                classStatement = foundClass;
                return true;
            }
            construction = null!;
            classStatement = null!;
            return false;
        }

        private bool HasExactClassType(Expr expression, string className) =>
            _typeMap.Get(expression) is TypeInfo.Instance instance
            && instance.ResolvedClassType switch
            {
                TypeInfo.Class type => type.Name == className,
                TypeInfo.MutableClass type => type.Name == className,
                _ => false
            };

        private static Expr? Unwrap(Expr? expression)
        {
            while (expression is Expr.Grouping or Expr.NonNullAssertion)
            {
                expression = expression switch
                {
                    Expr.Grouping grouping => grouping.Expression,
                    Expr.NonNullAssertion assertion => assertion.Expression,
                    _ => expression
                };
            }
            return expression;
        }

        public readonly record struct LocalCandidate(
            (int Scope, string Name) Key,
            Stmt.Class ClassStatement);
    }
}
