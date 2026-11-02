using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DataCloneErrorTransportTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["function-error-value", "try{structuredClone(()=>{})}catch(e:any){console.log(typeof e,e.name,e instanceof Error)}", "object DataCloneError true\n"];
        yield return ["symbol-error-value", "try{structuredClone(Symbol(\"x\"))}catch(e:any){console.log(typeof e,e.name,e instanceof Error)}", "object DataCloneError true\n"];
        yield return ["error-message-value", "try{structuredClone(()=>{})}catch(e:any){console.log(e.name,typeof e.message,e.message.length>0,e instanceof Error,e instanceof TypeError)}", "DataCloneError string true true false\n"];
        yield return ["nested-call", "function clone(){return structuredClone(()=>{})}try{clone()}catch(e:any){console.log(e.name,e instanceof Error)}", "DataCloneError true\n"];
        yield return ["nested-array-value", "try{structuredClone([()=>{}])}catch(e:any){console.log(e.name,e instanceof Error)}", "DataCloneError true\n"];
        yield return ["rethrow-identity", "let seen:any;try{try{structuredClone(()=>{})}catch(e){seen=e;throw e}}catch(e:any){console.log(e===seen,e.name,e instanceof Error)}", "true DataCloneError true\n"];
        yield return ["guest-string", "try{throw \"DataCloneError: guest\"}catch(e:any){console.log(typeof e,e)}", "string DataCloneError: guest\n"];
        yield return ["guest-string-through-frame", "function fail(){throw \"DataCloneError: guest\"}try{fail()}catch(e:any){console.log(typeof e,e)}", "string DataCloneError: guest\n"];
        yield return ["guest-error-name", "try{throw new Error(\"DataCloneError: guest\")}catch(e:any){console.log(e.name,e.message,e instanceof Error)}", "Error DataCloneError: guest true\n"];
        yield return ["guest-object-identity", "const value:any={name:\"DataCloneError\"};try{throw value}catch(e:any){console.log(e===value,e.name)}", "true DataCloneError\n"];
        yield return ["promise-executor", "async function main(){try{await new Promise((resolve,reject)=>{structuredClone(()=>{})})}catch(e:any){console.log(e.name,e instanceof Error)}}main();", "DataCloneError true\n"];
        yield return ["promise-then", "async function main(){try{await Promise.resolve(1).then(()=>structuredClone(()=>{}))}catch(e:any){console.log(e.name,e instanceof Error)}}main();", "DataCloneError true\n"];
        yield return ["promise-guest-string", "async function main(){try{await Promise.reject(\"DataCloneError: guest\")}catch(e:any){console.log(typeof e,e)}}main();", "string DataCloneError: guest\n"];
        yield return ["async-call", "async function fail(){structuredClone(()=>{})}async function main(){try{await fail()}catch(e:any){console.log(e.name,e instanceof Error)}}main();", "DataCloneError true\n"];
        yield return ["argument-preserves-value", "function show(e:any){console.log(e.name,e instanceof Error)}try{structuredClone(()=>{})}catch(e){show(e)}", "DataCloneError true\n"];
        yield return ["typeerror-control", "try{throw new TypeError(\"guest\")}catch(e:any){console.log(e.name,e instanceof Error,e instanceof TypeError)}", "TypeError true true\n"];
        yield return ["sync-finally-propagation", "let n=0;try{try{structuredClone(()=>{})}finally{n++}}catch(e:any){console.log(e.name,n,e instanceof Error)}", "DataCloneError 1 true\n"];
        yield return ["async-finally-propagation", "async function main(){let n=0;try{try{await Promise.resolve(1);structuredClone(()=>{})}finally{n++}}catch(e:any){console.log(e.name,n,e instanceof Error)}}main();", "DataCloneError 1 true\n"];
        yield return ["catch-finally-function-propagation", "function fail(){try{throw 1}catch{structuredClone(()=>{})}finally{console.log(\"finally\")}}try{fail()}catch(e:any){console.log(e.name,e instanceof Error)}", "finally\nDataCloneError true\n"];
        yield return ["promise-reaction-rejection-value", "Promise.resolve(1).then(()=>{structuredClone(()=>{})}).then(undefined,(e:any)=>console.log(e.name,e instanceof Error));", "DataCloneError true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void NominalCloneFailuresRetainErrorValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
