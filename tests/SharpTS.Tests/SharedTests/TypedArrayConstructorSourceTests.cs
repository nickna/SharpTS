using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayConstructorSourceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["array-like-all-element-kinds", """
            const source:any={0:3,1:5,length:2};
            console.log(new Int8Array(source)[1],new Uint8Array(source)[1],new Uint8ClampedArray(source)[1]);
            console.log(new Int16Array(source)[1],new Uint16Array(source)[1],new Int32Array(source)[1],new Uint32Array(source)[1]);
            console.log(new Float32Array(source)[1],new Float64Array(source)[1]);
            const big:any={0:3n,1:5n,length:2};
            console.log(String(new BigInt64Array(big)[1]),String(new BigUint64Array(big)[1]));
            """, "5 5 5\n5 5 5 5\n5 5\n5 5\n"];
        yield return ["length-coercion-and-empty", """
            console.log(new Uint8Array({} as any).length,new Uint8Array({length:-2} as any).length);
            console.log(new Uint8Array({0:7,length:'1.9'} as any)[0]);
            const source:any={0:9,length:{valueOf(){console.log('length');return 1;}}};
            console.log(new Uint8Array(source)[0]);
            function pair(first:any,second:any){} const callable=new Uint8Array(pair as any);
            console.log(callable.length,callable[0],callable[1]);
            """, "0 0\n7\nlength\n9\n2 0 0\n"];
        yield return ["inherited-indices-and-holes", """
            const source:any=Object.create({0:3,length:3}); source[2]=5;
            const value=new Float64Array(source);
            console.log(value.length,value[0],Number.isNaN(value[1]),value[2]);
            console.log(new Uint8Array({length:2} as any)[0]);
            """, "3 3 true 5\n0\n"];
        yield return ["live-index-conversion", """
            const source:any={length:2,1:5};
            source[0]={valueOf(){console.log('convert');source[1]=9;source.length=0;return 3;}};
            const value=new Uint8Array(source); console.log(value.length,value[0],value[1]);
            """, "convert\n2 3 9\n"];
        yield return ["proxy-read-order", """
            const source:any=new Proxy({0:3,1:5,length:2},{get(target:any,key:any){
                console.log(typeof key==='symbol'?'iterator':key);return target[key];
            }});
            const value=new Uint8Array(source);console.log(value[0],value[1]);
            """, "iterator\nlength\n0\n1\n3 5\n"];
        yield return ["iterator-before-conversion", """
            let i=0;
            const source:any={get length(){throw 'length must not be read';},[Symbol.iterator](){
                return {next(){console.log('next',i);if(i++===2)return {done:true};
                    return {done:false,value:{valueOf(){console.log('convert');return 7;}}};}};
            }};
            const value=new Uint8Array(source);console.log(value.length,value[0],value[1]);
            """, "next 0\nnext 1\nnext 2\nconvert\nconvert\n2 7 7\n"];
        yield return ["abrupt-reads-and-conversions", """
            try{new Uint8Array({get length(){throw 'length';}} as any);}catch(e){console.log(e);}
            try{new Uint8Array({length:2,get 0(){throw 'index';},get 1(){console.log('unreachable');return 1;}} as any);}catch(e){console.log(e);}
            try{new Uint8Array({length:2,0:{valueOf(){throw 'conversion';}},get 1(){console.log('unreachable');return 1;}} as any);}catch(e){console.log(e);}
            try{new Uint8Array({length:Infinity} as any);}catch(e){console.log(e instanceof RangeError);}
            try{new Uint8Array({[Symbol.iterator]:7,length:0} as any);}catch(e){console.log(e instanceof TypeError);}
            """, "length\nindex\nconversion\ntrue\ntrue\n"];
        yield return ["existing-array-buffer-copy", """
            const array=new Uint8Array([3,5]);const copy=new Uint8Array(array);array[0]=9;
            const buffer=new ArrayBuffer(4);const view=new Uint8Array(buffer);view[0]=7;
            const shared=new SharedArrayBuffer(4);const sharedView=new Uint8Array(shared);sharedView[1]=8;
            console.log(copy[0],copy[1],new Uint8Array(buffer)[0],new Uint8Array(shared)[1]);
            """, "3 5 7 8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ObjectSourcesFollowGuestProtocols(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode).Replace("\r\n", "\n"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedSourcesVerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var compiler = new ILCompiler("typed_array_source_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, new TypeMap());
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        if (name == "proxy-read-order")
        {
            // Proxy is an explicit native-profile refusal. Managed modes above
            // still assert the full Node-verified property read sequence.
            var error = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiledWithTimeout(
                () => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
            Assert.Contains("Proxy is not supported in native guest output", error.ToString());
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
        }
    }
}
