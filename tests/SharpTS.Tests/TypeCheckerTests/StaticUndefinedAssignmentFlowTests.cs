using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticUndefinedAssignmentFlowTests
{
    [Theory]
    [InlineData("this.value = undefined;", true)]
    [InlineData("this['value'] = undefined;", true)]
    [InlineData("this['value'] = maybe;", true)]
    [InlineData("this['value'] = 1; this['value'] = undefined;", true)]
    [InlineData("this['value'] = 1;", false)]
    [InlineData("const n = undefined; this.value = n;", true)]
    [InlineData("this.value = maybe;", true)]
    [InlineData("this.value = anyValue;", true)]
    [InlineData("this.value = void 0;", true)]
    [InlineData("this.value = 1; this.value = undefined;", true)]
    [InlineData("if (flag) this.value = 1; else this.value = undefined;", true)]
    [InlineData("this.value = 1;", false)]
    [InlineData("const n = 1; this.value = n;", false)]
    [InlineData("this.value = null;", false)]
    [InlineData("this.value = neverValue;", false)]
    [InlineData("if (maybe !== undefined) this.value = maybe; else throw 0;", false)]
    [InlineData("this.value ??= 1;", false)]
    public void InitializationDependsOnTheAssignedValue(string body, bool reportsUninitializedRead)
    {
        foreach (string declaration in new[] { "class C", "const C = class" })
            Check($$"""
                declare const flag: boolean;
                declare const maybe: number | undefined;
                declare const anyValue: any;
                declare const neverValue: never;
                {{declaration}} {
                    static value: number | null | undefined;
                    static { {{body}} }
                    static read = this.value;
                }
                """, reportsUninitializedRead);
    }

    [Theory]
    [InlineData("this.value = 1;", "this.value = undefined;", true)]
    [InlineData("this.value = undefined;", "this.value = 1;", false)]
    [InlineData("this.value = 1;", "if (flag) this.value = undefined;", true)]
    public void LaterBlocksCanReplaceTheEarlierInitializationState(
        string first, string second, bool reportsUninitializedRead)
    {
        Check($$"""
            declare const flag: boolean;
            class C {
                static value: number | undefined;
                static { {{first}} }
                static { {{second}} }
                static read = this.value;
            }
            """, reportsUninitializedRead);
    }

    [Fact]
    public void PrivateStaticAssignmentsAlsoRetainUndefined()
    {
        Check("""
            const C = class {
                static #value: number | undefined;
                static { this.#value = undefined; }
                static read = this.#value;
            };
            """, reportsUninitializedRead: true);
    }

    [Fact]
    public void AssignmentFromAnyToANonUndefinedFieldRemainsInitialized()
    {
        Check("""
            declare const value: any;
            class C {
                static value: number;
                static { this.value = value; }
                static read = this.value;
            }
            """, reportsUninitializedRead: false);
    }

    [Theory]
    [InlineData("number | undefined", true)]
    [InlineData("number", false)]
    public void GenericAssignmentsConsiderTheirConstraint(string constraint, bool reportsUninitializedRead)
    {
        Check($$"""
            function check<T extends {{constraint}}>(value: T) {
                class C {
                    static value: number | undefined;
                    static { this.value = value; }
                    static read = this.value;
                }
            }
            """, reportsUninitializedRead);
    }

    private static void Check(string source, bool reportsUninitializedRead)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (reportsUninitializedRead)
            Assert.Equal("TS2729", Assert.Single(diagnostics).TsCode);
        else
            Assert.Empty(diagnostics);
    }
}
