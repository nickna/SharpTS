using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for static class members (fields and methods). Runs against both interpreter and compiler.
/// </summary>
public class StaticMembersTests
{
    #region Static Fields

    [Theory, ModeData]
    public void StaticField_InitializedCorrectly(ExecutionMode mode)
    {
        var source = """
            class Config {
                static version: number = 42;
            }
            console.log(Config.version);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void StaticField_StringInitializer(ExecutionMode mode)
    {
        var source = """
            class Config {
                static name: string = "SharpTS";
            }
            console.log(Config.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("SharpTS\n", output);
    }

    [Theory, ModeData]
    public void StaticField_ModificationPersists(ExecutionMode mode)
    {
        var source = """
            class Counter {
                static count: number = 0;
            }
            console.log(Counter.count);
            Counter.count = 10;
            console.log(Counter.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n10\n", output);
    }

    #endregion

    #region Static Methods

    [Theory, ModeData]
    public void StaticMethod_CanBeCalled(ExecutionMode mode)
    {
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
            }
            console.log(Math2.square(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\n", output);
    }

    [Theory, ModeData]
    public void StaticMethod_CallsOtherStatic(ExecutionMode mode)
    {
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
                static cube(x: number): number {
                    return x * Math2.square(x);
                }
            }
            console.log(Math2.cube(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("27\n", output);
    }

    [Theory, ModeData]
    public void StaticMethod_ModifiesStaticField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                static count: number = 0;
                static increment(): number {
                    Counter.count = Counter.count + 1;
                    return Counter.count;
                }
            }
            console.log(Counter.increment());
            console.log(Counter.increment());
            console.log(Counter.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n2\n", output);
    }

    [Theory, ModeData]
    public void StaticMethod_WithParameters(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                static add(a: number, b: number): number {
                    return a + b;
                }
                static multiply(a: number, b: number): number {
                    return a * b;
                }
            }
            console.log(Calculator.add(3, 5));
            console.log(Calculator.multiply(4, 6));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n24\n", output);
    }

    #endregion

    #region Static and Instance Separation

    [Theory, ModeData]
    public void StaticAndInstance_AreSeparate(ExecutionMode mode)
    {
        var source = """
            class Box {
                static total: number = 0;
                value: number;
                constructor(v: number) {
                    this.value = v;
                    Box.total = Box.total + 1;
                }
            }
            let a: Box = new Box(10);
            let b: Box = new Box(20);
            console.log(a.value);
            console.log(b.value);
            console.log(Box.total);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n2\n", output);
    }

    #endregion

    #region Static Access Through Indirection (issue #57)

    // Direct `Foo.bar()` compiles to a Call IL instruction. Indirect access
    // (`const Alias = Foo; Alias.bar()` or `obj.Cls.bar()`) routes through the
    // dynamic property path — and prior to #57 that path didn't look up static
    // members on a Type value, so the binding silently became undefined and the
    // call returned null. Babel-transpiled CJS (minimatch, uuid, many others)
    // hits this through `const mod = require('./submodule'); mod.AST.fromGlob(...)`.

    [Theory, ModeData]
    public void StaticMethod_ThroughLocalAlias(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static bar(): number { return 42; }
            }
            const Alias: any = Foo;
            console.log(typeof Alias.bar);
            console.log(Alias.bar());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n42\n", output);
    }

