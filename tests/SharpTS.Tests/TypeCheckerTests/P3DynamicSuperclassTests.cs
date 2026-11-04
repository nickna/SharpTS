using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3DynamicSuperclassTests
{
    [Fact]
    public void AnySuperclassKeepsUnknownInheritedMembers()
    {
        const string source = """
            class Parent { value = 7; }
            const Runtime: any = Parent;
            class Child extends Runtime { own: string = 'yes'; }
            const value: number = new Child().value;
            const own: string = new Child().own;
            """;
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics);
    }

    [Fact]
    public void OwnMembersRemainCheckedOnDynamicDerivedClasses()
    {
        const string source = """
            declare const Runtime: any;
            class Child extends Runtime { own: string = 'yes'; }
            const own: number = new Child().own;
            """;
        Assert.Contains(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }
}
