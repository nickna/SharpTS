using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EnumNumericStringReverseMappingTests
{
    [Theory, ModeData]
    public void NumericStringKeysUseTheEnumReverseMapping(ExecutionMode mode)
    {
        const string source = """
            enum E { Two = 2, Zero = 0, Half = 2.5, Below = -1 }
            const numericName: string = E["2"];
            function read(key: "2" | "Two") { return E[key]; }
            console.log(numericName, E[2], E["0"], E["2.5"], E["-1"], read("2"), read("Two"));
            type EnumObject = typeof E;
            const alias: EnumObject = E;
            console.log(alias["2"]);
            const value: any = E;
            console.log(value["2"], value[2], value["02"], value["-0"], value["2.5"], value["-1"], value["0"]);
            enum Mixed { Label = "label", Two = 2 }
            const mixedName: string = Mixed["2"];
            console.log(mixedName, Mixed["Label"]);
            """;
        Assert.Equal("Two Two Zero Half Below Two 2\nTwo\nTwo Two undefined undefined Half Below Zero\nTwo label\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ImportedEnumObjectTypeKeepsItsReverseMapping(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["enum.ts"] = """
                export enum E { Two = 2 }
                export type EnumObject = typeof E;
                """,
            ["main.ts"] = """
                import { E } from "./enum";
                import type { EnumObject } from "./enum";
                const alias: EnumObject = E;
                console.log(alias["2"]);
                """
        };
        Assert.Equal("Two\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("\"5\"")]
    [InlineData("1")]
    [InlineData("5")]
    public void NoUncheckedIndexedAccessIncludesUndefinedInReverseReads(string key)
    {
        const string declaration = "enum E { A = 1 }";
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { NoUncheckedIndexedAccess = true });
        checker.Check(TestHarness.ParseOrThrow($"""
            {declaration}
            const named: E = E["A"];
            const reverse: string | undefined = E[{key}];
            """));
        var error = Assert.ThrowsAny<TypeCheckException>(() => checker.Check(
            TestHarness.ParseOrThrow($"""{declaration} const wrong: string = E[{key}];""")));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("enum E { Two = 2 }", "02")]
    [InlineData("enum E { Zero = 0 }", "-0")]
    [InlineData("enum E { Two = '2' }", "2")]
    public void OtherStringKeysDoNotAcquireANumericIndexSignature(string declaration, string key)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} E[\"{key}\"];")));
        Assert.Equal("TS7053", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("const value = E.Two")]
    [InlineData("const value: E = E.Two")]
    [InlineData("declare const pickObject: boolean; const value = pickObject ? E : E.Two")]
    public void EnumMemberValuesDoNotHaveNumericStringReverseMappings(string declaration)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"enum E {{ Two = 2 }} {declaration}; value[\"2\"];")));
        Assert.Equal("TS7053", error.Diagnostic.TsCode);
    }
}
