using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for named tuple element parsing: [x: number, y: string]
/// </summary>
public class NamedTupleTests
{
    private static TypeInfo NumberType => new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    private static TypeInfo StringType => new TypeInfo.Primitive(TokenType.TYPE_STRING);

    [Fact]
    public void NamedTuple_BasicParsing_Works()
    {
        var source = """
            type Point = [x: number, y: number];
            let p: Point = [10, 20];
            console.log(p[0]);
            console.log(p[1]);
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);

        // Should parse without errors
        Assert.NotNull(typeMap);
    }

    [Fact]
    public void NamedTuple_MixedNamedAndUnnamed_Works()
    {
        var source = """
            type Mixed = [x: number, string, z: boolean];
            let m: Mixed = [1, "hello", true];
            console.log(m[0]);
            console.log(m[1]);
            console.log(m[2]);
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);

        Assert.NotNull(typeMap);
    }

    [Fact]
    public void NamedTuple_OptionalElement_Works()
    {
        var source = """
            type Coord = [x: number, y?: number];
            let c1: Coord = [10];
            let c2: Coord = [10, 20];
            console.log(c1[0]);
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);

        Assert.NotNull(typeMap);
    }

    [Fact]
    public void NamedTuple_TypeInfoToString_IncludesNames()
    {
        // Create a tuple type with names
        var elementTypes = new List<TypeInfo> { NumberType, StringType };
        var elementNames = new List<string?> { "x", "y" };
        var tuple = TypeInfo.Tuple.FromTypes(elementTypes, 2, null, elementNames);

        var str = tuple.ToString();
        Assert.Contains("x:", str);
        Assert.Contains("y:", str);
    }

    [Fact]
    public void NamedTuple_HasNames_ReturnsTrueWhenNamesPresent()
    {
        var elementTypes = new List<TypeInfo> { NumberType, NumberType };
        var elementNames = new List<string?> { "x", "y" };
        var tuple = TypeInfo.Tuple.FromTypes(elementTypes, 2, null, elementNames);

        Assert.True(tuple.HasNames);
    }

    [Fact]
    public void NamedTuple_HasNames_ReturnsFalseWhenNoNames()
    {
        var elementTypes = new List<TypeInfo> { NumberType, NumberType };
        var tuple = TypeInfo.Tuple.FromTypes(elementTypes, 2);

        Assert.False(tuple.HasNames);
    }
}
