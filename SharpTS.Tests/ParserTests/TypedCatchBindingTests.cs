using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for typed catch bindings (#215): `catch (e: any)` and
/// `catch (e: unknown)` parse per the TS spec; any other annotation parses
/// but produces checker error TS1196 — never a parse error.
/// </summary>
public class TypedCatchBindingTests
{
    [Theory]
    [InlineData("any")]
    [InlineData("unknown")]
    [InlineData("string")]
    public void CatchBindingAnnotation_Parses(string annotation)
    {
        var stmts = TestHarness.ParseOrThrow($"try {{ throw 1; }} catch (e: {annotation}) {{ }}");
        var tryCatch = Assert.IsType<Stmt.TryCatch>(stmts[0]);
        Assert.Equal("e", tryCatch.CatchParam?.Lexeme);
        Assert.Equal(annotation, tryCatch.CatchParamType);
    }

    [Fact]
    public void CatchBinding_NoAnnotation_TypeIsNull()
    {
        var stmts = TestHarness.ParseOrThrow("try { throw 1; } catch (e) { }");
        var tryCatch = Assert.IsType<Stmt.TryCatch>(stmts[0]);
        Assert.Null(tryCatch.CatchParamType);
    }

    [Theory, ModeData]
    public void CatchBinding_AnyAndUnknown_Run(ExecutionMode mode)
    {
        var source = """
            try { throw 1; } catch (e: any) { console.log("any", e); }
            try { throw 2; } catch (e: unknown) { console.log("unknown"); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("any 1\nunknown\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void CatchBinding_OtherAnnotation_IsTypeErrorNotParseError(ExecutionMode mode)
    {
        var source = """
            try { throw 1; } catch (e: string) { console.log(e); }
            """;

        var ex = Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.Run(source, mode));
        Assert.Contains("must be 'any' or 'unknown'", ex.Message);
    }
}
