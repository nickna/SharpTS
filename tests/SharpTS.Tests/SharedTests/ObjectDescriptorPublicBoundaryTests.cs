using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectDescriptorPublicBoundaryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["retained-nullish", """
            const own=Object.getOwnPropertyDescriptor;
            for(const target of [null,undefined]){let calls=0;const key:any={[Symbol.toPrimitive](){calls++;throw new Error('key');}};try{own(target as any,key);}catch(error){console.log(error instanceof TypeError,calls);}}
            """, "true 0\ntrue 0\n"];
        yield return ["direct-expression-order", """
            let trace='';function makeKey():any{trace+='expression';return {[Symbol.toPrimitive](){trace+='coercion';return 'x';}};}
            try{Object.getOwnPropertyDescriptor(null as any,makeKey());}catch(error){console.log(error instanceof TypeError,trace);}
            """, "true expression\n"];
        yield return ["call-apply-bind-spread", """
            let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return 'x';}},own=Object.getOwnPropertyDescriptor;
            const bound=own.bind(null,undefined as any);const forwarded:any=own;const args:any[]=[null,key];
            for(const invoke of [()=>own.call(null,null as any,key),()=>own.apply(null,[undefined as any,key]),()=>bound(key),()=>forwarded(...args)]){
                try{invoke();}catch(error){console.log(error instanceof TypeError,calls);}
            }
            """, "true 0\ntrue 0\ntrue 0\ntrue 0\n"];
        yield return ["missing-versus-null-key-and-metadata", """
            const own:any=Object.getOwnPropertyDescriptor,target:any={undefined:1,null:2};
            console.log(own(target).value,own(target,null).value,own.name,own.length,own===Object.getOwnPropertyDescriptor);
            try{own();}catch(error){console.log(error instanceof TypeError);}
            """, "1 2 getOwnPropertyDescriptor 2 true\ntrue\n"];
        yield return ["valid-target-and-abrupt-key", """
            let calls=0;const marker={};const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 'x';}};
            console.log(Object.getOwnPropertyDescriptor({x:7},key)!.value,calls);
            const throwing:any={[Symbol.toPrimitive](){calls++;throw marker;}};
            try{Object.getOwnPropertyDescriptor({},throwing);}catch(error){console.log(error===marker,calls);}
            """, "string\n7 1\ntrue 2\n"];
        yield return ["real-await-and-order", """
            let trace='';const object:any={x:7};
            function target(){trace+='target';return object;}
            async function key():Promise<any>{await new Promise<void>(resolve=>setTimeout(resolve,1));trace+='expression';return {[Symbol.toPrimitive](){trace+='coercion';return 'x';}};}
            (async()=>{console.log(Object.getOwnPropertyDescriptor(target(),await key())!.value,trace);trace='';try{Object.getOwnPropertyDescriptor(null as any,await key());}catch(error){console.log(error instanceof TypeError,trace);}})();
            """, "7 targetexpressioncoercion\ntrue expression\n"];
        yield return ["direct-tuple-spread", """
            let trace='';function argumentsForCall():[any,any]{trace+='arguments';return [null,{[Symbol.toPrimitive](){trace+='coercion';return 'x';}}];}
            try{Object.getOwnPropertyDescriptor(...argumentsForCall());}catch(error){console.log(error instanceof TypeError,trace);}
            """, "true arguments\n"];
    }

    public static IEnumerable<object[]> Modes()=>Cases().SelectMany(row=>new[]{ExecutionMode.Interpreted,ExecutionMode.Compiled}
        .Select(mode=>new object[]{row[0],row[1],row[2],mode}));

    [Theory,MemberData(nameof(Modes))]
    public void PublicEntryValidatesAfterArgumentsAndBeforeKeyConversion(string name,string source,string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if(mode==ExecutionMode.Compiled){var(errors,output)=TestHarness.CompileVerifyAndRun(source);Assert.Empty(errors);Assert.Equal(expected,output);}
        else Assert.Equal(expected,TestHarness.Run(source,mode));
    }

    [Theory,MemberData(nameof(Cases))]
    public void NativePreparedPublicEntryVerifiesBeforeExecution(string name,string source,string expected)
    {
        Assert.NotEmpty(name);var statements=TestHarness.ParseOrThrow(source);var types=new TypeChecker().Check(statements);
        var compiler=new ILCompiler("object_descriptor_public_"+Guid.NewGuid().ToString("N"),false,true,null,null,null,OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();compiler.Compile(statements,types);var bytes=compiler.SaveToBytes();
        using var verifier=new ILVerifier(extraProbeDirectories:[AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected,TestHarness.RunCompiledWithTimeout(()=>Assembly.Load(bytes),TimeSpan.FromSeconds(20)));
    }
}
