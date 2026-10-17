using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateInstanceReceiverTests
{
    public static IEnumerable<object[]> InvalidReceivers()
    {
        foreach (string operation in new[] { "return other.#x;", "other.#x = 2;" })
        {
            yield return ["class Other { #x = 9; }", "Other", operation, "TS18013"];
            yield return ["class Other {}", "Other", operation, "TS2339"];
            yield return ["", "{}", operation, "TS2339"];
            yield return ["", "unknown", operation, "TS18046"];
            yield return ["", "typeof C", operation, "TS2339"];
            yield return ["class Other { #x = 9; }", "C | Other", operation, "TS18013"];
        }
    }

    [Theory, MemberData(nameof(InvalidReceivers))]
    public void ReadsAndWritesRequireTheLexicalOwnersBrand(
        string declaration, string receiver, string operation, string code)
    {
        string source = $$"""
            {{declaration}}
            class C {
                #x: number = 1;
                inspect(other: {{receiver}}) { {{operation}} }
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("return other.#method;", "Other")]
    [InlineData("return other.#method();", "Other")]
    [InlineData("return other.#callable();", "Other")]
    public void CallableMembersRequireTheSameInstanceBrand(string operation, string receiver)
    {
        string source = $$"""
            class Other { #method() {} #callable = () => 1; }
            class C {
                #method() {}
                #callable = () => 1;
                inspect(other: {{receiver}}) { {{operation}} }
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS18013", error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void DerivedAndDynamicInstancesRetainTheLexicalBrand(ExecutionMode mode)
    {
        const string source = """
            class C {
                #x: number = 1;
                #method() { return this.#x; }
                inspect(other: C, loose: any) {
                    other.#x = 2;
                    loose.#x = 3;
                    return other.#method() + loose.#x;
                }
                constrained(other: C & { tag: string }) { return other.#x; }
                derived(other: D) { return other.#x; }
            }
            class D extends C { #x = 9; tag: string = "derived"; }
            const value = new D();
            console.log(new C().inspect(value, value), new C().constrained(value));
            console.log(new C().derived(value));
            """;
        Assert.Equal("6 3\n3\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("C", "return other.#x;")]
    [InlineData("C", "other.#x = 2;")]
    [InlineData("any", "return other.#x;")]
    [InlineData("any", "other.#x = 2;")]
    public void ConstrainedReceiversAreCheckedAgainstTheirConstraint(string constraint, string operation)
    {
        string source = $$"""
            class C {
                #x: number = 1;
                inspect() {
                    function use<T extends {{constraint}}>(other: T) { {{operation}} }
                }
            }
            """;
        if (constraint == "C")
            new TypeChecker().Check(TestHarness.ParseOrThrow(source));
        else
        {
            var error = Assert.Throws<TypeCheckException>(() =>
                new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
            Assert.Equal("TS2339", error.Diagnostic.TsCode);
        }
    }
}
