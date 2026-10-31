using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed class ParameterDefaultBindings
    {
        private readonly TypeChecker _checker;
        private readonly bool _validate;
        private readonly Dictionary<BindingSymbol, (int Index, bool Property)> _parameters = [];
        private readonly Dictionary<Token, Stmt.Sequence> _initializers = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BindingSymbol> _initialized = [];

        internal ParameterDefaultBindings(TypeChecker checker, object? owner,
            IReadOnlyList<Stmt.Parameter> parameters, IReadOnlyList<TypeInfo> types, TypeEnvironment environment,
            bool validate = true)
        {
            _checker = checker;
            _validate = validate;
            List<Stmt>? body = owner switch
            {
                Stmt.Function function => function.Body,
                Expr.ArrowFunction function => function.BlockBody,
                _ => null,
            };
            if (body is not null)
                foreach (var statement in body)
                    if (statement is Stmt.Sequence { ParameterName: { } parameter } initializer)
                        _initializers.Add(parameter, initializer);

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                TypeInfo type = parameter.IsOptional && parameter.DefaultValue is null
                    ? CreateUnion(types[i], TypeInfo.Undefined.Shared) : types[i];
                _parameters[DeclarePending(parameter.Name, type, parameter.IsParameterProperty)] = (i, parameter.IsParameterProperty);
                if (!_initializers.TryGetValue(parameter.Name, out var initializer)) continue;
                foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                {
                    TypeInfo leafType = checker.ProjectParameterPatternBindingType(parameter, type, name, initializer);
                    _parameters[DeclarePending(name, leafType, false)] = (i, false);
                }
            }

            BindingSymbol DeclarePending(Token name, TypeInfo type, bool property)
            {
                var symbol = checker.RegisterValueDeclaration(environment, name);
                if (property) symbol.DenyRename(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits);
                environment.Define(name.Lexeme, type);
                environment.DefineValueBinding(name.Lexeme, symbol);
                checker.RecordEditorBinding(environment, name, symbol, BindingNamespace.Value, type,
                    EditorFactAvailability.Unavailable);
                return symbol;
            }
        }

        internal void Check(Expr expression, int index) => Visitor(index).Visit(expression);

        internal void Initialize(Stmt.Parameter parameter, int index)
        {
            if (_checker.Bindings.GetSymbol(parameter.Name) is { } root) _initialized.Add(root);
            if (_validate && _initializers.TryGetValue(parameter.Name, out var initializer))
                Visitor(index).Visit(initializer);
            foreach (var (symbol, declaration) in _parameters)
                if (declaration.Index == index) _initialized.Add(symbol);
        }

        private ImmediateParameterReadVisitor Visitor(int index) =>
            new(_checker.Bindings, _parameters, _initialized, index);
    }

    private sealed class ImmediateParameterReadVisitor(BindingIndex bindings,
        Dictionary<BindingSymbol, (int Index, bool Property)> parameters,
        HashSet<BindingSymbol> initialized, int index) : ParameterDefaultReadVisitor
    {
        protected override void VisitVariable(Expr.Variable expression)
        {
            if (DeferredDepth != 0 || bindings.GetSymbol(expression.Name) is not { } symbol ||
                initialized.Contains(symbol) || !parameters.TryGetValue(symbol, out var declaration) ||
                declaration.Index < index || declaration.Index == index && declaration.Property) return;
            bool own = declaration.Index == index;
            throw new TypeCheckException(own
                    ? $"Parameter '{expression.Name.Lexeme}' cannot reference itself."
                    : $"Parameter cannot reference identifier '{expression.Name.Lexeme}' declared after it.",
                expression.Name.Line, tsCode: own ? "TS2372" : "TS2373");
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            base.VisitVar(statement);
            if (DeferredDepth == 0 && bindings.GetSymbol(statement.Name) is { } symbol && parameters.ContainsKey(symbol))
                initialized.Add(symbol);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            base.VisitConst(statement);
            if (DeferredDepth == 0 && bindings.GetSymbol(statement.Name) is { } symbol && parameters.ContainsKey(symbol))
                initialized.Add(symbol);
        }
    }

    private abstract class ParameterDefaultReadVisitor : AstVisitorBase
    {
        protected int DeferredDepth { get; private set; }

        protected override void VisitCall(Expr.Call expression)
        {
            Expr callee = expression.Callee;
            while (callee is Expr.Grouping grouping) callee = grouping.Expression;
            if (callee is not Expr.ArrowFunction immediate)
            { base.VisitCall(expression); return; }
            base.VisitArrowFunction(immediate);
            foreach (var argument in expression.Arguments) Visit(argument);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            DeferredDepth++;
            try { base.VisitArrowFunction(expression); }
            finally { DeferredDepth--; }
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
            DeferredDepth++;
            try { base.VisitFunction(statement); }
            finally { DeferredDepth--; }
        }
    }

    private TypeInfo ProjectParameterPatternBindingType(Stmt.Parameter parameter, TypeInfo sourceType, Token leaf, Stmt.Sequence initializer)
    {
        // Only inspect the lowered source/read chain. In particular, do not check
        // a leaf default, computed-key body or other executable expression early.
        Dictionary<string, TypeInfo> sources = new(StringComparer.Ordinal)
        { [parameter.Name.Lexeme] = sourceType };
        ProjectStatements(initializer.Statements);
        return sources.GetValueOrDefault(leaf.Lexeme) ?? TypeInfo.Unknown.Shared;

        void ProjectStatements(IEnumerable<Stmt> statements)
        {
            foreach (var statement in statements)
                switch (statement)
                {
                    case Stmt.Var { Initializer: { } value } variable:
                        sources[variable.Name.Lexeme] = Project(value); break;
                    case Stmt.Const constant:
                        sources[constant.Name.Lexeme] = Project(constant.Initializer); break;
                    case Stmt.Sequence sequence:
                        ProjectStatements(sequence.Statements); break;
                }
        }

        TypeInfo Project(Expr value) => value switch
        {
            Expr.Variable variable => sources.GetValueOrDefault(variable.Name.Lexeme) ?? TypeInfo.Unknown.Shared,
            Expr.Grouping grouping => Project(grouping.Expression),
            Expr.Literal literal => GetLiteralType(literal.Value),
            Expr.Get get => Member(Project(get.Object), get),
            Expr.GetIndex index => Indexed(Project(index.Object), Project(index.Index), index),
            Expr.Call { Callee: Expr.Variable { Name.Lexeme: "__arrayDestructure" }, Arguments: [var input] }
                => NormalizeArrayDestructureSourceType(Project(input)),
            Expr.Ternary
            {
                Condition: Expr.Binary
                {
                    Left: Expr.Variable tested, Operator.Type: TokenType.EQUAL_EQUAL_EQUAL,
                    Right: Expr.Literal { Value: SharpTS.Runtime.Types.SharpTSUndefined }
                },
                ElseBranch: Expr.Variable matched
            } when tested.Name.Lexeme == matched.Name.Lexeme => WithoutUndefined(Project(matched)),
            _ => TypeInfo.Unknown.Shared
        };

        TypeInfo Member(TypeInfo source, Expr.Get get)
        {
            if (source is TypeInfo.Any or TypeInfo.Unknown) return source;
            try { return ResolveMemberType(get, source); }
            catch (TypeCheckException error) when (get.Defaulted && error.Diagnostic.TsCode == "TS2339")
            { return TypeInfo.Undefined.Shared; }
        }

        TypeInfo Indexed(TypeInfo source, TypeInfo index, Expr.GetIndex syntax)
        {
            if (source is TypeInfo.Any or TypeInfo.Unknown) return source;
            if (index is TypeInfo.Unknown) return index;
            if (source is TypeInfo.TypeParameter { Constraint: { } constraint }) return Indexed(constraint, index, syntax);
            if (source is TypeInfo.Union union)
                return CollapseOrCreateUnion(union.FlattenedTypes.Select(type => Indexed(type, index, syntax)).ToList());
            TypeInfo projected = CheckGetIndexOnType(source, index, syntax) ?? TypeInfo.Unknown.Shared;
            if (source is TypeInfo.Tuple tuple && index is TypeInfo.NumberLiteral { Value: var position } &&
                (int)position >= 0 && (int)position < tuple.Elements.Count)
                projected = WithOptionalPropertyUndefined(projected, tuple.Elements[(int)position].IsOptional);
            return projected;
        }

        static TypeInfo WithoutUndefined(TypeInfo matched)
        {
            if (matched is not TypeInfo.Union union) return matched;
            var present = union.FlattenedTypes.Where(type => type is not TypeInfo.Undefined).ToList();
            return present.Count == 0 ? TypeInfo.Unknown.Shared : CollapseOrCreateUnion(present);
        }
    }

}
