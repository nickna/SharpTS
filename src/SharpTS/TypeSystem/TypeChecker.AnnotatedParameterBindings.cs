using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private readonly HashSet<Stmt.Var> _annotatedParameterBindings = new(ReferenceEqualityComparer.Instance);

    private void RetainAnnotatedParameterBindingTypes(Stmt.Parameter parameter, Stmt.Sequence initializer)
    {
        if (parameter.Type is null && parameter.TypeAnnotationNode is null) return;
        Record(initializer);

        void Record(Stmt.Sequence sequence)
        {
            foreach (var statement in sequence.Statements)
                switch (statement)
                {
                    // Both projection temporaries and written leaves derive from the parameter's
                    // annotation. Do not descend into executable defaults or nested callable bodies.
                    case Stmt.Var variable:
                        _annotatedParameterBindings.Add(variable);
                        break;
                    case Stmt.Sequence nested:
                        Record(nested);
                        break;
                }
        }
    }
}
