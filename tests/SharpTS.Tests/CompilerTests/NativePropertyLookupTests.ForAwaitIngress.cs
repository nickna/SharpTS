using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed partial class NativePropertyLookupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryArrayForAwaitAdmitsItsActualCompilerProducedIterator(bool native)
    {
        const string source = """
            async function run() {
                let key: any = "outer";
                const read = () => key;
                for await (key of ["one", "two"]) {}
                console.log(key, read());
            }
            run();
            """;
        Assert.Equal("two two\n", Run(Compile(source, native, out _)));
    }
}
