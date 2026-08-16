using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Hot lib globals in TYPE position (#226 round 4): Object/Date/RegExp/String/Number/Boolean
/// previously fell through the user-type lookup to <c>any</c>, making every such annotation
/// vacuously compatible. Also TS2403 through the VarHoister rewrite: a duplicate top-level
/// `var` becomes a synthesized assignment, and an incompatible one reports TS2403.
/// </summary>
public class LibGlobalTypesTests
{
    [Fact]
    public void DateAnnotation_RejectsNumber()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var d: Date = 1;
            """));
    }

    [Fact]
    public void DateAnnotation_AcceptsDateValue()
    {
        TestHarness.RunInterpreted("""
            var d: Date = new Date();
            var d2: Date = d;
            """);
    }

    [Fact]
    public void ObjectAnnotation_AcceptsNonNullish_RejectsIntoSpecificTypes()
    {
        TestHarness.RunInterpreted("""
            var o: Object = { a: 1 };
            var o2: Object = 42;
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            enum E { A }
            var o: Object = 42;
            var e: E = E.A;
            function use() { e = o; }
            """));
    }

    [Fact]
    public void StringWrapperAnnotation_RejectsNumericEnum()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            enum E { A }
            var e = E.A;
            var q: String = e;
            """));
    }

    [Fact]
    public void DuplicateTopLevelVar_DifferentType_IsError()
    {
        // Through VarHoister: the duplicate becomes a synthesized assignment; an incompatible
        // value reports TS2403 (subsequent declarations must have the same type).
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var r = 1;
            var r = "two";
            """));
    }

    [Fact]
    public void DuplicateTopLevelVar_SameType_IsAccepted()
    {
        TestHarness.RunInterpreted("""
            var r = 1;
            var r = 2;
            """);
    }
}
