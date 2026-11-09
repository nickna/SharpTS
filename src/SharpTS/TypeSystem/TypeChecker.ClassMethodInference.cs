using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed class ClassMethodInferenceScope(
        TypeInfo.MutableClass owner, TypeEnvironment environment, Action<Stmt.Function> check)
    {
        public TypeInfo.MutableClass Owner { get; } = owner;
        public TypeEnvironment Environment { get; } = environment;
        public Dictionary<(MemberFacet Facet, string Name), Stmt.Function> InferredMethods { get; } = [];
        public HashSet<Stmt.Function> Checked { get; } = new(ReferenceEqualityComparer.Instance);

        public void CheckOnce(Stmt.Function method)
        {
            if (Checked.Add(method))
                check(method);
        }
    }

    private ClassMethodInferenceScope? _classMethodInferenceScope;

    private void CheckClassMethodsInDependencyOrder(IEnumerable<Stmt.Function> methods,
        TypeInfo.MutableClass owner, Action<Stmt.Function> check)
    {
        var ordered = methods.ToArray();
        var scope = new ClassMethodInferenceScope(owner, _environment, check);
        foreach (var method in ordered)
        {
            if (method.ReturnType is not null || method.ReturnTypeNode is not null ||
                method.PrivateAccessorName is not null || method.Name.Lexeme == "constructor")
                continue;
            string? name = method.ComputedKey is { } key ? TryGetComputedClassMemberName(key) : method.Name.Lexeme;
            if (name is null) continue;
            MemberFacet facet = (method.IsStatic, method.IsPrivate) switch
            {
                (true, true) => MemberFacet.PrivateStatic,
                (true, false) => MemberFacet.Static,
                (false, true) => MemberFacet.PrivateInstance,
                _ => MemberFacet.Instance,
            };
            scope.InferredMethods[(facet, name)] = method;
        }

        var previous = _classMethodInferenceScope;
        _classMethodInferenceScope = scope;
        try
        {
            foreach (var method in ordered)
                scope.CheckOnce(method);
        }
        finally
        {
            _classMethodInferenceScope = previous;
        }
    }

    private TypeInfo ResolveSiblingClassMethod(TypeInfo owner, MemberFacet facet, string name, TypeInfo fallback)
    {
        if (_classMethodInferenceScope is not { } scope ||
            fallback is not (TypeInfo.Function { ReturnType: TypeInfo.Inferred } or
                TypeInfo.GenericFunction { ReturnType: TypeInfo.Inferred }) ||
            SelectedSourceClassId(owner) != scope.Owner.DeclarationId ||
            _currentClass?.Core.DeclarationId != scope.Owner.DeclarationId ||
            !scope.InferredMethods.TryGetValue((facet, name), out var method))
            return fallback;

        // A forward call needs the callee's signature now. The checked set also
        // cuts recursive references off at the usual unresolved placeholder.
        if (!scope.Checked.Contains(method))
        {
            var flow = CaptureValueFlow();
            var narrowing = PushIsolatedClassFieldNarrowingScope();
            try
            {
                using var environment = new EnvironmentScope(this, scope.Environment);
                scope.CheckOnce(method);
            }
            finally
            {
                RestoreClassFieldNarrowingScope(narrowing);
                RestoreValueFlow(flow);
            }
        }

        var methods = facet switch
        {
            MemberFacet.Static => scope.Owner.StaticMethods,
            MemberFacet.PrivateInstance => scope.Owner.PrivateMethods,
            MemberFacet.PrivateStatic => scope.Owner.StaticPrivateMethods,
            _ => scope.Owner.Methods,
        };
        return methods.GetValueOrDefault(name, fallback);
    }
}
