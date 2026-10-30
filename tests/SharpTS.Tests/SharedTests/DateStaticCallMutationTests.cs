using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DateStaticCallMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [ExecutionMode.Interpreted, "reported-three-static-replacements", """
            const D:any=Date;D.now=()=>7;D.UTC=()=>8;D.parse=()=>9;console.log(Date.now()===7,Date.UTC(2000,0)===8,Date.parse('2000-01-01')===9);
            """, "true true true\n"];
        yield return [ExecutionMode.Compiled, "reported-three-static-replacements", """
            const D:any=Date;D.now=()=>7;D.UTC=()=>8;D.parse=()=>9;console.log(Date.now()===7,Date.UTC(2000,0)===8,Date.parse('2000-01-01')===9);
            """, "true true true\n"];
        yield return [ExecutionMode.Interpreted, "direct-calls-after-delete", """
            function rejected(body:()=>unknown){try{body();return false;}catch(error){return error instanceof TypeError;}}const D:any=Date;delete D.now;delete D.UTC;delete D.parse;console.log(rejected(()=>Date.now()),rejected(()=>Date.UTC(2000,0)),rejected(()=>Date.parse('2000-01-01')));
            """, "true true true\n"];
        yield return [ExecutionMode.Compiled, "direct-calls-after-delete", """
            function rejected(body:()=>unknown){try{body();return false;}catch(error){return error instanceof TypeError;}}const D:any=Date;delete D.now;delete D.UTC;delete D.parse;console.log(rejected(()=>Date.now()),rejected(()=>Date.UTC(2000,0)),rejected(()=>Date.parse('2000-01-01')));
            """, "true true true\n"];
        yield return [ExecutionMode.Interpreted, "getter-receiver-and-argument-order", """
            const D:any=Date,events:string[]=[];Object.defineProperty(D,'parse',{get(){events.push('get');return function(this:any,value:string){events.push(this===D?'this':'bad',value);return 7;};}});console.log(Date.parse((events.push('arg'),'text')),events.join(','));
            """, "7 get,arg,this,text\n"];
        yield return [ExecutionMode.Compiled, "getter-receiver-and-argument-order", """
            const D:any=Date,events:string[]=[];Object.defineProperty(D,'parse',{get(){events.push('get');return function(this:any,value:string){events.push(this===D?'this':'bad',value);return 7;};}});console.log(Date.parse((events.push('arg'),'text')),events.join(','));
            """, "7 get,arg,this,text\n"];
        yield return [ExecutionMode.Interpreted, "abrupt-getter-before-arguments", """
            const D:any=Date,events:string[]=[],marker={};Object.defineProperty(D,'parse',{get(){events.push('get');throw marker;}});try{Date.parse((events.push('arg'),'text'));}catch(error){console.log(error===marker,events.join(','));}
            """, "true get\n"];
        yield return [ExecutionMode.Compiled, "abrupt-getter-before-arguments", """
            const D:any=Date,events:string[]=[],marker={};Object.defineProperty(D,'parse',{get(){events.push('get');throw marker;}});try{Date.parse((events.push('arg'),'text'));}catch(error){console.log(error===marker,events.join(','));}
            """, "true get\n"];
        yield return [ExecutionMode.Interpreted, "noncallable-evaluates-arguments", """
            const D:any=Date,events:string[]=[];D.parse=1;try{Date.parse((events.push('arg'),'text'));}catch(error){console.log(error instanceof TypeError,events.join(','));}
            """, "true arg\n"];
        yield return [ExecutionMode.Compiled, "noncallable-evaluates-arguments", """
            const D:any=Date,events:string[]=[];D.parse=1;try{Date.parse((events.push('arg'),'text'));}catch(error){console.log(error instanceof TypeError,events.join(','));}
            """, "true arg\n"];
        yield return [ExecutionMode.Interpreted, "opaque-source-mutation", """
            function mutate(owner:any){owner.now=()=>7;}mutate(Date);console.log(Date.now());
            """, "7\n"];
        yield return [ExecutionMode.Compiled, "opaque-source-mutation", """
            function mutate(owner:any){owner.now=()=>7;}mutate(Date);console.log(Date.now());
            """, "7\n"];
        yield return [ExecutionMode.Interpreted, "raw-return-values", """
            const D:any=Date;D.now=()=> 'changed';const value:any=Date.now();console.log(value,Date.now()+1);D.UTC=()=>({tag:8});const record:any=Date.UTC(2000,0);console.log(record.tag);
            """, "changed changed1\n8\n"];
        yield return [ExecutionMode.Compiled, "raw-return-values", """
            const D:any=Date;D.now=()=> 'changed';const value:any=Date.now();console.log(value,Date.now()+1);D.UTC=()=>({tag:8});const record:any=Date.UTC(2000,0);console.log(record.tag);
            """, "changed changed1\n8\n"];
        yield return [ExecutionMode.Interpreted, "lexically-shadowed-date", """
            function read(Date:any){return Date.now();}console.log(read({now(){return 7;}}));{const Date={now(){return 8;}};console.log(Date.now());}
            """, "7\n8\n"];
        yield return [ExecutionMode.Compiled, "lexically-shadowed-date", """
            function read(Date:any){return Date.now();}console.log(read({now(){return 7;}}));{const Date={now(){return 8;}};console.log(Date.now());}
            """, "7\n8\n"];
        yield return [ExecutionMode.Interpreted, "captured-callee-before-argument-mutation", """
            const D:any=Date;console.log(Number.isNaN(Date.parse((D.parse=()=>7,'not a date'))),Date.parse('text'));
            """, "true 7\n"];
        yield return [ExecutionMode.Compiled, "captured-callee-before-argument-mutation", """
            const D:any=Date;console.log(Number.isNaN(Date.parse((D.parse=()=>7,'not a date'))),Date.parse('text'));
            """, "true 7\n"];
        yield return [ExecutionMode.Interpreted, "unmutated-static-control", """
            const now=Date.now();console.log(Number.isInteger(now),Date.UTC(2000,0)===946684800000,Date.parse('2000-01-01T00:00:00.000Z')===946684800000);
            """, "true true true\n"];
        yield return [ExecutionMode.Compiled, "unmutated-static-control", """
            const now=Date.now();console.log(Number.isInteger(now),Date.UTC(2000,0)===946684800000,Date.parse('2000-01-01T00:00:00.000Z')===946684800000);
            """, "true true true\n"];
        yield return [ExecutionMode.Interpreted, "raw-local-and-forwarded-result", """
            const D:any=Date;D.now=()=> 'changed';const value=Date.now();function read(){return Date.now();}console.log(value+1,read()+1);
            """, "changed1 changed1\n"];
        yield return [ExecutionMode.Compiled, "raw-local-and-forwarded-result", """
            const D:any=Date;D.now=()=> 'changed';const value=Date.now();function read(){return Date.now();}console.log(value+1,read()+1);
            """, "changed1 changed1\n"];
        yield return [ExecutionMode.Interpreted, "raw-bigint-operator-kind", """
            const D:any=Date;D.now=()=>1n;const value=Date.now();console.log(typeof value,typeof -Date.now());try{console.log(value+1);}catch(error){console.log(error instanceof TypeError);}
            """, "bigint bigint\ntrue\n"];
        yield return [ExecutionMode.Compiled, "raw-bigint-operator-kind", """
            const D:any=Date;D.now=()=>1n;const value=Date.now();console.log(typeof value,typeof -Date.now());try{console.log(value+1);}catch(error){console.log(error instanceof TypeError);}
            """, "bigint bigint\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void StaticCallsReadTheLiveReceiverBeforeArguments(ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
