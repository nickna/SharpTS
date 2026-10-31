using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ForwardOverloadScriptTests
{
    public static IEnumerable<object[]> Valid()
    {
        yield return ["original", "const result:number=pick(1);function pick(x:string):string;function pick(x:number):number;function pick(x:any):any{return x;}console.log(result);"];
        yield return ["retained", "const held=pick;const result:number=held(1);function pick(x:string):string;function pick(x:number):number;function pick(x:any):any{return x;}"];
        yield return ["ambient", "const result:number=pick(1);declare function pick(x:string):string;declare function pick(x:number):number;"];
        yield return ["generic-owners", "const a:number=pick(1),b:string=pick('x');function pick<T extends number>(x:T):T;function pick<U extends string>(x:U):U;function pick(x:any):any{return x;}"];
        yield return ["generic-constraints", "const result:{value:number}=pick({value:1});function pick<T extends string>(x:T):T;function pick<T extends {value:number}>(x:T):T;function pick(x:any):any{return x;}"];
        yield return ["nested-shadow", "function pick(x:string):string;function pick(x:number):number;function pick(x:any):any{return x;}function run(){const value:boolean=pick(true);function pick(x:boolean):boolean;function pick(x:boolean):boolean{return x;}return value;}const result:boolean=run();"];
        yield return ["exports", "export const result:number=pick(1);export function pick(x:string):string;export function pick(x:number):number;export function pick(x:any):any{return x;}"];
        yield return ["namespace", "namespace N{export const result:number=pick(1);export function pick(x:string):string;export function pick(x:number):number;export function pick(x:any):any{return x;}}const result:number=N.result;"];
    }

    public static IEnumerable<object[]> Invalid()
    {
        yield return ["implementation-hidden", "pick(true);function pick(x:string):string;function pick(x:number):number;function pick(x:any):any{return x;}"];
        yield return ["required-arity", "pick();function pick(x:string):string;function pick(x:number,y:number):number;function pick(x:any,y?:any):any{return x;}"];
        yield return ["generic-constraint", "pick(false);function pick<T extends string>(x:T):T;function pick<U extends number>(x:U):U;function pick(x:any):any{return x;}"];
        yield return ["lexical-shadow", "function pick(x:number):number{return x;}function run(){pick(1);function pick(x:boolean):boolean;function pick(x:boolean):boolean{return x;}}"];
    }

    public static IEnumerable<object[]> Modes() => Valid().SelectMany(row => new[] { "script", "module", "editor" }
        .Select(mode => new object[] { row[0], row[1], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void EarlierCallsUseTheCompleteDeclaredGroup(string name, string source, string mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(Check(source, mode));
    }

    [Theory, MemberData(nameof(Invalid))]
    public void ImplementationAndOuterBindingsCannotReplaceThePublicGroup(string name, string source)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(Check(source, "script"));
    }

    private static IReadOnlyList<Diagnostic> Check(string source, string mode)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (mode == "module")
        {
            string path = Path.Combine(Path.GetTempPath(), "forward-overload-scripts", "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string,string> { [path] = source });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            return checker.GetDiagnostics();
        }
        if (mode == "editor")
        {
            var document = new SourceDocument("forward-overloads.ts", source);
            var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
            return checker.WithEditorMetadata().CheckWithRecovery(statements, document).Diagnostics;
        }
        return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
    }
}
