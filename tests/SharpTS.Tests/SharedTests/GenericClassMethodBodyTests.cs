using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericClassMethodBodyTests
{
    public static IEnumerable<object[]> InvalidBodies()
    {
        yield return ["original-read", "TS2322", """
            class C {
                bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            }
            """];
        yield return ["constraint-write", "TS2322", """
            class C {
                bad<T extends { value: number }>(other: T) { other.value = "wrong"; }
            }
            """];
        yield return ["any-constraint", "TS2339", """
            class C { bad<T extends any>(other: T) { return other.missing; } }
            """];
        yield return ["unknown-constraint", "TS2339", """
            class C { bad<T extends unknown>(other: T) { return other.missing; } }
            """];
        yield return ["unconstrained", "TS2339", """
            class C { bad<T>(other: T) { return other.missing; } }
            """];
        yield return ["static", "TS2322", """
            class C {
                static bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            }
            """];
        yield return ["class-expression", "TS2322", """
            const C = class {
                bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            };
            """];
        yield return ["class-expression-static", "TS2322", """
            const C = class {
                static bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            };
            """];
        yield return ["computed", "TS2322", """
            class C {
                [Symbol.iterator]<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            }
            """];
        yield return ["private", "TS2322", """
            class C {
                #bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            }
            """];
        yield return ["method-shadows-class-parameter", "TS2322", """
            class C<T> {
                bad<T extends { value: number }>(other: T) {
                    const result: string = other.value;
                }
            }
            """];
    }

    [Theory, MemberData(nameof(InvalidBodies))]
    public void MethodBodiesRespectTheirOwnTypeParameters(string name, string code, string source)
    {
        Assert.NotEmpty(name);
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void ConstrainedInstanceStaticAndExpressionMethodsAcceptValidObjects(ExecutionMode mode)
    {
        const string source = """
            class C {
                read<T extends { value: number }>(other: T): number { return other.value; }
                static read<T extends { value: number }>(other: T): number { return other.value; }
            }
            const Expression = class {
                read<T extends { value: number }>(other: T): number { return other.value; }
            };
            console.log(new C().read({ value: 3 }), C.read({ value: 4 }),
                new Expression().read({ value: 5 }));
            """;
        Assert.Equal("3 4 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MethodAndContainingClassTypeParameterScopesRemainIndependent(ExecutionMode mode)
    {
        const string source = """
            class Container<Outer> {
                value: Outer;
                constructor(value: Outer) { this.value = value; }
                read<T extends { value: number }>(other: T): number {
                    const saved: Outer = this.value;
                    console.log(saved);
                    return other.value;
                }
            }
            class Shadow<T> {
                read<T extends { value: number }>(other: T): number { return other.value; }
            }
            console.log(new Container<string>("outer").read({ value: 6 }),
                new Shadow<string>().read({ value: 7 }));
            """;
        Assert.Equal("outer\n6 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericMethodCallsInferAndDefaultTheirTypeArguments(ExecutionMode mode)
    {
        const string source = """
            class C {
                identity<T>(value: T) { return value; }
                defaulted<T = number>(value: any): T { return value; }
                apply<T>(operation: (value: T) => T, value: T): T { return operation(value); }
            }
            const value = new C();
            const inferred: number = value.identity(8);
            const defaulted: number = value.defaulted(9);
            const mapped: number = value.apply((input: number) => input + 1, 10);
            const text: string = value.apply((input: string) => input + "!", "ok");
            console.log(inferred, defaulted, mapped, text);
            """;
        Assert.Equal("8 9 11 ok!\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericMethodsSubstituteContainingClassReturnsConstraintsAndDefaults(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                get<U>(other: U): T { return this.value; }
                accept<U extends T>(value: U): T { return value; }
                defaulted<U = T>(value: any): U { return value; }
            }
            const words = new Box<string>("outer"), numbers = new Box<number>(2);
            const word: string = words.get(0);
            const number: number = numbers.get("ignored");
            const acceptedWord: string = words.accept("value");
            const acceptedNumber: number = numbers.accept(3);
            const defaultWord: string = words.defaulted("default");
            const defaultNumber: number = numbers.defaulted(4);
            console.log(word, number, acceptedWord, acceptedNumber, defaultWord, defaultNumber);
            """;
        Assert.Equal("outer 2 value 3 default 4\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ContainingClassGenericReturnDoesNotBecomeAnyAtTheCallSite()
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                get<U>(other: U): T { return this.value; }
            }
            const result: number = new Box<string>("value").get(0);
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void MethodTypeParameterDoesNotCaptureAnEnclosingFunctionParameter(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                get<U>(other: U): T { return this.value; }
            }
            function read<U>(box: Box<U>): U { return box.get<number>(1); }
            console.log(read(new Box<string>("outer")), read(new Box<number>(2)));
            """;
        Assert.Equal("outer 2\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void MethodTypeArgumentDoesNotReplaceAnEnclosingFunctionReturnType()
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                get<U>(other: U): T { return this.value; }
            }
            function read<U>(box: Box<U>): number { return box.get<number>(1); }
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void MethodTypeParameterDoesNotCaptureATemplateInterpolation()
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                get<U>(other: U): T { return this.value; }
            }
            function read<U extends string>(box: Box<`x${U}`>): `x${U}` {
                return box.get<number>(1);
            }
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Theory, ModeData]
    public void MixedGenericOverloadsKeepTheirPublicSignaturesAndOptionalImplementation(ExecutionMode mode)
    {
        const string source = """
            class C {
                choose<T>(value: T): T;
                choose(value: string, repeat: boolean): string;
                choose(value: any, repeat: boolean = false): any {
                    return repeat ? value + value : value;
                }
            }
            const value = new C();
            const number: number = value.choose(12);
            const text: string = value.choose("a", true);
            console.log(number, text);
            """;
        Assert.Equal("12 aa\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateGenericCallsInferWithoutExplicitTypeArguments(ExecutionMode mode)
    {
        const string source = """
            class C {
                #identity<T>(value: T): T { return value; }
                read(): number { return this.#identity(10); }
            }
            console.log(new C().read());
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }
}
