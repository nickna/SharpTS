using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ForwardConstructorConformanceRegressionTests
{
    [Fact]
    public void OpenGenericThisSatisfiesAnInstantiationWithEquivalentMembers()
    {
        const string source = """
            class Bar<T extends string> {
                cast(_name: ([T] extends [string] ? string : string)) { }
                pushThis() {
                    const instances: Bar<string>[] = [];
                    instances.push(this);
                }
            }
            """;
        new TypeChecker(new TypeCheckerOptions { StrictFunctionTypes = true })
            .Check(TestHarness.ParseOrThrow(source));
    }

    [Fact]
    public void OpenGenericThisRetainsItsTypeDependentMemberChecks()
    {
        const string source = """
            class Bar<T> {
                value!: T;
                pushThis() {
                    const instances: Bar<string>[] = [];
                    instances.push(this);
                }
            }
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker(new TypeCheckerOptions { StrictFunctionTypes = true })
                .Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2345", error.Diagnostic.TsCode);
    }

    private const string ConstructorSource = """
        interface Options<Data, Computed> { data?: Data; computed?: Computed; }
        declare class Component<Data, Computed> {
            constructor(options: Options<Data, Computed>);
            data: Data;
            get<K extends keyof (Data & Computed)>(key: K): (Data & Computed)[K];
        }
        const component = new Component({ data: { hello: "" } });
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorInfersFromGenericInterfaceProperties(bool inherited)
    {
        string source = inherited
            ? ConstructorSource.Replace(
                "interface Options<Data, Computed> { data?: Data; computed?: Computed; }",
                "interface DataOptions<Data> { data?: Data; } " +
                "interface Options<Data, Computed> extends DataOptions<Data> { computed?: Computed; }")
            : ConstructorSource;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source +
            "const text: string = component.get('hello');"));
    }

    [Theory]
    [InlineData("component.get('missing');", "TS2345")]
    [InlineData("const value: number = component.data.hello;", "TS2322")]
    public void InferredConstructorMembersRetainTheirKeyAndValueTypes(string use, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(ConstructorSource + use)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
