using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveStringOwnDescriptorInterpreterTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const descriptor:any=Object.getOwnPropertyDescriptor('abc','0');console.log(descriptor.value,descriptor.writable,descriptor.enumerable,descriptor.configurable);", "a false true false\n"];
        yield return ["length", "for(const value of ['','abc']){const descriptor:any=Object.getOwnPropertyDescriptor(value,'length');console.log(descriptor.value,descriptor.writable,descriptor.enumerable,descriptor.configurable);}", "0 false false false\n3 false false false\n"];
        yield return ["canonical-indices", "for(const key of ['01','-0','+0',' 0','0.0','1e0','-1','3','4294967295','toString','charAt'])console.log(Object.getOwnPropertyDescriptor('abc',key)===undefined);const descriptor:any=Object.getOwnPropertyDescriptor('abc',-0);console.log(descriptor.value,descriptor.enumerable);", "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\na true\n"];
        yield return ["retained-coerced-key", "let calls=0;const own=Object.getOwnPropertyDescriptor;const key:any={[Symbol.toPrimitive](hint:string){calls++;return '0';}};const descriptor:any=own('abc',key);console.log(descriptor.value,calls,descriptor.writable,descriptor.enumerable,descriptor.configurable);", "a 1 false true false\n"];
        yield return ["utf16", "const value='\uD83D\uDE00';for(const key of ['0','1']){const descriptor:any=Object.getOwnPropertyDescriptor(value,key);console.log(descriptor.value.charCodeAt(0),descriptor.enumerable,descriptor.writable,descriptor.configurable);}", "55357 true false false\n56832 true false false\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CharacterIndicesAreEnumerableOwnDataProperties(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
