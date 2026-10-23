using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericIndexedSiblingFieldAssignmentTypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["different-type-parameter", """
            class Box<T, U> {
                other: T;
                value: Box<T, U>['other'];
                constructor(other: T, value: U) { this.other = other; this.value = value; }
            }
            """];
        yield return ["string-not-arbitrary-type-parameter", """
            class Box<T> {
                other: T;
                value: Box<T>['other'];
                constructor(other: T, value: string) { this.other = other; this.value = value; }
            }
            """];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IncompatibleAssignmentsRemainRejected(string name, string source)
    {
        Assert.NotEmpty(name);
        // Mirror the CLI's module checker so the generic class type facet is retained.
        var path = Path.Combine(Path.GetTempPath(), "generic-indexed-sibling-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { MaxErrors = 50 });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var error = Assert.Single(errors);
        Assert.Equal("TS2322", error.TsCode);
        Assert.Contains("field 'value'", error.Message);
    }
}
