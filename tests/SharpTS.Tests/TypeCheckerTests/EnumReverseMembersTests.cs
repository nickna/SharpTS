using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EnumReverseMembersTests
{
    [Fact]
    public void PublicEnumTypeAllowsNumericAliases()
    {
        var members = new Dictionary<string, object> { ["First"] = 2d, ["Last"] = 2d };
        var enumeration = new TypeInfo.Enum("E", members.ToFrozenDictionary(), EnumKind.Numeric);

        var alias = Assert.Single(enumeration.ReverseMembers);
        Assert.Equal(2d, alias.Key);
        Assert.Contains(alias.Value, members.Keys);
    }

    [Fact]
    public void CheckedEnumUsesTheLastDeclaredAlias()
    {
        var statements = TestHarness.ParseOrThrow("""
            enum E { First = 2, Longer = 2, End = 2 }
            E;
            """);
        var map = new TypeChecker().Check(statements);
        var expression = Assert.IsType<Stmt.Expression>(statements[1]);
        var enumeration = Assert.IsType<TypeInfo.Enum>(map.Get(expression.Expr));

        Assert.Equal("End", Assert.Single(enumeration.ReverseMembers).Value);
    }
}
