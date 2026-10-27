using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeModuleConstructorBindingTests
{
    [Theory, ModeData]
    public void NamedImportsKeepTheirAdaptersAcrossAliasesSuspensionAndLocalShadows(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { CountQueuingStrategy as Strategy } from "node:stream/web";
                const value = new Strategy({ highWaterMark: 4 });
                class Local { marker = 11; }
                function local(Strategy: any): number { return new Strategy().marker; }
                console.log(value.highWaterMark, value.size(null), local(Local));
                function* generate(): Generator<number> {
                    yield new Strategy({ highWaterMark: 7 }).highWaterMark;
                }
                console.log(generate().next().value);
                async function run(): Promise<void> {
                    const mark = await Promise.resolve(6);
                    console.log(new Strategy({ highWaterMark: mark }).highWaterMark);
                }
                run();
                """
        };
        Assert.Equal("4 1 11\n7\n6\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void VmNamespacesUseNativeAdaptersWhileOrdinaryMembersRemainValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from "vm";
                const script = new vm.Script("40 + 2");
                console.log(script.runInNewContext());
                const holder = { Script: class { marker = 9; } };
                console.log(new holder.Script().marker);
                """
        };
        Assert.Equal("42\n9\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void EmbeddedEventEmitterFacadeKeepsItsNativeResultAndLexicalShadows(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from "events";
                let calls = 0;
                const emitter = new EventEmitter();
                emitter.on("value", () => { calls++; });
                emitter.emit("value");
                class Local { marker = 11; }
                function shadow(EventEmitter: any): number {
                    const value: any = new EventEmitter();
                    return value.marker;
                }
                console.log(calls, shadow(Local));
                """
        };
        Assert.Equal("1 11\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
