using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for TypeScript utility types: Partial, Required, Readonly, Record, Pick, Omit.
/// </summary>
public class UtilityTypesTests
{
    #region Partial<T>

    [Fact]
    public void Partial_AllPropertiesOptional_EmptyObjectValid()
    {
        var source = """
            interface Person { name: string; age: number; }
            let p: Partial<Person> = {};
            console.log("ok");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void Partial_SomePropertiesProvided()
    {
        var source = """
            interface Person { name: string; age: number; }
            let p: Partial<Person> = { name: "Alice" };
            console.log(p.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Partial_AllPropertiesProvided()
    {
        var source = """
            interface Person { name: string; age: number; }
            let p: Partial<Person> = { name: "Bob", age: 30 };
            console.log(p.name);
            console.log(p.age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n30\n", output);
    }

    [Fact]
    public void Partial_WrongArgCount_Throws()
    {
        var source = """
            let x: Partial<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Partial<T> requires exactly 1 type argument", ex.Message);
    }

    [Fact]
    public void Partial_OnRecord()
    {
        var source = """
            type Config = { host: string; port: number; };
            let c: Partial<Config> = { host: "localhost" };
            console.log(c.host);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("localhost\n", output);
    }

    #endregion

    #region Required<T>

    [Fact]
    public void Required_AllPropertiesRequired()
    {
        var source = """
            interface Config { name?: string; debug?: boolean; }
            let c: Required<Config> = { name: "app", debug: true };
            console.log(c.name);
            console.log(c.debug);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("app\ntrue\n", output);
    }

    [Fact]
    public void Required_MissingProperty_Throws()
    {
        var source = """
            interface Config { name?: string; debug?: boolean; }
            let c: Required<Config> = { name: "app" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("debug", ex.Message);
    }

    [Fact]
    public void Required_WrongArgCount_Throws()
    {
        var source = """
            let x: Required<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Required<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region Readonly<T>

    [Fact]
    public void Readonly_PropertiesAccessible()
    {
        var source = """
            interface Point { x: number; y: number; }
            let p: Readonly<Point> = { x: 10, y: 20 };
            console.log(p.x);
            console.log(p.y);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void Readonly_PreservesOptionalProperties()
    {
        var source = """
            interface Config { name: string; debug?: boolean; }
            let c: Readonly<Config> = { name: "app" };
            console.log(c.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Readonly_WrongArgCount_Throws()
    {
        var source = """
            let x: Readonly<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Readonly<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region Record<K, V>

    [Fact]
    public void Record_StringLiteralKeys()
    {
        var source = """
            type Status = "active" | "inactive";
            let statuses: Record<Status, number> = { active: 1, inactive: 0 };
            console.log(statuses.active);
            console.log(statuses.inactive);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n0\n", output);
    }

    [Fact]
    public void Record_SingleStringLiteral()
    {
        // Use type alias to avoid parser issue with string literal in generic type argument
        var source = """
            type Key = "key";
            let obj: Record<Key, number> = { key: 42 };
            console.log(obj.key);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Record_StringIndexSignature()
    {
        var source = """
            let dict: Record<string, number> = { a: 1, b: 2, c: 3 };
            console.log(dict.a);
            console.log(dict.b);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void Record_WrongArgCount_Throws()
    {
        var source = """
            let x: Record<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Record<K, V> requires exactly 2 type arguments", ex.Message);
    }

    [Fact]
    public void Record_MissingKey_Throws()
    {
        var source = """
            type Keys = "a" | "b";
            let obj: Record<Keys, number> = { a: 1 };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("b", ex.Message);
    }

    #endregion

    #region Pick<T, K>

    [Fact]
    public void Pick_SelectsSingleKey()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let nameOnly: Pick<Person, "name"> = { name: "Bob" };
            console.log(nameOnly.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n", output);
    }

    [Fact]
    public void Pick_SelectsMultipleKeys()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let basic: Pick<Person, "name" | "age"> = { name: "Bob", age: 25 };
            console.log(basic.name);
            console.log(basic.age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void Pick_ExtraProperty_Throws()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let nameOnly: Pick<Person, "name"> = { name: "Bob", age: 25 };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("age", ex.Message);
    }

    [Fact]
    public void Pick_PreservesOptional()
    {
        var source = """
            interface Config { name: string; debug?: boolean; level: number; }
            let picked: Pick<Config, "name" | "debug"> = { name: "app" };
            console.log(picked.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Pick_WrongArgCount_Throws()
    {
        var source = """
            let x: Pick<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Pick<T, K> requires exactly 2 type arguments", ex.Message);
    }

    #endregion

    #region Omit<T, K>

    [Fact]
    public void Omit_ExcludesSingleKey()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let noEmail: Omit<Person, "email"> = { name: "Bob", age: 25 };
            console.log(noEmail.name);
            console.log(noEmail.age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void Omit_ExcludesMultipleKeys()
    {
        var source = """
            interface Person { name: string; age: number; email: string; phone: string; }
            let basic: Omit<Person, "email" | "phone"> = { name: "Bob", age: 25 };
            console.log(basic.name);
            console.log(basic.age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void Omit_ExcludedKeyNotAllowed()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let noEmail: Omit<Person, "email"> = { name: "Bob", age: 25, email: "test@test.com" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("email", ex.Message);
    }

    [Fact]
    public void Omit_MissingRequiredKey_Throws()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let noEmail: Omit<Person, "email"> = { name: "Bob" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("age", ex.Message);
    }

    [Fact]
    public void Omit_PreservesOptional()
    {
        var source = """
            interface Config { name: string; debug?: boolean; level: number; }
            let omitted: Omit<Config, "level"> = { name: "app" };
            console.log(omitted.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Omit_WrongArgCount_Throws()
    {
        var source = """
            let x: Omit<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Omit<T, K> requires exactly 2 type arguments", ex.Message);
    }

    #endregion

    #region Composition

    [Fact]
    public void Partial_Readonly_Composed()
    {
        var source = """
            interface Person { name: string; age: number; }
            let p: Partial<Readonly<Person>> = { name: "Test" };
            console.log(p.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Test\n", output);
    }

    [Fact]
    public void Readonly_Partial_Composed()
    {
        var source = """
            interface Person { name: string; age: number; }
            let p: Readonly<Partial<Person>> = { age: 25 };
            console.log(p.age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("25\n", output);
    }

    [Fact]
    public void Pick_Then_Partial()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let p: Partial<Pick<Person, "name" | "age">> = { name: "Alice" };
            console.log(p.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Omit_Then_Required()
    {
        var source = """
            interface Config { name?: string; debug?: boolean; level?: number; }
            let c: Required<Omit<Config, "level">> = { name: "app", debug: true };
            console.log(c.name);
            console.log(c.debug);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("app\ntrue\n", output);
    }

    [Fact]
    public void TripleNestedGenerics_ClosingBrackets()
    {
        // Tests parsing of >>> which the lexer tokenizes as a single GREATER_GREATER_GREATER token
        var source = """
            interface Data { value: number; }
            let x: Partial<Readonly<Required<Data>>> = { value: 42 };
            console.log(x.value);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region With Classes

    [Fact]
    public void Partial_FromClass()
    {
        var source = """
            class User {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let partial: Partial<User> = { name: "Alice" };
            console.log(partial.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Pick_FromClass()
    {
        var source = """
            class User {
                name: string;
                age: number;
                email: string;
                constructor(n: string, a: number, e: string) {
                    this.name = n;
                    this.age = a;
                    this.email = e;
                }
            }
            let nameOnly: Pick<User, "name"> = { name: "Alice" };
            console.log(nameOnly.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Omit_FromClass()
    {
        var source = """
            class User {
                name: string;
                age: number;
                email: string;
                constructor(n: string, a: number, e: string) {
                    this.name = n;
                    this.age = a;
                    this.email = e;
                }
            }
            let noEmail: Omit<User, "email"> = { name: "Alice", age: 30 };
            console.log(noEmail.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Partial_EmptyInterface()
    {
        var source = """
            interface Empty { }
            let e: Partial<Empty> = {};
            console.log("ok");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void Record_WithObjectValue()
    {
        var source = """
            interface User { name: string; }
            let users: Record<string, User> = {
                alice: { name: "Alice" },
                bob: { name: "Bob" }
            };
            console.log(users.alice.name);
            console.log(users.bob.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\nBob\n", output);
    }

    [Fact]
    public void Pick_KeyofPattern()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            type NameAge = Pick<Person, "name" | "age">;
            let p: NameAge = { name: "Test", age: 20 };
            console.log(p.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Test\n", output);
    }

    #endregion

    #region Array Suffix

    [Fact]
    public void Partial_WithArraySuffix()
    {
        var source = """
            interface Item { name: string; value: number; }
            let items: Partial<Item>[] = [
                { name: "first" },
                { value: 42 },
                {}
            ];
            console.log(items.length);
            console.log(items[0].name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\nfirst\n", output);
    }

    [Fact]
    public void Partial_WithMultiDimensionalArray()
    {
        var source = """
            interface Point { x: number; y: number; }
            let grid: Partial<Point>[][] = [
                [{ x: 1 }, { y: 2 }],
                [{ x: 3, y: 4 }]
            ];
            console.log(grid.length);
            console.log(grid[0].length);
            console.log(grid[1][0].x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n2\n3\n", output);
    }

    [Fact]
    public void NestedUtilityType_WithArraySuffix()
    {
        var source = """
            interface Config { host: string; port: number; }
            let configs: Partial<Readonly<Config>>[] = [
                { host: "localhost" },
                { port: 8080 }
            ];
            console.log(configs.length);
            console.log(configs[0].host);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\nlocalhost\n", output);
    }

    [Fact]
    public void Promise_WithArraySuffix()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            let promises: Promise<number>[] = [getValue(), getValue()];
            console.log(promises.length);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void UserDefinedGeneric_WithArraySuffix()
    {
        var source = """
            class Box<T> {
                value: T;
                constructor(v: T) { this.value = v; }
            }
            let boxes: Box<number>[] = [new Box<number>(1), new Box<number>(2), new Box<number>(3)];
            console.log(boxes.length);
            console.log(boxes[0].value);
            console.log(boxes[2].value);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n1\n3\n", output);
    }

    [Fact]
    public void Record_WithArraySuffix()
    {
        var source = """
            type StringMap = Record<string, number>;
            let maps: StringMap[] = [
                { a: 1, b: 2 },
                { x: 10 }
            ];
            console.log(maps.length);
            console.log(maps[0].a);
            console.log(maps[1].x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n1\n10\n", output);
    }

    [Fact]
    public void Pick_WithArraySuffix()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let names: Pick<Person, "name">[] = [
                { name: "Alice" },
                { name: "Bob" }
            ];
            console.log(names.length);
            console.log(names[1].name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\nBob\n", output);
    }

    [Fact]
    public void Omit_WithArraySuffix()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let people: Omit<Person, "email">[] = [
                { name: "Alice", age: 30 },
                { name: "Bob", age: 25 }
            ];
            console.log(people.length);
            console.log(people[0].name);
            console.log(people[1].age);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\nAlice\n25\n", output);
    }

    [Fact]
    public void Required_WithArraySuffix()
    {
        var source = """
            interface Config { name?: string; debug?: boolean; }
            let configs: Required<Config>[] = [
                { name: "app1", debug: true },
                { name: "app2", debug: false }
            ];
            console.log(configs.length);
            console.log(configs[0].name);
            console.log(configs[1].debug);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\napp1\nfalse\n", output);
    }

    [Fact]
    public void Readonly_WithArraySuffix()
    {
        var source = """
            interface Point { x: number; y: number; }
            let points: Readonly<Point>[] = [
                { x: 1, y: 2 },
                { x: 3, y: 4 }
            ];
            console.log(points.length);
            console.log(points[0].x);
            console.log(points[1].y);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n1\n4\n", output);
    }

    [Fact]
    public void GenericArray_TypeErrorStillDetected()
    {
        var source = """
            interface Item { name: string; }
            let items: Partial<Item>[] = [{ name: 123 }];
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region ReturnType<T>

    [Fact]
    public void ReturnType_ExtractsFromSimpleFunction()
    {
        var source = """
            function greet(): string { return "hello"; }
            type R = ReturnType<typeof greet>;
            let x: R = "world";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("world\n", output);
    }

    [Fact]
    public void ReturnType_ExtractsFromFunctionType()
    {
        var source = """
            type Fn = (x: number) => boolean;
            type R = ReturnType<Fn>;
            let result: R = true;
            console.log(result);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ReturnType_ExtractsFromArrowFunction()
    {
        var source = """
            const add = (a: number, b: number): number => a + b;
            type R = ReturnType<typeof add>;
            let sum: R = 42;
            console.log(sum);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ReturnType_WrongArgCount_Throws()
    {
        var source = """
            type R = ReturnType<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("ReturnType<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region Parameters<T>

    [Fact]
    public void Parameters_ExtractsFromSimpleFunction()
    {
        var source = """
            function add(a: number, b: number): number { return a + b; }
            type P = Parameters<typeof add>;
            let args: P = [1, 2];
            console.log(args[0]);
            console.log(args[1]);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void Parameters_ExtractsFromFunctionType()
    {
        var source = """
            type Fn = (name: string, age: number) => void;
            type P = Parameters<Fn>;
            let args: P = ["Alice", 30];
            console.log(args[0]);
            console.log(args[1]);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void Parameters_EmptyForNoArgs()
    {
        var source = """
            function noArgs(): void {}
            type P = Parameters<typeof noArgs>;
            let args: P = [];
            console.log(args.length);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Parameters_WrongArgCount_Throws()
    {
        var source = """
            type P = Parameters<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Parameters<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region ConstructorParameters<T>

    [Fact]
    public void ConstructorParameters_ExtractsFromClass()
    {
        var source = """
            class Person {
                constructor(public name: string, public age: number) {}
            }
            type CP = ConstructorParameters<typeof Person>;
            let args: CP = ["Bob", 25];
            console.log(args[0]);
            console.log(args[1]);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void ConstructorParameters_EmptyForNoArgs()
    {
        var source = """
            class Empty {}
            type CP = ConstructorParameters<typeof Empty>;
            let args: CP = [];
            console.log(args.length);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void ConstructorParameters_WrongArgCount_Throws()
    {
        var source = """
            type CP = ConstructorParameters<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("ConstructorParameters<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region InstanceType<T>

    [Fact]
    public void InstanceType_ExtractsFromClass()
    {
        var source = """
            class Animal {
                name: string = "unknown";
                speak(): string { return "..."; }
            }
            type I = InstanceType<typeof Animal>;
            let a: I = new Animal();
            console.log(a.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("unknown\n", output);
    }

    [Fact]
    public void InstanceType_WrongArgCount_Throws()
    {
        var source = """
            type I = InstanceType<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("InstanceType<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region ThisType<T>

    [Fact]
    public void ThisType_AcceptsType()
    {
        // ThisType<T> is a marker type that just returns T
        var source = """
            type T = ThisType<string>;
            let x: T = "hello";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void ThisType_WrongArgCount_Throws()
    {
        var source = """
            type T = ThisType<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("ThisType<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region Awaited<T>

    [Fact]
    public void Awaited_UnwrapsPromise()
    {
        var source = """
            type A = Awaited<Promise<string>>;
            let x: A = "resolved";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("resolved\n", output);
    }

    [Fact]
    public void Awaited_UnwrapsNestedPromise()
    {
        var source = """
            type A = Awaited<Promise<Promise<number>>>;
            let x: A = 42;
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Awaited_PassesThroughNonPromise()
    {
        var source = """
            type A = Awaited<string>;
            let x: A = "direct";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("direct\n", output);
    }

    [Fact]
    public void Awaited_WrongArgCount_Throws()
    {
        var source = """
            type A = Awaited<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Awaited<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region NonNullable<T>

    [Fact]
    public void NonNullable_RemovesNull()
    {
        var source = """
            type NN = NonNullable<string | null>;
            let x: NN = "hello";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void NonNullable_RemovesUndefined()
    {
        var source = """
            type NN = NonNullable<number | undefined>;
            let x: NN = 42;
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void NonNullable_RemovesBothNullAndUndefined()
    {
        var source = """
            type NN = NonNullable<string | null | undefined>;
            let x: NN = "test";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void NonNullable_PassesThroughNonNullable()
    {
        var source = """
            type NN = NonNullable<string>;
            let x: NN = "direct";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("direct\n", output);
    }

    [Fact]
    public void NonNullable_WrongArgCount_Throws()
    {
        var source = """
            type NN = NonNullable<string, number>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("NonNullable<T> requires exactly 1 type argument", ex.Message);
    }

    #endregion

    #region Extract<T, U>

    [Fact]
    public void Extract_FiltersByType()
    {
        var source = """
            type E = Extract<string | number | boolean, string | boolean>;
            let x: E = "hello";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Extract_FiltersByLiteral()
    {
        var source = """
            type Status = "pending" | "success" | "error";
            type SuccessStatus = Extract<Status, "success">;
            let x: SuccessStatus = "success";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("success\n", output);
    }

    [Fact]
    public void Extract_CanExtractBoolean()
    {
        var source = """
            type E = Extract<string | number | boolean, boolean>;
            let x: E = true;
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Extract_WrongArgCount_Throws()
    {
        var source = """
            type E = Extract<string>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Extract<T, U> requires exactly 2 type arguments", ex.Message);
    }

    #endregion

    #region Exclude<T, U>

    [Fact]
    public void Exclude_RemovesByType()
    {
        var source = """
            type E = Exclude<string | number | boolean, boolean>;
            let x: E = "hello";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Exclude_RemovesByLiteral()
    {
        var source = """
            type Status = "pending" | "success" | "error";
            type NonError = Exclude<Status, "error">;
            let x: NonError = "pending";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("pending\n", output);
    }

    [Fact]
    public void Exclude_RemovesNull()
    {
        var source = """
            type E = Exclude<string | null, null>;
            let x: E = "test";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void Exclude_WrongArgCount_Throws()
    {
        var source = """
            type E = Exclude<string>;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Exclude<T, U> requires exactly 2 type arguments", ex.Message);
    }

    #endregion

    #region Utility Type Composition

    [Fact]
    public void NonNullable_Extract_Composed()
    {
        var source = """
            type Mixed = string | number | null | undefined;
            type StringOnly = NonNullable<Extract<Mixed, string | null>>;
            let x: StringOnly = "hello";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Exclude_NonNullable_Composed()
    {
        var source = """
            type Mixed = string | number | boolean | null;
            type NoNullNoBoolean = NonNullable<Exclude<Mixed, boolean>>;
            let x: NoNullNoBoolean = "test";
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n", output);
    }

    #endregion
}
