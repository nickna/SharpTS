using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class SymbolTests
{
    [Fact]
    public void Symbol_CreateWithoutDescription_Works()
    {
        var source = """
            let s = Symbol();
            console.log(typeof s === "symbol");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Symbol_CreateWithDescription_Works()
    {
        var source = """
            let s = Symbol("mySymbol");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(mySymbol)\n", output);
    }

    [Fact]
    public void Symbol_Uniqueness_Works()
    {
        var source = """
            let s1 = Symbol("test");
            let s2 = Symbol("test");
            console.log(s1 === s2);
            console.log(s1 !== s2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Symbol_AsObjectKey_Works()
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            obj[sym] = "hello";
            console.log(obj[sym]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Symbol_MultipleSymbolKeys_Works()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void Symbol_TypeAnnotation_Works()
    {
        var source = """
            let s: symbol = Symbol("typed");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(typed)\n", output);
    }

    [Fact]
    public void Symbol_InFunction_Works()
    {
        var source = """
            function createSymbol(name: string): symbol {
                return Symbol(name);
            }
            let s = createSymbol("func");
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Symbol(func)\n", output);
    }

    #region Symbol.for() and Symbol.keyFor() - Global Registry

    [Fact(Skip = "Symbol.for() call syntax not yet working in interpreter")]
    public void SymbolFor_ReturnsSameSymbolForSameKey()
    {
        var source = """
            let s1 = Symbol.for("shared");
            let s2 = Symbol.for("shared");
            console.log(s1 === s2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact(Skip = "Symbol.for() call syntax not yet working in interpreter")]
    public void SymbolFor_ReturnsDifferentSymbolsForDifferentKeys()
    {
        var source = """
            let s1 = Symbol.for("key1");
            let s2 = Symbol.for("key2");
            console.log(s1 === s2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact(Skip = "Symbol.for() call syntax not yet working in interpreter")]
    public void SymbolFor_DifferentFromRegularSymbol()
    {
        var source = """
            let global = Symbol.for("test");
            let local = Symbol("test");
            console.log(global === local);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact(Skip = "Symbol.keyFor() call syntax not yet working in interpreter")]
    public void SymbolKeyFor_ReturnsKeyForGlobalSymbol()
    {
        var source = """
            let s = Symbol.for("myKey");
            console.log(Symbol.keyFor(s));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("myKey\n", output);
    }

    [Fact(Skip = "Symbol.keyFor() call syntax not yet working in interpreter")]
    public void SymbolKeyFor_ReturnsUndefinedForLocalSymbol()
    {
        var source = """
            let s = Symbol("local");
            console.log(Symbol.keyFor(s));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region Well-Known Symbols

    [Fact]
    public void Symbol_Iterator_Exists()
    {
        var source = """
            console.log(typeof Symbol.iterator);
            console.log(Symbol.iterator !== undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Fact]
    public void Symbol_AsyncIterator_Exists()
    {
        var source = """
            console.log(typeof Symbol.asyncIterator);
            console.log(Symbol.asyncIterator !== undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Fact]
    public void Symbol_ToStringTag_Exists()
    {
        var source = """
            console.log(typeof Symbol.toStringTag);
            console.log(Symbol.toStringTag !== undefined);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Fact]
    public void Symbol_HasInstance_Exists()
    {
        var source = """
            console.log(typeof Symbol.hasInstance);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("symbol\n", output);
    }

    [Fact]
    public void Symbol_ToPrimitive_Exists()
    {
        var source = """
            console.log(typeof Symbol.toPrimitive);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("symbol\n", output);
    }

    [Fact]
    public void Symbol_WellKnown_AreUnique()
    {
        var source = """
            console.log(Symbol.iterator === Symbol.asyncIterator);
            console.log(Symbol.iterator === Symbol.toStringTag);
            console.log(Symbol.iterator === Symbol.iterator);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\nfalse\ntrue\n", output);
    }

    #endregion

    #region Symbol Description Property

    [Fact(Skip = "Symbol.description property not yet accessible in interpreter")]
    public void Symbol_Description_ReturnsDescription()
    {
        var source = """
            let s = Symbol("myDesc");
            console.log(s.description);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("myDesc\n", output);
    }

    [Fact(Skip = "Symbol.description property not yet accessible in interpreter")]
    public void Symbol_Description_UndefinedWhenNoDescription()
    {
        var source = """
            let s = Symbol();
            console.log(s.description);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region Symbol Identity and Equality

    [Fact]
    public void Symbol_SameSymbolEqualsItself()
    {
        var source = """
            let s = Symbol("test");
            console.log(s === s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Symbol_StoredInVariable_MaintainsIdentity()
    {
        var source = """
            let s1 = Symbol("test");
            let s2 = s1;
            console.log(s1 === s2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Symbol_PassedToFunction_MaintainsIdentity()
    {
        var source = """
            function check(a: symbol, b: symbol): boolean {
                return a === b;
            }
            let s = Symbol("test");
            console.log(check(s, s));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Symbol as Object Key - Advanced

    [Fact]
    public void Symbol_ObjectKey_OverwriteValue()
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: number } = {};
            obj[sym] = 10;
            obj[sym] = 20;
            console.log(obj[sym]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Symbol_ObjectKey_CoexistsWithStringKey()
    {
        var source = """
            let sym = Symbol("name");
            let obj: any = {};
            obj["name"] = "string key";
            obj[sym] = "symbol key";
            console.log(obj["name"]);
            console.log(obj[sym]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("string key\nsymbol key\n", output);
    }

    [Fact(Skip = "delete operator with symbol keys not working in interpreter")]
    public void Symbol_ObjectKey_DeleteProperty()
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            obj[sym] = "value";
            console.log(obj[sym]);
            delete obj[sym];
            console.log(obj[sym]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("value\nundefined\n", output);
    }

    [Fact(Skip = "'in' operator with symbol keys not working in interpreter")]
    public void Symbol_InOperator_Works()
    {
        var source = """
            let sym = Symbol("key");
            let obj: { [key: symbol]: string } = {};
            console.log(sym in obj);
            obj[sym] = "value";
            console.log(sym in obj);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntrue\n", output);
    }

    #endregion

    #region Symbol in Classes

    [Fact(Skip = "Computed property names with symbols in classes not working in interpreter")]
    public void Symbol_AsClassPropertyKey()
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

            let obj = new MyClass();
            console.log(obj.getValue());
            obj.setValue("updated");
            console.log(obj.getValue());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("initial\nupdated\n", output);
    }

    #endregion
}
