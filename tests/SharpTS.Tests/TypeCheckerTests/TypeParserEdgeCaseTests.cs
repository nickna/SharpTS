using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-parser edge cases: parenthesized conditional types, constrained <c>infer</c>, and
/// <c>abstract new</c> constructor types.
/// </summary>
public class TypeParserEdgeCaseTests
{
    [Fact]
    public void ParenthesizedConditionalType_Parses()
    {
        TestHarness.RunInterpreted("type S<T> = { a: string } & (T extends object ? { b: T } : { c: number }); let s: S<object>;");
    }

    [Fact]
    public void ConstrainedInfer_Parses()
    {
        TestHarness.RunInterpreted("type First<T> = T extends [infer A extends number, ...unknown[]] ? A : never; let x: First<[1, 2]>;");
    }

    [Fact]
    public void AbstractNewConstructorType_Parses()
    {
        TestHarness.RunInterpreted("type C<T> = abstract new (x: string) => T; let c: C<object>;");
    }

    [Fact]
    public void RegularGroupedAndInferAndNew_StillParse()
    {
        // Regression guards for the unmodified common forms.
        TestHarness.RunInterpreted(
            "let g: (string | number)[] = []; " +
            "type U<T> = T extends (infer E)[] ? E : never; " +
            "let f: new (x: number) => object;");
    }
}
