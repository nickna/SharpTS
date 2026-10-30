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
                    : checker.ProjectParameterPatternBindingType(parameter, parameterTypes[i], shadow, _initializers[parameter.Name]);
                _maskedParameter = checker.RegisterValueDeclaration(parameterScope, shadow);
                parameterScope.Define(shadow.Lexeme, shadowType);
                checker.RecordEditorBinding(parameterScope, shadow, _maskedParameter,
                    BindingNamespace.Value, shadowType, EditorFactAvailability.Unavailable);
                _maskedParameterIndex = i;
                break;
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
        BindingSymbol? pendingParameter, bool ownInitializer) : ParameterDefaultReadVisitor
    {
        private bool _parameterInitialized;

        private void CheckWrite(Token name)
        {
            if (ReferenceEquals(bindings.GetSymbol(name), self))
                throw new TypeCheckException($"Cannot assign to '{name.Lexeme}' because it is a function.",
                    name.Line, tsCode: "TS2630");
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (DeferredDepth != 0 || _parameterInitialized || pendingParameter is null ||
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

    }
}
