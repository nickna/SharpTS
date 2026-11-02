using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeOwnSymbolEnumerationTests
{
    // Plain assignment isolates enumeration from the independent #3077 definition prerequisite.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-assigned-symbol-inventory", "const p:any=Number.prototype,s=Symbol('probe');p[s]=7;console.log(p[s],Object.getOwnPropertySymbols(p).includes(s),Reflect.ownKeys(p).includes(s));\n", "7 true true\n"];
        yield return ["boolean-assigned-symbol-inventory", "const p:any=Boolean.prototype,s=Symbol('probe');p[s]=7;console.log(p[s],Object.getOwnPropertySymbols(p).includes(s),Reflect.ownKeys(p).includes(s));\n", "7 true true\n"];
        yield return ["number-order-and-identity", "const p:any=Number.prototype;const a=Symbol('same'),b=Symbol('same');p.visible=3;p[a]=7;p[b]=9;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===a,keys[1]===b,keys.includes('visible' as any),p[a],p[b]);\n", "2 true true false 7 9\n"];
        yield return ["number-undefined-present", "const p:any=Number.prototype;const s=Symbol('field');p[s]=undefined;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,p[s]===undefined);\n", "1 true true\n"];
        yield return ["number-string-symbol-collision", "const p:any=Number.prototype;const s=Symbol('field'),alias=String(s);p[alias]=9;p[s]=7;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,p[s],p[alias]);\n", "1 true 7 9\n"];
        yield return ["number-does-not-coerce-values", "const p:any=Number.prototype;const s=Symbol('field');let calls=0;const value:any={[Symbol.toPrimitive](){calls++;throw 0}};p[s]=value;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,calls,p[s]===value);\n", "1 true 0 true\n"];
        yield return ["number-explicit-parent-own-only", "const p:any=Number.prototype;const own=Symbol('own'),inherited=Symbol('parent'),base:any={};base[inherited]=9;Object.setPrototypeOf(p,base);p[own]=7;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===own,keys.includes(inherited),Reflect.ownKeys(p).includes(own));\n", "1 true false true\n"];
        yield return ["number-returned-snapshot", "const p:any=Number.prototype;const a=Symbol('a'),b=Symbol('b');p[a]=7;p[b]=9;const first=Object.getOwnPropertySymbols(p);first.pop();const next=Object.getOwnPropertySymbols(p);console.log(first.length,next.length,next[0]===a,next[1]===b,p[a],p[b]);\n", "1 2 true true 7 9\n"];
        yield return ["number-well-known-symbol", "const p:any=Number.prototype;const before=Object.getOwnPropertySymbols(p);p[Symbol.toStringTag]='tag';const keys=Object.getOwnPropertySymbols(p);console.log(before.length,keys.length,keys[0]===Symbol.toStringTag,p[Symbol.toStringTag]);\n", "0 1 true tag\n"];
        yield return ["boolean-order-and-identity", "const p:any=Boolean.prototype;const a=Symbol('same'),b=Symbol('same');p.visible=3;p[a]=7;p[b]=9;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===a,keys[1]===b,keys.includes('visible' as any),p[a],p[b]);\n", "2 true true false 7 9\n"];
        yield return ["boolean-undefined-present", "const p:any=Boolean.prototype;const s=Symbol('field');p[s]=undefined;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,p[s]===undefined);\n", "1 true true\n"];
        yield return ["boolean-string-symbol-collision", "const p:any=Boolean.prototype;const s=Symbol('field'),alias=String(s);p[alias]=9;p[s]=7;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,p[s],p[alias]);\n", "1 true 7 9\n"];
        yield return ["boolean-does-not-coerce-values", "const p:any=Boolean.prototype;const s=Symbol('field');let calls=0;const value:any={[Symbol.toPrimitive](){calls++;throw 0}};p[s]=value;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,calls,p[s]===value);\n", "1 true 0 true\n"];
        yield return ["boolean-explicit-parent-own-only", "const p:any=Boolean.prototype;const own=Symbol('own'),inherited=Symbol('parent'),base:any={};base[inherited]=9;Object.setPrototypeOf(p,base);p[own]=7;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===own,keys.includes(inherited),Reflect.ownKeys(p).includes(own));\n", "1 true false true\n"];
        yield return ["boolean-returned-snapshot", "const p:any=Boolean.prototype;const a=Symbol('a'),b=Symbol('b');p[a]=7;p[b]=9;const first=Object.getOwnPropertySymbols(p);first.pop();const next=Object.getOwnPropertySymbols(p);console.log(first.length,next.length,next[0]===a,next[1]===b,p[a],p[b]);\n", "1 2 true true 7 9\n"];
        yield return ["boolean-well-known-symbol", "const p:any=Boolean.prototype;const before=Object.getOwnPropertySymbols(p);p[Symbol.toStringTag]='tag';const keys=Object.getOwnPropertySymbols(p);console.log(before.length,keys.length,keys[0]===Symbol.toStringTag,p[Symbol.toStringTag]);\n", "0 1 true tag\n"];
        yield return ["ordinary-object-control", "const s=Symbol('field'),p:any={};p[s]=7;const keys=Object.getOwnPropertySymbols(p);console.log(keys.length,keys[0]===s,p[s]);\n", "1 true 7\n"];
        yield return ["string-prototype-control", "const s=Symbol('field'),p:any=String.prototype;p[s]=7;console.log(Object.getOwnPropertySymbols(p).includes(s),p[s]);\n", "true 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void EnumeratesActualOwnedSymbolsWithoutReadingTheirValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
