using SharpTS.Diagnostics;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassSiblingIndexedFieldTypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["negative-wrong-sibling-initializer", """
            class Wrong {
                other: string = 'other';
                value: Wrong['other'] = 1;
            }
            """, new[] { "TS2322" }, new[] { 3 }];
        yield return ["negative-direct-self-indexed-field", """
            class Direct {
                value: Direct['value'] = 1;
            }
            """, new[] { "TS2502" }, new[] { 2 }];
        yield return ["negative-mutual-indexed-field-cycle", """
            class Cycle {
                first: Cycle['second'] = 1;
                second: Cycle['first'] = 2;
            }
            """, new[] { "TS2502", "TS2502" }, new[] { 2, 3 }];
        yield return ["negative-forward-optional-sibling-numeric-initializer", """
            class ForwardOptional {
                value: ForwardOptional['other'] = 1;
                other?: string;
            }
            """, new[] { "TS2322" }, new[] { 2 }];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InvalidInitializersAndActualCyclesRetainTheirStrictDiagnostics(
        string name, string source, string[] expectedCodes, int[] expectedLines)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict with { MaxErrors = 50 })
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expectedCodes.Zip(expectedLines),
            diagnostics.Select(diagnostic => (diagnostic.TsCode!, diagnostic.Line)));
    }
}
