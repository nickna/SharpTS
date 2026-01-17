using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for keyof and typeof type operators.
/// Interpreter tests using RunInterpreted.
/// </summary>
public class KeyOfTypeOfTests
{
    #region keyof Tests

    [Fact]
    public void KeyOf_InterfaceExtractsKeysAsUnion()
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "name";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("name\n", output);
    }

    [Fact]
    public void KeyOf_InterfaceAgeKey()
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "age";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("age\n", output);
    }

    [Fact]
    public void KeyOf_WithTypeAlias()
    {
        var source = """
            type Config = { host: string; port: number; debug: boolean; };
            type ConfigKeys = keyof Config;
            let k: ConfigKeys = "host";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("host\n", output);
    }

    [Fact]
    public void KeyOf_InvalidKeyAssignment_Throws()
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "invalid";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void KeyOf_InlineObjectType()
    {
        var source = """
            type Keys = keyof { x: number; y: number; };
            let k: Keys = "x";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("x\n", output);
    }

    [Fact]
    public void KeyOf_EmptyInterface_NeverType()
    {
        // keyof {} should be never, so no value can be assigned
        var source = """
            interface Empty { }
            type Keys = keyof Empty;
            // Cannot assign anything to never
            console.log("ok");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void KeyOf_WithOptionalProperties()
    {
        var source = """
            interface Config { name: string; debug?: boolean; }
            type Keys = keyof Config;
            let k1: Keys = "name";
            let k2: Keys = "debug";
            console.log(k1);
            console.log(k2);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("name\ndebug\n", output);
    }

    #endregion

    #region typeof Tests

    [Fact]
    public void TypeOf_SimpleVariable()
    {
        var source = """
            let greeting = "hello";
            type GreetingType = typeof greeting;
            let x: GreetingType = "world";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("world\n", output);
    }

    [Fact]
    public void TypeOf_NumberVariable()
    {
        var source = """
            let count: number = 42;
            type CountType = typeof count;
            let x: CountType = 100;
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void TypeOf_ObjectVariable()
    {
        var source = """
            let config = { host: "localhost", port: 8080 };
            type ConfigType = typeof config;
            let c: ConfigType = { host: "example.com", port: 443 };
            console.log(c.host);
            console.log(c.port);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("example.com\n443\n", output);
    }

    [Fact]
    public void TypeOf_Function()
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            type GreetFn = typeof greet;
            let fn: GreetFn = (s: string) => "Hi, " + s;
            console.log(fn("World"));
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hi, World\n", output);
    }

    [Fact]
    public void TypeOf_PropertyAccess()
    {
        var source = """
            let obj = { nested: { value: 42 } };
            type ValueType = typeof obj.nested.value;
            let v: ValueType = 100;
            console.log(v);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void TypeOf_NestedProperty()
    {
        var source = """
            let data = { user: { name: "Alice", age: 30 } };
            type UserType = typeof data.user;
            let u: UserType = { name: "Bob", age: 25 };
            console.log(u.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n", output);
    }

    [Fact]
    public void TypeOf_ArrayElement()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            type ElemType = typeof arr[0];
            let x: ElemType = 42;
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void TypeOf_TupleElement()
    {
        var source = """
            let tuple: [string, number] = ["hello", 42];
            type FirstType = typeof tuple[0];
            type SecondType = typeof tuple[1];
            let s: FirstType = "world";
            let n: SecondType = 100;
            console.log(s);
            console.log(n);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("world\n100\n", output);
    }

    [Fact]
    public void TypeOf_ObjectIndexAccess()
    {
        var source = """
            let obj = { key: "value" };
            type KeyType = typeof obj["key"];
            let k: KeyType = "another value";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("another value\n", output);
    }

    [Fact]
    public void TypeOf_UndefinedVariable_Throws()
    {
        // Type aliases are lazily evaluated, so we need to use T to trigger the error
        var source = """
            type T = typeof undefinedVar;
            let x: T = 42;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("undefinedVar", ex.Message);
    }

    [Fact]
    public void TypeOf_ClassInstance()
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
            let p = new Point(1, 2);
            type PointType = typeof p;
            let p2: PointType = new Point(3, 4);
            console.log(p2.x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region keyof typeof Combined Tests

    [Fact]
    public void KeyOfTypeOf_ObjectLiteral()
    {
        var source = """
            let config = { host: "localhost", port: 8080, debug: true };
            type ConfigKeys = keyof typeof config;
            let k: ConfigKeys = "host";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("host\n", output);
    }

    [Fact]
    public void KeyOfTypeOf_AllKeys()
    {
        var source = """
            let settings = { theme: "dark", fontSize: 14 };
            type Keys = keyof typeof settings;
            let k1: Keys = "theme";
            let k2: Keys = "fontSize";
            console.log(k1);
            console.log(k2);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("theme\nfontSize\n", output);
    }

    [Fact]
    public void KeyOfTypeOf_InvalidKey_Throws()
    {
        var source = """
            let obj = { a: 1, b: 2 };
            type Keys = keyof typeof obj;
            let k: Keys = "c";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void KeyOfTypeOf_NestedObject()
    {
        var source = """
            let data = { nested: { x: 1, y: 2 } };
            type NestedKeys = keyof typeof data.nested;
            let k: NestedKeys = "x";
            console.log(k);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("x\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TypeOf_WithArrayMethods()
    {
        var source = """
            let numbers: number[] = [1, 2, 3];
            type ArrayType = typeof numbers;
            let arr: ArrayType = [4, 5, 6];
            console.log(arr.length);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void KeyOf_WithIndexSignature()
    {
        var source = """
            interface StringMap { [key: string]: number; }
            type Keys = keyof StringMap;
            // keyof with string index signature includes string
            console.log("ok");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void TypeOf_MultipleLevelDeepAccess()
    {
        var source = """
            let deep = { a: { b: { c: { value: 42 } } } };
            type DeepValue = typeof deep.a.b.c.value;
            let v: DeepValue = 100;
            console.log(v);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void KeyOf_Interface_UseInFunction()
    {
        var source = """
            interface Person { name: string; age: number; }

            function getProperty(obj: Person, key: keyof Person): string | number {
                if (key === "name") {
                    return obj.name;
                }
                return obj.age;
            }

            let p: Person = { name: "Alice", age: 30 };
            console.log(getProperty(p, "name"));
            console.log(getProperty(p, "age"));
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", output);
    }

    #endregion
}
