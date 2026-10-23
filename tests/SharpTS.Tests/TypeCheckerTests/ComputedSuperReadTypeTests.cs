using SharpTS.Diagnostics;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedSuperReadTypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["literal-static-super-return-type", """
            class First { static value = 7; }
            class Derived extends First {
                static read(): string { return super['value']; }
            }
            """, "TS2322", 3];
        yield return ["dynamic-static-super-return-type", """
            class First { static value = 7; }
            class Derived extends First {
                static read(key: 'value'): string { return super[key]; }
            }
            """, "TS2322", 3];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ComputedSuperReadsRetainTheirSelectedPropertyType(
        string name, string source, string expectedCode, int expectedLine)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict with { MaxErrors = 50 })
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(new[] { (expectedCode, expectedLine) },
            diagnostics.Select(diagnostic => (diagnostic.TsCode!, diagnostic.Line)));
    }
}
