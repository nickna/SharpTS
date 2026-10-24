using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ReadonlyClassIndexTests
{
    [Theory]
    [InlineData("string", "'x'")]
    [InlineData("number", "0")]
    [InlineData("symbol", "Symbol.iterator")]
    public void ReadonlyIndex_RejectsWritesAndAllowsReads(string keyType, string key)
    {
        foreach (var modifier in new[] { "readonly ", "" })
        {
            var source = "class C { " + modifier + "[key: " + keyType + "]: number; } const c = new C(); c[" + key + "] = 1; const n: number = c[" + key + "];";
            var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(modifier.Length > 0, result.Diagnostics.Any(d => d.TsCode == "TS2542"));
        }
    }
    [Fact]
    public void NamedMutableField_IsWritableAlongsideReadonlyIndex()
    {
        var source = "class C { readonly [key: string]: number; value: number = 1; } new C()['value'] = 2;";
        Assert.Empty(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
    }
    [Theory]
    [InlineData("class C { readonly [key: string]: number; } const c = new C(); c.value = 1;")]
    [InlineData("function write() { const c = new C(); c['value'] = 1; } class C { readonly [key: string]: number; }")]
    [InlineData("class C { readonly [key: string]: number; } const c = new C(); const key: string = 'x'; c[key] = 1;")]
    [InlineData("declare class C { readonly [key: string]: number; } declare const c: C; c['value'] = 1;")]
    public void ReadonlyIndexes_PersistThroughNamedWritesAndForwardClassMetadata(string source)
    {
        Assert.Contains(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics,
            d => d.TsCode == "TS2542");
    }
    [Theory]
    [InlineData("c[0] = 1")]
    [InlineData("c[0] += 1")]
    [InlineData("c[0] ||= 1")]
    [InlineData("c[0] &&= 1")]
    [InlineData("c[0] ??= 1")]
    [InlineData("c[0]++")]
    [InlineData("++c[0]")]
    [InlineData("c[0]--")]
    [InlineData("--c[0]")]
    public void EveryIndexWriteForm_EnforcesReadonlyAndKeepsMutableNumericIndexes(string write)
    {
        foreach (string modifier in new[] { "readonly ", "" })
        {
            var source = "class C { " + modifier + "[key: number]: number; } const c = new C(); " + write + ";";
            var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            if (modifier.Length > 0) Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2542");
            else Assert.Empty(result.Diagnostics);
        }
    }
    [Theory]
    [InlineData("class C { static readonly [key: number]: number; } C[0]++;")]
    [InlineData("class C { static readonly [key: number]: number; } C[0] += 1;")]
    [InlineData("class B { readonly [key: string]: number; } class C extends B {} const c = new C(); c['x'] ||= 1;")]
    [InlineData("class B { readonly [key: number]: number; } class C extends B {} const c = new C(); c[0] += 1;")]
    [InlineData("class C { readonly [key: string]: number; } const c = new C(); c.value += 1;")]
    [InlineData("class C { readonly [key: string]: number; } const c = new C(); c.value ||= 1;")]
    public void ReadonlyIndexWriteForms_RespectInheritedAndConstructorFacets(string source)
    {
        Assert.Contains(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics,
            d => d.TsCode == "TS2542");
    }
}
