using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IteratorCloseResultTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-bigint", """
            const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return 1n;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "original-symbol", """
            const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return Symbol("result");}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "original-number", """
            const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return 3;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "other-primitives", """
            function check(value:any){const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){return value;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}}
            check(null);check(undefined);check(false);check("");check(0n);
            """, "TypeError\nTypeError\nTypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "valid-objects", """
            function check(value:any){const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){return value;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}}
            check({});check([]);check(function(){});
            """, "accepted\naccepted\naccepted\n" };
        yield return new object[] { "throw-bigint", """
            const marker={tag:7};let closes=0;const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){closes++;return 1n;}};try{for(const n of it)throw marker;}catch(e:any){console.log(e===marker,closes);}
            """, "true 1\n" };
        yield return new object[] { "throw-symbol", """
            const marker={tag:7};let closes=0;const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){closes++;return Symbol("close");}};try{for(const n of it)throw marker;}catch(e:any){console.log(e===marker,closes);}
            """, "true 1\n" };
        yield return new object[] { "return-completion", """
            const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){return 1n;}};function run(){for(const n of it)return 9;}try{console.log(run());}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "getter-receiver", """
            let gets=0;let calls=0;const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},get return(){gets++;return function(){calls++;console.log(this===it);return Symbol("close");};}};try{for(const n of it)break;}catch(e:any){console.log(e.name,gets,calls);}
            """, "true\nTypeError 1 1\n" };
        yield return new object[] { "getter-throw-precedence", """
            const marker={tag:7};const closingError={tag:9};const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},get return(){throw closingError;}};try{for(const n of it)throw marker;}catch(e:any){console.log(e===marker);}try{for(const n of it)break;}catch(e:any){console.log(e===closingError);}
            """, "true\ntrue\n" };
        yield return new object[] { "method-throw-precedence", """
            const marker={tag:7};const closingError={tag:9};const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){throw closingError;}};try{for(const n of it)throw marker;}catch(e:any){console.log(e===marker);}try{for(const n of it)break;}catch(e:any){console.log(e===closingError);}
            """, "true\ntrue\n" };
    }

    // This consumer materializes the entire iterable before reaching Close.
    // Retained separately from the shared close primitive's passing coverage.
    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "destructure-close-independent", """
            const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return Symbol("close");}};try{const [value]=it;console.log(value);}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledCloseValidatesObjectsAndPreservesIncomingThrow(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
