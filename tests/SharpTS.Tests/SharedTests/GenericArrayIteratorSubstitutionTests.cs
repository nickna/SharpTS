using SharpTS.Parsing;
using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericArrayIteratorSubstitutionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["generic-declaration", """
            class Values<T> extends Array<T> {}
            const numbers = new Values<number>(1, 2), strings = new Values<string>("a", "b");
            let sum = 0, text = "";
            for (const item of numbers) { const value: number = item; sum += value; }
            for (const item of strings) { const value: string = item; text += value; }
            console.log(sum, text, numbers.length, strings.length);
            """.ReplaceLineEndings("\r\n") + "\r\n", "3 ab 2 2\n"];
        yield return ["concrete-declaration-control", """
            class Numbers extends Array<number> {}
            class Strings extends Array<string> {}
            const numbers = new Numbers(1, 2), strings = new Strings("a", "b");
            let sum = 0, text = "";
            for (const item of numbers) { const value: number = item; sum += value; }
            for (const item of strings) { const value: string = item; text += value; }
            console.log(sum, text, numbers.length, strings.length);
            """.ReplaceLineEndings("\r\n").Replace("class Numbers extends Array<number> {}\r\n", "class Numbers extends Array<number> {}\n") + "\r\n", "3 ab 2 2\n"];
        yield return ["composed-array-ancestry-substitutes-the-element", """
            class Base<Unused, Element> extends Array<Element> {}
            class Swapped<Left, Right> extends Base<Right, Left> {}
            class Composed<Noise, Item> extends Swapped<Item, Noise> {}
            const numbers = new Composed<string, number>(2, 3), strings = new Composed<boolean, string>("x", "y");
            let sum = 0, text = "";
            for (const item of numbers) { const value: number = item; sum += value; }
            for (const item of strings) { const value: string = item; text += value; }
            console.log(sum, text, numbers.length, strings.length);
            """.ReplaceLineEndings("\n") + "\n", "5 xy 2 2\n"];
        yield return ["generic-native-array-expression-retains-iterator-element-types", """
            const Values = class<T> extends Array<T> {};
            const numbers = new Values<number>(1, 2), strings = new Values<string>("a", "b");
            let sum = 0, text = "";
            for (const item of numbers) { const value: number = item; sum += value; }
            for (const item of strings) { const value: string = item; text += value; }
            console.log(sum, text, numbers.length, strings.length);
            """.ReplaceLineEndings("\r\n") + "\r\n", "3 ab 2 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InstantiatedArrayAncestorsRetainTheirElementType(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["composed-array-elements-retain-precision-rejected", """
            class Base<Unused, Element> extends Array<Element> {}
            class Swapped<Left, Right> extends Base<Right, Left> {}
            class Composed<Noise, Item> extends Swapped<Item, Noise> {}
            const numbers = new Composed<string, number>(1, 2), strings = new Composed<boolean, string>("a", "b");
            for (const item of numbers) { const wrong: string = item; }
            for (const item of strings) { const wrong: number = item; }
            """.ReplaceLineEndings("\n") + "\n", new string[] { "TS2322", "TS2322" }, new int[] { 5, 6 }];
        yield return ["generic-native-array-iterator-result-is-not-string", """
            const Values = class<T> extends Array<T> {};
            const values = new Values<number>(1, 2);
            for (const value of values) { const wrong: string = value; }
            """.ReplaceLineEndings("\r\n") + "\r\n", new string[] { "TS2322" }, new int[] { 3 }];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void SubstitutedElementsRejectIncompatibleAssignments(
        string name, string source, string[] expectedCodes, int[] expectedLines)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(statements, document).Diagnostics;
        var expected = expectedCodes.Zip(expectedLines, (code, line) => (Code: code, Line: line))
            .OrderBy(item => item.Line).ThenBy(item => item.Code).ToArray();
        var actual = diagnostics.Select(diagnostic => (Code: Assert.IsType<string>(diagnostic.TsCode), Line: diagnostic.Line))
            .OrderBy(item => item.Line).ThenBy(item => item.Code).ToArray();
        Assert.Equal(expected, actual);
    }
}
