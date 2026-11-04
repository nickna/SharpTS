using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed partial class NativePropertyLookupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArgumentsArrayFromAdmitsItsActualCompilerProducedIterator(bool native)
    {
        const string source = """
            function copy(...values: any[]) {
                console.log(Array.from(arguments).length);
            }
            copy(1, 2);
            """;
        Assert.Equal("2\n", Run(Compile(source, native, out _)));
    }
}
