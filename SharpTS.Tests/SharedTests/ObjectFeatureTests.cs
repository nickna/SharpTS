using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for object features including property shorthand, method shorthand,
/// rest pattern, Object.keys/values/entries, computed properties, and more.
/// Runs against both interpreter and compiler.
/// </summary>
public class ObjectFeatureTests
{
    // Property Shorthand
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_PropertyShorthand_Works(ExecutionMode mode)
    {
        var source = """
            let name: string = "Alice";
            let age: number = 30;
            let obj: { name: string, age: number } = { name, age };
            console.log(obj.name);
            console.log(obj.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MixedShorthandAndExplicit_Works(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            let obj: { x: number, y: number } = { x, y: 20 };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Method Shorthand
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { add(a: number, b: number): number } = {
                add(a: number, b: number): number {
                    return a + b;
                }
            };
            console.log(obj.add(3, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodWithDefaultParams_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { greet(name: string): string } = {
                greet(name: string = "World"): string {
                    return "Hello, " + name;
                }
            };
            console.log(obj.greet("Alice"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, Alice\n", output);
    }

    // Object Rest Pattern
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_RestPattern_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };
            let { x, ...rest }: { x: number, y: number, z: number } = obj;
            console.log(x);
            console.log(rest.y);
            console.log(rest.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_RestPattern_MultipleExtracted_Works(ExecutionMode mode)
    {
        var source = """
            let data: { id: number, name: string, age: number, city: string } = { id: 1, name: "Alice", age: 30, city: "NYC" };
            let { id, name, ...others }: { id: number, name: string, age: number, city: string } = data;
            console.log(id);
            console.log(name);
            console.log(others.age);
            console.log(others.city);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nAlice\n30\nNYC\n", output);
    }

