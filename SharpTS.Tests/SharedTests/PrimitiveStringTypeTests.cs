using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #1108: <c>string</c> has a dedicated <see cref="TypeInfo.String"/>; the vestigial
/// <c>Primitive(TYPE_STRING)</c> representation was matched by neither the <c>typeof</c> type-guards nor the
/// compiled string-classification switches, so it silently miscompiled. The representation is now forbidden
/// at construction, and the few sites that built it (<c>__dirname</c>/<c>__filename</c>) use
/// <see cref="TypeInfo.String"/>.
/// </summary>
public class PrimitiveStringTypeTests
{
    [Fact]
    public void PrimitiveTypeString_IsForbiddenAtConstruction()
    {
        // The assert/analyzer: 'string' must be TypeInfo.String, never Primitive(TYPE_STRING).
        Assert.Throws<ArgumentException>(() => new TypeInfo.Primitive(TokenType.TYPE_STRING));
        // number/boolean remain valid Primitive tokens.
        _ = TypeInfo.Primitive.Number;
        _ = TypeInfo.Primitive.Boolean;
    }

    [Fact]
    public void TypeofString_NarrowsDirname_ToString()
    {
        // Before #1108 __dirname's type was Primitive(TYPE_STRING). `typeof x === "string"` matches only
        // `TypeInfo.String or StringLiteral`, so the guard failed and narrowed __dirname to `never` inside
        // the block (and `never` IS assignable to number, hiding the error). With __dirname now typed
        // TypeInfo.String, it narrows to `string`, which is NOT assignable to number.
        var source = """
            if (typeof __dirname === "string") {
                const num: number = __dirname;
                console.log(num);
            }
            """;
        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("'string' is not assignable to type 'number'", ex.Message);
    }

    [Fact]
    public void TypeofString_NarrowsFilename_AllowsStringUse()
    {
        // The positive side: narrowing to `string` keeps string members available (no false error).
        var source = """
            if (typeof __filename === "string") {
                const upper: string = __filename.toUpperCase();
                console.log(typeof upper);
            }
            """;
        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("string\n", result);
    }

    [Theory, ModeData]
    public void StringFieldObjectLocal_PromotesAndRoundTrips(ExecutionMode mode)
    {
        // The compiled ObjectLocalPromotionAnalyzer classifies a string field by TypeInfo.String now
        // (previously only the never-constructed Primitive(TYPE_STRING)), so an object local with a
        // string field is promotable to a shape struct with a String slot. Interp and compiled must agree.
        var source = """
            function run(): string {
                const o = { name: "alice", age: 30 };
                o.name = "bob";
                return o.name + ":" + o.age;
            }
            console.log(run());
            """;
        var result = TestHarness.Run(source, mode);
        Assert.Equal("bob:30\n", result);
    }
}
