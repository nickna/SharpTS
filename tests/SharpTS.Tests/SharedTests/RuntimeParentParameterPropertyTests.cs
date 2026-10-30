using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RuntimeParentParameterPropertyTests
{
    public static IEnumerable<object[]> ModuleCases()
    {
        foreach (var (imports, parent) in new[]
        {
            ("import {Base as Generic,Parent as Imported} from './lib';", "Imported"),
            ("import {Parent as Imported,Base as Generic} from './lib';", "Imported"),
            ("import {Parent as Imported} from './lib';", "Imported"),
            ("import {Base,Parent} from './lib';", "Parent"),
            ("import {Base as Generic,Parent} from './lib';", "Parent"),
            ("import * as lib from './lib';", "lib.Parent"),
            ("import {Base as Generic} from './lib';import {Parent as Imported} from './lib';", "Imported"),
            ("import {type Shape,Parent as Imported} from './lib';", "Imported"),
        })
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
                yield return [imports, parent, mode];
    }

    [Theory, MemberData(nameof(ModuleCases))]
    public void ImportedParameterPropertyParentKeepsItsRealConstructorValue(string imports, string parent, ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                export class Base {constructor(public value:any){}}
                export class Parent {constructor(public value:any=7){}}
                export interface Shape {value:number;}
                """,
            ["./main.ts"] = imports + "class Child extends " + parent + "{} console.log(new Child().value,new Child(11).value);",
        };
        Assert.Equal("7 11\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void IntactGenericAndRestModuleAliasesKeepTheirValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                export class Base<T> { constructor(public value:T){} }
                export class Parent { constructor(public value:any=7,...rest:any[]){this.value+=":"+rest.join(",");} }
                """,
            ["./main.ts"] = """
                import {Base as Generic, Parent as Imported} from "./lib";
                class Child extends Imported {}
                const C:any=Child;
                const G:any=Generic;
                console.log(new C(undefined,1,2).value, new G("generic").value);
                """,
        };
        Assert.Equal("7:1,2 generic\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["base-default-fields-once", """
            let defaults=0; let fields=0; let bodies=0;
            function initial():number{defaults++;return 7;}
            class Parent {
                early=(fields++,typeof(this as any).value);
                constructor(public value:number=initial()){bodies++;this.value++;}
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value?:number){super(value);}}
            const first=new Child(),second=new Child(11);
            console.log(first.early,first.value,second.early,second.value,defaults,fields,bodies);
            """, "undefined 8 undefined 12 1 2 2\n"];
        yield return ["derived-shadowed-parameter", """
            const events:string[]=[];
            class Base {constructor(){events.push('super');}}
            class Parent extends Base {
                own=(events.push('field'),2);
                constructor(public value:number=40){
                    value+=5;
                    const change=()=>{value+=3;};change();
                    {let value='shadow';super();events.push('body:'+this.value+':'+value);}
                    this.value+=2;
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value?:number){super(value);}}
            const first=new Child(),second=new Child(2);
            console.log(first.value,second.value,events.join(','));
            """, "50 12 super,field,body:48:shadow,super,field,body:10:shadow\n"];
        yield return ["derived-captured-parameter", """
            class Base {}
            class Parent extends Base {
                constructor(public value:number){
                    const change=()=>{value+=3;};
                    change();
                    super();console.log(this.value);
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value:number){super(value);}}
            console.log(new Child(44).value);
            """, "47\n47\n"];
        yield return ["super-argument-throws-before-own-elements", """
            let argumentsRun=0; let fields=0; let bodies=0;
            function argument(fail:boolean):number{argumentsRun++;if(fail)throw 'stop';return 3;}
            class Base {constructor(public base:number){}}
            class Parent extends Base {
                own=(fields++,1);
                constructor(public value:number,fail:boolean){super(argument(fail));bodies++;}
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value:number,fail:boolean){super(value,fail);}}
            try{new Child(5,true);}catch(error){console.log(error,fields,bodies);}
            const result=new Child(7,false);
            console.log(result.value,result.base,argumentsRun,fields,bodies);
            """, "stop 0 0\n7 3 2 1 1\n"];
        yield return ["class-expression-parent", """
            const Parent=class {constructor(public value:number=8){}};
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value?:number){super(value);}}
            console.log(new Child().value,new Child(12).value);
            """, "8 12\n"];
        yield return ["access-modifiers-and-new-target", """
            class Parent {
                seen:any;
                constructor(private value:number=7,protected amount:number=3,readonly tag:string='tag'){this.seen=new.target;}
                read():number{return this.value+this.amount;}
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {}
            const result=new Child();
            console.log(result.read(),result.tag,result.seen===Child);
            """, "10 tag true\n"];
        yield return ["conditional-super-initializes-once", """
            let fields=0;let bodies=0;
            class Base {constructor(public base:number){}}
            class Parent extends Base {
                own=(fields++,1);
                constructor(public value:number,flag:boolean){super(flag?1:2);bodies++;}
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value:number,flag:boolean){super(value,flag);}}
            const a=new Child(2,true),b=new Child(3,false);
            console.log(a.value,b.value,a.base,b.base,fields,bodies);
            """, "2 3 1 2 2 2\n"];
        yield return ["evaluated-constructor-choice", """
            class Parent {constructor(public value:number=7){}}
            class Alternate {constructor(public value:number=9){}}
            let choices=0;
            function choose():typeof Parent{choices++;return Alternate;}
            const Runtime:typeof Parent=choose();
            class Child extends Runtime {}
            const value=new Child();
            console.log(value.value,value instanceof Alternate,value instanceof Parent,choices);
            """, "9 true false 1\n"];
        yield return ["logical-and-numeric-captured-writes", """
            class Base {}
            class Parent extends Base {
                constructor(public value:number=0){
                    const change=()=>{value+=3;};
                    value ||=5;value*=2;change();super();console.log(this.value);
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {constructor(value?:number){super(value);}}
            const first=new Child(),second=new Child(2);
            console.log(first.value,second.value);
            """, "13\n7\n13 7\n"];
        yield return ["string-captured-writes", """
            class Base {}
            class Parent extends Base {
                constructor(public value:string='a'){
                    const change=()=>{value+='c';};
                    value+='b';change();super();
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {}
            console.log(new Child().value);
            """, "abc\n"];
        yield return ["later-default-captures-earlier-parameter", """
            function read(callback:()=>number):number{return callback();}
            class Parent {
                constructor(public value:number=7,other:number=read(()=>{value+=3;return value;})){
                    console.log(value,other,this.value);
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {}
            new Child();new Child(11);
            """, "10 10 10\n14 14 14\n"];
        yield return ["later-default-direct-and-arrow-writes", """
            function read(callback:()=>number):number{return callback();}
            class Parent {
                constructor(public value:number=7,direct:number=(value+=2),arrow:number=read(()=>{value+=3;return value;})){
                    console.log(value,direct,arrow,this.value);
                }
            }
            const Runtime:typeof Parent=Parent;
            class Child extends Runtime {}
            new Child();new Child(11);
            """, "12 9 12 12\n16 13 16 16\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void RuntimeParameterPropertiesUseOwnedValuesAndSuccessfulSuperTiming(string name,string source,string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
