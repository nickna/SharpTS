using System.Reflection;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class TypedArrayReadRepresentationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["literal-present-and-missing-return", "function present():number{const a=new Uint8Array(1);a[0]=9;return a[0]}function missing():number{const a=new Uint8Array(1);return a[1]}console.log(present(),typeof missing(),Number.isNaN(missing()));", "9 undefined false\n", new string[] { "present" }, new string[] { "missing" }];
        yield return ["dynamic-length-capture-and-alias", "function dynamic(n:number):number{const a=new Uint8Array(n);return a[0]}function captured():number{const a=new Uint8Array(1);const get=()=>a[0];return get()}function escaped():number{const a=new Uint8Array(1);const alias:any=a;return a[0]}console.log(typeof dynamic(0),dynamic(1),captured(),escaped());", "undefined 0 0 0\n", new string[] {  }, new string[] { "dynamic", "captured", "escaped" }];
        yield return ["observable-constructor-retains-raw-slot", "const ctor=Uint8Array;function observed():number{const a=new Uint8Array(1);return a[0]}console.log(observed(),ctor===Uint8Array);", "0 true\n", new string[] {  }, new string[] { "observed" }];
        yield return ["number-literal-equality-retains-value-boundaries", "function run(index:number){const a=new Uint8Array([0,9]);console.log(a[index]===0,0===a[index],a[index]!==0,a[index]==0,0!=a[index],a[index]===undefined,Object.is(a[index],undefined),Object.is(a[index],NaN),a[index]===a[index])}run(0);run(1);run(2);run(0.5);run(NaN);run(Infinity);const big=new BigInt64Array(1);console.log(big[1]===undefined,big[0]===0n,big[0]===1n);", "true true false true false false false false true\nfalse false true false true false false false true\nfalse false true false true true true false true\nfalse false true false true true true false true\nfalse false true false true true true false true\nfalse false true false true true true false true\ntrue true false\n", new string[] {  }, new string[] {  }];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LiteralPresenceAndEqualityPreserveValueAndClrReturnContracts(
        string name, string source, string expected, string[] nativeFunctions, string[] rawFunctions)
    {
        _ = name;
        var (directory, path) = TestHarness.CompileWithRefAsm(source);
        try
        {
            var (errors, output) = TestHarness.VerifySavedAssemblyAndRun(path, () =>
            {
                var program = Assembly.Load(File.ReadAllBytes(path)).GetType("$Program")!;
                var functions = program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (string function in nativeFunctions)
                    Assert.Equal(typeof(double), functions.Single(method => method.Name.EndsWith(function, StringComparison.Ordinal)).ReturnType);
                foreach (string function in rawFunctions)
                    Assert.Equal(typeof(object), functions.Single(method => method.Name.EndsWith(function, StringComparison.Ordinal)).ReturnType);
                return TestHarness.ExecuteCompiledDll(path);
            });
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
