using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericConstructorDependentDefaultRuntimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [ExecutionMode.Interpreted, "interface F { new<T,U extends T=T>(value:T):{value:U} } class Box { value:any; constructor(value:any){this.value=value;} } const f=Box as unknown as F; const result=new f<number>(7); console.log(result.value);", "7\n"];
        yield return [ExecutionMode.Compiled, "interface F { new<T,U extends T=T>(value:T):{value:U} } class Box { value:any; constructor(value:any){this.value=value;} } const f=Box as unknown as F; const result=new f<number>(7); console.log(result.value);", "7\n"];
        yield return [ExecutionMode.Interpreted, "interface F { new<T,U extends T=T,V extends U=U>(value:T):{value:V} } class Box { value:any; constructor(value:any){this.value=value;} } const f=Box as unknown as F; const result=new f<string>('ok'); console.log(result.value);", "ok\n"];
        yield return [ExecutionMode.Compiled, "interface F { new<T,U extends T=T,V extends U=U>(value:T):{value:V} } class Box { value:any; constructor(value:any){this.value=value;} } const f=Box as unknown as F; const result=new f<string>('ok'); console.log(result.value);", "ok\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PreservesTheSubstitutedResultAndConstructorValue(ExecutionMode mode, string source, string expected)
        => Assert.Equal(expected, TestHarness.Run(source, mode));
}
