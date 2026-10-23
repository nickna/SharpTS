using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ReflectLexicalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-parameter-static-call", """
            function read(Reflect: any) { return Reflect.get({}, 'value'); }
            console.log(read({ get: (_target: any, _key: any) => 21 }));
            """ + "\n", "21\n"];
        yield return ["reflect-lexical-member-routing", """
            function parameter(Reflect: any) {
                const alias: any = Reflect, key = 'get', method: any = Reflect.get;
                console.log('parameter', Reflect.get({}, 'value'), Reflect['get']({}, 'value'),
                    Reflect[key]({}, 'value'), method({}, 'value'), alias === Reflect,
                    method === alias.get, Reflect.tag);
            }
            parameter({ get: (_target: any, _key: any) => 21, tag: 22 });
            {
                const Reflect: any = { get: (_target: any, _key: any) => 31, tag: 32 };
                console.log('block', Reflect.get({}, 'value'), Reflect['get']({}, 'value'), Reflect.tag);
            }
            function outer() {
                const Reflect: any = { get: (_target: any, _key: any) => 41 };
                return () => Reflect.get({}, 'value');
            }
            console.log('captured', outer()());
            console.log('real', Reflect.get({ value: 7 }, 'value'), typeof Reflect.get);
            """ + "\n", "parameter 21 21 21 21 true true 22\nblock 31 31 32\ncaptured 41\nreal 7 function\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LexicalReflectMembersUseTheirResolvedBinding(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
