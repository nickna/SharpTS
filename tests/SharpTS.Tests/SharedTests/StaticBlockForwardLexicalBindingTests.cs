using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StaticBlockForwardLexicalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["later-let-after-initialization", """
            class Example {
                static {
                    const read = () => value;
                    let value = 7;
                    console.log(read());
                }
            }
            """ + "\n", "7\n"];
        yield return ["later-const-after-initialization", """
            class Example {
                static {
                    const read = () => value;
                    const value = 8;
                    console.log(read());
                }
            }
            """ + "\n", "8\n"];
        yield return ["earlier-binding-control", """
            class Example {
                static {
                    let value = 9;
                    const read = () => value;
                    console.log(read());
                }
            }
            """ + "\n", "9\n"];
        yield return ["selected-static-block-tdz", """
            const events: string[] = [];
            class Declaration {
                static {
                    for (var step of [1]) {}
                    const read = () => later;
                    try { read(); } catch (error: any) { events.push("declaration:" + (error instanceof ReferenceError)); }
                    let later = 7;
                    events.push("after:" + read());
                }
            }
            const Named = class Self {
                static {
                    for (var step of [1]) {}
                    const read = () => later;
                    try { read(); } catch (error: any) { events.push("named:" + (error instanceof ReferenceError)); }
                    let later = 8;
                    events.push("after:" + read());
                }
            };
            const Anonymous = class {
                static {
                    for (var step of [1]) {}
                    const read = () => later;
                    try { read(); } catch (error: any) { events.push("anonymous:" + (error instanceof ReferenceError)); }
                    let later = 9;
                    events.push("after:" + read());
                }
            };
            console.log(events.join(","));
            """ + "\n", "declaration:true,after:7,named:true,after:8,anonymous:true,after:9\n"];
        yield return ["typed-forward-captures-and-sibling-static-scopes", """
            const value = 99;
            class Scope {
                static {
                    const read = () => value;
                    let value: number = 11;
                    const result: number = read();
                    console.log('number', result);
                }
                static {
                    const read = () => value;
                    const value: string = 'two';
                    const result: string = read();
                    console.log('string', result);
                }
                static {
                    const read = () => value;
                    const value: boolean = true;
                    {
                        const value: string = 'inner';
                        const result: string = value;
                        console.log('nested', result);
                    }
                    const result: boolean = read();
                    console.log('boolean', result);
                }
            }
            console.log('outer', value);
            """ + "\n", "number 11\nstring two\nnested inner\nboolean true\nouter 99\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ForwardClosuresKeepTheirOwnStaticBlockBindings(
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
    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["post-declaration-annotated-write", """
            class Wrong { static { let value: number = 1; value = 'bad'; } }
            """ + "\n", "TS2322"];
        yield return ["sibling-block-local-is-not-visible", """
            class Separate { static { const hidden = 1; } static { console.log(hidden); } }
            """ + "\n", "TS2304"];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void StaticBlockLocalsRetainDeclaredTypesAndSiblingIsolation(
        string name, string source, string expectedCode)
    {
        Assert.NotEmpty(name);
        var result = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics).TsCode);
    }

}
