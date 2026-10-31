using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPromiseAssimilation3207Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["foreign-resolve-original", "import * as vm from 'node:vm';const p:any=vm.runInNewContext('Promise.resolve(7)');const resolved=Promise.resolve(p);console.log(resolved===p);resolved.then((value:any)=>console.log(value));", "false\n7\n"];
        yield return ["foreign-rejection-undefined", "import * as vm from 'node:vm';const p:any=vm.runInNewContext('Promise.reject(undefined)');Promise.resolve(p).then(value=>console.log('wrong'),reason=>console.log(typeof reason));", "undefined\n"];
        yield return ["both-resolving-functions", "import * as vm from 'node:vm';const executor:any=vm.runInNewContext('(resolve,reject)=>{resolve(11);reject(12);resolve(13);}');new Promise(executor).then(value=>console.log(value),error=>console.log('wrong'));", "11\n"];
        yield return ["settled-fifo", "import * as vm from 'node:vm';const log:string[]=[];const p:any=vm.runInNewContext('Promise.resolve(7)');Promise.resolve(p).then(value=>log.push('outer'+value));Promise.resolve().then(()=>log.push('tick'));queueMicrotask(()=>log.push('queue'));log.push('sync');setTimeout(()=>console.log(log.join(',')),0);", "sync,tick,queue,outer7\n"];
        yield return ["unproved-invoke-is-not-admitted", "import * as vm from 'node:vm';let calls=0;class $PromiseResolveCallback{Invoke(...args:any[]){calls++;return 7;}}const inspect:any=vm.runInNewContext('(value)=>typeof value');console.log(inspect(new $PromiseResolveCallback()),calls);", "object 0\n"];
        yield return ["foreign-subclass-callback-metadata-original", "import * as vm from 'node:vm';\nconst ctx:any=vm.createContext({});\nconst p:any=vm.runInContext(`const log=[];class P extends Promise{then(resolve,reject){for(const fn of [resolve,reject]){let constructor=true;try{Reflect.construct(function(){},[],fn);}catch(e){constructor=false;}log.push([typeof fn,fn.name,fn.length,Object.prototype.hasOwnProperty.call(fn,'prototype'),constructor].join(':'));}return super.then(resolve,reject);}}P.resolve(7);`,ctx);\nPromise.resolve(p).then(value=>{console.log(vm.runInContext('log.join(\",\")',ctx));console.log(value);});", "function::1:false:false,function::1:false:false\n7\n"];
        yield return ["foreign-subclass-callback-metadata-closure", "import * as vm from 'node:vm';\nconst info:any=vm.runInNewContext(`const log=[];class P extends Promise{then(resolve,reject){for(const fn of [resolve,reject]){let constructor=true;try{Reflect.construct(function(){},[],fn);}catch(e){constructor=false;}log.push([typeof fn,fn.name,fn.length,Object.prototype.hasOwnProperty.call(fn,'prototype'),constructor].join(':'));}return super.then(resolve,reject);}}({promise:P.resolve(7),readLog(){return log.join(',');}});`);\nPromise.resolve(info.promise).then(value=>{console.log(info.readLog());console.log(value);},error=>console.log('error',error.name,error.message));", "function::1:false:false,function::1:false:false\n7\n"];
        yield return ["actual-resolve-callback-metadata", "import * as vm from 'node:vm';new Promise((resolve,reject)=>{for(const code of ['fn=>typeof fn','fn=>fn.name','fn=>fn.length','fn=>Object.prototype.hasOwnProperty.call(fn,\"prototype\")','fn=>{try{Reflect.construct(function(){},[],fn);return true;}catch(e){return false;}}']){try{const inspect:any=vm.runInNewContext(code);console.log(inspect(resolve));}catch(error){console.log('error',error.name,error.message);}}resolve(7);});", "function\n\n1\nfalse\nfalse\n"];
        yield return ["actual-reject-callback-metadata", "import * as vm from 'node:vm';new Promise((resolve,reject)=>{for(const code of ['fn=>typeof fn','fn=>fn.name','fn=>fn.length','fn=>Object.prototype.hasOwnProperty.call(fn,\"prototype\")','fn=>{try{Reflect.construct(function(){},[],fn);return true;}catch(e){return false;}}']){try{const inspect:any=vm.runInNewContext(code);console.log(inspect(reject));}catch(error){console.log('error',error.name,error.message);}}resolve(7);});", "function\n\n1\nfalse\nfalse\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void TransportsOnlyActualResolvingFunctions(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string,string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Compiled));
    }
}
