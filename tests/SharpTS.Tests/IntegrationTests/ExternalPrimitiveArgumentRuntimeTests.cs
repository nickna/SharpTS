using Xunit;
namespace SharpTS.Tests.IntegrationTests;
public sealed class ExternalPrimitiveArgumentRuntimeTests
{
    [Fact]
    public void CompleteBooleanDefaultAndExplicitControlVerifiesAndExecutes()
    {
        new ExternalSignatureRuntimeTests().OrdinaryExplicitReferenceCallsVerifyAndExecute("boolean-default",
            "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static OptionalBoolean(value?:boolean):boolean; } console.log(Api.OptionalBoolean(),Api.OptionalBoolean(false));", "true false\n");
    }
    public static IEnumerable<object[]> Controls()
    {
        yield return ["boolean", "boolean", "boolean", "false", "false"];
        yield return ["integer", "number", "number", "-7", "-7"];
        yield return ["number", "number", "number", "3.5", "3.5"];
        yield return ["single", "number", "number", "1.25", "1.25"];
        yield return ["long", "number", "number", "9876543210", "9876543210"];
        yield return ["short", "number", "number", "-1234", "-1234"];
        yield return ["unsigned", "number", "number", "4000000000", "4000000000"];
        yield return ["byte", "number", "number", "253", "253"];
        yield return ["signedByte", "number", "number", "-12", "-12"];
        yield return ["uShort", "number", "number", "65000", "65000"];
        yield return ["uLong", "number", "number", "123456789", "123456789"];
        yield return ["character", "string", "string", "\"z\"", "z"];
        yield return ["booleans", "boolean[]", "boolean[]", "[true,false]", "true|false"];
        yield return ["integers", "number[]", "number[]", "[1,-2,3]", "1|-2|3"];
        yield return ["numbers", "number[]", "number[]", "[1.5,-2.25]", "1.5|-2.25"];
        yield return ["strings", "string[]", "string[]", "[\"a\",\"b\"]", "a|b"];
    }
    [Theory]
    [MemberData(nameof(Controls))]
    public void SuppliedCoreAtomsAndStructuralArraysVerifyAndExecute(string method, string input, string output, string argument, string expected)
    {
        string name = char.ToUpperInvariant(method[0]) + method[1..];
        string result = $"Api.{name}({argument})";
        if (output.EndsWith("[]", StringComparison.Ordinal)) result += ".join(\"|\")";
        new ExternalSignatureRuntimeTests().OrdinaryExplicitReferenceCallsVerifyAndExecute(method,
            $"@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api {{ static {name}(value:{input}):{output}; }} console.log({result});", expected + "\n");
    }
}
