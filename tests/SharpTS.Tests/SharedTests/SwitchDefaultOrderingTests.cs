using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SwitchDefaultOrderingTests
{
    public static IEnumerable<object[]> Programs()
    {
        yield return ["middle-fallthrough", """
            function run(value:number):void {
                let log='';
                switch(value){case 0:log+='a';default:log+='d';case 1:log+='b';}
                console.log(value,log);
            }
            run(0);run(2);run(1);
            """, "0 adb\n2 db\n1 b\n"];
        yield return ["first-fallthrough", """
            function run(value:number):void {
                let log='';
                switch(value){default:log+='d';case 0:log+='a';case 1:log+='b';}
                console.log(value,log);
            }
            run(0);run(1);run(2);
            """, "0 ab\n1 b\n2 dab\n"];
        yield return ["last-and-absent-default", """
            function run(value:number):void {
                let log='';
                switch(value){case 0:log+='a';case 1:log+='b';default:log+='d';}
                let other='';
                switch(value){case 0:other+='a';case 1:other+='b';}
                console.log(value,log,other||'empty');
            }
            run(0);run(1);run(2);
            """, "0 abd ab\n1 bd b\n2 d empty\n"];
        yield return ["case-effects-stop-after-match", """
            function run(value:number):void {
                let log='';
                function subject():number{log+='s';return value;}
                function test(label:string,key:number):number{log+=label;return key;}
                switch(subject()){
                    case test('a',0):log+='A';
                    default:log+='D';
                    case test('b',1):log+='B';
                    case test('c',2):log+='C';
                }
                console.log(value,log);
            }
            run(0);run(1);run(2);run(3);
            """, "0 saADBC\n1 sabBC\n2 sabcC\n3 sabcDBC\n"];
        yield return ["empty-clauses", """
            function run(value:number):void {
                let log='';
                switch(value){case 0:default:case 1:log+='b';case 2:log+='c';break;case 3:log+='x';}
                console.log(value,log);
            }
            run(0);run(1);run(2);run(3);run(4);
            """, "0 bc\n1 bc\n2 c\n3 x\n4 bc\n"];
        yield return ["break-in-each-position", """
            function run(value:number):void {
                let log='';
                switch(value){case 0:log+='a';break;default:log+='d';break;case 1:log+='b';break;}
                console.log(value,log);
            }
            run(0);run(1);run(2);
            """, "0 a\n1 b\n2 d\n"];
        yield return ["labeled-loop-exits", """
            let log='';
            outer:for(let value=0;value<3;value++){
                switch(value){case 0:log+='a';default:log+='d';continue outer;case 1:log+='b';break;}
                log+='z';
            }
            console.log(log);
            """, "adbzd\n"];
        yield return ["return-through-finally", """
            function run(value:number):string {
                let log='';
                try{
                    switch(value){case 0:log+='a';default:log+='d';return log;case 1:log+='b';return log;}
                }finally{console.log('finally',value,log);}
            }
            console.log(run(0));console.log(run(1));console.log(run(2));
            """, "finally 0 ad\nad\nfinally 1 b\nb\nfinally 2 d\nd\n"];
        yield return ["throw-through-finally", """
            function run(value:number):void {
                let log='';
                try{
                    switch(value){case 0:log+='a';default:log+='d';throw new Error('stop');case 1:log+='b';}
                }catch{log+='c';}finally{log+='f';}
                console.log(value,log);
            }
            run(0);run(1);run(2);
            """, "0 adcf\n1 bf\n2 dcf\n"];
        yield return ["nested-defaults-and-breaks", """
            function run(value:number):void {
                let log='';
                switch(value){
                    case 0:log+='a';
                    default:
                        log+='d';
                        switch(value){default:log+='x';case 0:log+='y';break;case 1:log+='z';}
                    case 1:log+='b';
                }
                console.log(value,log);
            }
            run(0);run(1);run(2);
            """, "0 adyb\n1 b\n2 dxyb\n"];
        yield return ["subject-outside-switch-lexical-scope", """
            let value=2;
            switch(value){default:let value=3;console.log(value);case 1:console.log('tail');}
            console.log(value);
            """, "3\ntail\n2\n"];
        yield return ["shared-clause-lexical-tdz", """
            function run(value:number):void {
                try{
                    switch(value){
                        default:const read=()=>later;console.log(read());
                        case 1:let later=3;console.log(later);
                    }
                }catch(error){console.log(value,error instanceof ReferenceError);}
            }
            run(2);run(1);
            """, "2 true\n3\n"];
        yield return ["case-test-later-lexical-shadow", """
            function invoke<T>(read:()=>T):T{return read();}
            let value=7;
            try{
                switch(0){case invoke(()=>value):console.log('wrong');break;default:let value=0;}
            }catch(error){console.log(error instanceof ReferenceError);}
            console.log(value);
            class Marker{}
            const subject:any=0;
            try{
                switch(subject){case invoke(()=>Marker):console.log('wrong');break;default:class Marker{}}
            }catch(error){console.log(error instanceof ReferenceError);}
            """, "true\n7\ntrue\n"];
        yield return ["case-test-hoisted-function", """
            function choose():number{return 9;}
            switch(1){
                case choose():console.log('inner');break;
                default:console.log('default');function choose():number{return 1;}
            }
            console.log(choose());
            """, "inner\n9\n"];
        yield return ["named-cross-clause-capture", """
            function run(value:number):void{
                const name='outer';
                switch(value){
                    default:const name='inner';console.log(read());break;
                    case 1:
                        function read(){return name;}
                        try{console.log(read());}catch(error){console.log(error instanceof ReferenceError);}
                }
                console.log(name);
            }
            run(0);run(1);
            """, "inner\nouter\ntrue\nouter\n"];
        yield return ["named-cross-clause-live-binding", """
            function run(key:number):void{
                let read:()=>number=()=>-1;
                switch(key){
                    case 0:break;
                    default:let value=2;function capture():number{return value;}read=capture;
                    case 1:value=3;
                }
                console.log(read());
            }
            run(9);
            """, "3\n"];
        yield return ["awaited-tests-and-bodies", """
            async function run(value:number):Promise<void>{
                let log='';
                async function test(label:string,key:number):Promise<number>{log+=label;await Promise.resolve();return key;}
                switch(value){
                    case await test('a',0):log+='A';break;
                    default:await Promise.resolve();log+='D';
                    case await test('b',1):await Promise.resolve();log+='B';
                }
                console.log(value,log);
            }
            async function main():Promise<void>{await run(1);await run(2);await run(0);}
            main();
            """, "1 abB\n2 abDB\n0 aA\n"];
        yield return ["yielded-tests-and-bodies", """
            function* steps(value:number):Generator<string,void,number>{
                switch(value){case yield 'test0':yield 'a';break;default:yield 'd';case yield 'test1':yield 'b';}
            }
            function run(value:number):void{
                const iterator=steps(value);let next=iterator.next();let log='';
                while(!next.done){log+=next.value+',';next=iterator.next(next.value==='test0'?0:1);}
                console.log(value,log);
            }
            run(0);run(1);run(2);
            """, "0 test0,a,\n1 test0,test1,b,\n2 test0,test1,d,b,\n"];
        yield return ["generator-case-destructuring-spill", """
            function* run(value:number):Generator<string,void,number[]>{let sink:any=0;switch(value){default:yield 'default';break;case ([sink]=yield 'test',sink):yield 'match'+sink;}}const it=run(7);console.log(it.next().value);console.log(it.next([7]).value);
            """, "test\nmatch7\n"];
        yield return ["async-generator-body-suspension", """
            async function* steps(value:number):AsyncGenerator<string,void,unknown>{
                switch(value){case 0:yield 'a';default:await Promise.resolve();yield 'd';case 1:yield 'b';}
            }
            async function run(value:number):Promise<void>{let log='';for await(const item of steps(value))log+=item;console.log(value,log);}
            async function main():Promise<void>{await run(0);await run(1);await run(2);}
            main();
            """, "0 adb\n1 b\n2 db\n"];
        yield return ["eval-normal-and-abrupt-completions", """
            console.log(eval('switch(0){case 0:1;default:2;case 1:3;}'));
            console.log(eval('switch(1){case 0:1;default:2;case 1:3;}'));
            console.log(eval('switch(2){case 0:1;default:2;case 1:3;}'));
            console.log(eval('switch(0){case 0:1;break;default:2;case 1:3;}'));
            console.log(eval('switch(1){default:2;break;case 1:3;}'));
            console.log(eval('switch(1){default:2;case 1:;}'));
            console.log(eval('9;switch(2){case 0:1;default:;case 1:;}'));
            """, "3\n3\n3\n1\n3\nundefined\nundefined\n"];
        yield return ["case-null-is-not-default", """
            function run(value:number|null):void{
                switch(value){default:console.log('default');break;case null:console.log('null');break;case 1:console.log('one');}
            }
            run(null);run(1);run(2);
            """, "null\none\ndefault\n"];
    }

    public static IEnumerable<object[]> RuntimeCases() => Programs().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[1], row[2], mode }));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void DefaultPreservesSourcePositionAndCaseSelection(string source, string expected, ExecutionMode mode)
        => Assert.Equal(expected, TestHarness.Run(source, mode));

    [Theory, ModeData]
    public void ModuleExecutionPreservesCaseEvaluationOrder(ExecutionMode mode)
    {
        var program = Programs().Single(row => (string)row[0] == "case-effects-stop-after-match");
        var files = new Dictionary<string, string> { ["main.ts"] = (string)program[1] };
        Assert.Equal((string)program[2], TestHarness.RunModules(files, "main.ts", mode));
    }
}
