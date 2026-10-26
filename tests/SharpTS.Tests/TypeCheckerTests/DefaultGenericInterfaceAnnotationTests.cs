using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DefaultGenericInterfaceAnnotationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("interface V<T=number>{value:T;}const v:V={value:1};v.value=2;const n:number=v.value;", []),
            ("interface V<T=number>{value:T;}function write(v:V){v.value=4;return v;}const box={value:1,label:'kept'};const result=write(box);const n:number=result.value;", []),
            ("namespace N{export interface V<T=number>{value:T;}}const v:N.V={value:1};const n:number=v.value;", []),
            ("namespace N{export interface V<T=number>{value:T;}}const v:N.V<string>={value:'s'};const s:string=v.value;", []),
            ("interface V<T=number>{value:T;}type Alias=V;const v:Alias={value:1};const n:number=v.value;", []),
            ("interface V<T=number,U=T>{first:T;second:U;}const v:V={first:1,second:2};const n:number=v.second;", []),
            ("interface V<T=number,U={item:T}>{value:U;}const v:V={value:{item:1}};const n:number=v.value.item;", []),
            ("interface V<T extends number=1>{value:T;}const v:V={value:1};const n:1=v.value;", []),
            ("interface V<T=number>{(value:T):T;}declare const fn:V;const n:number=fn(1);", []),
            ("interface V<T=number>{run:(value:T)=>T;}declare const v:V;const n:number=v.run(1);", []),
            ("interface Base<T=number>{value:T;}interface V extends Base{}const v:V={value:1};const n:number=v.value;", []),
            ("interface V<T=number>{value?:T;}const v:V={};v.value=undefined;const n:number|undefined=v.value;", []),
            ("interface V<T=number>{value:T;}const v:V<string>={value:'s'};const s:string=v.value;", []),
            ("type Result=number;interface V<T=Result>{value:T;}function read(){type Result=string;const v:V={value:1};const n:number=v.value;}", []),
            ("interface V<T=number>{value:T;}function read(){interface V<T=string>{value:T;}const v:V={value:'s'};const n:string=v.value;}const outer:V={value:1};", []),
            ("interface V<T extends number=1>{value:T;}declare const v:V;v.value=2;", ["TS2322"]),
            ("interface V<T=number>{value:T;}declare const v:V<string>;v.value=1;", ["TS2322"]),
            ("interface V<T=number>{value:T;}declare const v:V;const s:string=v.value;", ["TS2322"]),
            ("interface V<T=number>{value:T;}declare const v:V;v.value=true;", ["TS2322"]),
            ("interface V<T=number>{value:T;}declare function read(v:V):void;const bad={value:'s'};read(bad);", ["TS2345"]),
            ("interface V<T>{value:T;}declare const v:V<number>;v.value='s';", ["TS2322"]),
            ("interface V<T=number>{readonly value:T;}declare const v:V;v.value=1;", ["TS2540"]),
            ("declare const key:unique symbol;declare const alias:typeof key;interface V<T=number>{readonly[k:symbol]:T;[alias]:T;}declare let v:V;v[key]=1;", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void BareReferencesInstantiateDefinitionOwnedDefaultsWithoutChangingExplicitArguments(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
        {
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
            return;
        }
        string path = Path.Combine(Path.GetTempPath(), "default-interface-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
    }

    [Fact]
    public void DefaultsKeepTheExactDefinitionAndWrittenAnnotationNodes()
    {
        const string source = "interface V<T=number,U=T>{first:T;second:U;}declare let bare:V;declare let explicit:V<number,number>;bare;explicit;";
        var statements = TestHarness.ParseOrThrow(source);
        var bareDeclaration = Assert.IsType<Stmt.Var>(statements[1]);
        var explicitDeclaration = Assert.IsType<Stmt.Var>(statements[2]);
        var bareNode = Assert.IsType<NamedTypeNode>(bareDeclaration.TypeAnnotationNode);
        var explicitNode = Assert.IsType<NamedTypeNode>(explicitDeclaration.TypeAnnotationNode);
        Assert.Null(bareNode.TypeArguments);
        Assert.Equal(2, explicitNode.TypeArguments!.Count);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var types = checker.Check(statements);
        var bareRead = Assert.IsType<Stmt.Expression>(statements[3]);
        var explicitRead = Assert.IsType<Stmt.Expression>(statements[4]);
        var bare = Assert.IsType<TypeInfo.InstantiatedGeneric>(types.Get(bareRead.Expr));
        var supplied = Assert.IsType<TypeInfo.InstantiatedGeneric>(types.Get(explicitRead.Expr));
        Assert.Same(bare.GenericDefinition, supplied.GenericDefinition);
        Assert.All(bare.TypeArguments, argument => Assert.Equal(TypeInfo.Primitive.Number, argument));
        Assert.Equal(2, bare.TypeArguments.Count);
        Assert.Same(bareNode, bareDeclaration.TypeAnnotationNode);
        Assert.Same(explicitNode, explicitDeclaration.TypeAnnotationNode);
        Assert.Null(bareNode.TypeArguments);
    }

    public static IEnumerable<object[]> RuntimeCases()
    {
        yield return ["interface View<T=number>{value:T;}function write(box:View){box.value=4;return box;}const box={value:1,label:'kept'};const result=write(box);console.log(result===box,result.value,box.value);", "true 4 4\n"];
        yield return ["interface View<T=number,U=T>{first:T;second:U;}const view:View={first:1,second:2};view.second=3;console.log(view.first,view.second);", "1 3\n"];
        yield return ["namespace N{export interface View<T=number>{value:T;}}const view:N.View={value:1};view.value=2;console.log(view.value);", "2\n"];
    }

    public static IEnumerable<object[]> ParityCases() => RuntimeCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], row[1], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void DefaultedInterfacesUseTheOriginalRuntimeObject(string source, string expected, ExecutionMode mode)
        => Assert.Equal(expected, TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void DefaultedInterfaceProgramsHaveVerifiedIl(string source, string expected)
    {
        Assert.NotEmpty(expected);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }
}
