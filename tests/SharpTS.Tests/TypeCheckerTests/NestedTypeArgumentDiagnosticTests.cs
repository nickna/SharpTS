using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NestedTypeArgumentDiagnosticTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["unknown-original", "interface Box<T>{value:T}const x:Box<MissingType>={value:7};", false, false, "MissingType"];
        yield return ["unknown-let", "interface Box<T>{value:T}let x:Box<MissingType>={value:7};", false, false, "MissingType"];
        yield return ["unknown-var", "interface Box<T>{value:T}var x:Box<MissingType>={value:7};", false, false, "MissingType"];
        yield return ["unknown-nested", "interface Box<T>{value:T}const x:Box<Box<MissingType>>=null as any;", false, false, "MissingType"];
        yield return ["unknown-array-argument", "interface Box<T>{value:T}const x:Box<MissingType[]>=null as any;", false, false, "MissingType"];
        yield return ["unknown-union-argument", "interface Box<T>{value:T}const x:Box<number|MissingType>=null as any;", false, false, "MissingType"];
        yield return ["unknown-tuple-argument", "interface Box<T>{value:T}const x:Box<[number,MissingType]>=null as any;", false, false, "MissingType"];
        yield return ["unknown-object-argument", "interface Box<T>{value:T}const x:Box<{x:MissingType}>=null as any;", false, false, "MissingType"];
        yield return ["unknown-signature-argument", "interface Box<T>{value:T}const x:Box< <T>(value:MissingType)=>T>=null as any;", false, false, "MissingType"];
        yield return ["unknown-generic-head", "const x:MissingBox<number>=null as any;", false, false, "MissingBox"];
        yield return ["unknown-namespace", "namespace N{interface Box<T>{value:T}export const x:Box<MissingType>={value:7};}", false, false, "MissingType"];
        yield return ["unknown-module", "interface Box<T>{value:T}export const x:Box<MissingType>={value:7};", false, false, "MissingType"];
        yield return ["profile-shared-argument", "interface Box<T>{value:T}declare const input:any;const x:Box<SharedArrayBuffer>=input;", false, true, "SharedArrayBuffer"];
        yield return ["profile-shared-typedarray", "declare const input:any;const x:Uint8Array<SharedArrayBuffer>=input;", false, true, "SharedArrayBuffer"];
        yield return ["primitive-argument", "interface Box<T>{value:T}const x:Box<number>={value:7};", true, true, ""];
        yield return ["explicit-any", "interface Box<T>{value:T}const x:Box<any>={value:7};", true, true, ""];
        yield return ["explicit-unknown", "interface Box<T>{value:T}const x:Box<unknown>={value:7};", true, true, ""];
        yield return ["forward-interface", "interface Box<T>{value:T}const x:Box<Later>={value:{n:7}};interface Later{n:number}", true, true, ""];
        yield return ["forward-alias", "interface Box<T>{value:T}const x:Box<Later>={value:7};type Later=number;", true, true, ""];
        yield return ["namespace-forward", "namespace N{interface Box<T>{value:T}export const x:Box<Later>={value:{n:7}};interface Later{n:number}}", true, true, ""];
        yield return ["function-forward", "interface Box<T>{value:T}function run(){const x:Box<Later>={value:{n:7}};interface Later{n:number}}", true, true, ""];
        yield return ["block-forward", "interface Box<T>{value:T}{const x:Box<Later>={value:{n:7}};interface Later{n:number}}", true, true, ""];
        yield return ["generic-signature-binder", "interface Box<T>{value:T}const x:Box< <T>(value:T)=>T>=null as any;", true, true, ""];
        yield return ["generic-constructor-binder", "interface Box<T>{value:T}const x:Box<new<T>(value:T)=>Box<T>>=null as any;", true, true, ""];
        yield return ["mapped-binder", "interface Box<T>{value:T}const x:Box<{[K in \"x\"]:Box<K>}>=null as any;", true, true, ""];
        yield return ["qualified-type", "namespace N{export interface Item{n:number}}interface Box<T>{value:T}const x:Box<N.Item>={value:{n:7}};", true, true, ""];
        yield return ["conditional-infer", "interface Box<T>{value:T}const x:Box<string extends infer U?U:never>=null as any;", true, true, ""];
        yield return ["library-array", "const x:Array<number>=[1,2];", true, true, ""];
        yield return ["hoisted-block-alias", "interface Box<T>{value:T}{type Local=any;var x:Box<Local>={value:7};}", true, true, ""];
        yield return ["hoisted-if-alias", "interface Box<T>{value:T}if(true){type Local=any;var x:Box<Local>={value:7};}", true, true, ""];
        yield return ["hoisted-loop-alias", "interface Box<T>{value:T}for(let i=0;i<1;i++){type Local=any;var x:Box<Local>={value:7};}", true, true, ""];
        yield return ["hoisted-function-alias", "interface Box<T>{value:T}function f(){type Local=any;{var x:Box<Local>={value:7};}}", true, true, ""];
        yield return ["top-var-no-initializer-still-rejects", "interface Box<T>{value:T}var x:Box<MissingType>;", false, false, "MissingType"];
    }

    [Theory]
    [InlineData("const x:Record<string,number>={};")]
    [InlineData("const x:Partial<{n:number}>={};")]
    [InlineData("const x:ReturnType<()=>any>=7;")]
    [InlineData("interface Box<T>{value:T}const x:Box<Date>=null as any;")]
    [InlineData("const x:Array<number>=[1];")]
    public void ExistingNoLibIntrinsicResolutionIsPreserved(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess);
        var result = new TypeChecker().CheckWithRecovery(parsed.Statements);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void NoLibIntrinsicHeadDoesNotHideUnknownArgument()
    {
        var parsed = new Parser(new Lexer("const x:Record<string,MissingType>={};").ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess);
        var result = new TypeChecker().CheckWithRecovery(parsed.Statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2304");
    }

    [Theory, MemberData(nameof(Cases))]
    public void WrittenTypeArgumentsKeepUnknownNameDiagnostics(string name, string source,
        bool defaultAccepted, bool modernAccepted, string unknown)
    {
        Assert.NotEmpty(name);
        foreach (bool modern in new[] { false, true })
        {
            string path = Path.GetFullPath(Path.Combine("nested-type-argument-diagnostics", "main.ts"));
            var options = modern ? TypeScriptProgramOptions.Default with
                { Lib = ["lib.es2022.d.ts", "lib.dom.d.ts"] } : TypeScriptProgramOptions.Default;
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, options);
            var entry = resolver.LoadProgram(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            var diagnostics = checker.GetDiagnostics();
            bool accepted = modern ? modernAccepted : defaultAccepted;
            if (accepted) Assert.Empty(diagnostics);
            else Assert.Contains(diagnostics, diagnostic =>
                diagnostic.TsCode == "TS2304" && diagnostic.Message.Contains(unknown, StringComparison.Ordinal));
        }
    }
}
