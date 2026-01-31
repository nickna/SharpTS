using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for keyof and typeof type operators.
/// Runs against both interpreter and compiler.
/// </summary>
public class KeyOfTypeOfTests
{
    #region keyof Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_InterfaceExtractsKeysAsUnion(ExecutionMode mode)
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "name";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("name\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_InterfaceAgeKey(ExecutionMode mode)
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "age";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("age\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_WithTypeAlias(ExecutionMode mode)
    {
        var source = """
            type Config = { host: string; port: number; debug: boolean; };
            type ConfigKeys = keyof Config;
            let k: ConfigKeys = "host";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("host\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_InvalidKeyAssignment_Throws(ExecutionMode mode)
    {
        var source = """
            interface Person { name: string; age: number; }
            type Keys = keyof Person;
            let k: Keys = "invalid";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_InlineObjectType(ExecutionMode mode)
    {
        var source = """
            type Keys = keyof { x: number; y: number; };
            let k: Keys = "x";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("x\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_EmptyInterface_NeverType(ExecutionMode mode)
    {
        // keyof {} should be never, so no value can be assigned
        var source = """
            interface Empty { }
            type Keys = keyof Empty;
            // Cannot assign anything to never
            console.log("ok");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_WithOptionalProperties(ExecutionMode mode)
    {
        var source = """
            interface Config { name: string; debug?: boolean; }
            type Keys = keyof Config;
            let k1: Keys = "name";
            let k2: Keys = "debug";
            console.log(k1);
            console.log(k2);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("name\ndebug\n", output);
    }

    #endregion

    #region typeof Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_SimpleVariable(ExecutionMode mode)
    {
        var source = """
            let greeting = "hello";
            type GreetingType = typeof greeting;
            let x: GreetingType = "world";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_NumberVariable(ExecutionMode mode)
    {
        var source = """
            let count: number = 42;
            type CountType = typeof count;
            let x: CountType = 100;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_ObjectVariable(ExecutionMode mode)
    {
        var source = """
            let config = { host: "localhost", port: 8080 };
            type ConfigType = typeof config;
            let c: ConfigType = { host: "example.com", port: 443 };
            console.log(c.host);
            console.log(c.port);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("example.com\n443\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_Function(ExecutionMode mode)
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            type GreetFn = typeof greet;
            let fn: GreetFn = (s: string) => "Hi, " + s;
            console.log(fn("World"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi, World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_PropertyAccess(ExecutionMode mode)
    {
        var source = """
            let obj = { nested: { value: 42 } };
            type ValueType = typeof obj.nested.value;
            let v: ValueType = 100;
            console.log(v);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_NestedProperty(ExecutionMode mode)
    {
        var source = """
            let data = { user: { name: "Alice", age: 30 } };
            type UserType = typeof data.user;
            let u: UserType = { name: "Bob", age: 25 };
            console.log(u.name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Bob\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_ArrayElement(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            type ElemType = typeof arr[0];
            let x: ElemType = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_TupleElement(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("world\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_ObjectIndexAccess(ExecutionMode mode)
    {
        var source = """
            let obj = { key: "value" };
            type KeyType = typeof obj["key"];
            let k: KeyType = "another value";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("another value\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_UndefinedVariable_Throws(ExecutionMode mode)
    {
        // Type aliases are lazily evaluated, so we need to use T to trigger the error
        var source = """
            type T = typeof undefinedVar;
            let x: T = 42;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("undefinedVar", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_ClassInstance(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region keyof typeof Combined Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOfTypeOf_ObjectLiteral(ExecutionMode mode)
    {
        var source = """
            let config = { host: "localhost", port: 8080, debug: true };
            type ConfigKeys = keyof typeof config;
            let k: ConfigKeys = "host";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("host\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOfTypeOf_AllKeys(ExecutionMode mode)
    {
        var source = """
            let settings = { theme: "dark", fontSize: 14 };
            type Keys = keyof typeof settings;
            let k1: Keys = "theme";
            let k2: Keys = "fontSize";
            console.log(k1);
            console.log(k2);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("theme\nfontSize\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOfTypeOf_InvalidKey_Throws(ExecutionMode mode)
    {
        var source = """
            let obj = { a: 1, b: 2 };
            type Keys = keyof typeof obj;
            let k: Keys = "c";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOfTypeOf_NestedObject(ExecutionMode mode)
    {
        var source = """
            let data = { nested: { x: 1, y: 2 } };
            type NestedKeys = keyof typeof data.nested;
            let k: NestedKeys = "x";
            console.log(k);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("x\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_WithArrayMethods(ExecutionMode mode)
    {
        var source = """
            let numbers: number[] = [1, 2, 3];
            type ArrayType = typeof numbers;
            let arr: ArrayType = [4, 5, 6];
            console.log(arr.length);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_WithIndexSignature(ExecutionMode mode)
    {
        var source = """
            interface StringMap { [key: string]: number; }
            type Keys = keyof StringMap;
            // keyof with string index signature includes string
            console.log("ok");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeOf_MultipleLevelDeepAccess(ExecutionMode mode)
    {
        var source = """
            let deep = { a: { b: { c: { value: 42 } } } };
            type DeepValue = typeof deep.a.b.c.value;
            let v: DeepValue = 100;
            console.log(v);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void KeyOf_Interface_UseInFunction(ExecutionMode mode)
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    #endregion
}
