using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BigIntErasedUnaryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const minus=-3n;const not=~3n;console.log(typeof minus,minus===(-3n as any),typeof not,not===(-4n as any));", "bigint true bigint true\n"];
        yield return ["wrappers", """
            console.log(typeof (-(3n as any)), (-(3n as any)) === (BigInt('-3') as any));
            console.log(typeof (~(3n as any)), (~(3n as any)) === (BigInt('-4') as any));
            console.log(String(-((3n satisfies bigint)!)), String(~((3n satisfies bigint)!)));
            """, "bigint true\nbigint true\n-3 -4\n"];
        yield return ["stored-local", """
            function run(){const minus=-(3n as any);const not=~(3n as any);
                console.log(typeof minus,minus===(BigInt('-3') as any),typeof not,not===(BigInt('-4') as any));}
            run();
            """, "bigint true bigint true\n"];
        yield return ["returns-and-typed-forwarding", """
            function pass(value:number){return value;}
            function minus(value:any){return -value;}
            function run(){const a=pass(-(3n as any));const b=minus(3n);
                console.log(typeof a,String(a),typeof b,String(b));}
            run();
            """, "bigint -3 bigint -3\n"];
        yield return ["asserted-object-coercion", """
            const events:string[]=[];
            const value={valueOf(){events.push('convert');return 3n;}};
            const minus=-(value as unknown as number),not=~(value as unknown as number);
            console.log(typeof minus,String(minus),typeof not,String(not),events.join(','));
            """, "bigint -3 bigint -4 convert,convert\n"];
        yield return ["asserted-number-slot", """
            function run(){const value:number=(-3n as any);console.log(typeof value,String(value));}run();
            """, "bigint -3\n"];
        yield return ["ordered-operand", """
            const events:string[]=[];
            console.log(String(-((events.push('minus'),3n) as any)));
            console.log(String(~((events.push('not'),3n) as any)));
            console.log(events.join(','));
            """, "-3\n-4\nminus,not\n"];
        yield return ["async-and-generator", """
            async function run(){await Promise.resolve(0);console.log(typeof (-(3n as any)),String(-(3n as any)),String(~(3n as any)));}
            function* values(){yield -(3n as any);yield ~(3n as any);}
            for(const value of values())console.log(typeof value,String(value));run();
            """, "bigint -3\nbigint -4\nbigint -3 -4\n"];
        yield return ["number-and-strict-kind", """
            console.log(-(3 as any),~(3 as any));
            console.log((-3n as any)===(-3 as any),(-3n as any)!==(-3 as any));
            console.log((-3n as any)==(-3 as any),(-3n as any)!=(-3 as any));
            """, "-3 -4\nfalse true\ntrue false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void TypeOnlyWrappersPreserveUnaryBigIntKind(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedOutputVerifiesBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("erased_bigint_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
