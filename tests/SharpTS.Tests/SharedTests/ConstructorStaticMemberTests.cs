using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ConstructorStaticMemberTests
{
    private const string DotNetConvertDeclaration = """
        @DotNetType("System.Convert")
        declare class Convert { static toString(value: boolean): string; }
        """;

    [Theory, ModeData]
    public void DotNetStaticAliasesRetainTheirDeclaredSignatures(ExecutionMode mode)
    {
        string source = DotNetConvertDeclaration + """
            const Alias = Convert;
            console.log(Convert.toString(true), Convert.ToString(false), Alias.ToString(true));
            """;
        Assert.Equal("True False True\n", TestHarness.Run(source, mode, DecoratorMode.Legacy));
    }

    [Theory, ModeData]
    public void PartialDotNetDeclarationsAllowUndeclaredClrStaticMembers(ExecutionMode mode)
    {
        string source = DotNetConvertDeclaration + """
            const Alias = Convert;
            const one: number = Convert.ToInt32(true);
            const zero: number = Alias.ToInt32(false);
            console.log(one, zero);
            """;
        Assert.Equal("1 0\n", TestHarness.Run(source, mode, DecoratorMode.Legacy));
    }

    [Theory]
    [InlineData("Convert.ToString(1);", "TS2345")]
    [InlineData("const value: number = Convert.ToString(true);", "TS2322")]
    public void DotNetStaticAliasesKeepArgumentAndReturnChecks(string use, string code)
    {
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var error = Assert.ThrowsAny<TypeCheckException>(() => checker.Check(
            new Parser(new Lexer(DotNetConvertDeclaration + use).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow()));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void SourceConstructorWithTheSameNameDoesNotAcquireDotNetAliases()
    {
        string source = DotNetConvertDeclaration + """
            function read() {
                class Convert { static toString(value: boolean): string { return "source"; } }
                return Convert.ToString(true);
            }
            """;
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var error = Assert.ThrowsAny<TypeCheckException>(() => checker.Check(
            new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow()));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C {} const value = C.missing;")]
    [InlineData("const C = class {}; const value = C.missing;")]
    [InlineData("const C = class Inner { static inspect() { return Inner.missing; } };")]
    [InlineData("class C<T> {} const value = C.missing;")]
    [InlineData("class C { value = 1; } const value = C.value;")]
    [InlineData("class C {} class D extends C {} const value = D.missing;")]
    [InlineData("class C {} C.missing();")]
    [InlineData("class C { [key: string]: number; } const value = C.missing;")]
    public void KnownConstructorsRejectMissingStaticMembers(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("name", "number")]
    [InlineData("length", "string")]
    public void ConstructorIntrinsicPropertiesRetainTheirTypes(string member, string incompatibleType)
    {
        string source = $"class C {{}} const value: {incompatibleType} = C.{member};";
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void ConstructorsKeepFunctionAndObjectIntrinsicMembers()
    {
        const string source = """
            class C {}
            const constructorName: string = C.name;
            const arity: number = C.length;
            const prototype: C = C.prototype;
            const call = C.call, apply = C.apply, bind = C.bind;
            const caller = C.caller, args = C.arguments;
            const text: string = C.toString();
            const locale: string = C.toLocaleString();
            const own: boolean = C.hasOwnProperty("name");
            const inherited: boolean = C.isPrototypeOf({});
            const enumerable: boolean = C.propertyIsEnumerable("name");
            const object: object = C.valueOf();
            const constructor = C.constructor;
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Theory, ModeData]
    public void OwnAndInheritedStaticMembersKeepTheirValues(ExecutionMode mode)
    {
        const string source = """
            class C {
                static value: number = 3;
                static read(value: number): number { return value + 1; }
            }
            class D extends C {}
            console.log(C.value, D.value, D.read(4));
            """;
        Assert.Equal("3 3 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BoundConstructorsRemainConstructable(ExecutionMode mode)
    {
        const string source = """
            class C { value = 1; }
            const Bound = C.bind(null);
            console.log(new Bound().value);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("class Derived extends dynamicBase {}")]
    [InlineData("const Derived = class extends dynamicBase {};")]
    public void AnyReceiversAndAnyValuedSuperclassesRetainDynamicMembers(string declaration)
    {
        string source = $$"""
            class Base {}
            const dynamicBase: any = Base;
            {{declaration}}
            const inherited = Derived.missing;
            const dynamic: any = Base;
            const direct = dynamic.missing;
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Theory]
    [InlineData("class C { static [key: string]: number; static present: number = 1; }")]
    [InlineData("class C<T> { static [key: string]: number; }")]
    [InlineData("const C = class { static [key: string]: number; };")]
    [InlineData("declare class C { static [key: string]: number; }")]
    [InlineData("class Base { static [key: string]: number; } class C extends Base {}")]
    public void StaticStringIndexSignaturesRemainConstructorMembers(string declaration)
    {
        string source = declaration + """
            const result: number = C.missing;
            const indexed: number = C["missing"];
            const constructorName: string = C.name;
            const arity: number = C.length;
            const own: boolean = C.hasOwnProperty("missing");
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Theory]
    [InlineData("C.missing")]
    [InlineData("C[\"missing\"]")]
    [InlineData("Derived.missing")]
    public void StaticStringIndexReadsRetainTheirDeclaredValueType(string expression)
    {
        string source = $$"""
            class C { static [key: string]: number; }
            class Derived extends C {}
            const result: string = {{expression}};
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void ForwardConstructorRetainsItsStaticStringIndex()
    {
        const string source = """
            function read(): number { return Later.missing; }
            class Later { static [key: string]: number; }
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }
}
