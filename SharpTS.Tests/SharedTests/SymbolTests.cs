using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Symbol type support. Runs against both interpreter and compiler.
/// </summary>
public class SymbolTests
{
    #region Basic Symbol Creation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_CreateWithoutDescription_Works(ExecutionMode mode)
    {
        var source = """
            let s = Symbol();
            console.log(typeof s === "symbol");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_CreateWithDescription_Works(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("mySymbol");
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(mySymbol)\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_Uniqueness_Works(ExecutionMode mode)
    {
        var source = """
            let s1 = Symbol("test");
            let s2 = Symbol("test");
            console.log(s1 === s2);
            console.log(s1 !== s2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    #endregion

    #region Symbol as Object Key

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_AsObjectKey_Works(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            obj[sym] = "hello";
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_MultipleSymbolKeys_Works(ExecutionMode mode)
    {
        var source = """
            let sym1 = Symbol("first");
            let sym2 = Symbol("second");
            let obj: { [key: symbol]: number } = {};
            obj[sym1] = 10;
            obj[sym2] = 20;
            console.log(obj[sym1]);
            console.log(obj[sym2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_ObjectKey_OverwriteValue(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: number } = {};
            obj[sym] = 10;
            obj[sym] = 20;
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_ObjectKey_CoexistsWithStringKey(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("name");
            let obj: any = {};
            obj["name"] = "string key";
            obj[sym] = "symbol key";
            console.log(obj["name"]);
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("string key\nsymbol key\n", output);
    }

    #endregion

    #region Symbol Type Annotation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_TypeAnnotation_Works(ExecutionMode mode)
    {
        var source = """
            let s: symbol = Symbol("typed");
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(typed)\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_InFunction_Works(ExecutionMode mode)
    {
        var source = """
            function createSymbol(name: string): symbol {
                return Symbol(name);
            }
            let s = createSymbol("func");
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(func)\n", output);
    }

    #endregion

    #region Well-Known Symbols

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_Iterator_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.iterator);
            console.log(Symbol.iterator !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_AsyncIterator_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.asyncIterator);
            console.log(Symbol.asyncIterator !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_ToStringTag_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.toStringTag);
            console.log(Symbol.toStringTag !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_HasInstance_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.hasInstance);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_ToPrimitive_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.toPrimitive);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_WellKnown_AreUnique(ExecutionMode mode)
    {
        var source = """
            console.log(Symbol.iterator === Symbol.asyncIterator);
            console.log(Symbol.iterator === Symbol.toStringTag);
            console.log(Symbol.iterator === Symbol.iterator);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\ntrue\n", output);
    }

    #endregion

    #region Symbol Identity and Equality

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_SameSymbolEqualsItself(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("test");
            console.log(s === s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_StoredInVariable_MaintainsIdentity(ExecutionMode mode)
    {
        var source = """
            let s1 = Symbol("test");
            let s2 = s1;
            console.log(s1 === s2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_PassedToFunction_MaintainsIdentity(ExecutionMode mode)
    {
        var source = """
            function check(a: symbol, b: symbol): boolean {
                return a === b;
            }
            let s = Symbol("test");
            console.log(check(s, s));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Symbol.for() and Symbol.keyFor() - Global Registry

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolFor_ReturnsSameSymbolForSameKey(ExecutionMode mode)
    {
        var source = """
            let s1 = Symbol.for("shared");
            let s2 = Symbol.for("shared");
            console.log(s1 === s2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolFor_ReturnsDifferentSymbolsForDifferentKeys(ExecutionMode mode)
    {
        var source = """
            let s1 = Symbol.for("key1");
            let s2 = Symbol.for("key2");
            console.log(s1 === s2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolFor_DifferentFromRegularSymbol(ExecutionMode mode)
    {
        var source = """
            let globalSym = Symbol.for("test");
            let local = Symbol("test");
            console.log(globalSym === local);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolKeyFor_ReturnsKeyForGlobalSymbol(ExecutionMode mode)
    {
        var source = """
            let s = Symbol.for("myKey");
            console.log(Symbol.keyFor(s));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myKey\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolKeyFor_ReturnsUndefinedForLocalSymbol(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("local");
            let key = Symbol.keyFor(s);
            console.log(key === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SymbolKeyFor_WellKnownSymbolsNotInRegistry(ExecutionMode mode)
    {
        var source = """
            let key = Symbol.keyFor(Symbol.iterator);
            console.log(key === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Symbol Description Property

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_Description_ReturnsDescription(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("myDesc");
            console.log(s.description);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myDesc\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_Description_UndefinedWhenNoDescription(ExecutionMode mode)
    {
        var source = """
            let s = Symbol();
            console.log(s.description);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region Symbol Object Property Operations (Not Yet Implemented)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_ObjectKey_DeleteProperty(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            obj[sym] = "value";
            console.log(obj[sym]);
            delete obj[sym];
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\nundefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_InOperator_Works(ExecutionMode mode)
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            console.log(sym in obj);
            obj[sym] = "value";
            console.log(sym in obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    #endregion

    #region Symbol in Classes (Not Yet Supported)

    /// <summary>
    /// Tests computed property names with symbols in class fields.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Symbol_AsClassPropertyKey(ExecutionMode mode)
    {
        var source = """
            const mySymbol = Symbol("myProp");

            class MyClass {
                [mySymbol]: string = "initial";

                getValue(): string {
                    return this[mySymbol];
                }

                setValue(v: string): void {
                    this[mySymbol] = v;
                }
            }

            let instance = new MyClass();
            console.log(instance.getValue());
            instance.setValue("updated");
            console.log(instance.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("initial\nupdated\n", output);
    }

    #endregion
}
