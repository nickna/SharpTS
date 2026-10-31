using System.Text.RegularExpressions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SwitchLiteralFlowTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["""
            let zero: 0 = 0; let one: 1 = 1; let two: 2 = 2;
            function read(x: 0 | 1 | 2) {
                switch (x) {
                    case zero: /* 0 */ x; break;
                    case one: /* 1 */ x; break;
                    case two: /* 2 */ x; break;
                    default: /* never */ x;
                }
            }
            """];
        yield return ["""
            let choice: 1 | 2 = <1 | 2>1;
            function read(x: 0 | 1 | 2) {
                switch (x) {
                    case 0: /* 0 */ x; break;
                    case choice: /* 1 | 2 */ x; break;
                    default: /* 1 | 2 */ x;
                }
            }
            """];
        yield return ["""
            function read(x: 0 | 1 | true | string) {
                switch (x) {
                    case 0: /* 0 */ x; break;
                    case 1: /* 1 */ x; break;
                    case "abc":
                    case "def": /* "abc" | "def" */ x; break;
                    case null: /* never */ x; break;
                    case undefined: /* never */ x; break;
                    default: /* string | true */ x;
                }
            }
            """];
        yield return ["""
            function read(x: string | number | boolean) {
                switch (x) {
                    case "abc": /* "abc" */ x; break;
                    case 0:
                    case 1: /* 0 | 1 */ x; break;
                    case true: /* true */ x; break;
                    case "hello":
                    case 123: /* "hello" | 123 */ x; break;
                    default: /* string | number | false */ x;
                }
            }
            """];
        yield return ["""
            function read(x: string) {
                switch (x) {
                    case "a": /* "a" */ x; break;
                    case "b": /* "b" */ x; break;
                    default: /* string */ x;
                }
                /* string */ x;
            }
            """];
        yield return ["""
            function read(x: 'a' | 'b' | 'c') {
                switch (x) {
                    default: /* "c" */ x; break;
                    case 'a':
                    case 'b': /* "a" | "b" */ x; break;
                }
                /* "a" | "b" | "c" */ x;
            }
            """];
        yield return ["""
            function read(x: 'a' | 'b' | 'c') {
                switch (x) {
                    case 'a': x = 'c'; /* "c" */ x;
                    case 'b': /* "b" | "c" */ x; break;
                    default: /* "c" */ x;
                }
                /* "b" | "c" */ x;
            }
            """];
        // Case expressions can mutate the live variable after the subject was captured.
        // Keep those writes even where TypeScript itself applies an optimistic case guard.
        yield return ["""
            function read(x: 'a' | 'b', replacement: 'a' | 'b') {
                switch (x) {
                    case (x = replacement, 'a'): /* "a" | "b" */ x; break;
                    default: /* "a" | "b" */ x;
                }
            }
            """];
        // Case expressions can mutate the live variable after the subject was captured.
        // Keep those writes even where TypeScript itself applies an optimistic case guard.
        yield return ["""
            function read(x: 'a' | 'b', replacement: 'a' | 'b') {
                switch (x) {
                    default: /* "a" | "b" */ x; break;
                    case (x = replacement, 'a'): /* "a" | "b" */ x; break;
                }
            }
            """];
        yield return ["""
            function read(x: string | null | undefined) {
                switch (x) {
                    case null: /* null */ x; break;
                    case undefined: /* undefined */ x; break;
                    default: /* string */ x;
                }
            }
            """];
        yield return ["""
            function read(x: boolean) {
                switch (x) {
                    case true: /* true */ x; break;
                    default: /* false */ x;
                }
                /* boolean */ x;
            }
            """];
        yield return ["""
            function read<T extends string>(x: T) {
                switch (x) { case 'a': /* T */ x; break; default: /* T */ x; }
            }
            """];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CheckedUsesFollowCaseSelectionFallthroughAndWrites(string source)
    {
        var document = new SourceDocument("switch-literals.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(strictNullChecks: true).WithEditorMetadata();
        checker.Check(statements, document);
        var facts = checker.EditorFacts.GetSourceTypeObservations(document);
        foreach (Match marker in Regex.Matches(source, @"/\* (?<type>.*?) \*/\s*(?<value>x);"))
        {
            var fact = Assert.Single(facts, fact => fact.Span.Start == marker.Groups["value"].Index);
            Assert.Equal(marker.Groups["type"].Value, fact.Display.Text);
        }
    }
}
