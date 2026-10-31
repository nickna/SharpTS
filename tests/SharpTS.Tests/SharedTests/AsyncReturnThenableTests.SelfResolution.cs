using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class AsyncReturnThenableTests
{
    public static IEnumerable<object[]> SelfResolutionCases()
    {
        yield return ["returned-thenable-self-cycle", """
            function start() {
                let pending: any;
                const value: any = { then(resolve: any) { resolve(pending); } };
                async function answer(): Promise<any> { return value; }
                pending = answer();
                pending.catch((error: any) => console.log(error.name));
                setTimeout(() => console.log('done'), 1);
            }
            start();
            """, "TypeError\ndone\n"];
        yield return ["nested-self-cycle-callable-forms", """
            function start(kind: number) {
                let pending: any;
                const value: any = { then(resolve: any) {
                    resolve({ then(again: any) { again(pending); } });
                } };
                async function declared(): Promise<any> { return value; }
                const ordinary = async function(): Promise<any> { return value; };
                const block = async (): Promise<any> => { return value; };
                const expression = async (): Promise<any> => value;
                const forms = [declared, ordinary, block, expression];
                const selected = forms[kind];
                pending = selected();
                return pending.then(() => false, (error: any) => error instanceof TypeError);
            }
            const checks: any[] = [];
            for (let kind = 0; kind < 4; kind++) checks.push(start(kind));
            Promise.all(checks).then(results => console.log(results.length, results.every(value => value)));
            """, "4 true\n"];
        yield return ["independent-invocations-and-first-settlement", """
            function start() {
                const marker = {}, state: any = { promises: [] };
                async function answer(index: number): Promise<any> {
                    return { then(resolve: any, reject: any) {
                        if (index === 0) {
                            resolve({ then(again: any) { again(state.promises[0]); } });
                            reject(marker); throw marker;
                        }
                        resolve(47); resolve(state.promises[1]); reject(marker);
                    } };
                }
                state.promises[0] = answer(0);
                state.promises[1] = answer(1);
                const rejected = state.promises[0].then(() => false, (error: any) => error instanceof TypeError);
                Promise.all([rejected, state.promises[1]]).then(results => console.log(results[0], results[1]));
            }
            start();
            """, "true 47\n"];
        yield return ["self-cycle-before-poisoned-then-read", """
            function start() {
                const marker = {}, state = { reads: 0 };
                let pending: any;
                const value: any = { then(resolve: any) { resolve(pending); } };
                async function answer(): Promise<any> { return value; }
                pending = answer();
                Object.defineProperty(pending, 'then', { get() { state.reads++; throw marker; } });
                const prototype: any = Promise.prototype;
                prototype.then.call(pending, undefined, (error: any) => {
                    console.log(error instanceof TypeError, state.reads);
                });
            }
            start();
            """, "true 0\n"];
        yield return ["nested-thenable-public-self", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve({then(inner:any){inner(pending);}});}};async function answer():Promise<any>{return value;}pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["arrow-public-self", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve(pending);}};const answer=async()=>value;pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["object-method-public-self", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve(pending);}};const api={async answer(){return value;}};pending=api.answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["generic-class-method-public-self", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve(pending);}};class C<T>{async answer():Promise<any>{return value;}}pending=new C<number>().answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["direct-self-after-await", """
            function start(){let pending:any;async function answer():Promise<any>{await 0;return pending;}pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["first-resolution-wins", """
            function start(){let pending:any;const marker={},value:any={then(resolve:any,reject:any){resolve(7);resolve(pending);reject(marker);throw marker;}};async function answer():Promise<any>{return value;}pending=answer();pending.then((value:any)=>console.log(value),(error:any)=>console.log('unexpected',error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "7\ndone\n"];
        yield return ["abrupt-then-getter", """
            function start(){const marker={},value:any={get then(){throw marker;}};async function answer():Promise<any>{return value;}answer().catch((error:any)=>console.log(error===marker));setTimeout(()=>console.log('done'),1);}start();
            """, "true\ndone\n"];
        yield return ["nested-independent-promise", """
            function start(){const other=Promise.resolve(9),value:any={then(resolve:any){resolve({then(inner:any){inner(other);}});}};async function answer():Promise<any>{return value;}answer().then(value=>console.log(value));setTimeout(()=>console.log('done'),1);}start();
            """, "9\ndone\n"];
        yield return ["self-rejection-before-then-get", """
            function start(){let pending:any,reads=0;const marker={},value:any={then(resolve:any){resolve(pending);}};async function answer():Promise<any>{return value;}pending=answer();pending.catch((error:any)=>console.log(error.name));Object.defineProperty(pending,'then',{get(){reads++;throw marker;},configurable:true});setTimeout(()=>console.log('done',reads),1);}start();
            """, "TypeError\ndone 0\n"];
        yield return ["cycle-first-settlement-wins", """
            function start(){let pending:any;const marker={},value:any={then(resolve:any,reject:any){resolve(pending);reject(marker);throw marker;}};async function answer():Promise<any>{return value;}pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
        yield return ["cycle-after-real-timer-suspension", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve(pending);}};async function answer():Promise<any>{await new Promise<void>(resolve=>setTimeout(resolve,1));return value;}pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),20);}start();
            """, "TypeError\ndone\n"];
        yield return ["self-boxed-outer-with-async-arrow", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve(pending);}};async function answer():Promise<any>{const inner=async()=>1;console.log(typeof inner);return value;}pending=answer();pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "function\nTypeError\ndone\n"];
        yield return ["ordinary-promise-resolve-root-unchanged", """
            function start(){let pending:any;const value:any={then(resolve:any){resolve({then(again:any){again(pending);}});}};pending=Promise.resolve(value);pending.catch((error:any)=>console.log(error.name));setTimeout(()=>console.log('done'),1);}start();
            """, "TypeError\ndone\n"];
    }

    public static IEnumerable<object[]> SelfResolutionModes() => SelfResolutionCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(SelfResolutionModes))]
    public void ReturnedThenableCyclesRejectThePublicPromise(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
