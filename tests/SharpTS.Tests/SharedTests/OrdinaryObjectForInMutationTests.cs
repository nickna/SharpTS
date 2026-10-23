using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class OrdinaryObjectForInMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            for (const action of ['delete', 'add']) {
                const target: any = { first: 1, second: 2 };
                const seen: string[] = [];
                try {
                    for (const key in target) {
                        seen.push(key);
                        if (key === 'first') { if (action === 'delete') delete target.second; else target.third = 3; }
                    }
                    console.log(action, seen.join(','));
                } catch (error) { console.log(action, 'threw'); }
            }
            """.ReplaceLineEndings("\n") + "\n", "delete first\nadd first,second\n"];
        yield return ["async-mutation", """
            async function run() {
                for (const action of ['delete', 'add']) {
                    const target: any = { first: 1, second: 2 };
                    const seen: string[] = [];
                    try {
                        for (const key in target) {
                            seen.push(key);
                            await Promise.resolve(0);
                            if (key === 'first') { if (action === 'delete') delete target.second; else target.third = 3; }
                        }
                        console.log(action, seen.join(','));
                    } catch (error) { console.log(action, 'threw'); }
                }
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """.ReplaceLineEndings("\n") + "\n", "delete first\nadd first,second\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ForInRetainsInitialCandidatesAndSkipsDeletedProperties(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "ordinary-for-in-mutation-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
