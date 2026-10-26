using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassExpressionFieldReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            const C = class {
                static first = 7;
                static second = this.first;
            };
            console.log(C.second);
            """, "", 0];

        // #2420 tracks the separate emitted static-arrow receiver behavior.
        yield return ["lexical-arrows", """
            const C = class { static first = 7; static second = (() => this.first)(); static read = () => this.first; }; console.log(C.second, C.read());
            """, "", 0];

        yield return ["ambient-static-method", """
            class Outer { static label = "outer"; static make() { return class { value = 3; copy = this.value; static first = 7; static second = this.first; }; } static read() { return this.label; } } const C = Outer.make(); console.log(new C().copy, C.second, Outer.read());
            """, "", 0];

        yield return ["ambient-static-block", """
            let saved: any; class Outer { static marker = 9; static { const Inner = class { value = 3; copy = this.value; static first = 7; static second = this.first; }; saved = Inner; const restored: number = this.marker; console.log(restored); } } console.log(new saved().copy, saved.second);
            """, "", 0];

        yield return ["ambient-explicit-this", """
            function make(this: { outer: number }) { const C = class { value = 3; copy = this.value; static first = 7; static second = this.first; }; const restored: number = this.outer; console.log(restored); return C; } const C = make.call({ outer: 11 }); console.log(new C().copy, C.second);
            """, "", 0];

        yield return ["wrong-instance-facet", """
            const C = class { static first = 7; copy = this.first; };
            """, "TS2576", 1];

        yield return ["wrong-static-result", """
            const C = class { static first = 7; static second: string = this.first; };
            """, "TS2322", 1];

        yield return ["wrong-instance-result", """
            const C = class { value = 3; copy: string = this.value; };
            """, "TS2322", 1];

        yield return ["restored-static-type-error", """
            class Outer { static label = "outer"; static make() { const C = class { static first = 7; static second = this.first; }; const wrong: number = this.label; return C; } }
            """, "TS2322", 1];

        yield return ["restored-explicit-this-type-error", """
            function make(this: { outer: string }) { const C = class { static first = 7; static second = this.first; }; const wrong: number = this.outer; return C; }
            """, "TS2322", 1];

        // Getter dependencies must preserve callable and scalar result types.
        yield return ["inferred-static-expression-getter-call", """
            const C = class {
                static get callback() { return () => 7; }
                static copied = this.callback;
                static result = this.copied();
            };
            const value: number = C.result;
            """, "", 0];

        yield return ["annotated-static-expression-getter-call", """
            const C = class {
                static get callback(): () => number { return () => 7; }
                static copied = this.callback;
                static result = this.copied();
            };
            const value: number = C.result;
            """, "", 0];

        yield return ["inferred-instance-expression-getter-call", """
            const C = class {
                get callback() { return () => 7; }
                copied = this.callback;
                result = this.copied();
            };
            const value: number = new C().result;
            """, "", 0];

        yield return ["inferred-static-declaration-getter-call", """
            class C {
                static get callback() { return () => 7; }
                static copied = this.callback;
                static result = this.copied();
            }
            const value: number = C.result;
            """, "", 0];

        yield return ["inferred-getter-call-result-must-be-number", """
            const C = class {
                static get callback() { return () => 7; }
                static copied = this.callback;
                static result: string = this.copied();
            };
            """, "TS2322", 4];

        yield return ["inferred-numeric-getter-is-not-callable", """
            const C = class {
                static get value() { return 7; }
                static copied = this.value;
                static result = this.copied();
            };
            """, "TS2349", 4];

        yield return ["explicit-owner-declaration-inferred", """
            class C {
                static get callback() { return () => 7; }
                static copied = C.callback;
                static result = C.copied();
            }
            const value: number = C.result;
            """, "", 0];

        yield return ["explicit-owner-declaration-inferred-wrong-result", """
            class C {
                static get callback() { return () => 7; }
                static copied = C.callback;
                static result = C.copied();
            }
            const value: string = C.result;
            """, "TS2322", 6];

        yield return ["explicit-owner-expression-inferred", """
            const C = class Named {
                static get callback() { return () => 7; }
                static copied = Named.callback;
                static result = Named.copied();
            };
            const value: number = C.result;
            """, "", 0];

        yield return ["explicit-owner-expression-inferred-wrong-result", """
            const C = class Named {
                static get callback() { return () => 7; }
                static copied = Named.callback;
                static result = Named.copied();
            };
            const value: string = C.result;
            """, "TS2322", 6];

        yield return ["explicit-owner-declaration-annotated", """
            class C {
                static get callback(): () => number { return () => 7; }
                static copied = C.callback;
                static result = C.copied();
            }
            const value: number = C.result;
            """, "", 0];

        yield return ["explicit-owner-expression-annotated", """
            const C = class Named {
                static get callback(): () => number { return () => 7; }
                static copied = Named.callback;
                static result = Named.copied();
            };
            const value: number = C.result;
            """, "", 0];

        // Pending numeric fields must never become an Any-callable getter dependency.
        yield return ["forward-public-numeric-field-is-not-callable", """
            class C {
                static get callback() { return C.later; }
                static copied = C.callback;
                static result = C.copied();
                static later = 7;
            }
            """, "TS2349", 4];

        yield return ["forward-private-numeric-field-is-not-callable", """
            class C {
                static get callback() { return C.#later; }
                static copied = C.callback;
                static result = C.copied();
                static #later = 7;
            }
            """, "TS2349", 4];

        yield return ["forward-literal-key-numeric-field-is-not-callable", """
            class C {
                get callback() { return this["later"]; }
                copied = this.callback;
                result = this.copied();
                later = 7;
            }
            """, "TS2349", 4];
    }

    [Theory, MemberData(nameof(Cases))]
    public void FieldInitializersSelectTheirOwnReceiverAndRestoreTheEnclosingContext(
        string name, string source, string expectedCode, int expectedLine)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var diagnostics = new TypeChecker(strictNullChecks: true, maxErrors: 20).CheckWithRecovery(statements)
            .Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (expectedCode.Length == 0)
        {
            Assert.Empty(diagnostics);
            return;
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.TsCode);
        Assert.Equal(expectedLine, diagnostic.Line);
        if (name == "wrong-instance-facet")
            Assert.Contains("Property 'first' does not exist", diagnostic.Message);
    }
}
