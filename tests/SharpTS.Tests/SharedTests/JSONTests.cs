using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for JSON.parse and JSON.stringify. Runs against both interpreter and compiler.
/// </summary>
public class JSONTests
{
    #region JSON.parse basic tests

    [Theory, ModeData]
    public void JSON_Parse_Number(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("42");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_String(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('"hello"');
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Boolean(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("true");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Null(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("null");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Object(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"name":"Alice","age":30}');
            console.log(result.name);
            console.log(result.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_Array(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("[1, 2, 3]");
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_NestedObject(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"outer":{"inner":42}}');
            console.log(result.outer.inner);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void JSON_Parse_WithReviver(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"a":1,"b":2}', (key: any, value: any): any => {
                if (typeof value === "number") {
                    return value * 2;
                }
                return value;
            });
            console.log(result.a);
            console.log(result.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n", output);
    }

    #endregion

    #region JSON.stringify basic tests

    [Theory, ModeData]
    public void JSON_Stringify_Number(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(42);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_String(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify("hello");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\"hello\"\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Boolean(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(true);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Null(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(null);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Undefined_ReturnsUndefinedNotNull(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.1 step 12: a top-level undefined makes
        // SerializeJSONProperty return the JS value `undefined`, NOT null.
        // Regression for #519 (interpreter previously coerced it to null).
        // `typeof` distinguishes JS undefined ("undefined") from null
        // ("object"), so this fails loudly if the old behavior returns.
        var source = """
            let r: any = JSON.stringify(undefined);
            console.log(typeof r);
            console.log(r);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Function_ReturnsUndefined(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.3 step 9: a top-level callable serializes to the JS
        // value `undefined` (same root cause / fix as the undefined case).
        var source = """
            let r: any = JSON.stringify((): number => 1);
            console.log(typeof r);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Object(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"b\":2}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_Array(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[1,2,3]\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_WithIndent(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, 2);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n  \"a\": 1\n}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_WithReplacerArray(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let result: string = JSON.stringify(obj, ["a", "c"]);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"c\":3}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    [Theory, ModeData]
    public void JSON_Roundtrip(ExecutionMode mode)
    {
        var source = """
            let original: { name: string, age: number } = { name: "Alice", age: 30 };
            let json: string = JSON.stringify(original);
            let parsed: any = JSON.parse(json);
            console.log(parsed.name);
            console.log(parsed.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    #endregion

    #region Enhanced JSON.stringify tests

    [Theory, ModeData]
    public void JSON_Stringify_ClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(name: string, age: number) {
                    this.name = name;
                    this.age = age;
                }
            }
            let p: Person = new Person("Bob", 25);
            let result: string = JSON.stringify(p);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"name\":\"Bob\",\"age\":25}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_ClassInstance_ToJSON(ExecutionMode mode)
    {
        var source = """
            class Data {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
                toJSON(): { custom: number } {
                    return { custom: this.value * 10 };
                }
            }
            let d: Data = new Data(5);
            let result: string = JSON.stringify(d);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"custom\":50}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_ObjectWithToJSON(ExecutionMode mode)
    {
        var source = """
            let obj: any = {
                x: 10,
                toJSON: (): string => {
                    return "custom";
                }
            };
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\"custom\"\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_InheritedToJSON_UsesOriginalReceiverAndKey(ExecutionMode mode)
    {
        var source = """
            const proto: any = {
                toJSON: function (key: string): any {
                    return { key: key, value: this.x };
                }
            };
            const child: any = Object.create(proto);
            child.x = 7;
            console.log(JSON.stringify({ child: child }));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"child\":{\"key\":\"child\",\"value\":7}}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_CustomPrototypeToJSONAccessor_IsObserved(ExecutionMode mode)
    {
        var source = """
            let calls: number = 0;
            const proto: any = {};
            Object.defineProperty(proto, "toJSON", {
                configurable: true,
                get: function (): any {
                    calls++;
                    return function (_key: string): any { return this.x; };
                }
            });
            const child: any = Object.create(proto);
            child.x = 3;
            console.log(JSON.stringify({ value: child }));
            console.log(calls);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"value\":3}\n1\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_OwnSetterOnlyToJSON_ShadowsPrototypeMethod(ExecutionMode mode)
    {
        var source = """
            const proto: any = {
                toJSON: function (): string { return "wrong"; }
            };
            const obj: any = Object.create(proto);
            Object.defineProperty(obj, "toJSON", {
                configurable: true,
                enumerable: true,
                set: function (_value: any): void {}
            });
            obj.x = 1;
            console.log(JSON.stringify(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"x\":1}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_KeySnapshot_StillReadsInheritedValueAfterDeletion(ExecutionMode mode)
    {
        // Own keys are captured before values are read. The first property's
        // toJSON deletes a later own property, so the later Get must fall back
        // to the prototype while retaining the snapshotted key.
        var source = """
            const proto: any = { b: 9 };
            const obj: any = Object.create(proto);
            obj.a = {
                toJSON: function (): number {
                    delete obj.b;
                    return 1;
                }
            };
            obj.b = 2;
            console.log(JSON.stringify(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"b\":9}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_EscapeFastPathFallsBackForSpecialCharacters(ExecutionMode mode)
    {
        var source = """
            const key: string = "a\"\\\n";
            const value: string = "x\t" + String.fromCharCode(0xd800);
            const obj: any = {};
            obj[key] = value;
            console.log(JSON.stringify(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\\\"\\\\\\n\":\"x\\t\\ud800\"}\n", output);
    }

    [Theory, CompiledOnlyData]
    public void JSON_Stringify_ToJSON_ReceivesPropertyKey(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.3 SerializeJSONProperty step 2.b.i: toJSON is invoked
        // with the property key as its first argument. Compiler-only baseline
        // (interpreter has the same gap, tracked separately).
        var source = """
            let obj: any = {
                a: { toJSON: function(k: string): string { return "k=" + k; } },
                b: [
                    { toJSON: function(k: string): string { return "i=" + k; } },
                    { toJSON: function(k: string): string { return "i=" + k; } }
                ]
            };
            console.log(JSON.stringify(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":\"k=a\",\"b\":[\"i=0\",\"i=1\"]}\n", output);
    }

    [Theory, CompiledOnlyData]
    public void JSON_Stringify_Replacer_ReceivesPropertyKey(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.3 step 3.a: replacer is called with (key, value).
        // The recursive helper must thread the property key through array
        // index ToString and object key paths.
        var source = """
            let obj: any = { x: 1, y: [10, 20] };
            let keys: string[] = [];
            let result: string = JSON.stringify(obj, function(k: string, v: any): any {
                keys.push(k);
                return v;
            });
            console.log(keys.join("|"));
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("|x|y|0|1\n{\"x\":1,\"y\":[10,20]}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_BigInt_Throws(ExecutionMode mode)
    {
        var source = """
            try {
                let result: string = JSON.stringify(123n);
                console.log("should not reach here");
            } catch (e) {
                console.log("caught error");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_StringIndent_Tab(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, "\t");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n\t\"a\": 1\n}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_StringIndent_Custom(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, ">>>");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n>>>\"a\": 1\n}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_NestedClassInstance(ExecutionMode mode)
    {
        var source = """
            class Inner {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            class Outer {
                inner: Inner;
                constructor(i: Inner) {
                    this.inner = i;
                }
            }
            let o: Outer = new Outer(new Inner(42));
            let result: string = JSON.stringify(o);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"inner\":{\"value\":42}}\n", output);
    }

    [Theory, ModeData]
    public void JSON_Stringify_ClassInstanceWithIndent(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            let p: Point = new Point(10, 20);
            let result: string = JSON.stringify(p, null, 2);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n  \"x\": 10,\n  \"y\": 20\n}\n", output);
    }

    #endregion

    #region JSON.stringify boxed primitive wrappers (#524)

    // ECMA-262 25.5.2.3 step 4: a boxed primitive wrapper (new Number/String/Boolean) serializes
    // as its underlying primitive, not as an object exposing the internal slots. The interpreter
    // previously emitted the wrapper's __primitiveType/__primitiveValue marker fields; compiled
    // mode was already correct, so these run in both modes as a parity check.

    [Theory, ModeData]
    public void JSON_Stringify_BoxedPrimitives_TopLevel(ExecutionMode mode)
    {
        var source = """
            console.log(JSON.stringify(new Number(5)));
            console.log(JSON.stringify(new String("hi")));
            console.log(JSON.stringify(new Boolean(true)));
            """;
        Assert.Equal("5\n\"hi\"\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedPrimitives_AsObjectProperties(ExecutionMode mode)
    {
        var source = """
            console.log(JSON.stringify({ a: new Number(5), b: new String("x"), c: new Boolean(false) }));
            """;
        Assert.Equal("{\"a\":5,\"b\":\"x\",\"c\":false}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedPrimitives_AsArrayElements(ExecutionMode mode)
    {
        var source = """
            console.log(JSON.stringify([new Number(1), new String("y"), new Boolean(false)]));
            """;
        Assert.Equal("[1,\"y\",false]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedNumber_AsSpace_Indents(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.1 step 5: a boxed Number `space` contributes its primitive value (2 → 2
        // spaces). A float is floored to an integer count by the existing space handling.
        var source = """
            console.log(JSON.stringify([7], null, new Number(2)));
            """;
        Assert.Equal("[\n  7\n]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedString_AsSpace_Indents(ExecutionMode mode)
    {
        // A boxed String `space` contributes its primitive value as the literal indent string.
        var source = """
            console.log(JSON.stringify({ k: 1 }, null, new String("--")));
            """;
        Assert.Equal("{\n--\"k\": 1\n}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_PlainObjectWithPrimitiveValueField_NotUnwrapped(ExecutionMode mode)
    {
        // An ordinary object that merely has a __primitiveValue field (but no __primitiveType) is
        // NOT a boxed wrapper and must serialize verbatim — the unwrap is gated on __primitiveType
        // being a string, matching the spec's [[NumberData]]/[[StringData]]/[[BooleanData]] semantics.
        var source = """
            console.log(JSON.stringify({ __primitiveValue: 7 }));
            """;
        Assert.Equal("{\"__primitiveValue\":7}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_PlainObjectWithPrimitiveValueField_WithSpace_NotUnwrapped(ExecutionMode mode)
    {
        // Same as above but exercises the StringifyFull code path (replacer/space present).
        var source = """
            console.log(JSON.stringify({ __primitiveValue: 7 }, null, 2));
            """;
        Assert.Equal("{\n  \"__primitiveValue\": 7\n}\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region JSON.stringify boxed wrapper valueOf/toString dispatch (#574)

    // ECMA-262 25.5.2.3 step 4 / 25.5.2.1 steps 4.b & 5: a boxed wrapper coerces via
    // ToNumber/ToString, which go through OrdinaryToPrimitive and so honor a user-
    // overridden own valueOf/toString — not the raw [[PrimitiveValue]] slot.

    [Theory, ModeData]
    public void JSON_Stringify_BoxedNumber_Value_HonorsValueOfOverride(ExecutionMode mode)
    {
        // value-number-object.js: a replacer returns a new Number whose own valueOf
        // is overridden; ToNumber must call it (toString must NOT be reached).
        var source = """
            const replacer = function (_key: any, value: any): any {
              if (value === "str") {
                const num: any = new Number(42);
                num.toString = function () { throw new Error("should not be called"); };
                num.valueOf = function () { return 2; };
                return num;
              }
              return value;
            };
            console.log(JSON.stringify(["str"], replacer));
            """;
        Assert.Equal("[2]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedString_Value_HonorsToStringOverride(ExecutionMode mode)
    {
        // value-string-object.js: a String wrapper with an own toString override
        // serializes via that override (string-hint ToPrimitive → toString first).
        var source = """
            const s: any = new String("ignored");
            s.toString = function () { return "OVERRIDE"; };
            console.log(JSON.stringify(s));
            """;
        Assert.Equal("\"OVERRIDE\"\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedNumber_Space_HonorsValueOfOverride(ExecutionMode mode)
    {
        // space-number-object.js: space = new Number(1) with valueOf → 3 indents by 3.
        var source = """
            const num: any = new Number(1);
            num.toString = function () { throw new Error("should not be called"); };
            num.valueOf = function () { return 3; };
            console.log(JSON.stringify({ k: 1 }, null, num));
            """;
        Assert.Equal("{\n   \"k\": 1\n}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedString_Space_HonorsToStringOverride(ExecutionMode mode)
    {
        // space-string-object.js: space = new String with toString override is the indent.
        var source = """
            const s: any = new String("ignored");
            s.toString = function () { return ">>"; };
            console.log(JSON.stringify({ k: 1 }, null, s));
            """;
        Assert.Equal("{\n>>\"k\": 1\n}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_ReplacerArray_BoxedNumber_UsesToString(ExecutionMode mode)
    {
        // replacer-array-number-object.js: a Number wrapper in the replacer array is a
        // PropertyList key via ToString — its own toString (not valueOf) is used.
        var source = """
            const num: any = new Number(10);
            num.toString = function () { return "toString"; };
            num.valueOf = function () { throw new Error("should not be called"); };
            const value: any = { 10: 1, toString: 2, valueOf: 3 };
            console.log(JSON.stringify(value, [num]));
            """;
        Assert.Equal("{\"toString\":2}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_ReplacerArray_BoxedString_UsedAsKey(ExecutionMode mode)
    {
        // replacer-array-string-object.js: a String wrapper element selects that key.
        var source = """
            const key: any = new String("z");
            console.log(JSON.stringify({ z: 1, y: 2 }, [key]));
            """;
        Assert.Equal("{\"z\":1}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_ReplacerArray_PlainNumber_CoercedToKey(ExecutionMode mode)
    {
        // ECMA-262 25.5.2.1 step 4.b: a plain Number element coerces to its ToString key.
        var source = """
            console.log(JSON.stringify({ a: 1, "2": 2, c: 3 }, ["a", 2, "c"]));
            """;
        Assert.Equal("{\"a\":1,\"2\":2,\"c\":3}\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Stringify_BoxedNumber_AbruptValueOf_Propagates(ExecutionMode mode)
    {
        // value-number-object.js abrupt case: a throwing valueOf/toString must propagate.
        var source = """
            let threw = false;
            const num: any = new Number(3.14);
            num.toString = function () { throw new Error("boom"); };
            num.valueOf = function () { throw new Error("boom"); };
            try { JSON.stringify({ key: num }); } catch (e) { threw = true; }
            console.log(threw);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JSON_Methods_HaveStandardOwnMetadata(ExecutionMode mode)
    {
        var source = """
            const json: any = JSON;
            const parse = json.parse;
            const descriptor = Object.getOwnPropertyDescriptor(json, "parse")!;
            console.log(Object.hasOwn(json, "parse"), parse.length);
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            console.log(delete json.parse, json.parse === undefined);
            Object.defineProperty(json, "parse", {
                value: parse,
                writable: true,
                enumerable: false,
                configurable: true
            });
            console.log(json.parse("1"), json.stringify.length);
            """;
        Assert.Equal("true 2\ntrue false true\ntrue true\n1 3\n",
            TestHarness.Run(source, mode));
    }

    #endregion
}
