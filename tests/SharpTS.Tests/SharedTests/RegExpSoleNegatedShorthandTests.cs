using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RegExpSoleNegatedShorthandTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported", "console.log(/[\\W]/.test('İ'),/[\\S]/.test(' '),/[\\S]/.test('　'));", "true false false\n"];
        yield return ["complements", """
            console.log(/[\D]/.test('5'),/[^\D]/.test('5'),/[\D]/.test('٥'),/[^\D]/.test('٥'));
            console.log(/[\W]/.test('İ'),/[^\W]/.test('İ'),/[\W]/.test('_'),/[^\W]/.test('_'));
            console.log(/[\S]/.test('\uFEFF'),/[^\S]/.test('\uFEFF'),/[\S]/.test('x'),/[^\S]/.test('x'));
            """, "false true true false\ntrue false false true\nfalse true true false\n"];
        yield return ["whitespace", """
            const whitespace="\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";
            console.log(whitespace.length,whitespace.split('').every(c=>!/[\S]/.test(c)),whitespace.split('').every(c=>/[^\S]/.test(c)));
            console.log(/[\S]/.test('\u0085'),/[^\S]/.test('\u0085'));
            """, "25 true true\ntrue false\n"];
        yield return ["escapes-and-unions", """
            console.log(new RegExp("\\[\\\\W\\]").test("[\\W]"),new RegExp("\\[\\\\S\\]").test("[\\S]"));
            console.log(/[a\W]/.test('!'),/[^a\W]/.test('b'),/\\[\W]/.test('\\İ'));
            """, "true true\ntrue true true\n"];
        yield return ["captures-and-fallback", """
            const expression=/^([\W])\1$/;
            console.log(expression.test('İİ'),expression.test('AA'),expression.source);
            const fallback=new RegExp('(?<value>[\\S]+)','s');
            console.log(fallback.test('\u00a0'),fallback.test('ok'));
            """, "true false ^([\\W])\\1$\nfalse true\n"];
        yield return ["dynamic", """
            const patterns=['[\\W]','[^\\W]','[\\D]','[^\\D]','[\\S]','[^\\S]'];
            for(const pattern of patterns){const expression=new RegExp(pattern);console.log(expression.source,expression.test('İ'),expression.test(' '));}
            """, "[\\W] true true\n[^\\W] false false\n[\\D] true true\n[^\\D] false false\n[\\S] true false\n[^\\S] false true\n"];
        yield return ["incomplete-classes", """
            for(const pattern of ['[','[^','[\\W','[^\\W']){
                try{new RegExp(pattern);console.log('accepted');}catch(error){console.log(error instanceof SyntaxError);}
            }
            """, "true\ntrue\ntrue\ntrue\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SoleNegatedClassUsesTheEcmaScriptCharacterSet(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedOutputRemainsSelfContainedAndVerifiable(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("regexp_sole_class_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        var assembly = Assembly.Load(bytes);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }
}
