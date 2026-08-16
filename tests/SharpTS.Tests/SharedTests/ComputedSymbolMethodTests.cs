using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for computed symbol-keyed class methods — `class C { [Symbol.iterator]() {…} }`, the
/// generator form `*[Symbol.iterator]()` and the async form `async *[Symbol.asyncIterator]()`
/// (#592 parser/type-checker/interpreter; #647 compiled mode). Cases proven in both back ends use
/// <see cref="ExecutionModes.All"/>; cases pending compiled support use
/// <see cref="ExecutionModes.InterpretedOnly"/>.
/// </summary>
public class ComputedSymbolMethodTests
{
    [Theory, ModeData]
    public void ObjectReturningIterator_ForOf_Works(ExecutionMode mode)
    {
        var source = """
            class Range {
              [Symbol.iterator]() {
                let i = 0;
                return { next() { return i < 2 ? { value: i++, done: false } : { value: 0, done: true }; } };
              }
            }
            for (const x of new Range()) console.log(x);
            """;

        Assert.Equal("0\n1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorIterator_ForOfAndSpread_Works(ExecutionMode mode)
    {
        var source = """
            class Range { *[Symbol.iterator]() { yield 1; yield 2; } }
            for (const x of new Range()) console.log(x);
            console.log([...new Range()].length);
            """;

        Assert.Equal("1\n2\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorIterator_CapturesThis(ExecutionMode mode)
    {
        var source = """
            class Range {
              constructor(private start: number, private end: number) {}
              *[Symbol.iterator](): Iterator<number> {
                for (let i = this.start; i < this.end; i++) yield i;
              }
            }
            let sum = 0;
            for (const n of new Range(1, 5)) sum += n;
            console.log(sum);
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorIterator_ElementTypeIsNumber(ExecutionMode mode)
    {
        // The spread must yield number[], not Iterator<number>[] — the factory's iterator
        // return type must not be double-wrapped into Generator<Iterator<number>>.
        var source = """
            class Range { *[Symbol.iterator](): Iterator<number> { yield 1; yield 2; yield 3; } }
            const arr: number[] = [...new Range()];
            console.log(arr.length);
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    // #791: non-symbol computed keys (string/number) fold to a string-named method. Bodies emit as
    // synthetic $symmethod_N and register in the symbol-method registry under the property-key string;
    // named/string-index access consults the registry as a fallback in $IHasFields.GetProperty.
    [Theory, ModeData]
    public void NonSymbolComputedMethodKey_FoldsToNamedMethod(ExecutionMode mode)
    {
        var source = """
            const KEY = "dyn";
            class C { [KEY]() { return 42; } }
            console.log((new C() as any).dyn());
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StringLiteralComputedMethodKey_BothAccessForms(ExecutionMode mode)
    {
        // Literal key, accessed both by name and by string index.
        var source = """
            class C { ["dyn"]() { return 42; } }
            const c = new C() as any;
            console.log(c.dyn());
            console.log(c["dyn"]());
            """;

        Assert.Equal("42\n42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MutableLocalComputedMethodKey_FoldsToNamedMethod(ExecutionMode mode)
    {
        // Fully dynamic form: the key is a mutable local, unknowable at compile time.
        var source = """
            let k = "dyn";
            class C { [k]() { return 42; } }
            console.log((new C() as any).dyn());
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericComputedMethodKey_FoldsToStringKey(ExecutionMode mode)
    {
        // A numeric key folds to its property-key string ("1"), reachable via either index form.
        var source = """
            class C { [1]() { return 7; } }
            const c = new C() as any;
            console.log(c[1]());
            console.log(c["1"]());
            """;

        Assert.Equal("7\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedComputedMethodKey_ResolvesOnSubclass(ExecutionMode mode)
    {
        // A computed key declared on the base resolves through the subclass via the base-chain walk.
        var source = """
            class Base { ["inh"]() { return 99; } }
            class Derived extends Base {}
            console.log((new Derived() as any).inh());
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorIterator_ForAwait_Works(ExecutionMode mode)
    {
        var source = """
            class ARange { async *[Symbol.asyncIterator]() { yield 10; yield 20; } }
            async function main() {
              for await (const x of new ARange()) console.log(x);
            }
            main();
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericIterableClass_Works(ExecutionMode mode)
    {
        // Exercises the symbol registry's generic-class handling (registry keyed by the open
        // definition; receiver/MethodInfo closed for invoke).
        var source = """
            class Box<T> {
              constructor(private items: T[]) {}
              *[Symbol.iterator](): Iterator<T> { for (const x of this.items) yield x; }
            }
            for (const n of new Box<number>([1, 2, 3])) console.log(n);
            """;

        Assert.Equal("1\n2\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedComputedIterator_Works(ExecutionMode mode)
    {
        // A subclass inherits the base's [Symbol.iterator] via the registry's base-chain walk.
        var source = """
            class Base { *[Symbol.iterator]() { yield 1; yield 2; } }
            class Derived extends Base {}
            console.log([...new Derived()].length);
            for (const x of new Derived()) console.log(x);
            """;

        Assert.Equal("2\n1\n2\n", TestHarness.Run(source, mode));
    }

    // Reading a symbol method as a value (`obj[Symbol.iterator]`) returns a receiver-bound callable in
    // both back ends, so a standalone `obj[Symbol.iterator]()` call keeps `this` (#755 sub-case 1). In
    // compiled mode the bracket-get wraps the method in `$TSFunction(obj, method)`, mirroring the
    // string-key method path. for...of / spread / for-await (which pass the receiver themselves) were
    // already unaffected.
    [Theory, ModeData]
    public void DirectSymbolMethodAccess_ReturnsCallable(ExecutionMode mode)
    {
        var source = """
            class R { *[Symbol.iterator]() { yield 5; } }
            const it = (new R() as any)[Symbol.iterator]();
            console.log(it.next().value);
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    // Class *expressions* now wire computed symbol-keyed methods through the same synthetic-method +
    // registry path as class declarations, including the generator state machine (#755 sub-case 2,
    // building on the class-expression generator routing in #765).
    [Theory, ModeData]
    public void ClassExpression_ComputedSymbolMethod_Works(ExecutionMode mode)
    {
        var source = """
            const Range = class { *[Symbol.iterator]() { yield 7; yield 8; } };
            for (const x of new Range()) console.log(x);
            """;

        Assert.Equal("7\n8\n", TestHarness.Run(source, mode));
    }

    // Class expression carrying an async computed symbol method (`async *[Symbol.asyncIterator]()`),
    // consumed with for-await — exercises the class-expression async-generator + symbol-registry
    // paths together (#755 sub-case 2).
    [Theory, ModeData]
    public void ClassExpression_AsyncComputedSymbolMethod_Works(ExecutionMode mode)
    {
        var source = """
            const A = class { async *[Symbol.asyncIterator]() { yield 10; yield 20; } };
            async function main() {
              for await (const x of new A()) console.log(x);
            }
            main();
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void PlainClass_StillNotIterable_ReportsError()
    {
        // A class with no [Symbol.iterator] is still rejected as non-iterable (TS2488 / #593).
        var source = """
            class Plain { x = 1; }
            for (const y of new Plain()) console.log(y);
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // #757: object literals accept the generator/async method modifiers (`*`, `async`, `async *`),
    // including the computed symbol-keyed form, which previously failed to parse ("Expect property
    // name"). The class-body parser already handled these (#592); this is the object-literal parser.
    // Tests are kept free of dynamic `this` inside the generator (object generator methods lose
    // `this` in both modes — pre-existing #775).

    [Theory, ModeData]
    public void ObjectLiteral_ComputedGeneratorMethod_Iterates(ExecutionMode mode)
    {
        var source = """
            const o = { *[Symbol.iterator]() { yield 1; yield 2; } };
            for (const x of o) console.log(x);
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectLiteral_NamedGeneratorMethod_Iterates(ExecutionMode mode)
    {
        var source = """
            const o = { *gen() { yield 10; yield 20; } };
            for (const x of o.gen()) console.log(x);
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectLiteral_AsyncMethod_Resolves(ExecutionMode mode)
    {
        var source = """
            const o = { async foo() { return 5; } };
            o.foo().then(v => console.log("async:", v));
            """;

        Assert.Equal("async: 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectLiteral_AsyncGeneratorMethod_Iterates(ExecutionMode mode)
    {
        var source = """
            const o = { async *ag() { yield 1; yield 2; } };
            async function main() { for await (const x of o.ag()) console.log("ag:", x); }
            main();
            """;

        Assert.Equal("ag: 1\nag: 2\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ObjectLiteral_AsyncAsPropertyName_StillParses()
    {
        // `async` is a method/property modifier only before a property-name start or `*`; otherwise
        // it remains an ordinary property name ({ async }, { async: 1 }, { async() {} }).
        var source = """
            const a = { async: 1 };
            const b = { async() { return 2; } };
            console.log(a.async, b.async());
            """;

        Assert.Equal("1 2\n", TestHarness.RunInterpreted(source));
    }
}
