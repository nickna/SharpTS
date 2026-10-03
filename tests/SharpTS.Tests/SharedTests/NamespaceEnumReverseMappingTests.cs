using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceEnumReverseMappingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            namespace Options {export enum Kind {First=2,Second}export const selected=Kind.Second;}const options:any=Options;console.log(Options.selected,options.Kind.First,options.Kind[3]);
            """, "3 2 Second\n" };
        yield return new object[] { "computed", """
            namespace Options {export enum Kind {First=2,Second}}const options:any=Options;const key=3;console.log(options["Kind"][key],options.Kind["2"],options.Kind.Second);
            """, "Second First 3\n" };
        yield return new object[] { "string", """
            namespace Options {export enum Kind {First="a",Second="b"}}const options:any=Options;console.log(options.Kind.First,options.Kind.Second,options.Kind["a"]===undefined,options.Kind[0]===undefined);
            """, "a b true true\n" };
        yield return new object[] { "mixed", """
            namespace Options {export enum Kind {First=2,Text="label",Last=5}}const options:any=Options;console.log(options.Kind[2],options.Kind[5],options.Kind.Text,options.Kind.label===undefined);
            """, "First Last label true\n" };
        yield return new object[] { "duplicate-values", """
            namespace Options {export enum Kind {First=2,Second=2,Third=3}}const options:any=Options;console.log(options.Kind.First,options.Kind.Second,options.Kind[2],options.Kind[3]);
            """, "2 2 Second Third\n" };
        yield return new object[] { "fraction-and-zero", """
            namespace Options {export enum Kind {Fraction=0.5,Zero=0}}const options:any=Options;console.log(options.Kind[0.5],options.Kind[0],options.Kind["0.5"]);
            """, "Fraction Zero Fraction\n" };
        yield return new object[] { "identity", """
            namespace Options {export enum Kind {First=2,Second}export const original=Kind;}const first:any=Options;const second:any=Options;const kind:any=first.Kind;console.log(kind===second.Kind,kind===first.original,kind[3]);
            """, "true true Second\n" };
        yield return new object[] { "nested-and-separate", """
            namespace First {export namespace Inner {export enum Kind {One=1,Two}}}namespace Second {export enum Kind {Other=2}}const first:any=First;const second:any=Second;console.log(first.Inner.Kind[2],second.Kind[2],first.Inner.Kind===second.Kind);
            """, "Two Other false\n" };
        yield return new object[] { "merged-namespace", """
            namespace Options {export enum Kind {First=2,Second}}namespace Options {export const selected=Kind.Second;}const options:any=Options;console.log(options.Kind[options.selected],options.Kind.First,Options.selected);
            """, "Second 2 3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void NamespaceEnumAliasesPreserveNumericReverseMappings(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
