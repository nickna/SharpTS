using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorIntrinsicOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [ExecutionMode.Interpreted, "unchanged-fake-date-intrinsic-order", """
            const C=class Date{static now(){return 7;}};console.log(Object.getOwnPropertyNames(C).join(','));
            """, "length,name,prototype,now\n"];
        yield return [ExecutionMode.Compiled, "unchanged-fake-date-intrinsic-order", """
            const C=class Date{static now(){return 7;}};console.log(Object.getOwnPropertyNames(C).join(','));
            """, "length,name,prototype,now\n"];
        yield return [ExecutionMode.Interpreted, "ordinary-declaration", """
            class Owner{static first(){}static get middle(){return 1;}static last(){}}console.log(Object.getOwnPropertyNames(Owner).join(','));
            """, "length,name,prototype,first,middle,last\n"];
        yield return [ExecutionMode.Compiled, "ordinary-declaration", """
            class Owner{static first(){}static get middle(){return 1;}static last(){}}console.log(Object.getOwnPropertyNames(Owner).join(','));
            """, "length,name,prototype,first,middle,last\n"];
        yield return [ExecutionMode.Interpreted, "named-expression", """
            const C=class Inner{static first(){}};console.log(Object.getOwnPropertyNames(C).join(','),C.name);
            """, "length,name,prototype,first Inner\n"];
        yield return [ExecutionMode.Compiled, "named-expression", """
            const C=class Inner{static first(){}};console.log(Object.getOwnPropertyNames(C).join(','),C.name);
            """, "length,name,prototype,first Inner\n"];
        yield return [ExecutionMode.Interpreted, "anonymous-expression-and-initializer", """
            const events:string[]=[];const C=class{static field=(events.push('field'),1);};console.log(Object.getOwnPropertyNames(C).join(','),events.join(','));
            """, "length,name,prototype,field field\n"];
        yield return [ExecutionMode.Compiled, "anonymous-expression-and-initializer", """
            const events:string[]=[];const C=class{static field=(events.push('field'),1);};console.log(Object.getOwnPropertyNames(C).join(','),events.join(','));
            """, "length,name,prototype,field field\n"];
        yield return [ExecutionMode.Interpreted, "runtime-parent", """
            const Parent:any=class Base{};const C=class extends Parent{static first(){}};console.log(Object.getOwnPropertyNames(C).join(','));
            """, "length,name,prototype,first\n"];
        yield return [ExecutionMode.Compiled, "runtime-parent", """
            const Parent:any=class Base{};const C=class extends Parent{static first(){}};console.log(Object.getOwnPropertyNames(C).join(','));
            """, "length,name,prototype,first\n"];
        yield return [ExecutionMode.Interpreted, "delete-recreate-intrinsic-name", """
            const C:any=class Inner{};C.extra=1;delete C.name;C.name='new';console.log(Object.getOwnPropertyNames(C).join(','),C.name);
            """, "length,prototype,extra \n"];
        yield return [ExecutionMode.Compiled, "delete-recreate-intrinsic-name", """
            const C:any=class Inner{};C.extra=1;delete C.name;C.name='new';console.log(Object.getOwnPropertyNames(C).join(','),C.name);
            """, "length,prototype,extra \n"];
        yield return [ExecutionMode.Interpreted, "symbol-and-string-order", """
            const key=Symbol('key');const C=class{static [key]=1;static first(){}};console.log(Reflect.ownKeys(C).map(k=>typeof k==='symbol'?'symbol':k).join(','));
            """, "length,name,prototype,first,symbol\n"];
        yield return [ExecutionMode.Compiled, "symbol-and-string-order", """
            const key=Symbol('key');const C=class{static [key]=1;static first(){}};console.log(Reflect.ownKeys(C).map(k=>typeof k==='symbol'?'symbol':k).join(','));
            """, "length,name,prototype,first,symbol\n"];
        yield return [ExecutionMode.Interpreted, "defaults-are-not-evaluated", """
            let hits=0;const C=class{constructor(value=(hits++,1)){}};console.log(Object.getOwnPropertyNames(C).join(','),C.length,hits);
            """, "length,name,prototype 0 0\n"];
        yield return [ExecutionMode.Compiled, "defaults-are-not-evaluated", """
            let hits=0;const C=class{constructor(value=(hits++,1)){}};console.log(Object.getOwnPropertyNames(C).join(','),C.length,hits);
            """, "length,name,prototype 0 0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ConstructorIntrinsicsKeepTheirCreationOrder(ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
