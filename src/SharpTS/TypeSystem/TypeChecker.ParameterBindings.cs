using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    /// <summary>
    /// Checks lowered destructuring bindings at their parameter's initialization point.
    /// The body retains the same source nodes for its later authoritative check.
    /// </summary>
    private sealed class ParameterBindingCheckScope : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly Dictionary<Token, Stmt.Sequence>? _initializers;
        private readonly Dictionary<string, string>? _aliases;
        private readonly Dictionary<BindingSymbol, bool>? _flow;
        private readonly bool _immediate;
        private readonly bool _validate;
        private readonly bool _deferClassContext;

        internal ParameterBindingCheckScope(TypeChecker checker, object? owner, bool validate,
            bool deferClassContext = false)
        {
            _checker = checker;
            _validate = validate;
            _deferClassContext = deferClassContext;
            List<Stmt>? body = owner switch
            {
                Stmt.Function function => function.Body,
                Expr.ArrowFunction function => function.BlockBody,
                _ => null,
            };
            if (body is null) return;
            foreach (var statement in body)
                if (statement is Stmt.Sequence { ParameterName: { } parameter } initializer)
                    (_initializers ??= new(ReferenceEqualityComparer.Instance)).Add(parameter, initializer);
            if (_initializers is null) return;

            // Lowered declarations must not alter the enclosing callable's flow/type frames.
            // Retain this frame across parameters so later defaults also see earlier binding types.
            _aliases = new(checker._variableAliases, StringComparer.Ordinal);
            checker.PushDeclaredVariableScope();
            checker.PushEmptyNarrowingScope();
            checker._escapeAnalyzer.EnterScope();
            checker._definiteAssignmentStack.TryPeek(out _flow);
            if (_flow is not null && owner is Expr.ArrowFunction arrow &&
                checker._immediateClassCallables.TryGetValue(arrow, out var callerFlow))
            {
                _immediate = true;
                checker._immediateClassFlows[_flow] = callerFlow;
            }
        }

        internal void Initialize(Stmt.Parameter parameter, TypeEnvironment environment, TypeInfo type)
        {
            if (_initializers is null) return;
            _checker.RecordDeclaredType(parameter.Name.Lexeme, type);
            _checker._escapeAnalyzer.DefineVariable(parameter.Name.Lexeme);
            if (!_initializers.TryGetValue(parameter.Name, out var initializer)) return;

            using var scope = new EnvironmentScope(_checker, environment);
            if (_deferClassContext)
            {
                var visitor = new MethodDefaultClassContextVisitor();
                visitor.Visit(initializer);
                if (visitor.RequiresContext)
                {
                    // The completed class pass checks this lowering. Do not visit its
                    // arrows while lexical this/private/super context is unavailable.
                    foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                        if (!environment.IsDefinedLocally(name.Lexeme))
                            _checker.DeclareValue(environment, name, TypeInfo.Any.Shared);
                    return;
                }
            }
            // An invalid binding aborts the signature, just like an invalid whole-parameter
            // default, rather than recovering here and repeating its diagnostic in the body.
            bool recoveryMode = _checker._recoveryMode;
            _checker._recoveryMode = false;
            if (!_validate) _checker._suppressDiagnostics++;
            if (!_immediate) _checker._deferredClassDefaultFlows.Push(_flow);
            try { _checker.CheckStmt(initializer); }
            catch (Exceptions.TypeCheckException) when (!_validate)
            {
                // Signature hoisting still needs the types of earlier pattern bindings, but
                // invalid binding expressions belong to the later validating declaration pass.
                foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                    if (!environment.IsDefinedLocally(name.Lexeme))
                        _checker.DeclareValue(environment, name, TypeInfo.Any.Shared);
            }
            finally
            {
                if (!_immediate) _checker._deferredClassDefaultFlows.Pop();
                if (!_validate) _checker._suppressDiagnostics--;
                _checker._recoveryMode = recoveryMode;
            }
        }

        public void Dispose()
        {
            if (_initializers is null) return;
            if (_flow is not null) _checker._immediateClassFlows.Remove(_flow);
            _checker._escapeAnalyzer.ExitScope();
            _checker.PopNarrowingScope();
            _checker.PopDeclaredVariableScope();
            _checker._variableAliases.Clear();
            foreach (var (name, alias) in _aliases!)
                _checker._variableAliases.Add(name, alias);
        }
    }
}
