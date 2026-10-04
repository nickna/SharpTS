using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceClassConstructionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            namespace Models {export class Point {value:number;constructor(value:number){this.value=value;}read(){return this.value;}}}const models:any=Models;const C:any=models.Point;console.log(new Models.Point(3).read(),new C(5).read(),C===Models.Point);
            """, "3 5 true\n" };
        yield return new object[] { "qualified-only", """
            namespace Models {export class Point {value:number;constructor(value:number){this.value=value;}read(){return this.value;}}}console.log(new Models.Point(3).read());
            """, "3\n" };
        yield return new object[] { "aliased-only", """
            namespace Models {export class Point {value:number;constructor(value:number){this.value=value;}read(){return this.value;}}}const models:any=Models;const C:any=models.Point;console.log(new C(5).read(),C===Models.Point);
            """, "5 true\n" };
        yield return new object[] { "computed-alias", """
            namespace Models {export class Point {value:number;constructor(value:number){this.value=value;}read(){return this.value;}}}const models:any=Models;const C:any=models["Point"];console.log(new C(7).read(),C===models.Point);
            """, "7 true\n" };
        yield return new object[] { "nested", """
            namespace Models {export namespace Inner {export class Point {value:number;constructor(value:number){this.value=value;}read(){return this.value;}}}}const models:any=Models;const C:any=models.Inner.Point;console.log(new Models.Inner.Point(3).read(),new C(5).read(),C===Models.Inner.Point);
            """, "3 5 true\n" };
        yield return new object[] { "constructor-argument-order", """
            let next=0;function take(){next=next+1;return next;}namespace Models {export class Point {value:number;constructor(a:number,b:number){this.value=a*10+b;}read(){return this.value;}}}console.log(new Models.Point(take(),take()).read(),next);
            """, "12 2\n" };
        yield return new object[] { "inheritance", """
            namespace Models {export class Base {read(){return 3;}}export class Point extends Base {extra(){return this.read()+2;}}}const models:any=Models;const C:any=models.Point;console.log(new Models.Point().extra(),new C().extra(),C===Models.Point);
            """, "5 5 true\n" };
        yield return new object[] { "member-function", """
            namespace Models {export class Point {read(){return 3;}}export function make(){return new Models.Point();}}console.log(Models.make().read());
            """, "3\n" };
        yield return new object[] { "generic", """
            namespace Models {export class Box<T> {value:T;constructor(value:T){this.value=value;}read():T{return this.value;}}}console.log(new Models.Box<number>(3).read(),new Models.Box<string>("ok").read());
            """, "3 ok\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void NamespaceClassesPreserveConstructionArgumentsMethodsAndIdentity(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
