using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstanceExpandoRecreationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-expando-recreation-order", """
            class C { stable = 10; }
            const target: any = new C();
            target.first = 1;
            target.second = 2;
            console.log(delete target.first, Object.keys(target).join(','));
            target.first = 3;
            console.log(Object.keys(target).join(','), target.first);
            """.ReplaceLineEndings("\n") + "\n", "true stable,second\nstable,second,first 3\n"];
        yield return ["class-expando-deletion-during-enumeration", """
            class C { stable = 10; }
            const target: any = new C();
            target.first = 1;
            target.second = 2;
            const seen: string[] = [];
            for (const key in target) {
                seen.push(key);
                if (key === 'stable') { delete target.first; delete target.second; }
            }
            console.log(seen.join(','), Object.keys(target).join(','));
            """.ReplaceLineEndings("\n") + "\n", "stable stable\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterDeletionKeepsSurvivorsAndAppendsRecreatedFields(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }
}
