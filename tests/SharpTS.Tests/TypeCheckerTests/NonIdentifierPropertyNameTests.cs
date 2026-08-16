using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Property names that aren't plain identifiers — keywords (e.g. <c>type</c>, <c>set</c>) and
/// string/numeric literals (e.g. <c>"1"</c>, <c>1</c>) — are valid in object type literals, class
/// bodies, and interface bodies. (Conformance gap: these were rejected with "Expect property/member
/// name" / "Expect method name".)
/// </summary>
public class NonIdentifierPropertyNameTests
{
    [Fact]
    public void ObjectType_KeywordAndLiteralNames_Parse()
    {
        TestHarness.RunInterpreted("let x: { type: string; set(v: number): void; 1?: string; \"2\": number };");
    }

    [Fact]
    public void Class_NumericAndStringFieldNames_Parse()
    {
        TestHarness.RunInterpreted("class S { 1: string = \"\"; \"2\": number = 0; }");
    }

    [Fact]
    public void Interface_NumericAndStringMemberNames_Parse()
    {
        TestHarness.RunInterpreted("interface I { 1: string; \"2\"?: number; type: boolean; } let i: I;");
    }

    [Fact]
    public void RegularIdentifierNames_StillWork()
    {
        TestHarness.RunInterpreted("interface I { foo: string; bar?: number } class C { baz: string = \"\"; } let x: { qux: number };");
    }
}
