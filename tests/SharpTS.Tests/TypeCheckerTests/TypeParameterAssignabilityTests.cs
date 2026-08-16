using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Strict type-parameter assignability (TypeScript: "type parameters are not assignable to one
/// another unless directly or indirectly constrained to one another") plus the per-statement error
/// recovery that lets all such errors surface at their own lines.
/// </summary>
public class TypeParameterAssignabilityTests
{
    private static int[] ErrorLines(string source)
    {
        var lexer = new Lexer(source);
        var parseResult = new Parser(lexer.ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess);
        var result = new TypeChecker().CheckWithRecovery(parseResult.Statements);
        return result.Diagnostics.Select(d => d.Line).OrderBy(l => l).ToArray();
    }

    [Fact]
    public void DistinctUnconstrainedParameters_NotAssignable()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("function f<T, U>(t: T, u: U) { t = u; }"));
    }

    [Fact]
    public void ConcreteType_NotAssignableToBareParameter()
    {
        // A concrete value cannot be assigned to a bare (unbound) type parameter.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("function f<T>(t: T) { t = 1; }"));
    }

    [Fact]
    public void SubtypeParameter_AssignableToConstraintParameter()
    {
        // U extends T ⇒ U is assignable to T (but not the reverse).
        TestHarness.RunInterpreted("function f<T, U extends T>(t: T, u: U) { t = u; }");
    }

    [Fact]
    public void TransitivelyConstrainedParameter_Assignable()
    {
        // T extends U ⇒ T assignable to U (single level).
        TestHarness.RunInterpreted("function f<T extends U, U>(t: T, u: U) { u = t; }");
    }

    [Fact]
    public void MultiLevelConstraintChain_TransitivelyAssignable()
    {
        // T extends U extends V ⇒ T assignable to V (the constraint chain must link three deep).
        TestHarness.RunInterpreted("function f<T extends U, U extends V, V>(t: T, v: V) { v = t; }");
    }

    [Fact]
    public void MultiLevelConstraintChain_ReverseNotAssignable()
    {
        // V is not assignable to T across the same chain.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("function f<T extends U, U extends V, V>(t: T, v: V) { t = v; }"));
    }

    [Fact]
    public void MultiLevelConstraintChain_AssignableToConcreteApparentType()
    {
        // T extends U extends V extends Date ⇒ T assignable to Date (apparent type via the chain).
        TestHarness.RunInterpreted("""
            function f<T extends U, U extends V, V extends Date>(t: T) {
                let d: Date = t;
            }
            """);
    }

    [Fact]
    public void Parameter_AssignableToItsConstraint()
    {
        // A constrained parameter is assignable wherever its apparent (constraint) type is.
        TestHarness.RunInterpreted("function f<T extends number>(t: T) { let n: number = t; }");
    }

    [Fact]
    public void Recovery_ReportsAllErrors_AtTheirOwnLines()
    {
        // Both assignments are errors; recovery must report both, each at its own line (not the
        // function declaration line).
        var source = """
            function f<T, U>(t: T, u: U) {
                t = u;
                u = t;
            }
            """;
        Assert.Equal(new[] { 2, 3 }, ErrorLines(source));
    }

    [Fact]
    public void GenericClassInheritance_StillTypeChecks()
    {
        // Guard: the strict rule must not break a class extending a generic parent (the inherited
        // constructor's parameters are substituted from `extends Box<string>`).
        var output = TestHarness.RunInterpreted("""
            class Box<T> { value: T; constructor(v: T) { this.value = v; } }
            class StringBox extends Box<string> { upper(): string { return this.value.toUpperCase(); } }
            let sb: StringBox = new StringBox("hello");
            console.log(sb.upper());
            """);
        Assert.Equal("HELLO\n", output);
    }
}
