using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NumericRestUndefinedGuardTests
{
    [Fact]
    public void ScalarUndefinedProducerRetainsBoxedRestConstruction()
    {
        const string source = """
            function absent(): number { return undefined as any; }
            function pack(prefix: number, ...values: number[]): number[] { return values; }
            function run(): number[] { return pack(0, absent(), 4); }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var assembly = StableNumericHotPathTests.Compile(source);
        var run = StableNumericHotPathTests.FindFunction(assembly, "run");
        var instructions = StableNumericHotPathTests.ReadInstructions(run).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is ConstructorInfo { DeclaringType.Name: "$Array" });
        Assert.DoesNotContain(instructions, instruction => instruction.Operand is MethodBase { Name: "CreateNumericRest" });
    }
}
