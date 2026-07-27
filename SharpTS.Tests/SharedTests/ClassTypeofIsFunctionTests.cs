using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiled-mode semantic divergence surfaced by the Phase 3g
/// <c>string_decoder</c> migration: <c>typeof MyClass</c> for a user-defined
/// class returns <c>'object'</c> in compiled mode, but Node/JS spec says
/// classes are functions so it should return <c>'function'</c>.
/// </summary>
/// <remarks>
/// Pre-existing gap — not caused by the migration. Surfaced because the
/// legacy C# <c>StringDecoder</c> constructor happened to typeof as
/// <c>'function'</c>, masking the issue; migrating to a TS class exposed it.
/// The existing <c>StringDecoder_Import_Named</c> / <c>_Namespace</c> tests
/// were updated to assert on construction behavior instead of typeof.
/// </remarks>
public class ClassTypeofIsFunctionTests
{
    [Theory, ModeData]
    public void TypeofClass_ReturnsFunction_SameModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                class Foo {}
                console.log(typeof Foo);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void TypeofClass_ReturnsFunction_ImportedClass(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export class Foo {}
                """,
            ["main.ts"] = """
                import { Foo } from './lib';
                console.log(typeof Foo);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }
}
