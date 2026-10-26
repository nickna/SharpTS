using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReadonlyInterfaceStringSymbolIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("interface V{readonly[k:string]:number;}declare let v:V;const n:number=v['key'];v['key']=1;v.named=1;v.named++;v.named+=1;v.named||=1;", ["TS2542","TS2542","TS2542","TS2542","TS2542"]),
            ("interface V{readonly[k:symbol]:number;}declare let v:V;declare const key:symbol;const n:number=v[key];v[key]=1;v[key]++;v[key]+=1;v[key]||=1;", ["TS2542","TS2542","TS2542","TS2542"]),
            ("interface Base<T>{readonly[k:string]:T;}interface Middle<T> extends Base<T>{}interface V extends Middle<number>{}declare let v:V;const n:number=v.key;v.key=1;v['other']=1;", ["TS2542","TS2542"]),
            ("interface Base<T>{readonly[k:symbol]:T;}interface Middle<T> extends Base<T>{}interface V extends Middle<number>{}declare let v:V;declare const key:symbol;const n:number=v[key];v[key]=1;", ["TS2542"]),
            ("interface V<T>{readonly[k:string]:T;}declare let v:V<number>;v['key']=1;const wrong:string=v.key;", ["TS2542","TS2322"]),
            ("interface V<T>{readonly[k:symbol]:T;}declare let v:V<number>;declare const key:symbol;v[key]=1;const wrong:string=v[key];", ["TS2542","TS2322"]),
            ("interface V{readonly[k:string]:number;[k:number]:number;}declare let v:V;v[1]=1;v['1']=1;v.named=1;", ["TS2542"]),
            ("interface V{[k:string]:number;readonly[k:number]:number;}declare let v:V;v[1]=1;v['1']=1;v.named=1;", ["TS2542","TS2542"]),
            ("interface V{readonly[k:string]:number;named:number;}declare let v:V;v.named=1;v['named']=1;v.other=1;", ["TS2542"]),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;[key]:number;}declare let v:V;v[key]=1;", []),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;readonly[key]:number;}declare let v:V;v[key]=1;", ["TS2540"]),
            ("declare const key:unique symbol;declare const alias:typeof key;interface V{readonly[k:symbol]:number;[alias]:number;}declare let v:V;v[key]=1;", []),
            ("declare const key:unique symbol;declare const alias:typeof key;interface V{readonly[k:symbol]:number;readonly[alias]:number;}declare let v:V;v[key]=1;", ["TS2540"]),
            ("declare const key:unique symbol;declare const alias:typeof key;interface Base<T>{readonly[k:symbol]:T;[alias]:T;}interface V<T> extends Base<T>{}declare let v:V<number>;v[key]=1;", []),
            ("declare const key:unique symbol;declare const alias:typeof key;interface V{readonly[k:symbol]:number;[alias]:number;}function write(v:V){const key:unique symbol=Symbol();v[key]=1;}", ["TS2542"]),
            ("interface Base{readonly[k:string]:number;}interface Middle extends Base{[k:string]:number;}interface V extends Middle{}declare let v:V;v.key=1;v.key='bad';", ["TS2322"]),
            ("interface Base<T>{readonly[k:symbol]:T;}interface Middle<T> extends Base<T>{[k:symbol]:T;}interface V extends Middle<number>{}declare let v:V;declare const key:symbol;v[key]=1;v[key]='bad';", ["TS2322"]),
            ("interface A{readonly[k:string]:number;}interface B{[k:string]:number;}declare let v:A|B;v.key=1;", ["TS2542"]),
            ("interface A{readonly[k:symbol]:number;}interface B{[k:symbol]:number;}declare let v:A|B;declare const key:symbol;v[key]=1;", ["TS2542"]),
            ("interface V{readonly[k:string]:number;}function set<K extends string>(v:V,k:K){v[k]=1;}", ["TS2542"]),
            ("interface V{readonly[k:string]:number;}function set<K extends number>(v:V,k:K){v[k]=1;}", ["TS2542"]),
            ("interface V{readonly[k:string]:number;[k:number]:number;}function set<K extends number>(v:V,k:K){v[k]=1;}", []),
            ("interface V{readonly[k:string]:number;}function set<K extends string|number>(v:V,k:K){v[k]=1;}", []),
            ("interface V{readonly[k:symbol]:number;}function set<K extends symbol>(v:V,k:K){v[k]=1;}", []),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;[key]:number;}function write<K extends typeof key>(v:V,k:K){v[k]=1;}", []),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;readonly[key]:number;}function write<K extends typeof key>(v:V,k:K){v[k]=1;}", []),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;}function write<K extends typeof key>(v:V,k:K){v[k]=1;}", []),
            ("declare const key:unique symbol;interface V{readonly[k:symbol]:number;[key]:number;}function write<K extends typeof key>(v:V,k:K){v[k]='bad';}", ["TS2322"]),
            ("interface V{readonly[k:string]:number;}declare let v:V;declare const key:any;v[key]=1;", ["TS2542"]),
            ("interface V{readonly[k:symbol]:number;}declare let v:V;declare const key:any;v[key]=1;", ["TS2542"]),
            ("interface V{[k:string]:number;}declare let v:V;v.key=1;v['other']=2;const n:number=v.key;", []),
            ("interface V{[k:symbol]:number;}declare let v:V;declare const key:symbol;v[key]=1;const n:number=v[key];", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PerKindReadonlyMetadataSurvivesInheritanceAndKeepsMutableOverrides(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
        {
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
            return;
        }
        string path = Path.Combine(Path.GetTempPath(), "readonly-interface-index-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedGenericMetadataRetainsBothIndependentReadonlyFacets(bool symbols)
    {
        string source = symbols ? "interface V<T>{readonly[k:symbol]:T;}declare let value:V<number>;value;"
            : "interface V<T>{readonly[k:string]:T;}declare let value:V<number>;value;";
        var statements = TestHarness.ParseOrThrow(source);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var types = checker.Check(statements);
        var declaration = Assert.IsType<SharpTS.Parsing.Stmt.Interface>(statements[0]);
        var index = Assert.Single(declaration.IndexSignatures!);
        Assert.True(index.IsReadonly);
        var read = Assert.IsType<SharpTS.Parsing.Stmt.Expression>(statements[^1]);
        var instance = Assert.IsType<TypeInfo.InstantiatedGeneric>(types.Get(read.Expr));
        var definition = Assert.IsType<TypeInfo.GenericInterface>(instance.GenericDefinition);
        Assert.Equal(symbols, definition.ReadonlySymbolIndex);
        Assert.Equal(!symbols, definition.ReadonlyStringIndex);
        Assert.False(definition.ReadonlyNumberIndex);
        Assert.Equal(TypeInfo.Primitive.Number, Assert.Single(instance.TypeArguments));
    }

    public static IEnumerable<object[]> RuntimeCases()
    {
        yield return ["interface V{readonly[k:string]:number;named:number;}const v:V={named:1};v.named=2;console.log(v.named);"];
        yield return ["interface Base<T>{readonly[k:string]:T;}interface V extends Base<number>{[k:string]:number;}const v:V={key:1};v.key=2;console.log(v.key);"];
        yield return ["interface Base<T>{readonly[k:symbol]:T;}interface V extends Base<number>{[k:symbol]:number;}const key=Symbol('key');const v:V={};v[key]=2;console.log(v[key]);"];
    }

    [Fact]
    public void ComputedAliasMetadataRetainsTheWrittenSourceToken()
    {
        const string source = "declare const key:unique symbol;declare const alias:typeof key;interface V{readonly[k:symbol]:number;[alias]:number;}";
        var tokens = new SharpTS.Parsing.Lexer(source).ScanTokens();
        var statements = new SharpTS.Parsing.Parser(tokens).ParseOrThrow();
        var declaration = Assert.IsType<SharpTS.Parsing.Stmt.Interface>(statements[2]);
        var member = Assert.Single(declaration.Members);
        var reference = member.ComputedSymbolReference;
        Assert.NotNull(reference);
        Assert.Same(tokens.Last(token => token.Lexeme == "alias"), reference);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Same(reference, member.ComputedSymbolReference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedSymbolAliasesKeepTheDeclarationBindingThroughGenericViews(bool readOnly)
    {
        string root = Path.Combine(Path.GetTempPath(), "symbol-index-import-" + Guid.NewGuid().ToString("N"));
        string keys = Path.Combine(root, "keys.ts");
        string view = Path.Combine(root, "view.ts");
        string main = Path.Combine(root, "main.ts");
        var files = new Dictionary<string, string>
        {
            [keys] = "export const key:unique symbol=Symbol();export const alias:typeof key=key;",
            [view] = "import {alias} from './keys';export interface View<T>{readonly[k:symbol]:T;" +
                (readOnly ? "readonly " : "") + "[alias]:T;}",
            [main] = "import {key} from './keys';import type {View} from './view';declare const value:View<number>;value[key]=1;"
        };
        var resolver = new ModuleResolver(main, files, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(main);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        string[] codes = readOnly ? ["TS2540"] : [];
        Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
    }

    public static IEnumerable<object[]> ParityCases() => RuntimeCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void MutableNamedAndRedeclaredIndexesRemainWritableAtRuntime(string source, ExecutionMode mode)
        => Assert.Equal("2\n", TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void MutableControlsHaveVerifiedIl(string source) => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
}
