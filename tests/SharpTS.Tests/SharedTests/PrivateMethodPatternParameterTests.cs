using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PrivateMethodPatternParameterTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-private-array-binding", """
            class Owner {
                #read([value]: any[]) { return value; }
                read() { return this.#read([1]); }
            }
            console.log(new Owner().read());
            """, "1\n"];
        yield return ["private-object-binding-declaration", """
            class Owner {
                #read({ value }: any) { return value; }
                read() { return this.#read({ value: 1 }); }
            }
            console.log(new Owner().read());
            """, "1\n"];
        yield return ["private-array-binding-class-expression", """
            const Owner = class {
                #read([value]: any[]) { return value; }
                read() { return this.#read([1]); }
            };
            console.log(new Owner().read());
            """, "1\n"];
        yield return ["static-private-object-binding-class-expression", """
            const Owner = class {
                static #read({ value }: any) { return value; }
                static read() { return this.#read({ value: 1 }); }
            };
            console.log(Owner.read());
            """, "1\n"];
        yield return ["ordinary-function-pattern-later-default-control", """
            function run([value]: any[], next = value) { return next; }
            console.log(run([1]));
            """, "1\n"];
        yield return ["public-method-pattern-later-default-control", """
            class Owner { read([value]: any[], next = value) { return next; } }
            console.log(new Owner().read([1]));
            """, "1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PrivateMethodPatternsBindBeforeLaterDefaults(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
