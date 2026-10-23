using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // This scope concerns only the private name newly exposed to defaults. Other
    // parameter names retain their existing progressive-initialization policy.
    private sealed class NamedExpressionParameterDefaults
    {
        private readonly TypeChecker _checker;
        private readonly BindingSymbol _self;
        private readonly Dictionary<Token, Stmt.Sequence> _initializers = new(ReferenceEqualityComparer.Instance);
        private readonly BindingSymbol? _maskedParameter;
        private readonly int _maskedParameterIndex = -1;
        private bool _initialized;

        internal NamedExpressionParameterDefaults(TypeChecker checker, Expr.ArrowFunction arrow,
            TypeEnvironment parameterScope, IReadOnlyList<TypeInfo> parameterTypes)
        {
            _checker = checker;
            _self = checker.Bindings.GetSymbol(arrow.Name!)!;
            if (arrow.BlockBody is { } body)
                foreach (var statement in body)
                    if (statement is Stmt.Sequence { ParameterName: { } parameter } initializer)
                        _initializers.Add(parameter, initializer);

            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                var parameter = arrow.Parameters[i];
                Token? shadow = !_initializers.ContainsKey(parameter.Name) &&
                    parameter.Name.Lexeme == arrow.Name!.Lexeme ? parameter.Name : null;
                if (shadow is null && _initializers.TryGetValue(parameter.Name, out var initializer))
                    shadow = LexicalDeclarationBindings.Collect(initializer.Statements)
                        .FirstOrDefault(name => name.Start >= 0 && name.Lexeme == arrow.Name!.Lexeme);
                if (shadow is null) continue;

                // Register the real declaration token so deferred closures bind the
                // parameter, never the enclosing private function-name binding.
                TypeInfo shadowType = ReferenceEquals(shadow, parameter.Name)
                    ? parameterTypes[i]
                    : ProjectPatternBindingType(parameter, parameterTypes[i], shadow);
                _maskedParameter = checker.RegisterValueDeclaration(parameterScope, shadow);
                parameterScope.Define(shadow.Lexeme, shadowType);
                checker.RecordEditorBinding(parameterScope, shadow, _maskedParameter,
                    BindingNamespace.Value, shadowType, EditorFactAvailability.Unavailable);
                _maskedParameterIndex = i;
                break;
            }
        }

        private TypeInfo ProjectPatternBindingType(Stmt.Parameter parameter, TypeInfo sourceType, Token leaf)
        {
            // Only inspect the lowered source/read chain. In particular, do not check
            // a leaf default, computed-key body or other executable expression early.
            Dictionary<string, TypeInfo> sources = new(StringComparer.Ordinal)
            { [parameter.Name.Lexeme] = sourceType };
            ProjectStatements(_initializers[parameter.Name].Statements);
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
                Expr.Literal literal => _checker.GetLiteralType(literal.Value),
                Expr.Get get => Member(Project(get.Object), get),
                Expr.GetIndex index => Indexed(Project(index.Object), Project(index.Index), index),
                Expr.Call { Callee: Expr.Variable { Name.Lexeme: "__arrayDestructure" }, Arguments: [var input] }
                    => _checker.NormalizeArrayDestructureSourceType(Project(input)),
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
                try { return _checker.ResolveMemberType(get, source); }
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
                TypeInfo projected = _checker.CheckGetIndexOnType(source, index, syntax) ?? TypeInfo.Unknown.Shared;
                if (source is TypeInfo.Tuple tuple && index is TypeInfo.NumberLiteral { Value: var position } &&
                    (int)position >= 0 && (int)position < tuple.Elements.Count)
                    projected = _checker.WithOptionalPropertyUndefined(projected, tuple.Elements[(int)position].IsOptional);
                return projected;
            }

            static TypeInfo WithoutUndefined(TypeInfo matched)
            {
                if (matched is not TypeInfo.Union union) return matched;
                var present = union.FlattenedTypes.Where(type => type is not TypeInfo.Undefined).ToList();
                return present.Count == 0 ? TypeInfo.Unknown.Shared : CollapseOrCreateUnion(present);
            }
        }

        internal void Check(Expr expression, int parameterIndex) => Visitor(parameterIndex).Visit(expression);

        internal void Initialize(Stmt.Parameter parameter, int parameterIndex)
        {
            if (_initializers.TryGetValue(parameter.Name, out var initializer))
                Visitor(parameterIndex).Visit(initializer);
            if (parameterIndex == _maskedParameterIndex) _initialized = true;
        }

        private DefaultBindingVisitor Visitor(int parameterIndex) => new(_checker.Bindings, _self,
            _initialized ? null : _maskedParameter, parameterIndex == _maskedParameterIndex);
    }

    private sealed class DefaultBindingVisitor(BindingIndex bindings, BindingSymbol self,
        BindingSymbol? pendingParameter, bool ownInitializer) : AstVisitorBase
    {
        private int _deferredDepth;
        private bool _parameterInitialized;

        private void CheckWrite(Token name)
        {
            if (ReferenceEquals(bindings.GetSymbol(name), self))
                throw new TypeCheckException($"Cannot assign to '{name.Lexeme}' because it is a function.",
                    name.Line, tsCode: "TS2630");
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (_deferredDepth != 0 || _parameterInitialized || pendingParameter is null ||
                !ReferenceEquals(bindings.GetSymbol(expression.Name), pendingParameter)) return;
            throw new TypeCheckException(ownInitializer
                    ? $"Parameter '{expression.Name.Lexeme}' cannot reference itself."
                    : $"Parameter cannot reference identifier '{expression.Name.Lexeme}' declared after it.",
                expression.Name.Line, tsCode: ownInitializer ? "TS2372" : "TS2373");
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            base.VisitVar(statement);
            if (ReferenceEquals(bindings.GetSymbol(statement.Name), pendingParameter)) _parameterInitialized = true;
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            base.VisitConst(statement);
            if (ReferenceEquals(bindings.GetSymbol(statement.Name), pendingParameter)) _parameterInitialized = true;
        }

        protected override void VisitAssign(Expr.Assign expression)
        { CheckWrite(expression.Name); base.VisitAssign(expression); }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        { CheckWrite(expression.Name); base.VisitCompoundAssign(expression); }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        { CheckWrite(expression.Name); base.VisitLogicalAssign(expression); }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        { CheckOperandWrite(expression.Operand); base.VisitPrefixIncrement(expression); }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        { CheckOperandWrite(expression.Operand); base.VisitPostfixIncrement(expression); }

        private void CheckOperandWrite(Expr operand)
        {
            while (operand is Expr.Grouping grouping) operand = grouping.Expression;
            if (operand is Expr.Variable variable) CheckWrite(variable.Name);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            Expr callee = expression.Callee;
            while (callee is Expr.Grouping grouping) callee = grouping.Expression;
            if (callee is not Expr.ArrowFunction immediate)
            { base.VisitCall(expression); return; }

            // Creating a closure defers its reads; immediately invoking it does not.
            base.VisitArrowFunction(immediate);
            foreach (var argument in expression.Arguments) Visit(argument);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            _deferredDepth++;
            try { base.VisitArrowFunction(expression); }
            finally { _deferredDepth--; }
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
            _deferredDepth++;
            try { base.VisitFunction(statement); }
            finally { _deferredDepth--; }
        }
    }
}
