using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledNativeMethodRecreationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-deleted-native-method", """
            const O:any=Object;console.log(delete O.keys);O.keys=()=>7;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);
            """, "true\n7 true true true\n"];
        yield return ["existing-native-method-retains-attributes", """
            const O:any=Object;O.keys=()=>7;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);
            """, "7 true false true\n"];
        yield return ["retained-function-identity", """
            const O:any=Object,original=O.keys;console.log(delete O.keys);O.keys=original;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value===original,O.keys===original,d.writable,d.enumerable,d.configurable);
            """, "true\ntrue true true true true\n"];
        yield return ["multiple-recreation", """
            const O:any=Object;for(let index=0;index<3;index++){console.log(delete O.keys);O.keys=()=>index;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);}
            """, "true\n0 true true true\ntrue\n1 true true true\ntrue\n2 true true true\n"];
        yield return ["recreated-property-integrity", """
            const O:any=Object,seal=Object.seal,freeze=Object.freeze,read=Object.getOwnPropertyDescriptor;delete O.keys;O.keys=()=>7;seal(O);let d:any=read(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);freeze(O);d=read(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);
            """, "7 true true false\n7 false true false\n"];
        yield return ["recreated-method-enumerates", """
            const O:any=Object,ownKeys=Object.keys;delete O.keys;O.keys=()=>7;console.log(ownKeys(O).includes('keys'),Object.getOwnPropertyNames(O).includes('keys'));
            """, "true true\n"];
        yield return ["other-native-constructors", """
            const pairs:any[]=[[Object,'keys'],[Array,'isArray'],[Date,'now']];for(const pair of pairs){const target:any=pair[0],key:string=pair[1],before=target[key];console.log(delete target[key]);target[key]=before;const d:any=Object.getOwnPropertyDescriptor(target,key);console.log(d.value===before,d.writable,d.enumerable,d.configurable);}
            """, "true\ntrue true true true\ntrue\ntrue true true true\ntrue\ntrue true true true\n"];
        yield return ["reflect-set-control", """
            const O:any=Object;delete O.keys;console.log(Reflect.set(O,'keys',()=>7));const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);
            """, "true\n7 true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void AssignmentRecreatesOrdinaryEnumerableData(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
