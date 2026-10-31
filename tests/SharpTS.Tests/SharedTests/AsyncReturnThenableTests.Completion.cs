using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class AsyncReturnThenableTests
{
    private const string BoundaryCatchFinallySource = """
        const token: any = {}, events: string[] = [];
        function make(label: string, kind: string): any {
            const value: any = {};
            Object.defineProperty(value, "then", {
                get() {
                    events.push(label + ":get");
                    if (kind === "getter") throw token;
                    return function(resolve: any, reject: any) {
                        events.push(label + ":call");
                        if (kind === "reject") reject(token);
                        else resolve(label);
                    };
                }
            });
            return value;
        }
        async function declared(value: any, label: string): Promise<any> {
            try { return value; }
            catch (error) { events.push(label + ":caught"); return "caught"; }
            finally { events.push(label + ":finally"); }
        }
        async function awaited(value: any, label: string): Promise<any> {
            try { return await value; }
            catch (error) { events.push(label + ":caught"); return "caught"; }
            finally { events.push(label + ":finally"); }
        }
        const block = async (value: any, label: string): Promise<any> => {
            try { return value; }
            catch (error) { events.push(label + ":caught"); return "caught"; }
            finally { events.push(label + ":finally"); }
        };
        async function pendingFinally(value: any, label: string): Promise<any> {
            try { return value; }
            catch (error) { events.push(label + ":caught"); return "caught"; }
            finally {
                events.push(label + ":before");
                await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                events.push(label + ":after");
            }
        }
        async function check(operation: any, label: string, kind: string) {
            events.length = 0;
            try { console.log("value", await operation(make(label, kind), label)); }
            catch (error) { console.log("outer", error === token); }
            console.log(events.join(","));
        }
        async function run() {
            await check(declared, "declared-getter", "getter");
            await check(declared, "declared-reject", "reject");
            await check(awaited, "awaited-getter", "getter");
            await check(awaited, "awaited-reject", "reject");
            await check(block, "block-reject", "reject");
            await check(pendingFinally, "pending-resolve", "resolve");
            await check(pendingFinally, "pending-reject", "reject");
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    private const string PrimitivePrototypeSource = """
        let reads=0;
        for (const prototype of [Number.prototype, String.prototype, Boolean.prototype, BigInt.prototype, Symbol.prototype]) {
            Object.defineProperty(prototype, "then", { configurable:true, get() { reads++; throw new Error("primitive then read"); } });
        }
        async function pass(value:any):Promise<any> { return value; }
        async function run() {
            for (const value of [7,"ok",false,0n,Symbol("x"),null,undefined]) {
                const result=await pass(value);
                console.log(typeof result, result===value);
            }
            console.log("reads",reads);
        }
        run().catch((error:any)=>console.log("unexpected",error.message));
        """;

    private const string SelfResolutionSource = """
        let declaredPromise:any, arrowPromise:any;
        async function declared():Promise<any> {
            await new Promise<number>(resolve=>setTimeout(()=>resolve(0),1));
            return declaredPromise;
        }
        const arrow = async (): Promise<any> => {
            await new Promise<number>(resolve=>setTimeout(()=>resolve(0),1));
            return arrowPromise;
        };
        async function run() {
            declaredPromise=declared();
            try {await declaredPromise; console.log("declared fulfilled");}
            catch(error) {console.log("declared",error instanceof TypeError);}
            arrowPromise=arrow();
            try {await arrowPromise; console.log("arrow fulfilled");}
            catch(error) {console.log("arrow",error instanceof TypeError);}
        }
        run().catch((error:any)=>console.log("unexpected",error.name,error.message));
        """;

    private const string NativeOwnThenSource = """
        const events:string[]=[];
        const native:any=Promise.resolve(10);
        Object.defineProperty(native,"then",{get(){events.push("get");return function(resolve:any){events.push("call");resolve(47);};}});
        async function pass():Promise<any>{return native;}
        const result=pass();
        events.push("after");
        result.then(value=>console.log(value,events.join(",")));
        """;

    private const string ForwardedPrimitiveCoreSource = """
        function raw():number {
            return {then(resolve:any) { resolve(47); }} as any;
        }
        async function wrapper():Promise<number> { return raw(); }
        wrapper().then(value=>console.log(value));
        """;

    private const string ForwardedStringCoreSource = """
        function raw():string {return {then(resolve:any) {resolve("ok");}} as any;}
        async function wrapper():Promise<string> {return raw();}
        async function ordinary():Promise<string> {return "plain";}
        async function run() {console.log(await ordinary(),await wrapper());}
        run();
        """;

    private const string PrimitiveAnnotationFinallySource = """
        const events:string[]=[], object:any={};
        const value=Promise.resolve(object) as Promise<number>;
        async function pass():Promise<number> {
            try {return await value;}
            finally {
                events.push("finally");
                object.then=(resolve:any)=>{events.push("then");resolve(47);};
            }
        }
        pass().then(result=>console.log(result,events.join(",")));
        """;

    private const string RawChainHandlerFinallySource = """
        const events:string[]=[], object:any={};
        function raw():number {return object;}
        async function pass():Promise<number> {
            let chain: Promise<number> = Promise.resolve(0);
            chain = chain.then((_value: number): number => raw());
            try {return await chain;}
            finally {
                events.push("finally");
                object.then=(resolve:any)=>{events.push("then");resolve(47);};
            }
        }
        pass().then(result=>console.log(result,events.join(",")));
        """;

    private const string RawChainHandlerGetterSource = """
        let reads=0;
        function raw():number {
            return {get then():any {
                reads++;
                return reads===1 ? undefined : (resolve:any)=>resolve(47);
            }} as any;
        }
        async function pass():Promise<number> {
            let chain: Promise<number> = Promise.resolve(0);
            chain = chain.then((_value: number): number => raw());
            return await chain;
        }
        pass().then(value=>console.log(typeof value,value===47,reads));
        """;

    private const string LockedAdoptionSource = """
        // Compiler-only @lock control, following existing LockDecoratorTests conventions.
        const token:any={}, events:string[]=[];
        let rejectFirst:any;
        function make(label:string):any {
            return { get then() {
                events.push("get:"+label);
                return function(resolve:any,reject:any) {
                    events.push("then:"+label);
                    if(label==="first") rejectFirst=reject;
                    else resolve(label);
                };
            }};
        }
        class Locked {
            @lock
            async read(label:string):Promise<any> {
                events.push("body:"+label);
                return make(label);
            }
        }
        async function run() {
            const owner=new Locked();
            const first=owner.read("first");
            const second=owner.read("second");
            console.log(await second);
            rejectFirst(token);
            try {await first;} catch(error) {console.log(error===token);}
            console.log(await owner.read("third"));
            console.log(events.join(","));
        }
        run().catch((error:any)=>console.log("unexpected",error.name,error.message));
        """;

    private static IEnumerable<object[]> CompletionCases()
    {
        yield return ["boundary-catch-finally", BoundaryCatchFinallySource, "outer true\ndeclared-getter:finally,declared-getter:get\nouter true\ndeclared-reject:finally,declared-reject:get,declared-reject:call\nvalue caught\nawaited-getter:get,awaited-getter:caught,awaited-getter:finally\nvalue caught\nawaited-reject:get,awaited-reject:call,awaited-reject:caught,awaited-reject:finally\nouter true\nblock-reject:finally,block-reject:get,block-reject:call\nvalue pending-resolve\npending-resolve:before,pending-resolve:after,pending-resolve:get,pending-resolve:call\nouter true\npending-reject:before,pending-reject:after,pending-reject:get,pending-reject:call\n"];
        yield return ["primitive-prototype-no-read", PrimitivePrototypeSource, "number true\nstring true\nboolean true\nbigint true\nsymbol true\nobject true\nundefined true\nreads 0\n"];
        yield return ["native-own-then", NativeOwnThenSource, "47 get,after,call\n"];
        yield return ["forwarded-primitive-core", ForwardedPrimitiveCoreSource, "47\n"];
        yield return ["forwarded-string-core", ForwardedStringCoreSource, "plain ok\n"];
        yield return ["primitive-annotation-finally", PrimitiveAnnotationFinallySource, "47 finally,then\n"];
        yield return ["raw-chain-handler-finally", RawChainHandlerFinallySource, "47 finally,then\n"];
        yield return ["raw-chain-handler-getter", RawChainHandlerGetterSource, "number true 2\n"];
    }

    public static IEnumerable<object[]> CompiledCases()
    {
        const string expected = "second\ntrue\nthird\nbody:first,get:first,body:second,get:second,then:first,then:second,body:third,get:third,then:third\n";
        yield return ["locked-instance-adoption", LockedAdoptionSource, expected];
        yield return ["locked-static-adoption", LockedAdoptionSource
            .Replace("async read(", "static async read(")
            .Replace("const owner=new Locked();", "const owner=Locked;"), expected];
        yield return ["locked-instance-alias-adoption", LockedAdoptionSource
            .Replace("const owner=new Locked();", "const owner=new Locked(); const read:any=owner.read;")
            .Replace("owner.read(", "read.call(owner,"), expected];
        yield return ["locked-instance-generic-alias-adoption", LockedAdoptionSource
            .Replace("class Locked {", "class Locked<T> {")
            .Replace("const owner=new Locked();", "const owner=new Locked<number>(); const read:any=owner.read;")
            .Replace("owner.read(", "read.call(owner,"), expected];
        var staticAlias = LockedAdoptionSource
            .Replace("async read(", "static async read(")
            .Replace("const owner=new Locked();", "const owner=Locked; const read:any=owner.read;")
            .Replace("owner.read(", "read.call(owner,");
        yield return ["locked-static-alias-adoption", staticAlias, expected];
        yield return ["locked-static-generic-alias-adoption", staticAlias.Replace("class Locked {", "class Locked<T> {"), expected];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        foreach (var row in Cases()) yield return row;
        foreach (var row in CompiledCases()) yield return row;
        yield return ["self-resolution", SelfResolutionSource, "declared true\narrow true\n"];
        yield return ["preservation", PreservationSource, PreservationExpected];
        yield return ["private-field", PrivateCallableFieldTests.AsyncReturnThenableSource, "47\n"];
    }

    [Theory, MemberData(nameof(CompiledCases))]
    public void LockedAsyncReturnsReleaseBeforeAdoptingTheirResult(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunCompiled(source, DecoratorMode.Stage3));
    }

    [Theory, ModeData]
    public void SelfResolutionRejectsTheCurrentPublicTask(ExecutionMode mode)
    {
        Assert.Equal("declared true\narrow true\n", TestHarness.Run(SelfResolutionSource, mode));
    }
}
