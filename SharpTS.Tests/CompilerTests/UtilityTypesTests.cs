using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for TypeScript utility types: Partial, Required, Readonly, Record, Pick, Omit.
/// Compiler parity tests - same as InterpreterTests but using RunCompiled.
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Bob\n30\n", output);
    }

    [Fact]
    public void Partial_WrongArgCount_Throws()
    {
        var source = """
            let x: Partial<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("app\ntrue\n", output);
    }

    [Fact]
    public void Required_MissingProperty_Throws()
    {
        var source = """
            interface Config { name?: string; debug?: boolean; }
            let c: Required<Config> = { name: "app" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("debug", ex.Message);
    }

    [Fact]
    public void Required_WrongArgCount_Throws()
    {
        var source = """
            let x: Required<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Readonly_WrongArgCount_Throws()
    {
        var source = """
            let x: Readonly<string, number> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void Record_WrongArgCount_Throws()
    {
        var source = """
            let x: Record<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Record<K, V> requires exactly 2 type arguments", ex.Message);
    }

    [Fact]
    public void Record_MissingKey_Throws()
    {
        var source = """
            type Keys = "a" | "b";
            let obj: Record<Keys, number> = { a: 1 };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void Pick_ExtraProperty_Throws()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let nameOnly: Pick<Person, "name"> = { name: "Bob", age: 25 };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Pick_WrongArgCount_Throws()
    {
        var source = """
            let x: Pick<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Bob\n25\n", output);
    }

    [Fact]
    public void Omit_ExcludedKeyNotAllowed()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let noEmail: Omit<Person, "email"> = { name: "Bob", age: 25, email: "test@test.com" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("email", ex.Message);
    }

    [Fact]
    public void Omit_MissingRequiredKey_Throws()
    {
        var source = """
            interface Person { name: string; age: number; email: string; }
            let noEmail: Omit<Person, "email"> = { name: "Bob" };
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("app\n", output);
    }

    [Fact]
    public void Omit_WrongArgCount_Throws()
    {
        var source = """
            let x: Omit<string> = {};
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Test\n", output);
    }

    #endregion
}
