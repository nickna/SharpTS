using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for index signatures (string, number, symbol). Runs against both interpreter and compiler.
/// </summary>
public class IndexSignatureTests
{
    #region String Index Signatures

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringIndex_BasicGetSet_Works(ExecutionMode mode)
    {
        var source = """
            interface StringMap {
                [key: string]: number;
            }
            let map: StringMap = {};
            map["one"] = 1;
            map["two"] = 2;
            console.log(map["one"]);
            console.log(map["two"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringIndex_WithExplicitProperties_Works(ExecutionMode mode)
    {
        var source = """
            interface Config {
                name: string;
                [key: string]: string;
            }
            let config: Config = { name: "app" };
            config["version"] = "1.0";
            console.log(config.name);
            console.log(config["version"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("app\n1.0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringIndex_DynamicKeyAccess_Works(ExecutionMode mode)
    {
        var source = """
            interface Dict {
                [key: string]: number;
            }
            let dict: Dict = {};
            let key = "dynamic";
            dict[key] = 42;
            console.log(dict[key]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Number Index Signatures

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumberIndex_BasicGetSet_Works(ExecutionMode mode)
    {
        var source = """
            interface NumberMap {
                [index: number]: string;
            }
            let map: NumberMap = {};
            map[0] = "zero";
            map[1] = "one";
            console.log(map[0]);
            console.log(map[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("zero\none\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumberIndex_WithExplicitProperties_Works(ExecutionMode mode)
    {
        var source = """
            interface ArrayLike {
                length: number;
                [index: number]: string;
            }
            let arr: ArrayLike = { length: 2 };
            arr[0] = "first";
            arr[1] = "second";
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\nfirst\nsecond\n", output);
    }

    #endregion

    #region Symbol Index Signatures

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolIndex_BasicGetSet_Works(ExecutionMode mode)
    {
        var source = """
            interface SymbolMap {
                [key: symbol]: string;
            }
            let sym = Symbol("myKey");
            let map: SymbolMap = {};
            map[sym] = "value";
            console.log(map[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolIndex_MultipleSymbols_Works(ExecutionMode mode)
    {
        var source = """
            interface SymbolMap {
                [key: symbol]: number;
            }
            let s1 = Symbol("first");
            let s2 = Symbol("second");
            let map: SymbolMap = {};
            map[s1] = 100;
            map[s2] = 200;
            console.log(map[s1]);
            console.log(map[s2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    #endregion

    #region Mixed Index Signatures

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MixedIndex_StringAndNumber_Works(ExecutionMode mode)
    {
        var source = """
            interface Mixed {
                [key: string]: string;
                [index: number]: string;
            }
            let obj: Mixed = {};
            obj["name"] = "test";
            obj[0] = "first";
            console.log(obj["name"]);
            console.log(obj[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\nfirst\n", output);
    }

    #endregion

    #region Plain Object Bracket Notation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PlainObject_BracketNotation_Works(ExecutionMode mode)
    {
        var source = """
            let obj = { name: "test", value: 42 };
            console.log(obj["name"]);
            console.log(obj["value"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PlainObject_DynamicKeyAccess_Works(ExecutionMode mode)
    {
        var source = """
            let obj = { a: 1, b: 2, c: 3 };
            let key = "b";
            console.log(obj[key]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PlainObject_SetWithBracket_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { [key: string]: number } = { a: 1 };
            obj["b"] = 2;
            obj["c"] = 3;
            console.log(obj["a"]);
            console.log(obj["b"]);
            console.log(obj["c"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Class Instance Bracket Notation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassInstance_BracketNotation_Works(ExecutionMode mode)
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
            let p = new Person("Alice", 30);
            console.log(p["name"]);
            console.log(p["age"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassInstance_SymbolKey_Works(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("secret");
            class Box {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            let box = new Box(42);
            box[sym] = "hidden";
            console.log(box.value);
            console.log(box[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhidden\n", output);
    }

    #endregion

    #region Inline Type Index Signature

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InlineType_IndexSignature_Works(ExecutionMode mode)
    {
        var source = """
            let dict: { [key: string]: number } = {};
            dict["x"] = 10;
            dict["y"] = 20;
            console.log(dict["x"]);
            console.log(dict["y"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    #endregion
}