    [Theory, ModeData]
    public void StaticMethod_ThroughObjectProperty(ExecutionMode mode)
    {
        var source = """
            class Foo {
                static bar(): number { return 42; }
                static baz(x: number): number { return x * 2; }
            }
            const container: any = { Cls: Foo };
            console.log(typeof container.Cls.bar);
            console.log(container.Cls.bar());
            console.log(container.Cls.baz(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n42\n10\n", output);
    }

    [Theory, ModeData]
    public void StaticField_ThroughLocalAlias(ExecutionMode mode)
    {
        var source = """
            class Config {
                static version: number = 42;
            }
            const Alias: any = Config;
            console.log(Alias.version);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Issue #58 — same-module export then reflective static call

    // In multi-module compilation the class-pre-define pass ran twice, creating two
    // MethodBuilders for every static method on the same TypeBuilder. The dict
    // overwrote with the second one and EmitStaticMethodBody filled THAT one — but
    // the first MethodBuilder remained on the type with no IL. Reflection picked
    // the body-less twin, so any reflective Invoke (the path triggered by reading
    // `exports.X.staticMethod` after the static-Type-lookup #57 fix) blew up with
    // BadImageFormatException. Fixed by per-name idempotency in DefineClassMethodsOnly.

    [Fact]
    public void StaticMethod_ExportedFromSameModule_Compiled()
    {
        // Use RunModules so the class lives inside a real CJS module body —
        // single-file mode doesn't trigger the duplicate pre-define pass.
        var files = new Dictionary<string, string>
        {
            ["./main.cjs"] = """
                class Foo {
                    static bar() { return 42; }
                }
                exports.Cls = Foo;
                console.log(exports.Cls.bar());
                """
        };

        var output = TestHarness.RunModules(files, "./main.cjs", ExecutionMode.Compiled);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StaticMethod_ExportedFromSameModule_MultipleStatics_Compiled()
    {
        // Multiple static methods + a non-class export — exercises the same
        // duplicate-pre-define path with a wider class shape.
        var files = new Dictionary<string, string>
        {
            ["./main.cjs"] = """
                class Foo {
                    static add(x, y) { return x + y; }
                    static mul(x, y) { return x * y; }
                }
                exports.Other = 42;
                exports.Cls = Foo;
                console.log(exports.Cls.add(2, 3));
                console.log(exports.Cls.mul(4, 5));
                """
        };

        var output = TestHarness.RunModules(files, "./main.cjs", ExecutionMode.Compiled);
        Assert.Equal("5\n20\n", output);
    }

    #endregion

    #region Inherited Static Members (#332)

    // Static members declared on a base user class are inherited by subclasses. In compiled mode the
    // .NET token references the *declaring* class, so resolution walks the superclass chain (#332).

    [Theory, ModeData]
    public void InheritedStatic_Method(ExecutionMode mode)
    {
        var source = """
            class Base {
                static greet(): string { return "hi"; }
            }
            class Sub extends Base {}
            console.log(Sub.greet());
            """;

        Assert.Equal("hi\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_Field(ExecutionMode mode)
    {
        var source = """
            class Base {
                static count: number = 5;
            }
            class Sub extends Base {}
            console.log(Sub.count);
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_Getter(ExecutionMode mode)
    {
        var source = """
            class Base {
                static get label(): string { return "L"; }
            }
            class Sub extends Base {}
            console.log(Sub.label);
            """;

        Assert.Equal("L\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_MultiLevelChain(ExecutionMode mode)
    {
        var source = """
            class A {
                static who(): string { return "A"; }
                static tag: string = "a";
            }
            class B extends A {}
            class C extends B {}
            console.log(C.who());
            console.log(C.tag);
            """;

        Assert.Equal("A\na\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_SubclassShadowWins(ExecutionMode mode)
    {
        var source = """
            class Parent {
                static who(): string { return "Parent"; }
            }
            class Child extends Parent {
                static who(): string { return "Child"; }
            }
            console.log(Child.who());
            console.log(Parent.who());
            """;

        Assert.Equal("Child\nParent\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_FromGenericBase(ExecutionMode mode)
    {
        var source = """
            class Box<T> {
                static kind: string = "box";
                static make(): string { return "made"; }
                static get tag(): string { return "T"; }
            }
            class IntBox extends Box<number> {}
            console.log(IntBox.make());
            console.log(IntBox.kind);
            console.log(IntBox.tag);
            """;

        Assert.Equal("made\nbox\nT\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_FieldWriteDoesNotCorruptBase(ExecutionMode mode)
    {
        // A static-field write through the subclass must not mutate the base's storage.
        var source = """
            class Base {
                static n: number = 1;
            }
            class Sub extends Base {}
            Sub.n = 42;
            console.log(Base.n);
            """;

        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_FieldWriteCreatesOwnShadow(ExecutionMode mode)
    {
        // Writing an inherited static field through a subclass creates a per-subclass own shadow:
        // the subclass reads the new value while the base keeps its own (#339).
        var source = """
            class Base { static n: number = 1; }
            class Sub extends Base {}
            Sub.n = 42;
            console.log(Base.n);
            console.log(Sub.n);
            """;

        Assert.Equal("1\n42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_ShadowReadBeforeWriteSeesBase(ExecutionMode mode)
    {
        // Before a subclass write, an inherited-field read resolves the live base value; after the
        // write it resolves the subclass's own shadow, leaving the base untouched (#339).
        var source = """
            class Base { static n: number = 1; }
            class Sub extends Base {}
            console.log(Sub.n);
            Base.n = 7;
            console.log(Sub.n);
            Sub.n = 42;
            console.log(Sub.n);
            Base.n = 9;
            console.log(Sub.n);
            console.log(Base.n);
            """;

        Assert.Equal("1\n7\n42\n42\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_ShadowNearestAncestorWins(ExecutionMode mode)
    {
        // A shadow on an intermediate ancestor is visible to a deeper subclass until that subclass
        // installs its own shadow (proto-chain order), without disturbing the base field (#339).
        var source = """
            class A { static tag: string = "a"; }
            class B extends A {}
            class C extends B {}
            B.tag = "b";
            console.log(C.tag);
            C.tag = "c";
            console.log(C.tag);
            console.log(B.tag);
            console.log(A.tag);
            """;

        Assert.Equal("b\nc\nb\na\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_ShadowSiblingsAreIsolated(ExecutionMode mode)
    {
        // Each subclass owns its shadow; a write through one sibling is invisible to the other and
        // to the base (#339).
        var source = """
            class P { static v: number = 0; }
            class X extends P {}
            class Y extends P {}
            X.v = 1;
            console.log(X.v);
            console.log(Y.v);
            console.log(P.v);
            """;

        Assert.Equal("1\n0\n0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_ShadowFromGenericBase(ExecutionMode mode)
    {
        // Writing an inherited static declared on a generic base creates the subclass shadow without
        // touching the (type-erased) base storage (#339).
        var source = """
            class Box<T> { static kind: string = "box"; }
            class IntBox extends Box<number> {}
            IntBox.kind = "intbox";
            console.log(IntBox.kind);
            console.log(Box.kind);
            """;

        Assert.Equal("intbox\nbox\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticField_OwnCompoundAndIncrementViaClassName(ExecutionMode mode)
    {
        // Read-modify-write of an own static field accessed as `Class.field` must land on the field
        // that `Class.field` reads back (compound and ++/--), not a divergent dynamic store.
        var source = """
            class Counter { static count: number = 10; }
            Counter.count += 3;
            console.log(Counter.count);
            Counter.count++;
            console.log(Counter.count);
            console.log(Counter.count++);
            console.log(Counter.count);
            console.log(--Counter.count);
            """;

        Assert.Equal("13\n14\n14\n15\n14\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStatic_CompoundAndIncrementCreateShadow(ExecutionMode mode)
    {
        // Read-modify-write through a subclass reads the inherited value, then writes the result to
        // the subclass's own shadow, leaving the base untouched (#339).
        var source = """
            class P { static c: number = 100; }
            class S extends P {}
            S.c += 5;
            console.log(S.c);
            console.log(P.c);
            S.c++;
            console.log(S.c);
            console.log(P.c);
            console.log(S.c++);
            console.log(S.c);
            """;

        Assert.Equal("105\n100\n106\n100\n106\n107\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Inherited Static Read Through Class-As-Value (#358)

    // A dynamic / value-position read (`(Sub as any).field`) of an inherited *declared* static must
    // resolve up the superclass chain even when no per-subclass own shadow exists yet. In compiled
    // mode this goes through the runtime System.Type reader, which previously probed only the
    // subclass's own declared statics and ancestor expando shadows — never an ancestor's *declared*
    // static (#358). The interpreter was already correct.

    [Theory, ModeData]
    public void InheritedStaticAsValue_DeclaredFieldPreShadow(ExecutionMode mode)
    {
        var source = """
            class Base { static n: number = 7; }
            class Sub extends Base {}
            const S: any = Sub;
            console.log(S.n);
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStaticAsValue_DeclaredMethod(ExecutionMode mode)
    {
        var source = """
            class Base { static greet(): string { return "hi"; } }
            class Sub extends Base {}
            const S: any = Sub;
            console.log(S.greet());
            """;

        Assert.Equal("hi\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStaticAsValue_MultiLevelChain(ExecutionMode mode)
    {
        var source = """
            class Base { static n: number = 7; static greet(): string { return "hi"; } }
            class Mid extends Base {}
            class Sub extends Mid {}
            const S: any = Sub;
            console.log(S.n);
            console.log(S.greet());
            """;

        Assert.Equal("7\nhi\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStaticAsValue_OwnShadowWinsOverDeclared(ExecutionMode mode)
    {
        // Once a per-subclass own shadow exists, the dynamic read returns it, not the base value.
        var source = """
            class Base { static m: number = 10; }
            class Sub extends Base {}
            (Sub as any).m = 99;
            console.log((Sub as any).m);
            console.log(Base.m);
            """;

        Assert.Equal("99\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStaticAsValue_AncestorExpandoWinsOverDeclared(ExecutionMode mode)
    {
        // An expando shadow written on an ancestor (shadow-before-declared) is seen through the
        // subclass before the ancestor's declared field — proto-chain order at each level.
        var source = """
            class Base { static k: number = 1; }
            class Sub extends Base {}
            (Base as any).k = 50;
            console.log((Sub as any).k);
            """;

        Assert.Equal("50\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Missing Static Read Through Class-As-Value (#398)

    // ECMA-262 §7.3.2 (Get): reading a *missing* own/inherited property returns `undefined` — it
    // never throws. A statically-typed `Klass.missing` is already a compile-time error (TS2339), so
    // these reads come through an `any`/value position. Compiled mode was already correct; the
    // interpreter previously threw "Static member 'x' does not exist on class 'y'." (#398).

    [Theory, ModeData]
    public void MissingStaticAsValue_OnSubclass(ExecutionMode mode)
    {
        var source = """
            class Base {}
            class Sub extends Base {}
            const S: any = Sub;
            console.log(S.nope);
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingStaticAsValue_OnPlainClass(ExecutionMode mode)
    {
        var source = """
            class C { static known: number = 1; }
            const K: any = C;
            console.log(K.known);
            console.log(K.missing);
            """;

        Assert.Equal("1\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingStaticAsValue_UsedInExpression(ExecutionMode mode)
    {
        // A missing read yielding `undefined` participates in normal coercion rather than aborting.
        var source = """
            class Base {}
            class Sub extends Base {}
            const S: any = Sub;
            console.log(S.nope === undefined);
            console.log(typeof S.nope);
            """;

        Assert.Equal("true\nundefined\n", TestHarness.Run(source, mode));
    }

    #endregion
}
