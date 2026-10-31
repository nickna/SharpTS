using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterParameterDefaultTdzTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-callable-forms", """
            const later=99;
            function invoke(read:()=>any):any{return read();}
            function declaration(first:any=invoke(()=>later),later:number=2){}
            const expression=function(first:any=invoke(()=>later),later:number=2){};
            const arrow=(first:any=invoke(()=>later),later:number=2)=>{};
            class Owner{method(first:any=invoke(()=>later),later:number=2){}}
            for(const call of [()=>declaration(),()=>expression(),()=>arrow(),()=>new Owner().method()]){
                try{call();}catch(error:any){console.log(error.name);}
            }
            console.log(later);
            """, "ReferenceError\nReferenceError\nReferenceError\nReferenceError\n99\n"];
        yield return ["constructor-and-runtime-parent", """
            const later=99;
            function invoke(read:()=>any):any{return read();}
            class Base{constructor(public first:any=invoke(()=>later),later:number=2){}}
            const RuntimeBase:typeof Base=Base;
            class Derived extends RuntimeBase{}
            for(const call of [()=>new Base(),()=>new Derived()]){
                try{call();}catch(error:any){console.log(error.name);}
            }
            console.log(later);
            """, "ReferenceError\nReferenceError\n99\n"];
        yield return ["future-pattern-bindings", """
            const later=99;
            function invoke(read:()=>any):any{return read();}
            function objectCase(first:any=invoke(()=>later),{later}:{later:number}={later:2}){}
            function arrayCase(first:any=invoke(()=>later),[later]:[number]=[2]){}
            for(const call of [()=>objectCase(),()=>arrayCase()]){
                try{call();}catch(error:any){console.log(error.name);}
            }
            console.log(later);
            """, "ReferenceError\nReferenceError\n99\n"];
        yield return ["early-write-and-typeof", """
            let later=99;
            function invoke(read:()=>any):any{return read();}
            function write(first:any=invoke(()=>later=3),later:number=2){}
            function inspect(first:any=invoke(()=>typeof later),later:number=2){}
            for(const call of [()=>write(),()=>inspect()]){
                try{call();}catch(error:any){console.log(error.name);}
            }
            console.log(later);
            """, "ReferenceError\nReferenceError\n99\n"];
        yield return ["abrupt-default-leaves-captured-binding-uninitialized", """
            const later=99;let leaked:any;
            function capture(read:()=>number):any{leaked=read;throw 0;}
            function run(first:any=capture(()=>later),later:number=2){}
            try{run();}catch(error:any){console.log(error);}
            try{leaked();}catch(error:any){console.log(error.name);}
            function valid(read:()=>number=()=>later,later:number=2){later=3;return read;}
            console.log(valid()(),later);
            """, "0\nReferenceError\n3 99\n"];
        yield return ["async-callable-default-rejections", """
            const later=99;
            function invoke(read:()=>any):any{return read();}
            async function declaration(first:any=invoke(()=>later),later:number=2){return first;}
            const expression=async function(first:any=invoke(()=>later),later:number=2){return first;};
            const arrow=async(first:any=invoke(()=>later),later:number=2)=>first;
            async function main(){
                for(const call of [()=>declaration(),()=>expression(),()=>arrow()]){
                    try{await call();}catch(error:any){console.log(error.name);}
                }
                console.log(later);
            }
            main();
            """, "ReferenceError\nReferenceError\nReferenceError\n99\n"];
        yield return ["generator-defaults-run-before-iterator-creation", """
            const later=99;let bodies=0;
            function invoke(read:()=>any):any{return read();}
            function* generator(first:any=invoke(()=>later),later:number=2){bodies++;yield first;}
            async function* asyncGenerator(first:any=invoke(()=>later),later:number=2){bodies++;yield first;}
            for(const call of [()=>generator(),()=>asyncGenerator()]){
                try{call();console.log('created');}catch(error:any){console.log(error.name);}
            }
            console.log(bodies,later);
            """, "ReferenceError\nReferenceError\n0 99\n"];
        yield return ["sloppy-pattern-arguments-binding", """
            function invoke(read:()=>any):any{return read();}
            function early(first:any=invoke(()=>arguments),{value:arguments}:any={value:2}){}
            try{early();}catch(error:any){console.log(error.name);}
            function valid(read:()=>any=()=>arguments,{value:arguments}:any={value:2}){return read();}
            console.log(valid());
            """, "ReferenceError\n2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ParametersOwnTheirInitializationBoundary(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