    // Object.keys
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Keys_ReturnsPropertyNames(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let keys: string[] = Object.keys(obj);
            console.log(keys.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // Object.values
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Values_ReturnsPropertyValues(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            console.log(values[0]);
            console.log(values[1]);
            console.log(values[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Values_WithMixedTypes(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string, age: number, active: boolean } = { name: "Alice", age: 30, active: true };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // Object.entries
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Entries_ReturnsKeyValuePairs(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let entries: any[] = Object.entries(obj);
            console.log(entries.length);
            console.log(entries[0][0]);
            console.log(entries[0][1]);
            console.log(entries[1][0]);
            console.log(entries[1][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\na\n1\nb\n2\n", output);
    }

    // Object.keys on class instance
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Keys_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let keys: string[] = Object.keys(p);
            console.log(keys.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Object.values on class instance
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Values_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let values: any[] = Object.values(p);
            console.log(values.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Object.entries on class instance
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Entries_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let entries: any[] = Object.entries(p);
            console.log(entries.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Empty Object
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Empty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            console.log(typeof obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    // Nested Object Literals
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Nested_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { outer: { inner: number } } = { outer: { inner: 42 } };
            console.log(obj.outer.inner);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // Object Property Assignment
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_PropertyAssignment_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number } = { x: 1 };
            obj.x = 10;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    // Object with Array Property
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_WithArrayProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { items: number[] } = { items: [1, 2, 3] };
            console.log(obj.items.length);
            console.log(obj.items[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n", output);
    }

    // Computed Property Names
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_ComputedPropertyName_VariableKey(ExecutionMode mode)
    {
        var source = """
            let key: string = "dynamicKey";
            let obj: any = { [key]: 42 };
            console.log(obj["dynamicKey"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_ComputedPropertyName_StringConcatenation(ExecutionMode mode)
    {
        var source = """
            let prefix: string = "prop";
            let obj: any = { [prefix + "1"]: "one", [prefix + "2"]: "two" };
            console.log(obj["prop1"]);
            console.log(obj["prop2"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("one\ntwo\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_StringLiteralKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { "string-key": "hello", "another key": "world" };
            console.log(obj["string-key"]);
            console.log(obj["another key"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_NumberLiteralKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { 123: "numeric key", 456: "another" };
            console.log(obj["123"]);
            console.log(obj["456"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("numeric key\nanother\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MixedStaticAndComputedKeys(ExecutionMode mode)
    {
        var source = """
            let key: string = "computed";
            let obj: any = { regular: 1, [key]: 2, "literal": 3 };
            console.log(obj.regular);
            console.log(obj["computed"]);
            console.log(obj["literal"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_ComputedPropertyName_NumberKey(ExecutionMode mode)
    {
        var source = """
            let idx: number = 42;
            let obj: any = { [idx]: "value at 42" };
            console.log(obj["42"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value at 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_ComputedPropertyName_SymbolKey(ExecutionMode mode)
    {
        var source = """
            let sym: symbol = Symbol("myKey");
            let obj: any = { [sym]: "symbol value" };
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol value\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_ComputedPropertyName_WithSpread(ExecutionMode mode)
    {
        var source = """
            let key: string = "added";
            let base: { x: number } = { x: 1 };
            let obj: any = { ...base, [key]: 2 };
            console.log(obj.x);
            console.log(obj["added"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    // Object Method This Binding
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_ThisBinding_SingleProperty(ExecutionMode mode)
    {
        var source = """
            let obj = {
                x: 10,
                getX() {
                    return this.x;
                }
            };
            console.log(obj.getX());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_ThisBinding_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            let obj = {
                x: 10,
                y: 20,
                getSum() {
                    return this.x + this.y;
                }
            };
            console.log(obj.getSum());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_ThisBinding_NestedObject(ExecutionMode mode)
    {
        var source = """
            let obj = {
                nested: {
                    value: 100,
                    getValue() {
                        return this.value;
                    }
                }
            };
            console.log(obj.nested.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_ThisBinding_MultipleMethods(ExecutionMode mode)
    {
        var source = """
            let calculator = {
                value: 5,
                double() {
                    return this.value * 2;
                },
                triple() {
                    return this.value * 3;
                }
            };
            console.log(calculator.double());
            console.log(calculator.triple());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_MethodShorthand_ThisBinding_WithParameters(ExecutionMode mode)
    {
        var source = """
            let obj = {
                base: 10,
                add(n: number) {
                    return this.base + n;
                }
            };
            console.log(obj.add(5));
            console.log(obj.add(20));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n30\n", output);
    }

    // Object.fromEntries tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_BasicArray(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["a", 1], ["b", 2], ["c", 3]];
            let obj = Object.fromEntries(entries);
            console.log(obj.a);
            console.log(obj.b);
            console.log(obj.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [];
            let obj = Object.fromEntries(entries);
            console.log(Object.keys(obj).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_DuplicateKeys(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["a", 1], ["a", 2], ["a", 3]];
            let obj = Object.fromEntries(entries);
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_RoundTrip(ExecutionMode mode)
    {
        var source = """
            let original: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };
            let entries: any[] = Object.entries(original);
            let restored = Object.fromEntries(entries);
            console.log(restored.x);
            console.log(restored.y);
            console.log(restored.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_MixedValueTypes(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["name", "Alice"], ["age", 30], ["active", true]];
            let obj = Object.fromEntries(entries);
            console.log(obj.name);
            console.log(obj.age);
            console.log(obj.active);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_FromEntries_WithMapEntries(ExecutionMode mode)
    {
        var source = """
            let map = new Map<string, number>();
            map.set("x", 10);
            map.set("y", 20);
            let obj = Object.fromEntries(map.entries());
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Object.hasOwn tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_ReturnsTrueForOwnProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            console.log(Object.hasOwn(obj, "a"));
            console.log(Object.hasOwn(obj, "b"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_ReturnsFalseForMissingProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.hasOwn(obj, "b"));
            console.log(Object.hasOwn(obj, "c"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            console.log(Object.hasOwn(obj, "a"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_ClassInstanceField(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
                greet(): string {
                    return "Hello";
                }
            }
            let p = new Person("Alice", 30);
            console.log(Object.hasOwn(p, "name"));
            console.log(Object.hasOwn(p, "age"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_ClassInstanceMethod(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                constructor(n: string) {
                    this.name = n;
                }
                greet(): string {
                    return "Hello";
                }
            }
            let p = new Person("Alice");
            console.log(Object.hasOwn(p, "greet"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_HasOwn_WithNumberKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { "123": "value" };
            console.log(Object.hasOwn(obj, "123"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Object.assign tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_BasicMerge(ExecutionMode mode)
    {
        var source = """
            let target: { a: number, b?: number } = { a: 1 };
            let source: { b: number } = { b: 2 };
            let result = Object.assign(target, source);
            console.log(result.a);
            console.log(result.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_ModifiesTarget(ExecutionMode mode)
    {
        var source = """
            let target: { a: number, b?: number } = { a: 1 };
            Object.assign(target, { b: 2 });
            console.log(target.a);
            console.log(target.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_MultipleSources(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            let source1: { b: number } = { b: 2 };
            let source2: { c: number } = { c: 3 };
            Object.assign(target, source1, source2);
            console.log(target.a);
            console.log(target.b);
            console.log(target.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_OverridesProperties(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, { a: 100 });
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_LaterSourceWins(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, { a: 2 }, { a: 3 });
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_ReturnsTarget(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            let result = Object.assign(target, { b: 2 });
            console.log(result === target);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_EmptySource(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, {});
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_EmptyTarget(ExecutionMode mode)
    {
        var source = """
            let target: any = {};
            Object.assign(target, { a: 1, b: 2 });
            console.log(target.a);
            console.log(target.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_MixedTypes(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            Object.assign(target, { b: "hello", c: true });
            console.log(target.a);
            console.log(target.b);
            console.log(target.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Assign_NestedObjects(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            Object.assign(target, { nested: { x: 10 } });
            console.log(target.a);
            console.log(target.nested.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n", output);
    }

    // Object.freeze tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_ReturnsTheSameObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let frozen = Object.freeze(obj);
            console.log(frozen === obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_PreventsMutation(ExecutionMode mode)
    {
        // Compiler does not yet implement freeze mutation prevention
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            obj.a = 100;
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_PreventsAddingProperties(ExecutionMode mode)
    {
        // Compiler does not yet implement freeze property addition prevention
        var source = """
            let obj: any = { a: 1 };
            Object.freeze(obj);
            obj.b = 2;
            console.log(obj.a);
            console.log(obj.b === undefined || obj.b === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsFrozen_ReturnsTrueForFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            console.log(Object.isFrozen(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsFrozen_ReturnsFalseForNonFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.isFrozen(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsFrozen_ReturnsTrueForPrimitives(ExecutionMode mode)
    {
        var source = """
            console.log(Object.isFrozen(null));
            console.log(Object.isFrozen(42));
            console.log(Object.isFrozen("hello"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // Object.seal tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_ReturnsTheSameObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let sealed = Object.seal(obj);
            console.log(sealed === obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_AllowsPropertyModification(ExecutionMode mode)
    {
        // Compiler does not yet implement seal property modification behavior
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.seal(obj);
            obj.a = 100;
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_PreventsAddingProperties(ExecutionMode mode)
    {
        // Compiler does not yet implement seal property addition prevention
        var source = """
            let obj: any = { a: 1 };
            Object.seal(obj);
            obj.b = 2;
            console.log(obj.a);
            console.log(obj.b === undefined || obj.b === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsSealed_ReturnsTrueForSealedObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.seal(obj);
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsSealed_ReturnsFalseForNonSealedObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsSealed_ReturnsTrueForFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Array freeze/seal tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_ArrayPreventsModification(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr[0] = 100;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_ArrayPreventsPush(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr.push(4);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_ArrayAllowsModification(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr[0] = 100;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_ArrayPreventsPush(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr.push(4);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsFrozen_ArrayReturnsTrueForFrozen(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            console.log(Object.isFrozen(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Class instance freeze/seal tests (compiler class instance property mutation not yet implemented)
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_ClassInstancePreventsModification(ExecutionMode mode)
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
            let p = new Point(10, 20);
            Object.freeze(p);
            p.x = 100;
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_ClassInstanceAllowsModification(ExecutionMode mode)
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
            let p = new Point(10, 20);
            Object.seal(p);
            p.x = 100;
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_IsFrozen_ClassInstanceReturnsTrueForFrozen(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                constructor(x: number) {
                    this.x = x;
                }
            }
            let p = new Point(10);
            Object.freeze(p);
            console.log(Object.isFrozen(p));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Shallow freeze tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_IsShallow(ExecutionMode mode)
    {
        var source = """
            let obj: any = { nested: { value: 1 } };
            Object.freeze(obj);
            obj.nested.value = 100;
            console.log(obj.nested.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Freeze_ArrayReverseFails(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr.reverse();
            console.log(arr[0]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Object_Seal_ArrayReverseSucceeds(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr.reverse();
            console.log(arr[0]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n", output);
    }
}
