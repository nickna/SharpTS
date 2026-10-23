using SharpTS.Parsing;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassMethodDefaultContextTypeCheckTests
{
    [Theory, MemberData(nameof(ClassMethodDefaultContextTests.Cases), MemberType = typeof(ClassMethodDefaultContextTests))]
    public void DefaultsAreCheckedWithinTheirDeclaringClass(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();

        new TypeChecker().Check(statements, document);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        // Preserve SharpTS's existing outside-class private/super diagnostic codes.
        yield return ["private-context-does-not-leak-after-class", """
            class Owner { #secret = 7; make(read: any = () => this.#secret) { return read(); } }
            const outside = (owner: any) => owner.#secret;
            """, "TS18013", 2];
        yield return ["super-context-does-not-leak-after-class", """
            class Parent { read() { return 1; } }
            class Owner extends Parent { make(read: any = () => super.read()) { return read(); } }
            function outside(read: any = () => super.read()) { return read(); }
            """, "TS2335", 3];
        yield return ["static-private-default-cannot-use-instance-field", """
            class Owner {
                #secret = 1;
                static make(read: any = () => this.#secret) { return read(); }
            }
            """, "TS2339", 3];
        yield return ["instance-private-default-cannot-use-static-field", """
            class Owner {
                static #secret = 1;
                make(read: any = () => this.#secret) { return read(); }
            }
            """, "TS2339", 3];
        yield return ["static-super-default-cannot-use-instance-method", """
            class Parent { read() { return 1; } }
            class Owner extends Parent { static make(read: any = () => super.read()) { return read(); } }
            """, "TS2339", 2];
        yield return ["default-cannot-use-unrelated-private-owner", """
            class Other { #secret = 1; }
            class Owner { #secret = 2; make(other: Other, read: any = () => other.#secret) { return read(); } }
            """, "TS18013", 2];
        yield return ["default-value-type-remains-checked", """
            class Owner {
                #secret = 7;
                make(value: string = this.#secret) { return value; }
            }
            """, "TS2322", 3];
        // SharpTS reports whole-argument TS2345 with its existing class-line fallback;
        // TypeScript reports the contextually typed property as TS2322 on line 2.
        yield return ["field-call-keeps-inferred-destructured-default-type", """
            class Owner {
                bad = this.make({ x: "wrong" });
                make({ x = 1 } = {}) {}
            }
            """, "TS2345", 1];
        yield return ["field-call-rechecks-private-default-refinement", """
            class Owner {
                bad = this.make({ x: "wrong" });
                #secret = 7;
                make({ x = this.#secret } = {}) { return x; }
            }
            """, "TS2345", 1];
        yield return ["expression-field-call-rechecks-private-default-refinement", """
            const Owner = class {
                bad = this.make({ x: "wrong" });
                #secret = 7;
                make({ x = this.#secret } = {}) { return x; }
            };
            """, "TS2345", 1];
        yield return ["earlier-method-default-sees-refined-later-signature", """
            class Owner {
                first(value: any = this.make({ x: "wrong" })) { return value; }
                #secret = 7;
                make({ x = this.#secret } = {}) { return x; }
            }
            """, "TS2345", 2];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void ClassDefaultContextKeepsAccessRestrictionsAndTypes(
        string name, string source, string code, int line)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == code && diagnostic.Line == line);
    }

    [Fact]
    public void NamedPrivateMethodPatternDefaultWaitsForClassContext()
    {
        const string source = """
            class Owner {
                static make({ value = Owner.#read() } = {}) { return value; }
                static #read() { return 7; }
            }
            """;
        var document = new SourceDocument("private-call-default.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();

        new TypeChecker().Check(statements, document);
    }

    [Fact]
    public void InstanceAndStaticDefaultArrowsKeepDistinctClassContexts()
    {
        const string source = """
            class Owner {
                #instanceSecret = 7;
                static #staticSecret = 8;
                make(read: any = () => this.#instanceSecret) { return read(); }
                static make(read: any = () => this.#staticSecret) { return read(); }
            }
            """;
        var document = new SourceDocument("instance-static-defaults.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();

        new TypeChecker().Check(statements, document);
    }
}
