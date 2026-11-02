using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TypedArrayAnnotationCompatibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["fresh-buffer-constraint", "const a:Uint8Array<ArrayBuffer>=new Uint8Array(2);", true];
        yield return ["shared-buffer-constraint", "const a:Uint8Array<SharedArrayBuffer>=new Uint8Array(new SharedArrayBuffer(2));", true];
        yield return ["generic-buffer-retained", "function view<T extends ArrayBuffer>(b:T):Uint8Array<T>{return new Uint8Array(b)}", true];
        yield return ["constrained-declared-numeric-read", "function read<T extends Uint8Array>(a:T):number{return a[0]}", true];
        yield return ["buffer-copy-is-fresh", "const source:Uint8Array<SharedArrayBuffer>=new Uint8Array(new SharedArrayBuffer(1));const a:Uint8Array<ArrayBuffer>=new Uint8Array(source);", true];
        yield return ["union-buffer-facet", "function view(flag:boolean){const b=flag?new ArrayBuffer(2):new SharedArrayBuffer(2);const a:Uint8Array<ArrayBufferLike>=new Uint8Array(b);}", true];
        yield return ["readonly-length", "const a:Uint8Array=new Uint8Array(1);a.length=2;", false];
        yield return ["readonly-buffer", "const a:Uint8Array=new Uint8Array(1);a.buffer=new ArrayBuffer(1);", false];
        yield return ["wrong-number-element", "const a:Uint8Array=new Uint8Array(1);a[0]=1n;", false];
        yield return ["wrong-bigint-element", "const a:BigInt64Array=new BigInt64Array(1);a[0]=1;", false];
        yield return ["number-to-bigint-family", "const a:BigInt64Array=new Int8Array(1);", false];
        yield return ["bigint-to-number-family", "const a:Int8Array=new BigInt64Array(1);", false];
        yield return ["wrong-buffer-constraint", "const a:Uint8Array<number>=new Uint8Array(1);", false];
        yield return ["shared-is-not-arraybuffer", "const a:Uint8Array<ArrayBuffer>=new Uint8Array(new SharedArrayBuffer(1));", false];
        yield return ["arraybuffer-is-not-shared", "const a:Uint8Array<SharedArrayBuffer>=new Uint8Array(new ArrayBuffer(1));", false];
        yield return ["unknown-buffer-stays-constrained", "function view<T extends ArrayBufferLike>(b:T):Uint8Array<ArrayBuffer>{return new Uint8Array(b)}", false];
        yield return ["unknown-member", "const a:Uint8Array=new Uint8Array(1);a.missingMember;", false];
        yield return ["wrong-method-argument", "const a:Uint8Array=new Uint8Array(1);a.subarray(\"wrong\");", false];
        yield return ["local-same-name-not-intrinsic", "const good:Uint8Array=new Uint8Array(1);function check(){interface Uint8Array{mustBePresent:string}const bad:Uint8Array=new globalThis.Uint8Array(1)}", false];
        yield return ["namespace-same-name-not-intrinsic", "namespace Local{export interface Uint8Array{mustBePresent:string}}const a:Local.Uint8Array=new Uint8Array(1);", false];
        yield return ["buffer-cache-distinction", "const first:Uint8Array<ArrayBuffer>=new Uint8Array(1);const second:Uint8Array<ArrayBuffer>=new Uint8Array(new SharedArrayBuffer(1));", false];
        yield return ["nested-buffer-cache-distinction", "function consume(v:{x:Uint8Array<ArrayBuffer>}){}consume({x:new Uint8Array(1)});consume({x:new Uint8Array(new SharedArrayBuffer(1))});", false];
        yield return ["generic-bufferlike-argument-retained", "function view<T extends ArrayBufferLike>(b:T):Uint8Array<T>{return new Uint8Array(b)}", true];
        yield return ["generic-arraylike-copy-is-fresh", "function copy<T extends ArrayLike<number>>(a:T):Uint8Array<ArrayBuffer>{return new Uint8Array(a)}", true];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DeclaredAnnotationsRetainConstraints(string name, string source, bool accepted)
    {
        Assert.NotEmpty(name);
        foreach (bool respectLibraries in new[] { false, true })
        {
            var diagnostics = Check(source, respectLibraries);
            if (accepted) Assert.Empty(diagnostics);
            else Assert.NotEmpty(diagnostics);
        }
    }

    private static string[] Check(string source, bool respectLibraries)
    {
        string path = Path.GetFullPath(Path.Combine("typed-array-annotation-compatibility", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Default with { Lib = ["lib.es2022.d.ts", "lib.dom.d.ts"] });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { RespectLoadedLibraries = respectLibraries });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode ?? "").ToArray();
    }
}
