using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncCatchBindingLifetimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["async-arrow-catch-await-identity", """
            const token: any = { tag: 8 };
            const run = async () => {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    await Promise.resolve(0);
                    console.log(error === token);
                }
            };

            const task = run();
            """ + "\n", "true\n"];
        yield return ["catch-binding-lifetime", """
            const objectToken: any = { tag: "object" };

            async function declaration(token: any) {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    await Promise.resolve(0);
                    return { caught: true, same: error === token };
                }
                return { caught: false, same: false };
            }

            const arrow = async (token: any) => {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    await Promise.resolve(0);
                    return { caught: true, same: error === token };
                }
                return { caught: false, same: false };
            };

            const mutable = async (token: any, replacement: any) => {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    const original = error;
                    error = replacement;
                    await Promise.resolve(0);
                    const first = error === replacement;
                    error = token;
                    await Promise.resolve(0);
                    return { first, second: error === token, original: original === token };
                }
                return { first: false, second: false, original: false };
            };

            const nested = async (outerToken: any, innerToken: any) => {
                try {
                    await Promise.reject(outerToken);
                } catch (error: any) {
                    const original = error;
                    let innerSame = false;
                    try {
                        await Promise.reject(innerToken);
                    } catch (error: any) {
                        await Promise.resolve(0);
                        innerSame = error === innerToken;
                    }
                    await Promise.resolve(0);
                    return { outer: error === outerToken, original: original === outerToken, inner: innerSame };
                }
                return { outer: false, original: false, inner: false };
            };

            async function leaf(token: any) {
                try {
                    throw token;
                } catch (error: any) {
                    return error === token;
                }
            }

            async function main() {
                const cases: any[] = [
                    { label: "null", value: null },
                    { label: "undefined", value: undefined },
                    { label: "false", value: false },
                    { label: "zero", value: 0 },
                    { label: "empty", value: "" },
                    { label: "object", value: objectToken }
                ];
                for (let index = 0; index < cases.length; index++) {
                    const entry = cases[index];
                    const declared = await declaration(entry.value);
                    const arrowResult = await arrow(entry.value);
                    console.log(entry.label, declared.caught, declared.same, arrowResult.caught, arrowResult.same);
                }
                const replacement: any = { tag: "replacement" };
                const mutations = await mutable(objectToken, replacement);
                console.log("mutable", mutations.first, mutations.second, mutations.original);
                const innerToken: any = { tag: "inner" };
                const nesting = await nested(objectToken, innerToken);
                console.log("nested", nesting.outer, nesting.original, nesting.inner);
                const leafResult = await leaf(objectToken);
                console.log("leaf", leafResult);
            }

            const task = main().catch((error: any) => console.log("unexpected", error.name));
            """ + "\n", "null true true true true\nundefined true true true true\nfalse true true true true\nzero true true true true\nempty true true true true\nobject true true true true\nmutable true true true\nnested true true true\nleaf true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CatchBindingsPreserveValuesAcrossAwaitAndLexicalScopes(
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
