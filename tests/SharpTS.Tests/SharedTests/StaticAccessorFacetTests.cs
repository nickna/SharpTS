using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticAccessorFacetTests
{
    public const string IssueSource = """
        class B { static get value() { return () => this; } }
        class D extends B { static read() { return super.value(); } }
        console.log(D.read() === D);
        """;

    public const string ReadWriteSource = """
        let stored: number = 0;
        class Base {
            static get value(): number { return stored; }
            static set value(value: number | string) {
                stored = typeof value === "number" ? value : Number(value);
            }
            static get owner() { return () => this; }
            get value(): boolean { return true; }
            set value(value: boolean) {}
        }
        class Derived extends Base {
            static read() { return super.value; }
            static receiver() { return super.owner(); }
        }
        Derived.value = "7";
        const value: number = Derived.read();
        const instanceValue: boolean = new Derived().value;
        console.log(value, Derived.read(), instanceValue, Derived.receiver() === Derived, Base.value);
        new Derived().value = false;
        """;

    [Theory, ModeData]
    public void StaticSuperCallableGetterChecksAndPreservesReceiver(ExecutionMode mode)
        => Assert.Equal("true\n", TestHarness.Run(IssueSource, mode));

    [Theory, ModeData]
    public void StaticAccessorReadAndWriteTypesRemainSeparateFromInstances(ExecutionMode mode)
        => Assert.Equal("7 7 true true 7\n", TestHarness.Run(ReadWriteSource, mode));

    [Theory]
    [InlineData("", ExecutionMode.Interpreted)]
    [InlineData("Named", ExecutionMode.Interpreted)]
    [InlineData("", ExecutionMode.Compiled)]
    [InlineData("Named", ExecutionMode.Compiled)]
    public void ClassExpressionsInferCallableStaticGetters(string name, ExecutionMode mode)
    {
        string source = """
            let stored: number = 1;
            const Base = class NAME {
                static get value(): number { return stored; }
                static set value(value: number | string) { stored = Number(value); }
                static get owner() { return () => this; }
            };
            const Derived = class extends Base {
                static read() { return super.owner(); }
                static number() { return super.value; }
            };
            Derived.value = "9";
            const result: number = Derived.number();
            console.log(Derived.read() === Derived, Derived.number(), result, Base.value);
            """.Replace("NAME", name);
        Assert.Equal("true 9 9 9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InferredSetterPairingIncludesTheStaticFacet(ExecutionMode mode)
    {
        const string source = """
            class C {
                static stored: number = 0;
                static get value() { return this.stored; }
                static set value(value) { this.stored = value; }
                get value(): boolean { return true; }
                set value(value: boolean) {}
            }
            C.value = 8;
            const value: number = C.value;
            const instance: boolean = new C().value;
            new C().value = false;
            console.log(value, instance);
            """;
        Assert.Equal("8 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ProtectedStaticGetterResolvesThroughSuperInASubclass(ExecutionMode mode)
    {
        const string source = """
            class Base { protected static get owner() { return () => this; } }
            class Derived extends Base { static read() { return super.owner(); } }
            console.log(Derived.read() === Derived);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclarationGetterArrowsRetainMutableLexicalCaptures(ExecutionMode mode)
    {
        const string source = """
            class C {
                static get counter() {
                    let count: number = 0;
                    return () => ++count;
                }
            }
            const first = C.counter, second = C.counter;
            console.log(first(), first(), second());
            """;
        Assert.Equal("1 2 1\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("class C { static get value(): number { return 1; } } C.value = 2;", "TS2540")]
    [InlineData("class C { static get value(): number { return 1; } } class D extends C {} D.value = 2;", "TS2540")]
    [InlineData("const C = class { static get value() { return 1; } }; C.value = 2;", "TS2540")]
    [InlineData("class C { private static get value(): number { return 1; } } C.value;", "TS2341")]
    [InlineData("const C = class { private static get value(): number { return 1; } }; C.value;", "TS2341")]
    [InlineData("class C { protected static get value(): number { return 1; } } C.value;", "TS2445")]
    [InlineData("class C { static get value(): number { return 1; } static set value(value: number) {} } class D extends C { static get value(): number { return 2; } } D.value = 3;", "TS2540")]
    [InlineData("class C { static get value(): number { return 1; } static set value(value: number) {} } C.value = true;", "TS2322")]
    [InlineData("const C = class { static get value() { return 1; } static set value(value: number) {} }; C.value = true;", "TS2322")]
    [InlineData("class C { static get value(): number { return 1; } } const value: boolean = C.value;", "TS2322")]
    [InlineData("const C = class { static get value() { return 1; } }; const value: boolean = C.value;", "TS2322")]
    [InlineData("class C { static get value(): number { return 1; } } new C().value;", "TS2576")]
    [InlineData("const C = class { static get value(): number { return 1; } }; new C().value;", "TS2576")]
    public void StaticAccessorsEnforceReadWriteTypesReadonlyAndVisibility(string source, string tsCode)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(maxErrors: 50).CheckWithRecovery(statements);
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Contains(errors, diagnostic => diagnostic.TsCode == tsCode);
    }

    [Fact]
    public void StaticAccessorTypesAreStoredOnlyOnTheConstructorFacet()
    {
        var statements = new Parser(new Lexer("""
            class C { static get value(): number { return 1; } static set value(value: number | string) {} }
            """).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var type = typeMap.GetClassType((Stmt.Class)statements[0])!;
        Assert.Empty(type.Getters);
        Assert.Empty(type.Setters);
        Assert.Equal("number", type.StaticProperties["value"].ToString());
        Assert.IsType<TypeInfo.Union>(type.Core.StaticSetterTypes["value"]);
    }

    [Fact]
    public void GenericSuperclassAndAmbientDeclarationsPreserveStaticWriteTypes()
    {
        const string source = """
            declare class Base<T> {
                static get value(): number;
                static set value(value: number | string);
                static get fixed(): number;
            }
            class Derived extends Base<number> {}
            Derived.value = "7";
            const value: number = Derived.value;
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        new TypeChecker().Check(statements);
        var invalid = new Parser(new Lexer(source + "Derived.value = true; Derived.fixed = 2;").ScanTokens()).ParseOrThrow();
        var diagnostics = new TypeChecker(maxErrors: 50).CheckWithRecovery(invalid).Diagnostics;
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2540");
    }

    [Fact]
    public void AccessorDeclarationTypesFollowTheirStaticOrInstanceFacet()
    {
        const string source = """
            class C {
                static get value(): number { return 1; }
                static set value(value: number | string) {}
            }
            """;
        var document = new SourceDocument("static-accessors.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        Assert.True(checker.CheckWithRecovery(statements, document).IsSuccess);
        var facts = checker.EditorFacts.Freeze();
        var accessors = ((Stmt.Class)statements[0]).Accessors!;
        for (int index = 0; index < accessors.Count; index++)
        {
            var declaration = Assert.Single(facts.GetDeclarations(document, accessors[index]));
            Assert.Equal(EditorFactAvailability.Available, declaration.Availability);
            if (index == 1)
            {
                Assert.Contains("number", declaration.Type.Text);
                Assert.Contains("string", declaration.Type.Text);
            }
            else
                Assert.Equal(index == 0 ? "number" : "boolean", declaration.Type.Text);
        }
    }
}
