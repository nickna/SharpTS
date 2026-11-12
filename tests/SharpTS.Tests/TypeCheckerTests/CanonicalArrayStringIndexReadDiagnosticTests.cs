using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CanonicalArrayStringIndexReadDiagnosticTests
{
    [Theory]
    [InlineData("new Child<number>()")]
    [InlineData("null! as Child<number> | Child<boolean>")]
    public void GenericArraySubclassRetainsItsElementType(string initializer)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            class Child<T> extends Array<T> {}
            const value = {{initializer}};
            const correct: number | boolean = value['0'];
            const wrong: string = value['0'];
            """);

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(4, diagnostic.Line);
    }

    [Theory]
    [InlineData("Child<number>", "number")]
    [InlineData("Child<number> | Child<boolean>", "number | boolean")]
    [InlineData("Explicit", "0 | 1")]
    public void ArraySubclassRetainsItsElementTypeForAUnionOfCanonicalKeys(string receiver, string expectedType)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            class Child<T> extends Array<T> {}
            class Explicit extends Array<number> {
                0 = 0 as const;
                1 = 1 as const;
            }
            function read(child: {{receiver}}, key: '0' | '1') {
                const correct: {{expectedType}} = child[key];
                const wrong: string = child[key];
            }
            """);

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(8, diagnostic.Line);
    }

    [Theory]
    [InlineData("Child<string>", "'0' | 'length'", "string | number", "boolean")]
    [InlineData("Child<string>", "number | 'length'", "string | number", "boolean")]
    [InlineData("Child<string>", "'0' | 'label'", "string | 42", "string")]
    [InlineData("Child<string> | Child<number>", "'0' | 'length'", "string | number", "boolean")]
    [InlineData("Explicit", "'0' | 'length'", "'first' | number", "boolean")]
    public void ArraySubclassCombinesElementAndNamedPropertyTypes(
        string receiver, string key, string expectedType, string rejectedType)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            class Child<T> extends Array<T> { label = 42 as const; }
            class Explicit extends Array<string> { 0 = 'first' as const; }
            function read(child: {{receiver}}, key: {{key}}) {
                const correct: {{expectedType}} = child[key];
                const wrong: {{rejectedType}} = child[key];
            }
            """);

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(5, diagnostic.Line);
    }

    [Fact]
    public void MixedArraySubclassKeysRetainUncheckedElementUndefined()
    {
        var statements = TestHarness.ParseOrThrow("""
            class Child extends Array<string> {}
            function read(child: Child, key: '0' | 'length') {
                const correct: string | number | undefined = child[key];
                const wrong: string | number = child[key];
            }
            """);

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict with
        {
            NoUncheckedIndexedAccess = true
        }).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(4, diagnostic.Line);
    }

    [Theory]
    [InlineData("Child<string>", "'0' | 'missing'")]
    [InlineData("Child<string>", "number | 'missing'")]
    [InlineData("Child<string>", "'length' | 'missing'")]
    [InlineData("Child<string> | Child<number>", "'0' | 'missing'")]
    public void ArraySubclassRejectsAKeyUnionContainingAnUnknownName(string receiver, string key)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            class Child<T> extends Array<T> {}
            function read(child: {{receiver}}, key: {{key}}) {
                const wrong: boolean = child[key];
            }
            """);

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS7015", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
    }

    internal const string Source = """
        const numbers: number[] = [4, 8];
        const wrongNumber: string = numbers['0'];
        const words: readonly string[] = ['left', 'right'];
        const wrongReadonly: number = words['0'];
        function wrongUnion(values: number[] | string[]) {
            const result: boolean = values['0'];
        }
        function wrongKey(values: number[], key: '0' | '1') {
            const result: boolean = values[key];
        }
        console.log(numbers['missing']);
        """ + "\n";

    [Theory]
    [InlineData("Recovery")]
    [InlineData("Modules")]
    public void ElementReadsRetainPreciseTypesAndRejectArbitraryNames(string collection)
    {
        IReadOnlyList<Diagnostic> diagnostics;
        if (collection == "Recovery")
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(Source)).Diagnostics;
        }
        else
        {
            Assert.Equal("Modules", collection);
            var path = Path.Combine(Path.GetTempPath(), "canonical-array-string-read-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = Source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        Assert.Equal(5, diagnostics.Count);
        // The invalid named array key must retain the array-specific diagnostic.
        Assert.Equal("TS7015", Assert.Single(diagnostics, diagnostic => diagnostic.Line == 11).TsCode);
        Assert.Equal("TS2322:2,TS2322:4,TS2322:6,TS2322:9".Split(',').OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Where(item => item.Line != 11)
                .Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }
}
