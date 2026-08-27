using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class StrictCheckerOptionTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(
        string source,
        TypeCheckerOptions options)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        return new TypeChecker(options with { MaxErrors = 50 })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;
    }

    [Fact]
    public void NoImplicitThis_ReportsUnannotatedFunctionThis()
    {
        const string source = "function f() { return this.value; }";

        Assert.DoesNotContain(
            Diagnose(source, TypeCheckerOptions.Default),
            d => d.TsCode == "TS2683");
        Assert.Contains(
            Diagnose(source, TypeCheckerOptions.Default with { NoImplicitThis = true }),
            d => d.TsCode == "TS2683");
    }

    [Fact]
    public void UseUnknownInCatchVariables_RejectsUncheckedPropertyAccess()
    {
        const string source = "try {} catch (error) { error.message; }";

        Assert.DoesNotContain(
            Diagnose(source, TypeCheckerOptions.Default),
            d => d.TsCode == "TS18046");
        Assert.Contains(
            Diagnose(source, TypeCheckerOptions.Default with { UseUnknownInCatchVariables = true }),
            d => d.TsCode == "TS18046");
    }

    [Fact]
    public void ExactOptionalPropertyTypes_ControlsPresentUndefined()
    {
        const string source = """
            interface Config { value?: string; }
            const config: Config = { value: undefined };
            config.value = undefined;
            """;

        Assert.DoesNotContain(
            Diagnose(source, TypeCheckerOptions.Strict),
            d => d.TsCode == "TS2322");

        var exact = Diagnose(
            source,
            TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = true });
        Assert.Equal(2, exact.Count(d => d.TsCode == "TS2322"));
    }

    [Fact]
    public void OptionalPropertyRead_IncludesUndefined()
    {
        const string source = """
            interface Config { value?: string; }
            const config: Config = {};
            const value: string = config.value;
            """;

        Assert.Contains(
            Diagnose(source, TypeCheckerOptions.Strict),
            d => d.TsCode == "TS2322");
    }

    [Fact]
    public void NoUncheckedIndexedAccess_AddsUndefinedToArrayAndIndexSignatureReads()
    {
        const string source = """
            const values: number[] = [1];
            const fromArray: number = values[0];
            const table: { [key: string]: number } = { answer: 42 };
            const fromTable: number = table["missing"];
            """;

        Assert.DoesNotContain(
            Diagnose(source, TypeCheckerOptions.Strict),
            d => d.TsCode == "TS2322");

        var uncheckedReads = Diagnose(
            source,
            TypeCheckerOptions.Strict with { NoUncheckedIndexedAccess = true });
        Assert.Equal(2, uncheckedReads.Count(d => d.TsCode == "TS2322"));
    }

    [Fact]
    public void StrictPropertyInitialization_RequiresAllConstructorPaths()
    {
        const string source = """
            class Ready {
                value: string;
                constructor(flag: boolean) {
                    if (flag) this.value = "yes";
                    else this.value = "no";
                }
            }
            class Missing {
                value: string;
                constructor(flag: boolean) {
                    if (flag) this.value = "yes";
                }
            }
            """;

        var diagnostics = Diagnose(source, TypeCheckerOptions.Strict);
        var error = Assert.Single(diagnostics, d => d.TsCode == "TS2564");
        Assert.Equal(9, error.Line);
        Assert.Contains("'value'", error.Message);
    }

    [Fact]
    public void StrictPropertyInitialization_HonorsInitializersAssertionsAndParameterProperties()
    {
        const string source = """
            class C {
                initialized: string = "yes";
                asserted!: string;
                optional?: string;
                maybe: string | undefined;
                constructor(public parameterProperty: string) {}
            }
            """;

        Assert.DoesNotContain(
            Diagnose(source, TypeCheckerOptions.Strict),
            d => d.TsCode == "TS2564");
    }

    [Fact]
    public void StrictPropertyInitialization_OnlyReportsIdentifierNamedFields()
    {
        const string source = """
            class C {
                identifier: string;
                1: string;
                'literal': string;
            }
            """;

        var diagnostics = Diagnose(source, TypeCheckerOptions.Strict);
        var error = Assert.Single(diagnostics, d => d.TsCode == "TS2564");
        Assert.Contains("'identifier'", error.Message);
    }

    [Fact]
    public void InvalidAssignment_ReportsUseBeforeAssignmentAndStillAdvancesFlow()
    {
        const string source = """
            let source: { foo: string };
            let target: { foo: number };
            target = source;
            source = target;
            """;

        var diagnostics = Diagnose(source, TypeCheckerOptions.Strict);
        Assert.Equal(2, diagnostics.Count(d => d.TsCode == "TS2322"));
        Assert.Single(diagnostics, d => d.TsCode == "TS2454");
    }
}
