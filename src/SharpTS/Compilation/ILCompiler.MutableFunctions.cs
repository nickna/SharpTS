using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    // Mutable source functions need one binding shared by outer reads and class
    // captures. Stable functions retain their cached wrapper/direct-call path.
    private readonly Dictionary<Stmt.Function, FieldBuilder> _mutableFunctionFields =
        new(ReferenceEqualityComparer.Instance);

    private void EmitMutableFunctionInitialization(IEnumerable<Stmt> statements, ILEmitter emitter)
    {
        foreach (var declaration in statements.SelectMany(SourceDeclarations).OfType<Stmt.Function>())
            if (_mutableFunctionFields.TryGetValue(declaration, out var field))
                emitter.EmitHoistedModuleFunction(declaration, field);
    }
}
