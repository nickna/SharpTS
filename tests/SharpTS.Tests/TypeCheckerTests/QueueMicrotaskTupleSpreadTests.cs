using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class QueueMicrotaskTupleSpreadTests
{
    public static IEnumerable<object[]> ValidCalls()
    {
        yield return ["original", "export {};const callbacks:[()=>void]=[()=>console.log('job')];queueMicrotask(...callbacks);console.log('sync');"];
        yield return ["readonly", "export {};const args:readonly [()=>void]=[()=>{}];queueMicrotask(...args);"];
        yield return ["inline-const", "export {};queueMicrotask(...[()=>7] as const);"];
        yield return ["inline-array", "export {};queueMicrotask(...[()=>7]);"];
        yield return ["nested-spread", "export {};const args:[()=>void]=[()=>{}];queueMicrotask(...[...args]);"];
        yield return ["empty-prefix", "export {};const empty:[]=[];const args:[()=>void]=[()=>{}];queueMicrotask(...empty,...args);"];
        yield return ["empty-suffix", "export {};const empty:[]=[];const args:[()=>void]=[()=>{}];queueMicrotask(...args,...empty);"];
        yield return ["async-return", "export {};queueMicrotask(...[async()=>7] as const);"];
        yield return ["generic-callable", "export {};function callback<T>():T{throw 0;}queueMicrotask(...[callback] as const);"];
        yield return ["callable-interface", "export {};interface Callback{():number;}declare const callback:Callback;const args:[Callback]=[callback];queueMicrotask(...args);"];
        yield return ["optional-function-param", "export {};queueMicrotask(...[(value?:number)=>{}] as const);"];
        yield return ["any-callback", "export {};declare const callback:any;const args:[any]=[callback];queueMicrotask(...args);"];
        yield return ["local-shadow", "export {};const queueMicrotask=(value:number)=>value;const args:[number]=[7];const n:number=queueMicrotask(...args);"];
        yield return ["parameter-shadow", "export {};function run(queueMicrotask:(value:number,name:string)=>void){const args:[number,string]=[7,\"x\"];queueMicrotask(...args);}"];
        yield return ["block-shadow", "export {};queueMicrotask(()=>{});{const queueMicrotask=(value:number)=>value;const args:[number]=[7];const n:number=queueMicrotask(...args);}"];
        yield return ["direct-value-return", "export {};queueMicrotask(()=>7);"];
    }

    [Theory, MemberData(nameof(ValidCalls))]
    public void CheckedTupleSlotsSupplyTheSingleCallback(string name, string source)
    {
        Assert.NotEmpty(name);
        Assert.Empty(Check(source).Diagnostics);
    }

    public static IEnumerable<object[]> InvalidCalls()
    {
        yield return ["empty-tuple", "export {};const args:[]=[];queueMicrotask(...args);", new string[] { "TS2554" }];
        yield return ["extra-tuple", "export {};const args:[()=>void,()=>void]=[()=>{},()=>{}];queueMicrotask(...args);", new string[] { "TS2554" }];
        yield return ["numeric-tuple", "export {};const args:[number]=[7];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["numeric-inline-array", "export {};queueMicrotask(...[7]);", new string[] { "TS2345" }];
        yield return ["non-iterable", "export {};queueMicrotask(...7);", new string[] { "TS2556", "TS2488" }];
        yield return ["open-array", "export {};const args:(()=>void)[]=[()=>{}];queueMicrotask(...args);", new string[] { "TS2556" }];
        yield return ["optional-tuple", "export {};declare const args:[callback?:()=>void];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["open-tail", "export {};declare const args:[()=>void,...(()=>void)[]];queueMicrotask(...args);", new string[] { "TS2556" }];
        yield return ["missing-direct", "export {};queueMicrotask();", new string[] { "TS2554" }];
        yield return ["extra-direct", "export {};queueMicrotask(()=>{},()=>{});", new string[] { "TS2554" }];
        yield return ["required-callback-param", "export {};queueMicrotask(...[(value:number)=>{}] as const);", new string[] { "TS2345" }];
        yield return ["unknown-callback", "export {};declare const callback:unknown;const args:[unknown]=[callback];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["construct-only", "export {};class Callback{}const args:[typeof Callback]=[Callback];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["shadow-wrong-element", "export {};const queueMicrotask=(value:number)=>value;const args:[string]=[\"x\"];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["null-callback", "export {};const args:[null]=[null];queueMicrotask(...args);", new string[] { "TS2345" }];
        yield return ["direct-required-parameter", "export {};queueMicrotask((value:number)=>{});", new string[] { "TS2345" }];
    }

    [Theory, MemberData(nameof(InvalidCalls))]
    public void ArityCallabilityAndShadowedSignaturesRemainChecked(string name, string source, string[] codes)
    {
        Assert.NotEmpty(name);
        Assert.Contains(Check(source).Diagnostics, diagnostic => codes.Contains(diagnostic.TsCode));
    }

    [Fact]
    public void VoidCallbackValidationRetainsItsAlreadyCheckedReturnType()
    {
        var statements = new Parser(new Lexer("queueMicrotask(() => 7);").ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var call = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(Assert.Single(statements)).Expr);
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.Single(call.Arguments));
        var callback = Assert.IsType<TypeInfo.Function>(result.TypeMap.Get(arrow));
        Assert.IsNotType<TypeInfo.Void>(callback.ReturnType);
    }

    private static TypeCheckDiagnosticResult Check(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
    }
}
