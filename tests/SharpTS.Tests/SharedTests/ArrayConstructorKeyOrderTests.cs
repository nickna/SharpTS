using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayConstructorKeyOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const C:any=Array;Object.defineProperty(C,'7',{value:1,enumerable:true,configurable:true});console.log(Object.getOwnPropertyDescriptor(C,'7')!.value,Object.getOwnPropertyNames(C).slice(0,3).join(','),Reflect.ownKeys(C).slice(0,3).join(','));\n", "1 7,length,name 7,length,name\n"];
        yield return ["canonical-indices-and-ordinary-strings", "const C:any=Array;C['01']=1;C[7]=1;C[2]=1;C[4294967294]=1;C['4294967295']=1;C[1]=1;const selected=['1','2','7','01','4294967294','4294967295'];console.log(Object.getOwnPropertyNames(C).filter(key=>selected.includes(key)).join(','));console.log(Reflect.ownKeys(C).filter(key=>selected.includes(String(key))).join(','));", "1,2,7,4294967294,01,4294967295\n1,2,7,4294967294,01,4294967295\n"];
        yield return ["accessor-without-get", "const C:any=Array;let reads=0;Object.defineProperty(C,'3',{get(){reads++;return 9;},configurable:true});console.log(Object.getOwnPropertyNames(C).slice(0,3).join(','),Reflect.ownKeys(C).slice(0,3).join(','),reads);", "3,length,name 3,length,name 0\n"];
        yield return ["delete-and-readd-index", "const C:any=Array;C[7]=1;C[2]=2;delete C[2];C[2]=3;console.log(Object.getOwnPropertyNames(C).slice(0,4).join(','),Reflect.ownKeys(C).slice(0,4).join(','));", "2,7,length,name 2,7,length,name\n"];
        yield return ["descriptor-inventory", "const C:any=Array;Object.defineProperty(C,'7',{value:9,enumerable:false,configurable:true});const descriptors:any=Object.getOwnPropertyDescriptors(C);console.log(descriptors['7'].value,descriptors['7'].enumerable,Object.getOwnPropertyNames(descriptors).slice(0,3).join(','));", "9 false 7,length,name\n"];
        yield return ["deleted-native-and-metadata-deduplication", "const C:any=Array;delete C.isArray;C[7]=1;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes('isArray'),keys.includes('isArray'),names.filter(key=>key==='length').length,names.filter(key=>key==='name').length,names[0],keys[0]);", "false false 1 1 7 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReportsOwnIntegerKeysBeforeIntrinsicStringKeys(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted) Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
