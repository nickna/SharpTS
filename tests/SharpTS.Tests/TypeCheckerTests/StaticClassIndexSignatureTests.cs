using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticClassIndexSignatureTests
{
    [Theory]
    [InlineData("class C { static [key: number]: string; static [key: symbol]: string; }")]
    [InlineData("declare class C { static [key: number]: string; static [key: symbol]: string; }")]
    [InlineData("class C<T> { static [key: number]: string; static [key: symbol]: string; }")]
    public void ConstructorIndicesDoNotPublishInstanceIndexMetadata(string source)
    {
        var types = new TypeChecker().Check(TestHarness.ParseOrThrow(source));
        var type = Assert.IsType<TypeInfo.Class>(types.GetClassType("C"));
        Assert.Null(type.NumberIndexType);
        Assert.Null(type.SymbolIndexType);
        Assert.False(type.Core.HasIndexSignature);
        Assert.IsType<TypeInfo.String>(type.Core.StaticNumberIndexType);
        Assert.IsType<TypeInfo.String>(type.Core.StaticSymbolIndexType);
    }

    [Theory]
    [InlineData("class C")]
    [InlineData("declare class C")]
    [InlineData("const C = class")]
    public void ConstructorAndInstanceIndicesKeepIndependentValueTypes(string declaration)
    {
        string source = declaration + """
            {
                static [key: number]: string;
                static [key: symbol]: string;
                [key: number]: number;
                [key: symbol]: number;
            };
            const key: symbol = Symbol("key");
            const uniqueKey: unique symbol = Symbol("unique");
            const numericStatic: string = C[0];
            const numericStringStatic: string = C["0"];
            const symbolicStatic: string = C[key];
            const uniqueSymbolicStatic: string = C[uniqueKey];
            const instance = new C();
            const numericInstance: number = instance[0];
            const symbolicInstance: number = instance[key];
            C[0] = "number key";
            C["0"] = "numeric string key";
            C[key] = "symbol key";
            C[uniqueKey] = "unique symbol key";
            instance[0] = 1;
            instance[key] = 2;
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Theory]
    [InlineData("const incompatible: number = C[0];")]
    [InlineData("const incompatible: number = C[\"0\"];")]
    [InlineData("const incompatible: number = C[key];")]
    [InlineData("const incompatible: number = C[uniqueKey];")]
    [InlineData("C[0] = 1;")]
    [InlineData("C[\"0\"] = 1;")]
    [InlineData("C[key] = 1;")]
    [InlineData("C[uniqueKey] = 1;")]
    [InlineData("const incompatible: number = Derived[0];")]
    [InlineData("const incompatible: number = Derived[key];")]
    public void StaticIndexReadsAndWritesEnforceTheirDeclaredType(string use)
    {
        string source = """
            class C {
                static [key: number]: string;
                static [key: symbol]: string;
            }
            class Derived extends C {}
            const key: symbol = Symbol("key");
            const uniqueKey: unique symbol = Symbol("unique");
            """ + use;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void StaticNumericIndexTakesPrecedenceOverStaticStringIndex()
    {
        const string source = """
            class C {
                static [key: string]: string | number;
                static [key: number]: number;
            }
            const numeric: number = C[0];
            const numericString: number = C["0"];
            const textual: string | number = C["other"];
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Fact]
    public void UnionConstructorsKeepNumericAndSymbolIndexReadsAndWrites()
    {
        const string source = """
            class C { static [key: number]: string; static [key: symbol]: string; }
            class D { static [key: number]: string; static [key: symbol]: string; }
            function read(constructor: typeof C | typeof D, key: symbol): string {
                constructor[0] = "numeric";
                constructor["0"] = "numeric string";
                constructor[key] = "symbolic";
                return constructor[0] + constructor["0"] + constructor[key];
            }
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Fact]
    public void ForwardConstructorRetainsItsNumericAndSymbolIndices()
    {
        const string source = """
            const key: symbol = Symbol("key");
            function read(): string {
                const numeric: string = Later[0];
                const symbolic: string = Later[key];
                return numeric + symbolic;
            }
            class Later {
                static [key: number]: string;
                static [key: symbol]: string;
            }
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }
}
