using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3CheckerOrderTests
{
    [Fact]
    public void DiagnosticAnalysisDoesNotChangeLaterAuthoritativeAliasFacts()
    {
        const string source = "type Falsy=false|0|''|null|undefined; function f(value: Falsy) { return value; }";
        var first = Observe(source, editor: true);
        _ = Observe(source, editor: false);
        var afterDiagnostics = Observe(source, editor: true);
        Assert.Equal(first, afterDiagnostics);
        Assert.DoesNotContain(afterDiagnostics, observation => observation.Display.Unsupported is not null);
    }

    private static IReadOnlyList<CheckedSourceTypeObservation> Observe(string source, bool editor)
    {
        string path = Path.Combine(Path.GetTempPath(), "checker-order", "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata(editor);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
        return checker.EditorFacts.GetSourceTypeObservations(entry.Document!);
    }
}
