using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Structural decomposition of dedicated built-in object types — Date, RegExp, Map, Set, Promise and
/// the weak/iterator variants (#512). These types model their instance members through name-keyed
/// resolvers in <c>BuiltInTypes</c>; before #512 those resolvers were not enumerable, so:
/// <list type="bullet">
///   <item><c>keyof Date</c> produced <c>never</c> (no member was a valid key); and</item>
///   <item>a built-in instance was not assignable to a matching structural type
///   (<c>const o: { getTime(): number } = new Date()</c> errored).</item>
/// </list>
/// A shared apparent-members projection (<c>BuiltInTypes.GetInstanceMemberNames</c> /
/// <c>GetInstanceMemberType</c>) now backs keyof, structural assignability, and infer-matching, so
/// these match <c>tsc</c>. keyof/assignability are type-check-time, so both execution modes exercise
/// the same logic; running both also confirms the value executes after the check passes.
/// </summary>
public class BuiltInStructuralTypeTests
{
    // ----- keyof over built-ins -----

    [Theory, ModeData]
    public void KeyOf_Date_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof Date = "getTime";
            console.log(k);
            """;
        Assert.Equal("getTime\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_RegExp_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof RegExp = "source";
            console.log(k);
            """;
        Assert.Equal("source\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Map_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof Map<string, number> = "get";
            console.log(k);
            """;
        Assert.Equal("get\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Set_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof Set<number> = "add";
            console.log(k);
            """;
        Assert.Equal("add\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Promise_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof Promise<number> = "then";
            console.log(k);
            """;
        Assert.Equal("then\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Date_RejectsNonMember(ExecutionMode mode)
    {
        // keyof Date is the union of its members; a non-member literal must not be assignable.
        var source = """
            const k: keyof Date = "notAMember";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- structural assignability with a built-in source -----

    [Theory, ModeData]
    public void Structural_Date_AssignableToMatchingShape(ExecutionMode mode)
    {
        var source = """
            const d = new Date();
            const o: { getTime(): number } = d;
            console.log(typeof o.getTime());
            """;
        Assert.Equal("number\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_Map_AssignableToMatchingShape(ExecutionMode mode)
    {
        var source = """
            const m = new Map<string, number>();
            m.set("a", 1);
            const o: { get(k: string): number | null; size: number } = m;
            console.log(o.size);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_RegExp_AssignableToMatchingShape(ExecutionMode mode)
    {
        var source = """
            const re = /abc/g;
            const o: { source: string; test(s: string): boolean } = re;
            console.log(o.test("xabcy"));
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_Date_MissingMember_Rejected(ExecutionMode mode)
    {
        var source = """
            const d = new Date();
            const o: { bogusMember(): number } = d;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_Date_WrongMemberType_Rejected(ExecutionMode mode)
    {
        // getTime returns number, not string — width subtyping must still type-check member types.
        var source = """
            const d = new Date();
            const o: { getTime(): string } = d;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- keyof feeding indexed access and mapped types over a built-in -----

    [Theory, ModeData]
    public void IndexedAccess_OverBuiltIn_ResolvesMemberType(ExecutionMode mode)
    {
        var source = """
            type GetTimeFn = Date["getTime"];
            const f: GetTimeFn = new Date().getTime;
            console.log(typeof f);
            """;
        Assert.Equal("function\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MappedType_OverKeyOfBuiltIn_TypeChecks(ExecutionMode mode)
    {
        var source = """
            type Flags = { [K in keyof Set<number>]: boolean };
            const f = {} as Flags;
            console.log(typeof f);
            """;
        Assert.Equal("object\n", TestHarness.Run(source, mode));
    }

    // ----- #530: the projection extended to Error (and the other GetXxxMemberType built-ins) -----

    [Theory, ModeData]
    public void KeyOf_Error_AcceptsMemberName(ExecutionMode mode)
    {
        var source = """
            const k: keyof Error = "message";
            console.log(k);
            """;
        Assert.Equal("message\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void KeyOf_Error_RejectsNonMember(ExecutionMode mode)
    {
        var source = """
            const k: keyof Error = "notAMember";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_Error_AssignableToMatchingShape(ExecutionMode mode)
    {
        // The verbatim repro from #530: an Error value satisfies a matching structural shape.
        var source = """
            const e = new Error("boom");
            const o: { message: string } = e;
            console.log(o.message);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Structural_Error_MissingMember_Rejected(ExecutionMode mode)
    {
        var source = """
            const e = new Error("boom");
            const o: { notARealErrorMember: string } = e;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- #529: the weak-type check (TS2559) now sees a built-in source's members -----

    [Theory, ModeData]
    public void WeakType_BuiltInSource_NoSharedMember_Rejected(ExecutionMode mode)
    {
        // tsc: TS2559 — Date has no properties in common with the all-optional target. Before #529 the
        // built-in source contributed zero members, so the weak-type check was silently skipped.
        var source = """
            const o: { bogus?: number } = new Date();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void WeakType_BuiltInSource_SharedMember_Accepted(ExecutionMode mode)
    {
        // The weak-type rule fires only when NOTHING is in common: a target sharing a member name with
        // the Date source passes the weak check (and then ordinary structural assignability applies).
        var source = """
            const o: { getTime?: () => number } = new Date();
            console.log(typeof o.getTime);
            """;
        Assert.Equal("function\n", TestHarness.Run(source, mode));
    }
}
