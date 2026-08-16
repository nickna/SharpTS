using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// ECMA-262 §23.1.3: every <c>Array.prototype</c> iteration method begins with
/// <c>O = ToObject(this value)</c>. When such a method is borrowed onto a
/// primitive receiver via <c>.call</c>, the callback's final "array" argument is
/// therefore a wrapper object — <c>Array.prototype.forEach.call("ab", cb)</c>
/// passes a String wrapper (<c>typeof obj === "object"</c>,
/// <c>obj instanceof String === true</c>), not the bare <c>"ab"</c>.
///
/// Object.defineProperties (§19.1.2.3 ObjectDefineProperties) likewise reads each
/// own enumerable key of its Properties argument via <c>Get</c>, firing accessor
/// getters with <c>this</c> bound to the (possibly boxed) Properties object.
///
/// Regression coverage for #454. Runs in both interpreter and compiled mode.
/// </summary>
public class ArrayPrototypeToObjectTests
{
    // ── callback's `obj` argument is a wrapper object for a primitive `this` ──

    [Theory, ModeData]
    public void ForEach_OnStringPrimitive_CallbackReceivesStringWrapper(ExecutionMode mode)
    {
        var source = """
            let saw: string = "";
            Array.prototype.forEach.call("ab" as any, function (ch: any, i: any, obj: any): void {
                saw += (typeof obj) + ":" + (obj instanceof String) + ";";
            });
            console.log(saw);
            """;
        Assert.Equal("object:true;object:true;\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Map_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        var source = """
            let r = Array.prototype.map.call("ab" as any, function (ch: any, i: any, obj: any): any {
                return obj instanceof String;
            });
            console.log(JSON.stringify(r));
            """;
        Assert.Equal("[true,true]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Filter_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        // Keeps every element because `obj instanceof String` is true for each.
        var source = """
            let r = Array.prototype.filter.call("ab" as any, function (ch: any, i: any, obj: any): any {
                return obj instanceof String;
            });
            console.log(JSON.stringify(r));
            """;
        Assert.Equal("[\"a\",\"b\"]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Some_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        var source = """
            console.log(Array.prototype.some.call("ab" as any, function (ch: any, i: any, obj: any): any {
                return obj instanceof String;
            }));
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Every_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        // Mirrors Test262 built-ins/Array/prototype/every/15.4.4.16-1-7.js:
        // callback returns !(obj instanceof String); every must short-circuit
        // to false because obj IS a String wrapper.
        var source = """
            console.log(Array.prototype.every.call("ab" as any, function (ch: any, i: any, obj: any): any {
                return !(obj instanceof String);
            }));
            """;
        Assert.Equal("false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Reduce_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        var source = """
            let r = Array.prototype.reduce.call("ab" as any, function (acc: any, ch: any, i: any, obj: any): any {
                return acc + ((obj instanceof String) ? "T" : "F");
            }, "");
            console.log(r);
            """;
        Assert.Equal("TT\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReduceRight_OnStringPrimitive_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        var source = """
            let r = Array.prototype.reduceRight.call("ab" as any, function (acc: any, ch: any, i: any, obj: any): any {
                return acc + ((obj instanceof String) ? "T" : "F");
            }, "");
            console.log(r);
            """;
        Assert.Equal("TT\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Map_OnStringPrimitive_ElementsAreChars(ExecutionMode mode)
    {
        // The element argument (kValue) is still the per-index character string,
        // independent of the wrapper-ised `obj` argument.
        var source = """
            let r = Array.prototype.map.call("ab" as any, function (ch: any): any { return ch + "!"; });
            console.log(JSON.stringify(r));
            """;
        Assert.Equal("[\"a!\",\"b!\"]\n", TestHarness.Run(source, mode));
    }

    // ── String OBJECT receiver: ToObject is identity, wrapper passes through ──

    [Theory, ModeData]
    public void Every_OnStringObject_ObjArgIsStringWrapper(ExecutionMode mode)
    {
        // Mirrors Test262 .../every/15.4.4.16-1-8.js.
        var source = """
            let s: any = new String("ab");
            console.log(Array.prototype.every.call(s, function (ch: any, i: any, obj: any): any {
                return !(obj instanceof String);
            }));
            """;
        Assert.Equal("false\n", TestHarness.Run(source, mode));
    }

    // ── plain array-like / real array receivers must be unaffected ───────────

    [Theory, ModeData]
    public void Map_OnRealArray_ObjArgIsTheArray(ExecutionMode mode)
    {
        var source = """
            let r = Array.prototype.map.call([1, 2, 3], function (x: any, i: any, obj: any): any {
                return Array.isArray(obj) ? x * 10 : -1;
            });
            console.log(JSON.stringify(r));
            """;
        Assert.Equal("[10,20,30]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Map_OnPlainArrayLike_PassesReceiverThrough(ExecutionMode mode)
    {
        var source = """
            let o: any = { length: 2, 0: "a", 1: "b" };
            let r = Array.prototype.map.call(o, function (x: any): any { return x + "!"; });
            console.log(JSON.stringify(r));
            """;
        Assert.Equal("[\"a!\",\"b!\"]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Every_OnPlainArrayLike_BindsThisArgAndObservesMutation(ExecutionMode mode)
    {
        var source = """
            let receiver: any = { 0: 11, 1: 12, length: 2 };
            let thisArg: any = { threshold: 10 };
            let sawBinding: boolean = false;
            let result = Array.prototype.every.call(receiver, function (value: any, index: any, obj: any): any {
                sawBinding = this === thisArg && obj === receiver;
                if (index === 0) receiver[1] = 8;
                return value > this.threshold;
            }, thisArg);
            console.log(result + ":" + sawBinding);
            """;
        Assert.Equal("false:true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Every_OnPlainArrayLike_BoxesPrimitiveThisArg(ExecutionMode mode)
    {
        var source = """
            let receiver: any = { 0: 11, length: 1 };
            let result = Array.prototype.every.call(receiver, function (value: any): any {
                return this.valueOf() === 5;
            }, 5);
            console.log(result);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Map_OnPlainArrayLike_VisitsPropertyAddedByCallback(ExecutionMode mode)
    {
        var source = """
            let receiver: any = { 0: 1, length: 3 };
            let result = Array.prototype.map.call(receiver, function (value: any, index: any): any {
                if (index === 0) receiver[1] = 2;
                return value * 10;
            });
            console.log(JSON.stringify(result));
            """;
        Assert.Equal("[10,20,null]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReduceRight_OnPlainArrayLike_ObservesPropertyAddedByGetter(ExecutionMode mode)
    {
        var source = """
            let receiver: any = { length: 2 };
            Object.defineProperty(receiver, "1", {
                configurable: true,
                get: function (): any {
                    receiver[0] = 2;
                    return 3;
                }
            });
            let result = Array.prototype.reduceRight.call(receiver, function (acc: any, value: any): any {
                return acc + value;
            });
            console.log(result);
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericMethods_PreserveHolesAndSetterOnlyShadowing(ExecutionMode mode)
    {
        var source = """
            const re: any = new RegExp();
            re.length = 2;
            re[1] = true;
            const filtered = Array.prototype.filter.call(re, function(value: any, index: number, receiver: any) {
                return receiver instanceof RegExp;
            });
            console.log(JSON.stringify(filtered));

            const proto: any = {};
            Object.defineProperty(proto, "0", { get: function() { return 2; }, configurable: true });
            const Con: any = function() {};
            Con.prototype = proto;
            const child: any = new Con();
            child.length = 1;
            Object.defineProperty(child, "0", { set: function(_value: any) {}, configurable: true });
            console.log(Array.prototype.indexOf.call(child, undefined));
            console.log(Array.prototype.map.call(child, function(value: any) { return typeof value; })[0]);
            """;

        Assert.Equal("[true]\n0\nundefined\n", TestHarness.Run(source, mode));
    }

    // ── Object.defineProperties reads Properties via Get (accessor getters) ──

    [Theory, ModeData]
    public void DefineProperties_BooleanObjectProperties_GetterThisIsWrapper(ExecutionMode mode)
    {
        // Mirrors Test262 built-ins/Object/defineProperties/15.2.3.7-2-4.js:
        // the Properties argument is a Boolean wrapper carrying an enumerable
        // accessor; its getter must run with `this` === that wrapper.
        var source = """
            let obj: any = {};
            let props: any = new Boolean(true);
            let result: boolean = false;
            Object.defineProperty(props, "prop", {
                get: function (this: any): any { result = this instanceof Boolean; return {}; },
                enumerable: true
            });
            Object.defineProperties(obj, props);
            console.log(result);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefineProperties_NumberObjectProperties_GetterThisIsWrapper(ExecutionMode mode)
    {
        // Mirrors Test262 .../defineProperties/15.2.3.7-2-6.js.
        var source = """
            let obj: any = {};
            let props: any = new Number(-12);
            let result: boolean = false;
            Object.defineProperty(props, "prop", {
                get: function (this: any): any { result = this instanceof Number; return {}; },
                enumerable: true
            });
            Object.defineProperties(obj, props);
            console.log(result);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefineProperties_SkipsNonEnumerableSourceProperty(ExecutionMode mode)
    {
        // ObjectDefineProperties only processes own ENUMERABLE keys of Properties.
        var source = """
            let props: any = {};
            Object.defineProperty(props, "shown", { value: { value: 7 }, enumerable: true });
            Object.defineProperty(props, "hidden", { value: { value: 9 }, enumerable: false });
            let target: any = {};
            Object.defineProperties(target, props);
            console.log(target.shown + "," + target.hidden);
            """;
        Assert.Equal("7,undefined\n", TestHarness.Run(source, mode));
    }
}
