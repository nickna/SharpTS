using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class SuspendingParameterDefaultTdzTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-generator-and-async-outer-shadow", """
            const later=99;
            function invoke(read:()=>number):number{return read();}
            function* values(first:number=invoke(()=>later),later:number=2){yield first;}
            try{const iterator=values();console.log('created',iterator.next().value);}catch(error:any){console.log(error.name);}
            async function run(first:number=invoke(()=>later),later:number=3){return first;}
            run().then(value=>console.log('resolved',value),error=>console.log(error.name));
            """, "ReferenceError\nReferenceError\n"];
        yield return ["async-arrow-and-method-early-writes", """
            let later=99;let bodies=0;
            function invoke(read:()=>number):number{return read();}
            const arrow=async(first:number=invoke(()=>{later=7;return later;}),later:number=2)=>{bodies++;return first;};
            class Owner{async run(first:number=invoke(()=>later),later:number=3){bodies++;return first;}}
            (async()=>{
                try{await arrow();}catch(error:any){console.log(error.name);}
                try{await new Owner().run();}catch(error:any){console.log(error.name);}
                console.log(later,bodies);
            })();
            """, "ReferenceError\nReferenceError\n99 0\n"];
        yield return ["async-generator-default-is-eager", """
            const later=99;
            function invoke(read:()=>number):number{return read();}
            async function* values(first:number=invoke(()=>later),later:number=2){yield first;}
            try{values();console.log('created');}catch(error:any){console.log(error.name);}
            """, "ReferenceError\n"];
        yield return ["live-cells-survive-generator-resume", """
            function* values(read:()=>number=()=>later,later:number=2){yield read();later+=3;yield read();}
            console.log([...values()].join(','));
            async function* asyncValues(read:()=>number=()=>later,later:number=3){yield read();await Promise.resolve();later+=3;yield read();}
            (async()=>{const result:number[]=[];for await(const value of asyncValues())result.push(value);console.log(result.join(','));})();
            """, "2,5\n3,6\n"];
        yield return ["independent-eager-generator-cells", """
            let read:any;
            function* values(value:number=2,capture:any=(read=()=>value)){yield value;value+=3;yield value;}
            const first=values();const firstRead=read;const second=values(7);const secondRead=read;
            console.log(firstRead(),secondRead());first.next();first.next();console.log(firstRead(),secondRead());
            second.next();second.next();console.log(firstRead(),secondRead());
            """, "2 7\n5 7\n5 10\n"];
        yield return ["future-pattern-cells-survive-suspension", """
            function* values(read:()=>number=()=>later,[later]:[number]=[2]){yield read();later++;yield read();}
            console.log([...values()].join(','));
            async function run(read:()=>number=()=>later,{later}:{later:number}={later:3}){await Promise.resolve();later++;return read();}
            run().then(value=>console.log(value));
            """, "2,3\n4\n"];
        yield return ["generator-expression-parameter-environment", """
            const values=function*(read:()=>number=()=>later,later:number=2){yield read();later++;yield read();};
            console.log([...values()].join(','));
            const asyncValues=async function*(read:()=>number=()=>later,later:number=3){yield read();later++;yield read();};
            (async()=>{const result:number[]=[];for await(const value of asyncValues())result.push(value);console.log(result.join(','));})();
            """, "2,3\n3,4\n"];
        yield return ["body-shadows-do-not-replace-resumed-parameter", """
            function* values(read:()=>number=()=>later,later:number=2){
                {let later=9;const shadow=()=>later;yield shadow();}later+=3;yield read();
            }
            console.log([...values()].join(','));
            async function run(read:()=>number=()=>later,later:number=3){
                {let later=9;const shadow=()=>later;await Promise.resolve();console.log(shadow());}
                later+=3;return read();
            }
            run().then(value=>console.log(value));
            """, "9,5\n9\n6\n"];
        yield return ["abrupt-default-keeps-escaped-cell-uninitialized", """
            const later=99;let leaked:any;
            function capture(read:()=>number):any{leaked=read;throw 'token';}
            function* values(first:any=capture(()=>later),later:number=2){yield first;}
            try{values();}catch(error:any){console.log(error);}
            try{leaked();}catch(error:any){console.log(error.name);}
            async function run(first:any=capture(()=>later),later:number=3){return first;}
            (async()=>{
                try{await run();}catch(error:any){console.log(error);}
                try{leaked();}catch(error:any){console.log(error.name);}
            })();
            """, "token\nReferenceError\ntoken\nReferenceError\n"];
        yield return ["supplied-arguments-skip-early-defaults", """
            const later=99;let calls=0;
            function invoke(read:()=>number):number{calls++;return read();}
            function* values(first:number=invoke(()=>later),later:number=2){yield first;yield later;}
            console.log([...values(7,8)].join(','));
            async function run(first:number=invoke(()=>later),later:number=3){return [first,later];}
            run(9,10).then(value=>console.log(value.join(','),calls));
            """, "7,8\n9,10 0\n"];
        yield return ["async-default-cells-initialize-once", """
            let defaults=0;
            async function run(read:()=>number=(defaults++,()=>later),later:number=2){
                await Promise.resolve();later++;await Promise.resolve();return read();
            }
            async function* values(read:()=>number=(defaults++,()=>later),later:number=3){
                yield read();await Promise.resolve();later++;yield read();
            }
            (async()=>{
                console.log(await run(),defaults);
                const result:number[]=[];for await(const value of values())result.push(value);
                console.log(result.join(','),defaults);
            })();
            """, "3 1\n3,4 2\n"];
        yield return ["generic-owner-keeps-actual-iterator-cell", """
            class Owner<T>{
                constructor(public marker:T){}
                *values(read:()=>number=()=>later,later:number=2){yield read();later++;yield read();}
                async *asyncValues(read:()=>number=()=>later,later:number=3){yield read();await Promise.resolve();later++;yield read();}
                async get(read:()=>T=()=>later,later:T=null!){await Promise.resolve();return read();}
            }
            const owner=new Owner<string>('tag');console.log([...owner.values()].join(','));
            (async()=>{
                const result:number[]=[];for await(const value of owner.asyncValues())result.push(value);
                console.log(result.join(','));console.log(await owner.get(undefined,'ok'),await new Owner<number>(0).get(undefined,7));
            })();
            """, "2,3\n3,4\nok 7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CapturedParametersKeepTheirInitializationBoundary(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
