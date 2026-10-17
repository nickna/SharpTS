using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForwardClassConstructorTests
{
    [Theory, ModeData]
    public void FunctionsAndClassBodiesReadLaterConstructor(ExecutionMode mode)
    {
        const string source = """
            function read(): number { return Later.present; }
            const arrow = (): number => Later.present;
            class Earlier { static read(): number { return Later.present; } }
            const Expression = class { static read(): number { return Later.present; } };
            class Later extends Earlier { static present: number = 7; }
            console.log(read(), arrow(), Earlier.read(), Expression.read());
            """;
        Assert.Equal("7 7 7 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LocalDeclarationOwnsItsForwardConstructor(ExecutionMode mode)
    {
        const string source = """
            class Later { static present: string = "outer"; }
            function outer(): number {
                function read(): number { return Later.present; }
                class Later { static present: number = 5; }
                return read();
            }
            const Expression = class {
                static read(): number {
                    function read(): number { return Local.present; }
                    class Local { static present: number = 6; }
                    return read();
                }
            };
            console.log(outer(), Expression.read(), Later.present);
            """;
        Assert.Equal("5 6 outer\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleDeferredBodyUsesItsOwnLaterClass(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { read } from "./later";
                console.log(read());
                """,
            ["later.ts"] = """
                export function read(): number { return Later.present; }
                class Later { static present: number = 9; }
                """,
        };
        Assert.Equal("9\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ParameterShadowDoesNotAcquireOuterConstructorType(ExecutionMode mode)
    {
        const string source = """
            function read(Later: any): number { return Later.missing; }
            class Later {}
            console.log(read({ missing: 4 }));
            """;
        Assert.Equal("4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForwardStaticOverloadRetainsEveryAlternative(ExecutionMode mode)
    {
        const string source = """
            function read(): number { return Later.convert(7); }
            function text(): string { return Later.convert("ok"); }
            class Later {
                static convert(value: number): number;
                static convert(value: string): string;
                static convert(value: any): any { return value; }
            }
            console.log(read(), text());
            """;
        Assert.Equal("7 ok\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForwardStaticSignatureRetainsOptionalRestAndDefaultParameters(ExecutionMode mode)
    {
        const string source = """
            function read(): number {
                return Later.optional(undefined) + Later.rest(1, 2, 3) + Later.defaulted();
            }
            const seed: number = 4;
            class Later {
                static optional(value?: number): number { return value ?? 5; }
                static rest(...values: number[]): number { return values.length; }
                static defaulted(value: number = seed): number { return value; }
            }
            console.log(read());
            """;
        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }
}
