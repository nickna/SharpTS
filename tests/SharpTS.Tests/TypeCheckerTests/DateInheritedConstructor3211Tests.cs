using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DateInheritedConstructor3211Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["instance-and-prototype", "const value=new Date(0);console.log(Date.prototype.constructor===Date,value.constructor===Date);\n"];
        yield return ["accessor-override-and-delete", "const P:any=Date.prototype,replacement={};let reads=0;Object.defineProperty(P,\"constructor\",{get(){reads++;return replacement;},configurable:true});const value=new Date(0);console.log(value.constructor===replacement,reads);console.log(Reflect.deleteProperty(P,\"constructor\"),Object.hasOwn(P,\"constructor\"),P.constructor===Object);\n"];
        yield return ["inherited-getter-receiver", "const P:any=Date.prototype;let receiver:any;Object.defineProperty(P,\"constructor\",{get(){receiver=this;return Date;},configurable:true});const value=new Date(0);console.log(value.constructor===Date,receiver===value);\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DateInheritsObjectPrototypeMembers(string name, string source)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }

    [Theory]
    [InlineData("const value=new Date(0);console.log(value.noSuchDateMember);")]
    public void DateMemberValidationRemainsSpecific(string source)
    {
        Assert.NotEmpty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }
}
