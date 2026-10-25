using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ExplicitReadonlyInterfaceIndexTests
{
    [Theory]
    [InlineData("readonly ", true)]
    [InlineData("", false)]
    public void InterfaceIndexSyntaxAndCheckedTypeRetainTheWrittenModifier(string modifier, bool isReadonly)
    {
        string source = $"interface Values<T> {{ {modifier}[index: number]: T; }} declare let values: Values<number>; values;";
        var statements = TestHarness.ParseOrThrow(source);
        var declaration = Assert.IsType<Stmt.Interface>(statements[0]);
        var signature = Assert.Single(declaration.IndexSignatures!);
        Assert.Equal(isReadonly, signature.IsReadonly);
        Assert.Equal(TokenType.TYPE_NUMBER, signature.KeyType);
        Assert.Equal("index", signature.KeyName.Lexeme);
        Assert.Equal("T", Assert.IsType<NamedTypeNode>(signature.ValueTypeNode).Name);
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var read = Assert.IsType<Stmt.Expression>(statements[2]);
        var type = Assert.IsType<TypeInfo.InstantiatedGeneric>(result.TypeMap.Get(read.Expr));
        var definition = Assert.IsType<TypeInfo.GenericInterface>(type.GenericDefinition);
        Assert.Equal(isReadonly, definition.ReadonlyNumberIndex);
        Assert.Equal("T", Assert.IsType<TypeInfo.TypeParameter>(definition.NumberIndexType).Name);
        Assert.Equal(TypeInfo.Primitive.Number, Assert.Single(type.TypeArguments));
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("""
                interface Values { readonly [index: number]: number; }
                declare let values: Values;
                const value: number = values[0];
                values[0] = 1;
                values['1'] = 1;
                declare let key: 2 | '3';
                values[key] = 1;
                values[4]++;
                values[5] += 1;
                values[6] ||= 1;
                """, ["TS2542", "TS2542", "TS2542", "TS2542", "TS2542", "TS2542"]),
            ("""
                interface Base<T> { readonly [index: number]: T; named?: T; }
                interface Middle<T> extends Base<T> {}
                interface Values extends Middle<number> { 0: number; }
                declare let values: Values;
                const value: number = values[1];
                const optional: number | undefined = values.named;
                values[0] = 1;
                values['0'] = 1;
                values[1] = 1;
                const wrong: string = values[2];
                """, ["TS2542", "TS2322"]),
            ("""
                interface Base<T> { readonly [index: number]: T; }
                interface Values<T> extends Base<T> { [index: number]: T; }
                declare let values: Values<number>;
                values[0] = 1;
                values[1]++;
                values[2] = 'wrong';
                """, ["TS2322"]),
            ("""
                interface Base { [index: number]: number; }
                interface Values extends Base { readonly [index: number]: number; }
                declare let values: Values;
                values[0] = 1;
                """, ["TS2542"]),
            ("""
                interface Values { [key: string]: number; readonly [index: number]: number; }
                declare let values: Values;
                values.named = 1;
                values['named'] = 1;
                values[0] = 1;
                """, ["TS2542"]),
            ("""
                interface Values { [index: number]: number; }
                declare let values: Values;
                const value: number = values[0];
                values[0] = 1;
                values[1]++;
                values[2] = 'wrong';
                """, ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ExplicitReadonlyNumericIndexesKeepReadsTypesAndNearestOverrides(string source, string[] codes, bool modules) =>
        Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "explicit-readonly-index-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
