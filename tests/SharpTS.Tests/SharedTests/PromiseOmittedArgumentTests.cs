using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseOmittedArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-resolve", "Promise.resolve().then(value=>console.log(value===undefined));", "true\n"];
        yield return ["retained-resolve", "const resolve:any=Promise.resolve;resolve.call(Promise).then((value:any)=>console.log(value===undefined));", "true\n"];
        yield return ["direct-reject", "Promise.reject().catch(reason=>console.log(reason===undefined));", "true\n"];
        yield return ["retained-reject", "const reject:any=Promise.reject;reject.call(Promise).catch((reason:any)=>console.log(reason===undefined));", "true\n"];
        yield return ["explicit-null", "Promise.resolve(null).then(value=>console.log(value===null));", "true\n"];
        yield return ["explicit-null-reject", "Promise.reject(null).catch(reason=>console.log(reason===null));", "true\n"];
        yield return ["retained-null", "const resolve:any=Promise.resolve,reject:any=Promise.reject;resolve.call(Promise,null).then((v:any)=>console.log(v===null));reject.call(Promise,null).catch((v:any)=>console.log(v===null));", "true\ntrue\n"];
        yield return ["explicit-undefined", "Promise.resolve(undefined).then(v=>console.log(v===undefined));Promise.reject(undefined).catch(v=>console.log(v===undefined));", "true\ntrue\n"];
        yield return ["apply-and-bind", "const resolve:any=Promise.resolve,reject:any=Promise.reject;resolve.bind(Promise)().then((v:any)=>console.log(v===undefined));reject.apply(Promise,[]).catch((v:any)=>console.log(v===undefined));", "true\ntrue\n"];
        yield return ["metadata-and-constructor-refusal", "console.log(Promise.resolve.length,Promise.reject.length);const resolve:any=Promise.resolve,reject:any=Promise.reject;try{resolve.call(0)}catch(e){console.log(e instanceof TypeError)}try{reject.call(0)}catch(e){console.log(e instanceof TypeError)}", "1 1\ntrue\ntrue\n"];
        yield return ["intrinsic-identity", "const p=Promise.resolve(7);console.log(Promise.resolve.call(Promise,p)===p);", "true\n"];
        yield return ["subclass-omission", "class Sub extends Promise<any>{}const resolve:any=Promise.resolve;resolve.call(Sub).then((v:any)=>console.log(v===undefined));", "true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void MissingArgumentsUseUndefinedWithoutChangingExplicitValues(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
