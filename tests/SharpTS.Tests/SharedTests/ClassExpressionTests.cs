using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for class expressions (const C = class { ... }). Runs against both interpreter and compiler.
/// </summary>
public class ClassExpressionTests
{
    #region Anonymous Class Expressions

    [Theory, ModeData]
    public void AnonymousClassExpression_Basic(ExecutionMode mode)
    {
        var source = """
            const MyClass = class {
                x: number = 42;
            };
            let obj = new MyClass();
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AnonymousClassExpression_WithMethod(ExecutionMode mode)
    {
        var source = """
            const Counter = class {
                count: number = 0;
                increment(): number {
                    this.count = this.count + 1;
                    return this.count;
                }
            };
            let c = new Counter();
            c.increment();
            c.increment();
            console.log(c.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void AnonymousClassExpression_WithConstructor(ExecutionMode mode)
    {
        var source = """
            const Point = class {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            };
            let p = new Point(10, 20);
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    #endregion

    #region Named Class Expressions

    [Theory, ModeData]
    public void NamedClassExpression_Basic(ExecutionMode mode)
    {
        var source = """
            const Node = class Node {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            };
            let n = new Node(99);
            console.log(n.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Inheritance

    [Theory, ModeData]
    public void ClassExpression_WithInheritance(ExecutionMode mode)
    {
        var source = """
            const Base = class {
                getValue(): number { return 10; }
            };
            const Derived = class extends Base {
                getValue(): number { return 20; }
            };
            let b = new Base();
            let d = new Derived();
            console.log(b.getValue());
            console.log(d.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_SuperCall(ExecutionMode mode)
    {
        var source = """
            const Animal = class {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            };
            const Dog = class extends Animal {
                constructor(name: string) {
                    super(name);
                }
                bark(): string { return this.name + " barks"; }
            };
            let d = new Dog("Rex");
            console.log(d.bark());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Rex barks\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_InheritedMethod_NotOverridden(ExecutionMode mode)
    {
        // A child class expression that does NOT override a parent method must
        // still resolve it. Class-expression instances are anonymously typed, so
        // every call is dynamically dispatched through the compiled
        // GetProperty helper, which only covered a class's OWN members — an
        // inherited method resolved to undefined and threw. Now GetProperty
        // delegates to the base class. (#287 family)
        var source = """
            const Animal = class {
                constructor(public name: string) {}
                speak(): string { return this.name + " makes a sound"; }
            };
            const Dog = class extends Animal {
                constructor(name: string) { super(name); }
            };
            const d = new Dog("Fido");
            console.log(d.speak());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Fido makes a sound\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_MultiLevelInheritedMethod(ExecutionMode mode)
    {
        // Three-level class-expression chain: the grandchild inherits the
        // grandparent's method (using `this`), exercising recursive base-class
        // GetProperty delegation. (#287 family)
        var source = """
            const Animal = class {
                constructor(public name: string) {}
                speak(): string { return this.name + " sound"; }
            };
            const Dog = class extends Animal {
                constructor(name: string) { super(name); }
                speak(): string { return this.name + " barks"; }
            };
            const Puppy = class extends Dog {
                constructor() { super("Rex"); }
            };
            console.log(new Puppy().speak());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Rex barks\n", output);
    }

    #endregion

    #region Arrays and Variables

    [Theory, ModeData]
    public void ClassExpression_MultipleClassesInArray(ExecutionMode mode)
    {
        // Note: new <expression>() syntax isn't supported yet.
        // This test verifies class expressions can be stored and retrieved from arrays.
        var source = """
            const Class1 = class { value: number = 1; };
            const Class2 = class { value: number = 2; };
            let classes = [Class1, Class2];
            let obj1 = new Class1();
            let obj2 = new Class2();
            console.log(obj1.value);
            console.log(obj2.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_AssignedToVariable(ExecutionMode mode)
    {
        // Class expressions can be assigned to variables and instantiated
        var source = """
            const MyClass = class {
                value: number = 123;
            };
            let obj = new MyClass();
            console.log(obj.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("123\n", output);
    }

    #endregion

    #region Static Members

    [Theory, ModeData]
    public void ClassExpression_StaticField(ExecutionMode mode)
    {
        var source = """
            const MyClass = class {
                static count: number = 0;
            };
            console.log(MyClass.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_StaticMethod(ExecutionMode mode)
    {
        var source = """
            const Factory = class {
                static create(): number {
                    return 42;
                }
            };
            console.log(Factory.create());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Accessors (get / set)

    [Theory, ModeData]
    public void ClassExpression_Getter(ExecutionMode mode)
    {
        // Regression for #283: a named getter on a class expression returned undefined
        // in compiled mode because the class-expr GetProperty body never dispatched accessors.
        var source = """
            const Widget = class {
                constructor(public id: number) {}
                get label(): string { return "w" + this.id; }
                greet(): string { return "hi"; }
            };
            const w = new Widget(3);
            console.log(w.id);
            console.log(w.greet());
            console.log(w.label);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\nhi\nw3\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_Getter_DynamicAccess(ExecutionMode mode)
    {
        // Dynamic (bracket) access also routes through the class-expr GetProperty body (#283).
        var source = """
            const Widget = class {
                constructor(public id: number) {}
                get label(): string { return "w" + this.id; }
            };
            const w = new Widget(7);
            console.log((w as any)["label"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("w7\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_Setter(ExecutionMode mode)
    {
        // Symmetric gap (#283): the class-expr SetProperty body never dispatched setters,
        // so writes landed in _fields while reads kept hitting the getter.
        var source = """
            const Counter = class {
                private _n: number = 0;
                get n(): number { return this._n; }
                set n(v: number) { this._n = v * 2; }
            };
            const c = new Counter();
            c.n = 21;
            console.log(c.n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_Setter_DynamicAccess(ExecutionMode mode)
    {
        // Dynamic (bracket) assignment must invoke the setter in both modes. Compiled mode is
        // covered by #283's SetProperty dispatch; the interpreter's bracket path is fixed by #290.
        var source = """
            const Counter = class {
                private _n: number = 0;
                get n(): number { return this._n; }
                set n(v: number) { this._n = v * 2; }
            };
            const c = new Counter();
            (c as any)["n"] = 5;
            console.log((c as any)["n"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void ClassExpression_MethodBody_ResolvesCapturedTopLevelVariable(ExecutionMode mode)
    {
        // Regression for #300: a class-expression method or accessor body
        // referencing a top-level binding (a captured `let` here) threw
        // "ReferenceError: Undefined variable" in compiled mode —
        // CreateClassExpressionContext omitted the top-level-variable-access
        // wiring, the same gap #300 fixed for class-declaration accessors.
        var source = """
            let counter = 7;
            const Box = class {
                m(): string { return "m" + counter; }
                get tag(): string { return "t" + counter; }
            };
            const b = new Box();
            console.log(b.m());
            console.log(b.tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("m7\nt7\n", output);
    }

    #endregion

    #region Generic Class Expressions (#291)

    [Theory, ModeData]
    public void GenericClassExpression_InferredConstructorArg(ExecutionMode mode)
    {
        // Regression for #291: a generic class EXPRESSION rejected constructor
        // arguments whose type should be inferred for the class type parameter
        // (the type checker never substituted T, so `new Box("hello")` failed
        // type-checking). The identical generic class DECLARATION worked.
        var source = """
            const Box = class<T> {
                constructor(private value: T) {}
                get contents(): T { return this.value; }
            };
            const b = new Box("hello");
            console.log(b.contents);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void GenericClassExpression_ExplicitTypeArg(ExecutionMode mode)
    {
        // Explicit type argument on a generic class expression (`new Box<number>(42)`).
        var source = """
            const Box = class<T> {
                constructor(private value: T) {}
                get contents(): T { return this.value; }
            };
            const b = new Box<number>(42);
            console.log(b.contents);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Generator Methods (#765)

    [Theory, ModeData]
    public void ClassExpression_GeneratorMethod_Works(ExecutionMode mode)
    {
        // #765: a generator method in a class expression was emitted on the linear path and its
        // `yield` hit "Yield not supported in this context". It now routes through the generator
        // state machine like a class-declaration method.
        var source = """
            const C = class { *gen() { yield 1; yield 2; } };
            console.log([...new C().gen()].join(","));
            """;

        Assert.Equal("1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_GeneratorMethod_CapturesThis(ExecutionMode mode)
    {
        // Exercises `this`/constructor-parameter access and a loop inside a class-expression generator.
        var source = """
            const Range = class {
                constructor(public start: number, public end: number) {}
                *gen(): Generator<number> { for (let i = this.start; i < this.end; i++) yield i; }
            };
            console.log([...new Range(1, 5).gen()].join(","));
            """;

        Assert.Equal("1,2,3,4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_GeneratorMethod_YieldStar(ExecutionMode mode)
    {
        var source = """
            const C = class { *gen() { yield* [1, 2, 3]; } };
            console.log([...new C().gen()].join(","));
            """;

        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_StaticGeneratorMethod_Works(ExecutionMode mode)
    {
        var source = """
            const C = class { static *sg() { yield 7; yield 8; } };
            console.log([...C.sg()].join(","));
            """;

        Assert.Equal("7,8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_StaticGeneratorMethod_IgnoresExtraArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let effects = 0;
                const C = class { static *sg(value: number) { yield value; } };
                console.log([...C.sg(7, effects++, "ignored")].join(","));
                console.log(effects);
                """,
        };

        Assert.Equal(
            "7\n1\n",
            TestHarness.RunModules(files, "main.ts", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void ClassExpression_AsyncGeneratorMethod_Works(ExecutionMode mode)
    {
        var source = """
            const C = class { async *ag() { yield 1; yield 2; } };
            async function main() { for await (const x of new C().ag()) console.log(x); }
            main();
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_StaticAsyncGeneratorMethod_Works(ExecutionMode mode)
    {
        var source = """
            const C = class { static async *sag() { yield 5; yield 6; } };
            async function main() { for await (const x of C.sag()) console.log(x); }
            main();
            """;

        Assert.Equal("5\n6\n", TestHarness.Run(source, mode));
    }

    // #776: a class-EXPRESSION async (non-generator) method must compile to a real async state
    // machine returning a Promise. The compiled path previously emitted a synchronous stub that
    // returned the raw value, so `.then` threw (Double is not a Task) and an `await` in the body
    // emitted invalid IL. The interpreter and type-checker (#793) already handled these.
    [Theory, ModeData]
    public void ClassExpression_AsyncMethod_ThenReceivesValue(ExecutionMode mode)
    {
        var source = """
            const C = class { async m() { return 5; } };
            (new C().m() as Promise<number>).then(v => console.log(v));
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_StaticAsyncMethod_ThenReceivesValue(ExecutionMode mode)
    {
        var source = """
            const C = class { static async m() { return 7; } };
            (C.m() as Promise<number>).then(v => console.log(v));
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_AsyncMethod_AwaitsAndReadsInstanceField(ExecutionMode mode)
    {
        // A real suspension point (await) plus a `this` field read proves the state machine and
        // fieldsField wiring, not just a value passthrough.
        var source = """
            const C = class {
                v = 10;
                async m(n: number) { const x = await Promise.resolve(n); return x + this.v; }
            };
            async function main() { console.log(await new C().m(5)); }
            main();
            """;

        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Inferred Method Return Propagation (#793)

    // A class EXPRESSION's un-annotated method return must reach call sites, exactly as it does
    // for a class declaration (#658/#661/#687). Before #793 the inferred generator return came
    // back as Generator<Void>, so `for (const n of new C(...).gen()) sum += n` failed type-check
    // with "Compound assignment requires numeric operands".

    [Theory, ModeData]
    public void ClassExpression_InferredGeneratorMethodReturn_ReachesCallSite(ExecutionMode mode)
    {
        var source = """
            const Range = class {
                constructor(public start: number, public end: number) {}
                *gen() { for (let i = this.start; i < this.end; i++) yield i; }
            };
            let sum = 0;
            for (const n of new Range(1, 5).gen()) sum += n;
            console.log(sum);
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_InferredPlainMethodReturn_ReachesCallSite(ExecutionMode mode)
    {
        var source = """
            const Calc = class {
                add(a: number, b: number) { return a + b; }
            };
            const x: number = new Calc().add(2, 3);
            console.log(x * 2);
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassExpression_InferredAsyncMethodReturn_ReachesCallSite(ExecutionMode mode)
    {
        var source = """
            const Box = class {
                async getAsync(n: number) { return n + 1; }
            };
            async function main() {
                const v: number = await new Box().getAsync(4);
                console.log(v * 2);
            }
            main();
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    #endregion
}
