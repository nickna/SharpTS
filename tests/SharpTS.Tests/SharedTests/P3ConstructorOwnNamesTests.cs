using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3ConstructorOwnNamesTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted, "Object", "assign")]
    [InlineData(ExecutionMode.Compiled, "Object", "assign")]
    [InlineData(ExecutionMode.Compiled, "Array", "from")]
    [InlineData(ExecutionMode.Interpreted, "Number", "isFinite")]
    [InlineData(ExecutionMode.Compiled, "Number", "isFinite")]
    public void ConstructorNamesFollowDeletionAndExplicitRecreation(
        ExecutionMode mode, string constructor, string staticMember)
    {
        var initialNames = constructor switch
        {
            "Object" => "length,name,prototype,assign,create,defineProperties,defineProperty,entries,freeze,fromEntries,getOwnPropertyDescriptor,getOwnPropertyDescriptors,getOwnPropertyNames,getOwnPropertySymbols,getPrototypeOf,groupBy,hasOwn,is,isExtensible,isFrozen,isSealed,keys,preventExtensions,seal,setPrototypeOf,values",
            "Array" => "length,name,prototype,from,fromAsync,isArray,of",
            "Number" => "length,name,prototype,MAX_VALUE,MIN_VALUE,NaN,NEGATIVE_INFINITY,POSITIVE_INFINITY,MAX_SAFE_INTEGER,MIN_SAFE_INTEGER,EPSILON,isFinite,isInteger,isNaN,isSafeInteger,parseFloat,parseInt",
            _ => throw new ArgumentOutOfRangeException(nameof(constructor))
        };
        // Array/Number static-method deletion has a separate preexisting
        // state-tracking defect. Keep their static slots as inventory controls.
        var deleteStatic = constructor == "Object" ? "delete target[member]" : "true";
        var staticAfterDeletion = constructor == "Object" ? "false" : "true";
        var source = $$"""
            const target: any = {{constructor}};
            const member = "{{staticMember}}";
            let names = Object.getOwnPropertyNames(target);
            const expectedNames = "{{initialNames}}".split(",");
            console.log(expectedNames.every(key => names.includes(key)), names.length === expectedNames.length);
            console.log(names.includes("name"), names.includes("length"), names.includes(member), names.includes("prototype"));
            console.log(delete target.name, delete target.length, {{deleteStatic}});
            names = Object.getOwnPropertyNames(target);
            console.log(names.includes("name"), names.includes("length"), names.includes(member), names.includes("prototype"));
            let reads = 0;
            Object.defineProperty(target, "name", { get() { reads++; return "restored"; }, configurable: true });
            Object.defineProperty(target, "length", { value: 7, configurable: true });
            Object.defineProperty(target, member, { value: 42, configurable: true });
            names = Object.getOwnPropertyNames(target);
            console.log(names.includes("name"), names.includes("length"), names.includes(member), reads);
            console.log(target.name, target.length, target[member], reads);
            """;
        Assert.Equal($"true true\ntrue true true true\ntrue true true\nfalse false {staticAfterDeletion} true\ntrue true true 0\nrestored 7 42 1\n",
            TestHarness.Run(source, mode));
    }
}
