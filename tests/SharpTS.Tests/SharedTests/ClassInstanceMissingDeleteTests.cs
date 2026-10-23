using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

// Pins interpreter-owned SharpTSInstance deletion; compiled classes use separate storage.
[Collection("CompilationService")]
public sealed class ClassInstanceMissingDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["unary-delete-missing-class-property", """
            class C {}
            const target: any = new C();
            console.log(delete target.missing, Object.hasOwn(target, 'missing'));
            """.ReplaceLineEndings("\n") + "\n", "true false\n"];
        yield return ["class-instance-missing-delete-integrity-and-prototype", """
            class C { stable = 10; inherited() { return 7; } }
            class StrictDeleter { remove(value: any, key: string) { return delete value[key]; } }
            const strict = new StrictDeleter();
            for (const kind of ['open', 'sealed', 'frozen']) {
              const target: any = new C();
              if (kind === 'sealed') Object.seal(target);
              if (kind === 'frozen') Object.freeze(target);
              console.log(kind, delete target.missing, strict.remove(target, 'missing'), delete target.inherited, strict.remove(target, 'inherited'), Object.hasOwn(target, 'inherited'), typeof target.inherited, target.stable);
            }
            """.ReplaceLineEndings("\n") + "\n", "open true true true true false function 10\nsealed true true true true false function 10\nfrozen true true true true false function 10\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterDeletionSucceedsForAbsentOwnProperties(
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
