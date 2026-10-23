using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class OptionalComputedClassFieldParserTests
{
    [Theory]
    [InlineData("optional-literal", false)]
    [InlineData("optional-expression", true)]
    public void OptionalMarkersRemainFieldMetadata(string name, bool expression)
    {
        var source = (string)OptionalComputedClassFieldTests.Cases()
            .Single(row => (string)row[0] == name)[1];
        var statement = TestHarness.ParseOrThrow(source)[0];
        var fields = expression
            ? Assert.IsType<Expr.ClassExpr>(Assert.IsType<Stmt.Const>(statement).Initializer).Fields
            : Assert.IsType<Stmt.Class>(statement).Fields;

        var field = Assert.Single(fields);
        Assert.True(field.IsOptional);
        Assert.Equal("field", Assert.IsType<Expr.Literal>(field.ComputedKey).Value);
        Assert.Equal("number", field.TypeAnnotation);
        Assert.Null(field.Initializer);
        Assert.False(field.IsStatic);
        Assert.False(field.IsReadonly);
        Assert.False(field.IsPrivate);
        Assert.False(field.IsDeclare);
        Assert.False(field.HasDefiniteAssignmentAssertion);
    }
}
