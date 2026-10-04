using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            namespace Values {export const value=3;}const values:any=Values;console.log(delete values.value,values.value===undefined);
            """, "true true\n" };
        yield return new object[] { "repeated-and-unrelated", """
            namespace Values {export const value=3;export const other=7;}const first:any=Values;const second:any=Values;console.log(delete first.value,delete first.value,second["value"]===undefined,second.other,first===second);
            """, "true true true 7 true\n" };
        yield return new object[] { "computed", """
            namespace Values {export const value=3;}const values:any=Values;const key="value";console.log(delete values[key],values.value===undefined,typeof values[key],delete values["missing"]);
            """, "true true undefined true\n" };
        yield return new object[] { "added-null-and-undefined", """
            namespace Values {export const value=3;}const values:any=Values;values.nil=null;values.empty=undefined;values.extra=9;console.log(delete values.nil,delete values["empty"],delete values.extra,values.nil===undefined,values.value);
            """, "true true true true 3\n" };
        yield return new object[] { "recreated-live-binding", """
            namespace Values {export let value:any=3;export function read(){return value;}}const values:any=Values;delete values.value;console.log(values.read()===undefined,Values.value===undefined);values["value"]=9;console.log(values.value,Values.value,values.read());
            """, "true true\n9 9 9\n" };
        yield return new object[] { "function-and-nested", """
            namespace Values {export function read(){return 5;}export namespace Inner {export const n=7;}export const keep=2;}const values:any=Values;const read=values.read;const inner=values.Inner;console.log(delete values.read,delete values["Inner"],typeof values.read,values.Inner===undefined,read(),inner.n,values.keep);
            """, "true true undefined true 5 7 2\n" };
        yield return new object[] { "separate-objects", """
            namespace First {export const value=3;}namespace Second {export const value=4;}const first:any=First;const second:any=Second;console.log(delete first.value,first.value===undefined,second.value);
            """, "true true 4\n" };
        yield return new object[] { "strict", """
            "use strict";namespace Values {export const value=3;}const values:any=Values;console.log(delete values["value"],values.value===undefined,delete values.missing);
            """, "true true true\n" };
        yield return new object[] { "non-configurable", """
            namespace Values {export const value=3;}const values:any=Values;Object.defineProperty(values,"value",{value:3,configurable:false});function run(){"use strict";try{delete values.value;}catch(e){console.log(e.name);}try{delete values["value"];}catch(e){console.log(e.name);}console.log(values.value);}run();
            """, "TypeError\nTypeError\n3\n" };
        yield return new object[] { "sealed", """
            namespace Values {export const value=3;}const values:any=Values;Object.seal(values);function run(){"use strict";try{delete values.value;}catch(e){console.log(e.name);}try{delete values["value"];}catch(e){console.log(e.name);}console.log(delete values.missing,values.value);}run();
            """, "TypeError\nTypeError\ntrue 3\n" };
        yield return new object[] { "frozen-strict", """
            namespace Values {export const value=3;}const values:any=Values;Object.freeze(values);function run(){"use strict";try{delete values.value;}catch(e){console.log(e.name);}try{delete values["value"];}catch(e){console.log(e.name);}console.log(delete values.missing,values.value);}run();
            """, "TypeError\nTypeError\ntrue 3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void NamespaceDeletionRemovesPropertiesAndPreservesRemainingMembers(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
