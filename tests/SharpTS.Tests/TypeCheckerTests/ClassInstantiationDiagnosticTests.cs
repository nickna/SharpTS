using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassInstantiationDiagnosticTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["argument-type", "class Box<T>{constructor(public value:T){}}const C=Box<number>;new C('wrong');", "TS2345"];
        yield return ["instance-member", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const value:string=new C(7).value;", "TS2322"];
        yield return ["constraint", "class Box<T extends {id:number}>{constructor(public value:T){}}const C=Box<string>;", "TS2344"];
        yield return ["new-type-arguments", "class Box<T>{constructor(public value:T){}}const C=Box<number>;new C<string>('wrong');", "TS2558"];
        yield return ["abstract", "abstract class Box<T>{constructor(public value:T){}}const C=Box<number>;new C(7);", "TS2511"];
        yield return ["respecialized", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const Again=C<string>;", "TS2635"];
        yield return ["too-many-class-arguments", "class Box<T>{constructor(public value:T){}}const C=Box<number,string>;", "TS2635"];
        yield return ["missing-class-argument", "class Box<T,U>{constructor(public value:T,public other:U){}}const C=Box<number>;", "TS2635"];
        yield return ["class-value-has-no-instance-field", "class Box<T>{constructor(public value:T){}}const C=Box<number>;C.value;", "TS2339"];
        yield return ["typed-static-member", "class Box<T>{static label='box';constructor(public value:T){}}const C=Box<number>;const label:number=C.label;", "TS2322"];
        yield return ["constructor-signature-assignment", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const Wrong:new(value:string)=>Box<number>=C;", "TS2322"];
        yield return ["constructor-result-type", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const Wrong:new(value:number)=>Box<string>=C;", "TS2419"];
        yield return ["constructor-instance-member", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const Wrong:{new(value:number):Box<number>;value:number}=C;", "TS2741"];
        yield return ["constructor-private-static-member", "class Box<T>{private static secret='hidden';constructor(public value:T){}}const C=Box<number>;const Wrong:{new(value:number):Box<number>;secret:string}=C;", "TS2322"];
        yield return ["private-constructor-signature", "class Box<T>{private constructor(public value:T){}}const C=Box<number>;const Wrong:new(value:number)=>Box<number>=C;", "TS2322"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ClosedConstructorSignaturesRetainTheirRestrictions(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Contains(GenericInstantiationDiagnosticTests.CollectStrictModules(source), diagnostic => diagnostic.TsCode == expected);
    }
}
