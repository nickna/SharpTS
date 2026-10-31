using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DateGetTimeReceiverBrandTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prototype", """try{Date.prototype.getTime();}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["computed-prototype", """try{Date['prototype']['getTime']();}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["prototype-alias", """const p=Date.prototype;try{p.getTime();}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["ordinary-instance", """const value=new Date(0);console.log(value['getTime'](),value.getTime());""", "0 0\n"];
        yield return ["invalid-date-keeps-slot", """const value=new Date(NaN);console.log(Number.isNaN(value.getTime()),Number.isNaN(value.valueOf()));""", "true true\n"];
        yield return ["borrowed-object", """const read:any=Date.prototype.getTime;try{read.call({});}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["borrowed-null", """const read:any=Date.prototype.getTime;try{read.call(null);}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["ordinary-prototype-inheritor", """const value:any=Object.create(Date.prototype);try{value.getTime();}catch(e){console.log(e instanceof TypeError);}""", "true\n"];
        yield return ["no-receiver-coercion", """let reads=0;const value:any={valueOf(){reads++;return 0;}};const read:any=Date.prototype.getTime;try{read.call(value);}catch(e){console.log(e instanceof TypeError,reads);}""", "true 0\n"];
        yield return ["borrowed-invalid-date", """const read:any=Date.prototype.getTime;console.log(Number.isNaN(read.call(new Date(NaN))));""", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OnlyActualDateDataReceiversAreAccepted(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
