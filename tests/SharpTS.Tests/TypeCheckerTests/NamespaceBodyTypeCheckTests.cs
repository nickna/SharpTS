using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Statements inside a namespace/module body are type-checked like top-level statements.
/// Previously only declarations were checked, so type errors in expression statements (e.g. a
/// bad assignment) inside a namespace were silently ignored — a soundness hole.
/// </summary>
public class NamespaceBodyTypeCheckTests
{
    [Fact]
    public void BadAssignment_InNamespace_IsReported()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted(
                "class S { a: string = \"\"; } class T { b: number = 0; } " +
                "namespace N { let s: S; let t: T; s = t; }"));
    }

    [Fact]
    public void BadAssignment_InModule_IsReported()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted(
                "module M { let n: number = 1; let s: string = \"\"; n = s; }"));
    }

    [Fact]
    public void ValidStatements_InNamespace_AreClean()
    {
        TestHarness.RunInterpreted("module M { let a: number = 1; a = 2; let b = a + 1; }");
    }

    [Fact]
    public void BadCallResultAssignment_InNamespace_IsReported()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted(
                "namespace N { function f(): string { return \"\"; } let n: number = f(); }"));
    }
}
