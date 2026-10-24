using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OverloadedClassCompatibilityTests
{
    private static void Check(string source) => new TypeChecker().Check(
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Fact]
    public void OverloadedClassRemainsAssignableToItself()
    {
        Check("""
            class C {
                method(value: number): number;
                method(value: string): number;
                method(value: any): number { return 1; }
            }
            function identity(value: C): C { return value; }
            """);
    }

    [Fact]
    public void MethodResultContainingOverloadedClassCanBeAssignedToTheSameInterface()
    {
        Check("""
            class C {
                equals(value: C): boolean;
                equals(value: any): boolean;
                equals(value: any): boolean { return true; }
            }
            interface Materialized { readonly nodes: C[]; }
            class Root {
                materialize(): Materialized { return { nodes: [] }; }
                run(): void {
                    let nested: Materialized;
                    nested = this.materialize();
                }
            }
            """);
    }

    [Theory]
    [InlineData("string", "number", false)]
    [InlineData("boolean", "number", true)]
    [InlineData("string", "string", true)]
    public void DistinctClassesMustMatchEveryDeclaredOverload(
        string secondParameter, string result, bool rejected)
    {
        string source = $$"""
            class Target {
                method(value: number): number;
                method(value: string): number;
                method(value: any): number { return 1; }
            }
            class Source {
                method(value: number): {{result}};
                method(value: {{secondParameter}}): {{result}};
                method(value: any): any { return 1; }
            }
            declare const source: Source;
            const target: Target = source;
            """;
        if (rejected) Assert.ThrowsAny<TypeCheckException>(() => Check(source));
        else Check(source);
    }

    [Fact]
    public void OverloadedGenericMembersRetainTheirInstantiatedArguments()
    {
        Assert.ThrowsAny<TypeCheckException>(() => Check("""
            class Box<T> {
                read(value: number): T;
                read(value: string): T;
                read(value: any): any { return value; }
            }
            declare const strings: Box<string>;
            const numbers: Box<number> = strings;
            """));
    }

    [Fact]
    public void SourceOverloadsCannotRequireAdditionalArguments()
    {
        Assert.ThrowsAny<TypeCheckException>(() => Check("""
            class Target {
                method(value: number): number;
                method(value: string): number;
                method(value: any): number { return 1; }
            }
            class Source {
                method(value: number, extra: number): number;
                method(value: string, extra: number): number;
                method(value: any, extra?: number): number { return 1; }
            }
            declare const source: Source;
            const target: Target = source;
            """));
    }

    [Fact]
    public void MatchingOverloadsDoNotErasePrivateDeclarationIdentity()
    {
        Assert.ThrowsAny<TypeCheckException>(() => Check("""
            class Target {
                private brand: number = 1;
                method(value: number): number;
                method(value: string): number;
                method(value: any): number { return 1; }
            }
            class Source {
                private brand: number = 1;
                method(value: number): number;
                method(value: string): number;
                method(value: any): number { return 1; }
            }
            declare const source: Source;
            const target: Target = source;
            """));
    }
}
