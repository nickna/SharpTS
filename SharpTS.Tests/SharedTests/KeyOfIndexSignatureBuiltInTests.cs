using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// keyof over the index-signature-bearing built-ins — <c>string</c>, arrays, and tuples (#527,
/// follow-up to #512). Unlike the dedicated records (Date/Map/…), these carry a numeric index
/// signature, so <c>keyof</c> yields <c>number</c> together with their prototype member names
/// (and, for tuples, the literal element indices), matching <c>tsc</c>. Before #527 they produced
/// <c>never</c>, so every key assignment was wrongly rejected. keyof is type-check-time, so both
/// execution modes exercise the same logic; running both also confirms the value executes after.
/// </summary>
public class KeyOfIndexSignatureBuiltInTests
{
    // ----- keyof string -----

    [Theory, ModeData]
    public void KeyOf_String_AcceptsPropertyName(ExecutionMode mode)
    {
        var source = """
            const k: keyof string = "length";
            console.log(k);
            """;
        Assert.Equal("length\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_String_AcceptsMethodName(ExecutionMode mode)
    {
        var source = """
            const k: keyof string = "charAt";
            console.log(k);
            """;
        Assert.Equal("charAt\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_String_AcceptsNumberIndex(ExecutionMode mode)
    {
        // string carries a numeric index signature, so `number` is a valid key.
        var source = """
            const k: keyof string = 5;
            console.log(k);
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_String_RejectsNonMember(ExecutionMode mode)
    {
        var source = """
            const k: keyof string = "notAMember";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- keyof T[] -----

    [Theory, ModeData]
    public void KeyOf_Array_AcceptsMethodName(ExecutionMode mode)
    {
        var source = """
            const k: keyof number[] = "push";
            console.log(k);
            """;
        Assert.Equal("push\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Array_AcceptsLengthAndNumberIndex(ExecutionMode mode)
    {
        var source = """
            const a: keyof string[] = "length";
            const b: keyof string[] = 0;
            console.log(a, b);
            """;
        Assert.Equal("length 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Array_RejectsNonMember(ExecutionMode mode)
    {
        var source = """
            const k: keyof number[] = "nope";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- keyof [tuple] -----

    [Theory, ModeData]
    public void KeyOf_Tuple_AcceptsLiteralIndex(ExecutionMode mode)
    {
        // A tuple is an Array subtype: keyof includes the literal element indices "0" | "1".
        var source = """
            const k: keyof [string, number] = "1";
            console.log(k);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Tuple_AcceptsArrayMemberAndNumberIndex(ExecutionMode mode)
    {
        var source = """
            const a: keyof [string, number] = "length";
            const b: keyof [string, number] = 0;
            console.log(a, b);
            """;
        Assert.Equal("length 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Tuple_RejectsOutOfRangeLiteralIndex(ExecutionMode mode)
    {
        // [a, b] has indices "0" and "1" only; "2" is not a literal key (and "2" is a string, so the
        // numeric index signature does not cover it either).
        var source = """
            const k: keyof [string, number] = "2";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- keyof feeding a mapped type over an index-signature built-in -----

    [Theory, ModeData]
    public void MappedType_OverKeyOfArray_TypeChecks(ExecutionMode mode)
    {
        var source = """
            type Flags = { [K in keyof number[]]: boolean };
            const f = {} as Flags;
            console.log(typeof f);
            """;
        Assert.Equal("object\n", TestHarness.Run(source, mode));
    }
}
