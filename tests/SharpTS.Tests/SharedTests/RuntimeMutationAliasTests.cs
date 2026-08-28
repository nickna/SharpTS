using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Interpreter/compiled parity coverage for conservative runtime mutation detection (#1518).
/// </summary>
public sealed class RuntimeMutationAliasTests
{
    [Fact]
    public void RegExpConstructorAndPrototypeAliasesRemainObservableInCompiledCode()
    {
        const string source = """
            const R: any = RegExp;
            const prototype: any = R.prototype;
            prototype.test = function(): boolean { return false; };
            console.log(/x/.test("x"));
            """;

        Assert.Equal("false\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void DateMethodUpdateExpressionsDisableIntrinsicDispatchInCompiledCode()
    {
        const string source = """
            const postfix = new Date(0);
            (postfix as any).getTime++;
            try {
                postfix.getTime();
            } catch {
                console.log("postfix threw");
            }

            const prefix = new Date(0);
            ++(prefix as any)["getTime"];
            try {
                prefix.getTime();
            } catch {
                console.log("prefix threw");
            }
            """;

        Assert.Equal(
            "postfix threw\nprefix threw\n",
            TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void ModuleLevelPromiseBindingPreventsPrimitiveAwaitElision(ExecutionMode mode)
    {
        const string source = """
            const Promise: any = {
                resolve(value: number): any {
                    console.log("resolve", value);
                    return value + 10;
                }
            };
            async function work() {
                return await Promise.resolve(1);
            }
            work().then((value: number): void => console.log(value));
            """;

        Assert.Equal("resolve 1\n11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ImportedPromiseBindingPreventsPrimitiveAwaitElision(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["promise.ts"] = """
                export const Promise: any = {
                    resolve(value: number): any {
                        console.log("resolve", value);
                        return value + 20;
                    }
                };
                """,
            ["main.ts"] = """
                import { Promise } from "./promise";
                async function work() {
                    return await Promise.resolve(1);
                }
                work().then((value: number): void => console.log(value));
                """
        };

        Assert.Equal(
            "resolve 1\n21\n",
            TestHarness.RunModules(files, "main.ts", mode));
    }
}
