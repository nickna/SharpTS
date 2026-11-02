using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public sealed class TypedArrayOrdinaryNamedWriteTests
{
    public static IEnumerable<object[]> Cases() =>
        SharpTS.Tests.CompilerTests.TypedArrayDotAssignmentTests.AllCases()
            .Select(row => new object[] { row[0], row[1], row[2] });

    [Theory, MemberData(nameof(Cases))]
    public void OrdinaryNamesUseActualDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
