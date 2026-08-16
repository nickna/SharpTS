using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Three checker fixes from the #226 cluster survey, round 2: parameter defaults referencing
/// earlier parameters; numeric-named optional members vs number index signatures (the optional-
/// undefined exemption applies to STRING index signatures only); and tsc's diagnostic-code rules
/// for missing-property failures (TS2741 on a pair's first failure, plain TS2322 on repeats and
/// for union sides).
/// </summary>
public class Round2ConformanceTests
{
    [Fact]
    public void ParameterDefault_MayReferenceEarlierParameter()
    {
        var source = """
            function foo5<T, U extends T>(x: U, y: T = x) { }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ParameterDefault_IncompatibleEarlierParameter_IsError()
    {
        var source = """
            function foo4<T, U extends T>(x: T, y: U = x) { }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ParameterDefault_SelfReference_IsStillUndefined()
    {
        var source = """
            function bad(x: number = x) { }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OptionalNumericNamedMember_FailsNumberIndexSignature_UnderStrictNull()
    {
        // The optional-property undefined exemption applies to string index signatures only.
        var source = """
            declare let probablyArray: { [key: number]: string };
            declare let numberLiteralKeys: { 1?: string };
            probablyArray = numberLiteralKeys;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OptionalStringNamedMember_SatisfiesStringIndexSignature()
    {
        var source = """
            declare let stringDictionary: { [key: string]: string };
            declare let optionalProperties: { k1?: string };
            stringDictionary = optionalProperties;
            """;
        TestHarness.RunInterpreted(source);
    }
}
