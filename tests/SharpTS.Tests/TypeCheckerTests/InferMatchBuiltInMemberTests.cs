using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Conditional-type <c>infer</c> matching against the instance members of a dedicated built-in
/// type record — Date, RegExp, Map, Set, Promise and the weak/iterator variants (#491).
///
/// The property source for the Record/Interface extends branches of <c>CheckExtendsRecursive</c>
/// (<c>ExtractPropertiesWithTypes</c>) has no case for these records, so a pattern like
/// <c>T extends { toJSON(): infer R }</c> against <c>Date</c> silently took its FALSE branch. The fix
/// resolves the extends-side member through the same <c>BuiltInTypes</c> model that ordinary
/// <c>value.member</c> reads use (e.g. <c>Date.prototype.toJSON: () =&gt; string</c>).
///
/// Each positive test assigns a value valid for the TRUE branch but rejected by the false branch
/// (<c>"no"</c>), so it only type-checks once the member is found. Type resolution is
/// mode-independent; the interpreter harness exercises it.
///
/// Sibling coverage: class instance methods / inherited members in infer matching (#461, #492) are
/// tested in <c>InheritedInferMatchTests</c>.
/// </summary>
public class InferMatchBuiltInMemberTests
{
    [Fact]
    public void Date_ToJSON_InferBindsString()
    {
        // Date.prototype.toJSON(): string — the canonical #491 repro. A plain string is accepted
        // only if J<Date> widened to `string` (true branch); the false branch "no" rejects "hello".
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let s: J<Date> = "hello";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Date_ToJSON_InferIsString_NotAny()
    {
        // Guard against the conditional garbling to `any` (which would accept 42): J<Date> is
        // precisely `string`, so a number assignment must error.
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let n: J<Date> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Date_AlreadyModeledMember_GetTime_InferBindsNumber()
    {
        // getTime(): number was already modeled; it now participates in infer matching too.
        var source = """
            type J<T> = T extends { getTime(): infer R } ? R : "no";
            let n: J<Date> = 123;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void RegExp_Source_InferBindsString()
    {
        // RegExp.source is a string property (not a method) — exercises the non-method branch.
        var source = """
            type Src<T> = T extends { source: infer R } ? R : "no";
            let s: Src<RegExp> = "[a-z]+";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Map_Get_InferBindsValueOrNull()
    {
        // Map<string, number>.get(k): number | null — null is valid for the true branch only.
        var source = """
            type GetRet<T> = T extends { get(k: string): infer R } ? R : "no";
            let v: GetRet<Map<string, number>> = null;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Set_Has_InferBindsBoolean()
    {
        // Set<number>.has(e): boolean — a boolean is valid for the true branch only.
        var source = """
            type HasRet<T> = T extends { has(e: number): infer R } ? R : "no";
            let b: HasRet<Set<number>> = true;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Promise_Then_PresenceTakesTrueBranch()
    {
        // The extends clause only checks for the presence of `then`; Promise has it, so the
        // conditional takes its true branch ("yes"). Before the fix it fell through to "no".
        var source = """
            type HasThen<T> = T extends { then: infer _R } ? "yes" : "no";
            let y: HasThen<Promise<number>> = "yes";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void BuiltInRecord_AbsentMember_TakesFalseBranch()
    {
        // A member the built-in record does NOT have must still fail the match (false branch),
        // so the resolver does not over-match. "no" is accepted; any other value is not.
        var source = """
            type N<T> = T extends { definitelyNotAMember(): infer R } ? R : "no";
            let s: N<Date> = "no";
            let bad: N<Date> = "other";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("\"no\"", ex.Message);
    }
}
