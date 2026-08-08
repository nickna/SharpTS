using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Symbol type support. Runs against both interpreter and compiler.
/// </summary>
public class SymbolTests
{
    #region Basic Symbol Creation

    [Theory, ModeData]
    public void Symbol_CreateWithoutDescription_Works(ExecutionMode mode)
    {
        var source = """
            let s = Symbol();
            console.log(typeof s === "symbol");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Symbol_CreateWithDescription_Works(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("mySymbol");
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(mySymbol)\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Fact]
    public void Symbol_AssignmentCreatesEnumerableProperty_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const key = Symbol("key");
            const obj: any = {};
            obj[key] = 1;
            const descriptor = Object.getOwnPropertyDescriptor(obj, key)!;
            console.log(descriptor.writable);
            console.log(descriptor.enumerable);
            console.log(descriptor.configurable);
            console.log(obj.propertyIsEnumerable(key));
            """);

        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Symbol_ArrayProperty_PreservesNull_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const key = Symbol("key");
            const values: any = [];
            values[key] = null;
            console.log(values[key] === null);
            """);

        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Symbol_DefinePropertyPreservesDescriptorFlags_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const key = Symbol("key");
            const obj: any = {};
            Object.defineProperty(obj, key, {
                value: 1,
                writable: false,
                enumerable: false,
                configurable: false
            });
            const descriptor = Object.getOwnPropertyDescriptor(obj, key)!;
            console.log(descriptor.value);
            console.log(descriptor.writable);
            console.log(descriptor.enumerable);
            console.log(descriptor.configurable);
            console.log(obj.propertyIsEnumerable(key));
            obj[key] = 2;
            console.log(obj[key]);
            console.log(delete obj[key]);
            """);

        Assert.Equal("1\nfalse\nfalse\nfalse\nfalse\n1\nfalse\n", output);
    }

    #endregion

    #region Symbol Type Annotation

    [Theory, ModeData]
    public void Symbol_TypeAnnotation_Works(ExecutionMode mode)
    {
        var source = """
            let s: symbol = Symbol("typed");
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(typed)\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Symbol_Iterator_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.iterator);
            console.log(Symbol.iterator !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Symbol_AsyncIterator_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.asyncIterator);
            console.log(Symbol.asyncIterator !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Symbol_ToStringTag_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.toStringTag);
            console.log(Symbol.toStringTag !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Symbol_HasInstance_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.hasInstance);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\n", output);
    }

    [Theory, ModeData]
    public void Symbol_ToPrimitive_Exists(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol.toPrimitive);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Symbol_SameSymbolEqualsItself(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("test");
            console.log(s === s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void SymbolKeyFor_ReturnsKeyForGlobalSymbol(ExecutionMode mode)
    {
        var source = """
            let s = Symbol.for("myKey");
            console.log(Symbol.keyFor(s));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myKey\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Symbol_Description_ReturnsDescription(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("myDesc");
            console.log(s.description);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myDesc\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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
    [Theory, ModeData]
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

    #region Symbol.prototype Surface (#237)

    [Theory, ModeData]
    public void Symbol_ToString_ReturnsDescriptiveString(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("d");
            console.log(s.toString());
            let noDesc = Symbol();
            console.log(noDesc.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(d)\nSymbol()\n", output);
    }

    [Theory, ModeData]
    public void Symbol_ValueOf_ReturnsSelf(ExecutionMode mode)
    {
        var source = """
            let s = Symbol("d");
            console.log(s.valueOf() === s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Symbol_StringCallForm_ReturnsDescriptiveString(ExecutionMode mode)
    {
        // ECMA-262 §22.1.1.1: the String() call form is exempt from ToString's
        // Symbol TypeError and returns SymbolDescriptiveString instead.
        var source = """
            console.log(String(Symbol("d")));
            console.log(String(Symbol()));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(d)\nSymbol()\n", output);
    }

    [Theory, ModeData]
    public void Symbol_AnyTyped_PrototypeMembers_Work(ExecutionMode mode)
    {
        // Dynamic-dispatch path (receiver statically `any`) must resolve the
        // same Symbol.prototype surface as the typed path.
        var source = """
            const s: any = Symbol("dyn");
            console.log(s.description);
            console.log(s.toString());
            console.log(s.valueOf() === s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("dyn\nSymbol(dyn)\ntrue\n", output);
    }

    #endregion

    #region Implicit Symbol-to-String Coercion Throws (#245)

    [Theory, ModeData]
    public void Symbol_TemplateLiteralInterpolation_ThrowsTypeError(ExecutionMode mode)
    {
        // ECMA-262 §7.1.17 ToString: implicit coercion of a Symbol throws.
        var source = """
            const s = Symbol("d");
            try {
                const t = `value: ${s}`;
                console.log("no throw", t);
            } catch (e) {
                console.log(e instanceof TypeError, e.message);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true Cannot convert a Symbol value to a string\n", output);
    }

    [Theory, ModeData]
    public void Symbol_StringPlusConcat_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            const s = Symbol("d");
            try {
                const t = "x" + (s as any);
                console.log("no throw", t);
            } catch (e) {
                console.log(e instanceof TypeError, e.message);
            }
            try {
                const t = (s as any) + "x";
                console.log("no throw", t);
            } catch (e) {
                console.log(e instanceof TypeError, e.message);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "true Cannot convert a Symbol value to a string\n" +
            "true Cannot convert a Symbol value to a string\n", output);
    }

    [Theory, ModeData]
    public void Symbol_PlusEqualConcat_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            const s = Symbol("d");
            let acc = "x";
            try {
                acc += (s as any);
                console.log("no throw", acc);
            } catch (e) {
                console.log(e instanceof TypeError, e.message);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true Cannot convert a Symbol value to a string\n", output);
    }

    [Theory, ModeData]
    public void Symbol_TemplateLiteralInAsyncFunction_ThrowsTypeError(ExecutionMode mode)
    {
        // The async/generator state-machine emitters build template literals
        // through a separate path than the sync emitter — cover it explicitly.
        var source = """
            const s = Symbol("d");
            async function f(): Promise<string> {
                await Promise.resolve();
                return `v: ${s}`;
            }
            async function main(): Promise<void> {
                try {
                    console.log(await f());
                } catch (e) {
                    console.log(e instanceof TypeError, e.message);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true Cannot convert a Symbol value to a string\n", output);
    }

    [Theory, ModeData]
    public void Symbol_ExplicitForms_StillStringify(ExecutionMode mode)
    {
        // Only implicit coercion throws: String(sym), sym.toString(), and
        // console.log(sym) formatting all keep returning "Symbol(d)".
        var source = """
            const s = Symbol("d");
            console.log(String(s));
            console.log(s.toString());
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Symbol(d)\nSymbol(d)\nSymbol(d)\n", output);
    }

    #endregion

    #region Symbol as First-Class Global (#234)

    [Theory, ModeData]
    public void Symbol_TypeofGlobal_IsFunction(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Symbol_TypeofCallResult_IsSymbol(ExecutionMode mode)
    {
        var source = """
            console.log(typeof Symbol("x"));
            console.log(typeof Symbol());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\nsymbol\n", output);
    }

    [Theory, ModeData]
    public void Symbol_Aliased_CallCreatesSymbol(ExecutionMode mode)
    {
        var source = """
            const f: any = Symbol;
            const s = f("aliased");
            console.log(typeof s);
            console.log(s.description);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol\naliased\n", output);
    }

    [Theory, ModeData]
    public void Symbol_Aliased_WellKnownIdentity(ExecutionMode mode)
    {
        var source = """
            const f: any = Symbol;
            console.log(f.species === Symbol.species);
            console.log(f.iterator === Symbol.iterator);
            console.log(typeof f.asyncIterator);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nsymbol\n", output);
    }

    [Theory, ModeData]
    public void Symbol_Aliased_ForAndKeyFor(ExecutionMode mode)
    {
        var source = """
            const f: any = Symbol;
            const shared = f.for("registry-key");
            console.log(shared === Symbol.for("registry-key"));
            console.log(f.keyFor(shared));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nregistry-key\n", output);
    }

    [Theory, ModeData]
    public void Symbol_CastExpression_MemberAccess(ExecutionMode mode)
    {
        var source = """
            console.log((Symbol as any).species === Symbol.species);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion
}
