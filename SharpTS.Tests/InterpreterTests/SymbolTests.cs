using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class SymbolTests
{
    [Fact]
    public void Symbol_CreateWithoutDescription_Works()
    {
        var source = """
            let s = Symbol();
            console.log(typeof s === "symbol");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Symbol_CreateWithDescription_Works()
    {
        var source = """
            let s = Symbol("mySymbol");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(mySymbol)\n", output);
    }

    [Fact]
    public void Symbol_Uniqueness_Works()
    {
        var source = """
            let s1 = Symbol("test");
            let s2 = Symbol("test");
            console.log(s1 === s2);
            console.log(s1 !== s2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Symbol_AsObjectKey_Works()
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            obj[sym] = "hello";
            console.log(obj[sym]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Symbol_MultipleSymbolKeys_Works()
    {
        var source = """
            let sym1 = Symbol("first");
            let sym2 = Symbol("second");
            let obj: { [key: symbol]: number } = {};
            obj[sym1] = 10;
            obj[sym2] = 20;
            console.log(obj[sym1]);
            console.log(obj[sym2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void Symbol_TypeAnnotation_Works()
    {
        var source = """
            let s: symbol = Symbol("typed");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(typed)\n", output);
    }

    [Fact]
    public void Symbol_InFunction_Works()
    {
        var source = """
            function createSymbol(name: string): symbol {
                return Symbol(name);
            }
            let s = createSymbol("func");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(func)\n", output);
    }
}
