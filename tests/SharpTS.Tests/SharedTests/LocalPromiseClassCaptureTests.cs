using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LocalPromiseClassCaptureTests
{
    [Theory, ModeData]
    public void LocalPromiseSubclassGetterRetainsEnclosingCapture(ExecutionMode mode)
    {
        const string source = """
            function run() {
                const state = { gets: 0 };
                class Source extends Promise<any> {
                    static get value() { state.gets++; return 47; }
                }
                console.log('value', Source.value, state.gets);
            }
            run(); run();
            """;
        Assert.Equal("value 47 1\nvalue 47 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RepeatedLocalPromiseClassesKeepIndependentEnvironments(ExecutionMode mode)
    {
        const string source = """
            function make(value: number) {
                class Source extends Promise<any> { static get value() { return value; } }
                return Source;
            }
            const first: any = make(7), second: any = make(9);
            console.log(first !== second, first.value, second.value);
            new first((resolve: any) => resolve(11)).then((value: any) => console.log(value));
            """;
        Assert.Equal("true 7 9\n11\n", TestHarness.Run(source, mode));
    }
}
