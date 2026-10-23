using SharpTS.Diagnostics;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PrimitiveStringCanonicalIndexTests
{
    private const string Minimal = """
        const input: string = 'abc';
        console.log(input['0'], input['3'] === undefined);
        """ + "\n";

    private const string PreciseReads = """
        const input: string = "abc";
        const literal = "abc" as const;
        const direct: string = input["0"];
        const fromLiteral: string = literal["1"];
        function readKey(value: string, key: "0" | "1"): string { return value[key]; }
        function readLiteralUnion(value: "abc" | "xy"): string { return value["1"]; }
        function readConstrained<T extends string>(value: T): string { return value["0"]; }
        function readMixed(value: string | number[]): string | number { return value["0"]; }
        console.log(direct, fromLiteral, readKey(input, "1"), readLiteralUnion("xy"),
            readConstrained(input), readMixed([4, 5]));
        """ + "\n";

    private const string AssignmentErrors = """
        const input: string = "abc";
        const literal = "abc" as const;
        const wrongDirect: number = input["0"];
        const wrongLiteral: boolean = literal["1"];
        function wrongKey(value: string, key: "0" | "1") {
            const wrong: boolean = value[key];
        }
        function wrongConstrained<T extends string>(value: T) {
            const wrong: number = value["0"];
        }
        function wrongUnion(value: "abc" | "xy") {
            const wrong: boolean = value["1"];
        }
        """ + "\n";

    private const string InvalidIndicesAndWrites = """
        const input: string = "abc";
        input["missing"];
        input["01"];
        input["+1"];
        input["1.0"];
        input["-0"];
        function readWidened(value: string, key: string) { return value[key]; }
        input["0"] = "x";
        input[0] = "x";
        """ + "\n";

    private const string UncheckedRead = """
        const input: string = "abc";
        const maybe: string | undefined = input["0"];
        const required: string = input["1"];
        """ + "\n";

    private static TypeCheckerOptions Options(string profile) => profile switch
    {
        "Default" => TypeCheckerOptions.Default,
        "Strict" => TypeCheckerOptions.Strict,
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    private static IReadOnlyList<Diagnostic> Diagnose(string source, TypeCheckerOptions options) =>
        new TypeChecker(options with { MaxErrors = 50 })
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;

    [Theory]
    [InlineData("Default")]
    [InlineData("Strict")]
    public void MinimalCanonicalQuotedReadsAreAccepted(string profile)
    {
        Assert.Empty(Diagnose(Minimal, Options(profile)));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Strict")]
    public void LiteralUnionsAndConstrainedReadsRetainString(string profile)
    {
        Assert.Empty(Diagnose(PreciseReads, Options(profile)));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Strict")]
    public void AcceptedQuotedReadsDoNotBecomeAny(string profile)
    {
        var diagnostics = Diagnose(AssignmentErrors, Options(profile));
        Assert.Equal(5, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("TS2322", diagnostic.TsCode));
        Assert.Equal(new[] { 3, 4, 6, 9, 12 }, diagnostics.Select(item => item.Line).OrderBy(line => line));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Strict")]
    public void InvalidNamesAndStringWritesRemainRejected(string profile)
    {
        var diagnostics = Diagnose(InvalidIndicesAndWrites, Options(profile));
        Assert.Equal(8, diagnostics.Count);
        // Retain rejection without claiming unrelated index/write numeric-code parity.
        Assert.Equal(Enumerable.Range(2, 8), diagnostics.Select(item => item.Line).OrderBy(line => line));
    }

    [Fact]
    public void UncheckedReadsAddUndefinedOnlyWithStrictNullChecks()
    {
        Assert.Empty(Diagnose(UncheckedRead, TypeCheckerOptions.Default));
        Assert.Empty(Diagnose(UncheckedRead, TypeCheckerOptions.Strict));
        var diagnostic = Assert.Single(Diagnose(UncheckedRead,
            TypeCheckerOptions.Strict with { NoUncheckedIndexedAccess = true }));
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
        Assert.Empty(Diagnose(UncheckedRead, TypeCheckerOptions.Strict with
        {
            NoUncheckedIndexedAccess = true,
            StrictNullChecks = false
        }));
    }
}
