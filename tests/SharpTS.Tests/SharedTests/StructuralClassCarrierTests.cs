using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StructuralClassCarrierTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["field-read", "class View{value=0;}function read(box:View){return box.value;}console.log(read({value:1}));", "1\n"];
        yield return ["field-write-identity", "class View{value=0;}function write(box:View):View{box.value=4;return box;}const box={value:1,label:'kept'};const alias=write(box);console.log(alias===box,box.value,box.label);", "true 4 kept\n"];
        yield return ["primitive-fields", "class View{value=0;active=false;label='';}function write(box:View){box.value=4;box.active=true;box.label='changed';return box.value;}const box={value:1,active:false,label:'kept'};console.log(write(box),box.active,box.label);", "4 true changed\n"];
        yield return ["own-accessors", "class View{value=0;}let backing=1;let gets=0;const box={get value(){gets++;return backing;},set value(next:number){backing=next;}};function change(view:View){view.value=view.value+3;return view.value;}console.log(change(box),backing,gets);", "4 4 2\n"];
        yield return ["class-accessor-contract", "class View{get value(){return 0;}set value(next:number){}}function write(view:View){view.value=4;return view.value;}const box={value:1};console.log(write(box),box.value);", "4 4\n"];
        yield return ["method-this", "class View{value=0;read(add:number){return this.value+add;}}function read(view:View){return view.read(2);}const box={value:1,read(add:number){return this.value+add;}};console.log(read(box));", "3\n"];
        yield return ["inherited-view", "class Base{value=0;}class View extends Base{}function write(view:View){view.value=4;return view.value;}const box={value:1,label:'kept'};console.log(write(box),box.value,box.label);", "4 4 kept\n"];
        yield return ["generic-view", "class View{value=0;}function write<T extends View>(view:T):T{view.value=4;return view;}const box={value:1,label:'kept'};console.log(write(box)===box,box.value,box.label);", "true 4 kept\n"];
        yield return ["return-and-callback", "class View{value=0;}function identity(view:View):View{return view;}const read=(view:View)=>view.value;const box={value:1};console.log(read(identity(box)),identity(box)===box);", "1 true\n"];
        yield return ["receiver-and-rhs-effects", "class View{value=0;}let events='';const box={value:1};function receiver():View{events+='r';return box;}function rhs(){events+='v';return 4;}console.log(receiver().value=rhs(),events,box.value);", "4 rv 4\n"];
        yield return ["method-effects", "class View{value=0;read(add:number){return this.value+add;}}let events='';const box={value:1,read(add:number){events+='m';return this.value+add;}};function receiver():View{events+='r';return box;}function argument(){events+='a';return 2;}console.log(receiver().read(argument()),events);", "3 ram\n"];
        yield return ["nominal-control", "class View{value=1;read(add:number){return this.value+add;}}function change(view:View){view.value=4;return view.read(2);}const box=new View();console.log(change(box),box.value);", "6 4\n"];
        yield return ["raw-structural-value", "class View{value=0;}function read(view:View){console.log(view.value===undefined,typeof view.value);}const value:any=undefined;read({value});", "true undefined\n"];
        yield return ["raw-structural-string", "class View{value='';}function read(view:View){console.log(view.value===undefined,typeof view.value);}const value:any=undefined;read({value});", "true undefined\n"];
        yield return ["generic-class", "class View<T>{value:T;constructor(value:T){this.value=value;}}function write(view:View<number>){view.value=4;return view.value;}const box={value:1};console.log(write(box),box.value);", "4 4\n"];
        yield return ["bracket-and-update", "class View{value=0;}function write(view:View){view['value']+=2;return ++view.value;}const box={value:1};console.log(write(box),box.value);", "4 4\n"];
        yield return ["method-getter-order", "class View{read(add:number){return add;}}let events='';const box={get read(){events+='g';return (add:number)=>{events+='m';return add;};}};function argument(){events+='a';return 2;}function read(view:View){return view.read(argument());}console.log(read(box),events);", "2 gam\n"];
        yield return ["structural-setter-does-not-prove-field", "class View{value=0;}function bump(view:View,next:number){view.value=next;return view.value+1;}const box={get value():any{return 'text';},set value(next:number){}};console.log(bump(box,4));", "text1\n"];
        yield return ["holder-boundary", "class View{value=0;}class Holder{view:View;constructor(view:View){this.view=view;}}const original={value:1};const holder=new Holder(original);console.log(holder.view===original,holder.view.value);", "true 1\n"];
        yield return ["raw-structural-return", "class View{value=0;}function read(view:View){return view.value;}const value:any=undefined;const result=read({value});console.log(result===undefined,typeof result);", "true undefined\n"];
        yield return ["generic-holder-boundary", "class View{value=0;}class Holder<T>{view:T;constructor(view:T){this.view=view;}}const original={value:1};const holder=new Holder<View>(original);console.log(holder.view===original,holder.view.value);", "true 1\n"];
        yield return ["generic-self-getter", "class Box<T>{value:T;constructor(value:T){this.value=value;}read(other:Box<T>):T{return other.value;}own():T{return this.value;}}const numberBox=new Box<number>(1);console.log(numberBox.read(new Box<number>(2)),numberBox.own());const stringBox=new Box<string>('one');console.log(stringBox.read(new Box<string>('two')),stringBox.own());", "2 1\ntwo one\n"];
    }

    public static IEnumerable<object[]> ParityCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[1], row[2], mode }));

    public static IEnumerable<object[]> IlCases() => Cases().Select(row => new object[] { row[1] });

    [Theory, MemberData(nameof(ParityCases))]
    public void ClassViewsUseTheOriginalStructuralCarrier(string source, string expected, ExecutionMode mode)
        => Assert.Equal(expected, TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(IlCases))]
    public void StructuralClassDispatchHasVerifiedIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

    [Fact]
    public void StructuralClassWritePreservesIdentityInStandaloneOutput()
    {
        const string source = "class View{value=0;}function write(box:View):View{box.value=4;return box;}const box={value:1,label:'kept'};const alias=write(box);console.log(alias===box,box.value,box.label);";
        Assert.Equal("true 4 kept\n", TestHarness.RunCompiledStandalone(source));
    }
}
