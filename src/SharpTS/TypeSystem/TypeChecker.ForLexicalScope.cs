using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private ForLexicalScope? EnterForLexicalScope(Stmt.For statement)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        Collect(statement.Initializer);
        return names.Count == 0 ? null : new ForLexicalScope(this, statement, names);

        void Collect(Stmt? initializer)
        {
            switch (initializer)
            {
                case Stmt.Var { IsVar: false } variable:
                    names.Add(variable.Name.Lexeme);
                    break;
                case Stmt.Const constant:
                    names.Add(constant.Name.Lexeme);
                    break;
                case Stmt.Sequence sequence:
                    foreach (var child in sequence.Statements)
                        Collect(child);
                    break;
            }
        }
    }

    private sealed class ForLexicalScope : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly IReadOnlySet<string> _names;
        private readonly TypeEnvironment _previousEnvironment;
        private readonly TypeEnvironment _loopEnvironment;
        private readonly EnvironmentScope _environmentScope;
        private readonly (Dictionary<string, TypeInfo> Frame, Dictionary<string, TypeInfo?> Types)[] _declaredFrames;
        private readonly Dictionary<string, string?> _aliases = new(StringComparer.Ordinal);
        private readonly NarrowingContext[] _narrowings;
        private readonly IDisposable _escapeScope;

        internal ForLexicalScope(TypeChecker checker, Stmt.For statement, IReadOnlySet<string> names)
        {
            _checker = checker;
            _names = names;
            _previousEnvironment = checker._environment;
            _loopEnvironment = new(_previousEnvironment);
            checker.RegisterEditorScope(statement, _loopEnvironment, EditorScopeKind.Block, EditorSyntaxRole.Whole);
            _environmentScope = new(checker, _loopEnvironment);
            // Initializer closures may refer to the header's provisional type before
            // VisitVar records it, so no same-named outer declared type may win lookup.
            _declaredFrames = checker._declaredVariableTypesStack.Select(frame =>
                (frame, names.ToDictionary(name => name, name => frame.GetValueOrDefault(name), StringComparer.Ordinal))).ToArray();
            foreach (var (frame, _) in _declaredFrames)
                foreach (var name in names)
                    frame.Remove(name);
            foreach (var name in names)
            {
                _aliases[name] = checker._variableAliases.GetValueOrDefault(name);
                checker._variableAliases.Remove(name);
            }

            // Remove every inherited fact for these spellings: a later narrowing of the
            // header binding must never reveal a property fact about its outer shadow.
            _narrowings = checker._narrowingContextStack.ToArray();
            checker._narrowingContextStack.Clear();
            for (int i = _narrowings.Length - 1; i >= 0; i--)
                checker._narrowingContextStack.Push(_narrowings[i].RestoreVariableRoots(NarrowingContext.Empty, names));
            _escapeScope = checker._escapeAnalyzer.ShadowVariables(names);
        }

        public void Dispose()
        {
            // Assignment checking writes flow types into the current environment. Publish
            // only inherited bindings; declarations in this header end with the loop.
            if (_loopEnvironment.SnapshotLocalValueTypes() is { } flowTypes)
            {
                foreach (var (name, type) in flowTypes)
                    if (!_names.Contains(name) &&
                        _loopEnvironment.GetLocalValueBinding(name) is { } binding &&
                        ReferenceEquals(binding, _previousEnvironment.GetValueBinding(name)))
                        _previousEnvironment.Define(name, type);
            }

            foreach (var (frame, types) in _declaredFrames)
                foreach (var name in _names)
                    if (types[name] is { } type)
                        frame[name] = type;
                    else
                        frame.Remove(name);
            foreach (var name in _names)
            {
                if (_aliases[name] is { } alias)
                    _checker._variableAliases[name] = alias;
                else
                    _checker._variableAliases.Remove(name);
            }

            // Exit facts for unrelated outer variables survive; header-rooted facts do not.
            var current = _checker._narrowingContextStack.ToArray();
            _checker._narrowingContextStack.Clear();
            for (int i = current.Length - 1; i >= 0; i--)
            {
                int previousIndex = i - (current.Length - _narrowings.Length);
                var previous = previousIndex >= 0 && previousIndex < _narrowings.Length
                    ? _narrowings[previousIndex] : NarrowingContext.Empty;
                _checker._narrowingContextStack.Push(current[i].RestoreVariableRoots(previous, _names));
            }
            _escapeScope.Dispose();
            _environmentScope.Dispose();
        }
    }
}
