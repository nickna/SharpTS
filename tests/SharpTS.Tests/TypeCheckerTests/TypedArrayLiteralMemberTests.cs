using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TypedArrayLiteralMemberTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-length-original","const a=new Int8Array(2);console.log(a[\"length\"]);",true];
        yield return ["union-length-original","function measureLength(flag:boolean):number{const a=flag?new Int8Array(1):new Uint8Array(2);return a[\"length\"]}console.log(measureLength(true),measureLength(false));",true];
        yield return ["literal-key-binding","const a=new Uint16Array(3);const key=\"length\";const n:number=a[key];",true];
        yield return ["numeric-intrinsic-properties","const a=new Uint16Array(new ArrayBuffer(8),2,2);const n:number=a[\"length\"]+a[\"byteLength\"]+a[\"byteOffset\"]+a[\"BYTES_PER_ELEMENT\"];",true];
        yield return ["loaded-buffer-member","const a=new Uint8Array(1);const buffer:ArrayBufferLike=a[\"buffer\"];",true];
        yield return ["loaded-method-signature","const a=new Int16Array([1,2]);const same=a[\"subarray\"](0,1);const n:number=same.length;",true];
        yield return ["bigint-length","const a=new BigInt64Array(2);const n:number=a[\"length\"];",true];
        yield return ["optional-union-member","function read(flag:boolean){const a=flag?new Int8Array(1):new Uint8Array(2);const n:number=a?.[\"length\"];}",true];
        yield return ["unknown-member","const a=new Int8Array(1);a[\"missingMember\"];",false];
        yield return ["union-unknown-member","function read(flag:boolean){const a=flag?new Int8Array(1):new Uint8Array(2);a[\"missingMember\"]}",false];
        yield return ["dynamic-string-rejected","function read(key:string){const a=new Int8Array(1);return a[key]}",false];
        yield return ["numeric-length-not-any","const a=new Int8Array(1);const text:string=a[\"length\"];",false];
        yield return ["buffer-not-number","const a=new Uint8Array(1);const n:number=a[\"buffer\"];",false];
        yield return ["method-argument-check","const a=new Int16Array(1);a[\"subarray\"](\"wrong\");",false];
        yield return ["readonly-length-write","const a=new Int8Array(1);a[\"length\"]=3;",false];
        yield return ["numeric-string-policy-unchanged","const a=new Int8Array(1);const n:number=a[\"0\"];",false];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DeclaredLiteralMembersPreserveTheirTypes(string name, string source, bool accepted)
    {
        Assert.NotEmpty(name);
        foreach (bool respectLibraries in new[] { false, true })
        {
            var diagnostics = Check(source, respectLibraries, ["lib.es2022.d.ts", "lib.dom.d.ts"]);
            if (accepted) Assert.Empty(diagnostics);
            else Assert.NotEmpty(diagnostics);
        }
    }

    [Theory]
    [InlineData("length")]
    [InlineData("byteLength")]
    [InlineData("byteOffset")]
    [InlineData("BYTES_PER_ELEMENT")]
    public void LibraryFreeNumericIntrinsicMembersKeepCompatibility(string name)
        => Assert.Empty(Check($"const a=new Int8Array(2);const n:number=a['{name}'];", false, []));

    [Theory]
    [InlineData("missingMember")]
    [InlineData("0")]
    public void LibraryFreeUnknownNamesRemainRejected(string name)
        => Assert.NotEmpty(Check($"const a=new Int8Array(2);a['{name}'];", false, []));

    private static string[] Check(string source, bool respectLibraries, string[] libraries)
    {
        string path = Path.GetFullPath(Path.Combine("typed-array-literal-members", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Default with { NoLib = libraries.Length == 0, Lib = libraries });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { RespectLoadedLibraries = respectLibraries });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode ?? "").ToArray();
    }
}
