using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for object features including property shorthand, method shorthand,
/// rest pattern, Object.keys/values/entries, computed properties, and more.
/// Runs against both interpreter and compiler.
/// </summary>
public class ObjectFeatureTests
{
    // Property Shorthand
    [Theory, ModeData]
    public void Object_PropertyShorthand_Works(ExecutionMode mode)
    {
        var source = """
            let name: string = "Alice";
            let age: number = 30;
            let obj: { name: string, age: number } = { name, age };
            console.log(obj.name);
            console.log(obj.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory, ModeData]
    public void Object_MixedShorthandAndExplicit_Works(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            let obj: { x: number, y: number } = { x, y: 20 };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Method Shorthand
    [Theory, ModeData]
    public void Object_MethodShorthand_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { add(a: number, b: number): number } = {
                add(a: number, b: number): number {
                    return a + b;
                }
            };
            console.log(obj.add(3, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodWithDefaultParams_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { greet(name: string): string } = {
                greet(name: string = "World"): string {
                    return "Hello, " + name;
                }
            };
            console.log(obj.greet("Alice"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, Alice\n", output);
    }

    // Object Rest Pattern
    [Theory, ModeData]
    public void Object_RestPattern_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };
            let { x, ...rest }: { x: number, y: number, z: number } = obj;
            console.log(x);
            console.log(rest.y);
            console.log(rest.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_RestPattern_MultipleExtracted_Works(ExecutionMode mode)
    {
        var source = """
            let data: { id: number, name: string, age: number, city: string } = { id: 1, name: "Alice", age: 30, city: "NYC" };
            let { id, name, ...others }: { id: number, name: string, age: number, city: string } = data;
            console.log(id);
            console.log(name);
            console.log(others.age);
            console.log(others.city);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nAlice\n30\nNYC\n", output);
    }

    // Object.keys
    [Theory, ModeData]
    public void Object_Keys_ReturnsPropertyNames(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let keys: string[] = Object.keys(obj);
            console.log(keys.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // Object.values
    [Theory, ModeData]
    public void Object_Values_ReturnsPropertyValues(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            console.log(values[0]);
            console.log(values[1]);
            console.log(values[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_Values_WithMixedTypes(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string, age: number, active: boolean } = { name: "Alice", age: 30, active: true };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // Object.entries
    [Theory, ModeData]
    public void Object_Entries_ReturnsKeyValuePairs(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let entries: any[] = Object.entries(obj);
            console.log(entries.length);
            console.log(entries[0][0]);
            console.log(entries[0][1]);
            console.log(entries[1][0]);
            console.log(entries[1][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\na\n1\nb\n2\n", output);
    }

    // Object.keys on class instance
    [Theory, ModeData]
    public void Object_Keys_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let keys: string[] = Object.keys(p);
            console.log(keys.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Object.values on class instance
    [Theory, ModeData]
    public void Object_Values_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let values: any[] = Object.values(p);
            console.log(values.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Object.entries on class instance
    [Theory, ModeData]
    public void Object_Entries_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let entries: any[] = Object.entries(p);
            console.log(entries.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    // Empty Object
    [Theory, ModeData]
    public void Object_Empty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            console.log(typeof obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    // Nested Object Literals
    [Theory, ModeData]
    public void Object_Nested_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { outer: { inner: number } } = { outer: { inner: 42 } };
            console.log(obj.outer.inner);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // Object Property Assignment
    [Theory, ModeData]
    public void Object_PropertyAssignment_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number } = { x: 1 };
            obj.x = 10;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    // Object with Array Property
    [Theory, ModeData]
    public void Object_WithArrayProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { items: number[] } = { items: [1, 2, 3] };
            console.log(obj.items.length);
            console.log(obj.items[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n", output);
    }

    // Computed Property Names
    [Theory, ModeData]
    public void Object_ComputedPropertyName_VariableKey(ExecutionMode mode)
    {
        var source = """
            let key: string = "dynamicKey";
            let obj: any = { [key]: 42 };
            console.log(obj["dynamicKey"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Object_ComputedPropertyName_StringConcatenation(ExecutionMode mode)
    {
        var source = """
            let prefix: string = "prop";
            let obj: any = { [prefix + "1"]: "one", [prefix + "2"]: "two" };
            console.log(obj["prop1"]);
            console.log(obj["prop2"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("one\ntwo\n", output);
    }

    [Theory, ModeData]
    public void Object_StringLiteralKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { "string-key": "hello", "another key": "world" };
            console.log(obj["string-key"]);
            console.log(obj["another key"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nworld\n", output);
    }

    [Theory, ModeData]
    public void Object_NumberLiteralKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { 123: "numeric key", 456: "another" };
            console.log(obj["123"]);
            console.log(obj["456"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("numeric key\nanother\n", output);
    }

    [Theory, ModeData]
    public void Object_MixedStaticAndComputedKeys(ExecutionMode mode)
    {
        var source = """
            let key: string = "computed";
            let obj: any = { regular: 1, [key]: 2, "literal": 3 };
            console.log(obj.regular);
            console.log(obj["computed"]);
            console.log(obj["literal"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_ComputedPropertyName_NumberKey(ExecutionMode mode)
    {
        var source = """
            let idx: number = 42;
            let obj: any = { [idx]: "value at 42" };
            console.log(obj["42"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value at 42\n", output);
    }

    [Theory, ModeData]
    public void Object_ComputedPropertyName_SymbolKey(ExecutionMode mode)
    {
        var source = """
            let sym: symbol = Symbol("myKey");
            let obj: any = { [sym]: "symbol value" };
            console.log(obj[sym]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("symbol value\n", output);
    }

    [Theory, ModeData]
    public void Object_ComputedPropertyName_WithSpread(ExecutionMode mode)
    {
        var source = """
            let key: string = "added";
            let base: { x: number } = { x: 1 };
            let obj: any = { ...base, [key]: 2 };
            console.log(obj.x);
            console.log(obj["added"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    // Object Method This Binding
    [Theory, ModeData]
    public void Object_MethodShorthand_ThisBinding_SingleProperty(ExecutionMode mode)
    {
        var source = """
            let obj = {
                x: 10,
                getX() {
                    return this.x;
                }
            };
            console.log(obj.getX());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodShorthand_ThisBinding_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            let obj = {
                x: 10,
                y: 20,
                getSum() {
                    return this.x + this.y;
                }
            };
            console.log(obj.getSum());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodShorthand_ThisBinding_NestedObject(ExecutionMode mode)
    {
        var source = """
            let obj = {
                nested: {
                    value: 100,
                    getValue() {
                        return this.value;
                    }
                }
            };
            console.log(obj.nested.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    // Binding `this` for method shorthand must not shift the closure-scope chain;
    // otherwise the resolver's scope distances don't match the runtime chain and
    // outer-variable captures read as undefined. Originally surfaced as
    // PerformanceObserver's callback seeing `[undefined]` from `list.getEntries()`.
    [Theory, ModeData]
    public void Object_MethodShorthand_CapturesEnclosingVariable(ExecutionMode mode)
    {
        var source = """
            function trigger(entry: any): any {
                const list = {
                    getEntries(): any[] { return [entry]; }
                };
                return list.getEntries()[0];
            }
            const e = { name: 'm', val: 42 };
            const got = trigger(e);
            console.log(got.name + ':' + got.val);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("m:42\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodShorthand_ClosureAndThisCoexist(ExecutionMode mode)
    {
        var source = """
            function make(prefix: string): any {
                return {
                    name: 'obj',
                    describe(): string { return prefix + ':' + this.name; }
                };
            }
            console.log(make('item').describe());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("item:obj\n", output);
    }

    // Constructor-function pattern: `new F()` on a runtime $TSFunction value must
    // run the body with a fresh `this` and propagate property writes. Fixed for both
    // modes in #54.
    [Theory, ModeData]
    public void Function_ConstructedObject_SeparatesOwnAndPrototypeProperties(ExecutionMode mode)
    {
        var source = """
            function Ctor(this: any): void { this.own = 1; }
            (Ctor as any).prototype.inherited = 2;
            const instance = new (Ctor as any)();
            console.log(Object.keys(instance).join(','));
            console.log(instance.inherited);
            console.log(instance.constructor === Ctor);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("own\n2\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Function_ConstructorPattern_CapturesEnclosingVariable(ExecutionMode mode)
    {
        var source = """
            function makeCtor(greeting: string): any {
                function Ctor(this: any, name: string): void {
                    this.msg = greeting + ' ' + name;
                }
                return Ctor;
            }
            const Hi = makeCtor('Hello') as any;
            const x = new Hi('World');
            console.log(x.msg);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World\n", output);
    }

    [Theory, ModeData]
    public void Function_New_On_Returned_Ctor_MinimalClosure(ExecutionMode mode)
    {
        // The minimal-closure variant from #54 — ctor returned from a factory with
        // no captured state. Routes through a named local so the compiled path
        // picks up the $TSFunction and invokes it via NewOnFunction.
        var source = """
            function outer(): any {
                function Ctor(this: any, name: string): void { this.msg = 'Fixed ' + name; }
                return Ctor;
            }
            const C = outer() as any;
            console.log(new C('W').msg);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Fixed W\n", output);
    }

    [Theory, ModeData]
    public void Function_New_On_Inline_Callee_Expression(ExecutionMode mode)
    {
        // Inline form: `new (factory() as any)(args)` with no intermediate local.
        // The compiled path here goes through EmitCalleeExprConstruction (and the
        // expanded EmitFallbackConstruction dispatch added in #54's follow-up) so
        // non-Variable callees route through NewOnFunction instead of silently
        // emitting Ldnull.
        var source = """
            function outer(): any {
                function Ctor(this: any, name: string): void { this.msg = 'Inline ' + name; }
                return Ctor;
            }
            console.log(new (outer() as any)('W').msg);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Inline W\n", output);
    }

    [Theory, ModeData]
    public void Function_New_ArrowCapturesConstructedThis_DirectNew(ExecutionMode mode)
    {
        // #399: an arrow declared inside a plain `function` invoked with `new` must
        // lexically capture the freshly-constructed `this`. In compiled mode the
        // arrow's captured-`this` field was populated by a hand-rolled subset of the
        // `this`-resolution chain that fell back to Ldnull for plain function bodies
        // (which resolve `this` via the NewOnFunction thread-local), so the arrow saw
        // `this == null`. The fix routes the capture through the shared LoadThis().
        var source = """
            function Cap(this: any, cb: () => void) {
                this.tag = 'cap';
                const arrow = () => { console.log('arrow: ' + this.tag); };
                arrow();
            }
            new (Cap as any)(() => {});
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("arrow: cap\n", output);
    }

    [Theory, ModeData]
    public void Function_New_ArrowCapturesConstructedThis_DynamicNewViaValue(ExecutionMode mode)
    {
        // #399, dynamic-new form: the callee is a runtime value (`new C(...)`), so the
        // compiled path goes through NewOnFunction. The nested arrow must still capture
        // the constructed `this`.
        var source = """
            function Cap(this: any, cb: () => void) {
                this.tag = 'cap';
                const arrow = () => { console.log('arrow: ' + this.tag); };
                arrow();
            }
            const C: any = Cap;
            new C(() => {});
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("arrow: cap\n", output);
    }

    [Theory, ModeData]
    public void Function_New_NestedArrowsCaptureConstructedThis(ExecutionMode mode)
    {
        // #399, deep nesting: an arrow inside an arrow inside a function-as-constructor.
        // The inner arrow's `this` propagates as a capture up through the outer arrow,
        // so the outer arrow's display class carries `this` and the inner reads it from
        // that field — the function body's snapshot of `this` must be the real instance.
        var source = """
            function Cap(this: any) {
                this.tag = 'deep';
                const outer = () => {
                    const inner = () => 'inner: ' + this.tag;
                    return inner();
                };
                console.log(outer());
            }
            new (Cap as any)();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner: deep\n", output);
    }

    [Theory, ModeData]
    public void Function_New_ArrowOnInstanceEscapesAndKeepsThis(ExecutionMode mode)
    {
        // #399, escaping arrow: the arrow is stored on the instance and invoked AFTER
        // the constructor activation returns. Because `this` is snapshotted into the
        // arrow's display class at creation time, the captured instance must survive.
        var source = """
            function Make(this: any, v: string) {
                this.v = v;
                this.get = () => 'escaped: ' + this.v;
            }
            const o: any = new (Make as any)('X');
            console.log(o.get());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("escaped: X\n", output);
    }

    [Theory, ModeData]
    public void Function_New_ClassMethodArrowThis_StillWorks(ExecutionMode mode)
    {
        // #399 regression guard: routing captured-`this` through LoadThis() must not
        // disturb the class-method-arrow path (the minimatch
        // `.map(row => row.map(ss => this.x))` chain). Here the outermost capture
        // resolves to the class method's arg0; the nested arrow reads it from the
        // outer arrow's captured-`this` field.
        var source = """
            class Parser {
                prefix = 'P:';
                parseAll(items: string[][]): string {
                    return items.map(row => row.map(s => this.prefix + s).join(',')).join(' | ');
                }
            }
            console.log(new Parser().parseAll([['a', 'b'], ['c']]));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("P:a,P:b | P:c\n", output);
    }

    [Theory, ModeData]
    public void Function_Call_RoutesPropertyWritesToTarget(ExecutionMode mode)
    {
        // `Fn.call(target, ...)` must mutate `target`. Compiled path needed
        // InvokeWithThis to bridge thisArg into the body's `this` via the
        // _currentFunctionThis thread-local (otherwise no-`__this`-param bodies
        // dropped thisArg on the floor); interpreter path needed
        // BoundSharpTSFunctionWrapper to flush its synthetic-this wrapper back
        // to the target object after the call returns (otherwise writes landed
        // on the synthetic and disappeared).
        var source = """
            const target: any = { before: true };
            function Fn(this: any, x: number): void { this.x = x; }
            (Fn as any).call(target, 99);
            console.log(target.x);
            console.log(target.before);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Function_Apply_RoutesPropertyWritesToTarget(ExecutionMode mode)
    {
        // .apply walks the same InvokeWithThis path as .call (compiled) and the
        // same BoundSharpTSFunctionWrapper path (interpreter). Lock both modes in.
        var source = """
            const target: any = {};
            function Fn(this: any, x: number, y: number): void { this.sum = x + y; }
            (Fn as any).apply(target, [10, 32]);
            console.log(target.sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Function_New_Respects_Explicit_Object_Return(ExecutionMode mode)
    {
        // Per JS semantics: if a constructor returns a non-null object, `new` yields
        // that object rather than the implicit `this`. Guards against the helper
        // always returning newObj.
        var source = """
            function makeCtor(): any {
                function Ctor(this: any): any {
                    this.a = 'implicit';
                    return { a: 'explicit' };
                }
                return Ctor;
            }
            const C = makeCtor() as any;
            console.log(new C().a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("explicit\n", output);
    }

    [Theory, ModeData]
    public void Function_New_Primitive_Return_Is_Ignored(ExecutionMode mode)
    {
        // Per JS semantics: if a constructor returns a primitive, `new` yields the
        // implicit `this` instead. Guards against the helper treating any non-null
        // return as authoritative.
        var source = """
            function makeCtor(): any {
                function Ctor(this: any): any {
                    this.msg = 'from this';
                    return 42;
                }
                return Ctor;
            }
            const C = makeCtor() as any;
            console.log(new C().msg);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("from this\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodShorthand_ThisBinding_MultipleMethods(ExecutionMode mode)
    {
        var source = """
            let calculator = {
                value: 5,
                double() {
                    return this.value * 2;
                },
                triple() {
                    return this.value * 3;
                }
            };
            console.log(calculator.double());
            console.log(calculator.triple());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory, ModeData]
    public void Object_MethodShorthand_ThisBinding_WithParameters(ExecutionMode mode)
    {
        var source = """
            let obj = {
                base: 10,
                add(n: number) {
                    return this.base + n;
                }
            };
            console.log(obj.add(5));
            console.log(obj.add(20));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n30\n", output);
    }

    // Object.fromEntries tests
    [Theory, ModeData]
    public void Object_FromEntries_BasicArray(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["a", 1], ["b", 2], ["c", 3]];
            let obj = Object.fromEntries(entries);
            console.log(obj.a);
            console.log(obj.b);
            console.log(obj.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_FromEntries_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [];
            let obj = Object.fromEntries(entries);
            console.log(Object.keys(obj).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Object_FromEntries_DuplicateKeys(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["a", 1], ["a", 2], ["a", 3]];
            let obj = Object.fromEntries(entries);
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_FromEntries_RoundTrip(ExecutionMode mode)
    {
        var source = """
            let original: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };
            let entries: any[] = Object.entries(original);
            let restored = Object.fromEntries(entries);
            console.log(restored.x);
            console.log(restored.y);
            console.log(restored.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_FromEntries_MixedValueTypes(ExecutionMode mode)
    {
        var source = """
            let entries: any[] = [["name", "Alice"], ["age", 30], ["active", true]];
            let obj = Object.fromEntries(entries);
            console.log(obj.name);
            console.log(obj.age);
            console.log(obj.active);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_FromEntries_WithMapEntries(ExecutionMode mode)
    {
        var source = """
            let map = new Map<string, number>();
            map.set("x", 10);
            map.set("y", 20);
            let obj = Object.fromEntries(map.entries());
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Object.hasOwn tests
    [Theory, ModeData]
    public void Object_HasOwn_ReturnsTrueForOwnProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            console.log(Object.hasOwn(obj, "a"));
            console.log(Object.hasOwn(obj, "b"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_HasOwn_ReturnsFalseForMissingProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.hasOwn(obj, "b"));
            console.log(Object.hasOwn(obj, "c"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_HasOwn_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            console.log(Object.hasOwn(obj, "a"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Object_HasOwn_ClassInstanceField(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
                greet(): string {
                    return "Hello";
                }
            }
            let p = new Person("Alice", 30);
            console.log(Object.hasOwn(p, "name"));
            console.log(Object.hasOwn(p, "age"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_HasOwn_ClassInstanceMethod(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                constructor(n: string) {
                    this.name = n;
                }
                greet(): string {
                    return "Hello";
                }
            }
            let p = new Person("Alice");
            console.log(Object.hasOwn(p, "greet"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Object_HasOwn_WithNumberKey(ExecutionMode mode)
    {
        var source = """
            let obj: any = { "123": "value" };
            console.log(Object.hasOwn(obj, "123"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Object.assign tests
    [Theory, ModeData]
    public void Object_Assign_BasicMerge(ExecutionMode mode)
    {
        var source = """
            let target: { a: number, b?: number } = { a: 1 };
            let source: { b: number } = { b: 2 };
            let result = Object.assign(target, source);
            console.log(result.a);
            console.log(result.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_ModifiesTarget(ExecutionMode mode)
    {
        var source = """
            let target: { a: number, b?: number } = { a: 1 };
            Object.assign(target, { b: 2 });
            console.log(target.a);
            console.log(target.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_MultipleSources(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            let source1: { b: number } = { b: 2 };
            let source2: { c: number } = { c: 3 };
            Object.assign(target, source1, source2);
            console.log(target.a);
            console.log(target.b);
            console.log(target.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_OverridesProperties(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, { a: 100 });
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_LaterSourceWins(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, { a: 2 }, { a: 3 });
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_ReturnsTarget(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            let result = Object.assign(target, { b: 2 });
            console.log(result === target);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_EmptySource(ExecutionMode mode)
    {
        var source = """
            let target: { a: number } = { a: 1 };
            Object.assign(target, {});
            console.log(target.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_EmptyTarget(ExecutionMode mode)
    {
        var source = """
            let target: any = {};
            Object.assign(target, { a: 1, b: 2 });
            console.log(target.a);
            console.log(target.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_MixedTypes(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            Object.assign(target, { b: "hello", c: true });
            console.log(target.a);
            console.log(target.b);
            console.log(target.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Assign_NestedObjects(ExecutionMode mode)
    {
        var source = """
            let target: any = { a: 1 };
            Object.assign(target, { nested: { x: 10 } });
            console.log(target.a);
            console.log(target.nested.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n", output);
    }

    // Object.freeze tests
    [Theory, ModeData]
    public void Object_Freeze_ReturnsTheSameObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let frozen = Object.freeze(obj);
            console.log(frozen === obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_Freeze_PreventsMutation(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            obj.a = 100;
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Object_Freeze_PreventsAddingProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = { a: 1 };
            Object.freeze(obj);
            obj.b = 2;
            console.log(obj.a);
            console.log(obj.b === undefined || obj.b === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_IsFrozen_ReturnsTrueForFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            console.log(Object.isFrozen(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_IsFrozen_ReturnsFalseForNonFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.isFrozen(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Object_IsFrozen_ReturnsTrueForPrimitives(ExecutionMode mode)
    {
        var source = """
            console.log(Object.isFrozen(null));
            console.log(Object.isFrozen(42));
            console.log(Object.isFrozen("hello"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // Object.seal tests
    [Theory, ModeData]
    public void Object_Seal_ReturnsTheSameObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let sealed = Object.seal(obj);
            console.log(sealed === obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_AllowsPropertyModification(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.seal(obj);
            obj.a = 100;
            console.log(obj.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_PreventsAddingProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = { a: 1 };
            Object.seal(obj);
            obj.b = 2;
            console.log(obj.a);
            console.log(obj.b === undefined || obj.b === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_IsSealed_ReturnsTrueForSealedObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.seal(obj);
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_IsSealed_ReturnsFalseForNonSealedObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Object_IsSealed_ReturnsTrueForFrozenObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            Object.freeze(obj);
            console.log(Object.isSealed(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Array freeze/seal tests
    [Theory, ModeData]
    public void Object_Freeze_ArrayPreventsModification(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr[0] = 100;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Object_Freeze_ArrayPreventsPush(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr.push(4);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_ArrayAllowsModification(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr[0] = 100;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_ArrayPreventsPush(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr.push(4);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_IsFrozen_ArrayReturnsTrueForFrozen(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            console.log(Object.isFrozen(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Class instance freeze/seal tests
    [Theory, ModeData]
    public void Object_Freeze_ClassInstancePreventsModification(ExecutionMode mode)
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
            let p = new Point(10, 20);
            Object.freeze(p);
            p.x = 100;
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_ClassInstanceAllowsModification(ExecutionMode mode)
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
            let p = new Point(10, 20);
            Object.seal(p);
            p.x = 100;
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n20\n", output);
    }

    [Theory, ModeData]
    public void Object_IsFrozen_ClassInstanceReturnsTrueForFrozen(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                constructor(x: number) {
                    this.x = x;
                }
            }
            let p = new Point(10);
            Object.freeze(p);
            console.log(Object.isFrozen(p));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Shallow freeze tests
    [Theory, ModeData]
    public void Object_Freeze_IsShallow(ExecutionMode mode)
    {
        var source = """
            let obj: any = { nested: { value: 1 } };
            Object.freeze(obj);
            obj.nested.value = 100;
            console.log(obj.nested.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Object_Freeze_ArrayReverseFails(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr.reverse();
            console.log(arr[0]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n", output);
    }

    [Theory, ModeData]
    public void Object_Seal_ArrayReverseSucceeds(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr.reverse();
            console.log(arr[0]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n", output);
    }

    // Object.is tests
    [Theory, ModeData]
    public void Object_Is_SameNumbers(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(1, 1));
            console.log(Object.is(42, 42));
            console.log(Object.is(-5, -5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_DifferentNumbers(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(1, 2));
            console.log(Object.is(42, 43));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_NaN_ReturnsTrue(ExecutionMode mode)
    {
        // Object.is(NaN, NaN) should be true (this differs from === in standard JavaScript)
        var source = """
            console.log(Object.is(NaN, NaN));
            console.log(Object.is(NaN, 0));
            console.log(Object.is(NaN, "NaN"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_PositiveAndNegativeZero_ReturnsFalse(ExecutionMode mode)
    {
        // Unlike ===, Object.is(+0, -0) should be false
        var source = """
            console.log(Object.is(0, -0));
            console.log(Object.is(-0, 0));
            console.log(0 === -0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_SameZeros(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(0, 0));
            console.log(Object.is(-0, -0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_NullComparison(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(null, null));
            console.log(Object.is(null, undefined));
            console.log(Object.is(null, 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_UndefinedComparison(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(undefined, undefined));
            console.log(Object.is(undefined, null));
            console.log(Object.is(undefined, 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_StringComparison(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is("hello", "hello"));
            console.log(Object.is("hello", "world"));
            console.log(Object.is("", ""));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_BooleanComparison(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(true, true));
            console.log(Object.is(false, false));
            console.log(Object.is(true, false));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_ObjectReferenceEquality(ExecutionMode mode)
    {
        var source = """
            let obj1: { x: number } = { x: 1 };
            let obj2: { x: number } = { x: 1 };
            let obj3 = obj1;
            console.log(Object.is(obj1, obj1));
            console.log(Object.is(obj1, obj2));
            console.log(Object.is(obj1, obj3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_ArrayReferenceEquality(ExecutionMode mode)
    {
        var source = """
            let arr1: number[] = [1, 2, 3];
            let arr2: number[] = [1, 2, 3];
            let arr3 = arr1;
            console.log(Object.is(arr1, arr1));
            console.log(Object.is(arr1, arr2));
            console.log(Object.is(arr1, arr3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_MixedTypes(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(1, "1"));
            console.log(Object.is(true, 1));
            console.log(Object.is(null, undefined));
            console.log(Object.is(0, false));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_Is_Infinity(ExecutionMode mode)
    {
        var source = """
            console.log(Object.is(Infinity, Infinity));
            console.log(Object.is(-Infinity, -Infinity));
            console.log(Object.is(Infinity, -Infinity));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    // Object.defineProperty tests
    [Theory, ModeData]
    public void Object_DefineProperty_BasicDataProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "x", { value: 42, writable: true, enumerable: true, configurable: true });
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_ReturnsTheObject(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            let result = Object.defineProperty(obj, "x", { value: 10 });
            console.log(result === obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_NonWritableProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "x", { value: 42, writable: false });
            obj.x = 100;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_WritableProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "x", { value: 42, writable: true });
            obj.x = 100;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "x", { value: 1, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(obj, "y", { value: 2, writable: true, enumerable: true, configurable: true });
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_OverwriteExistingProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 10 };
            Object.defineProperty(obj, "x", { value: 42 });
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_OnClassInstance(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                constructor(x: number) {
                    this.x = x;
                }
            }
            let p = new Point(10);
            Object.defineProperty(p, "y", { value: 20, writable: true, enumerable: true, configurable: true });
            console.log(p.x);
            console.log((p as any).y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_OnArray(ExecutionMode mode)
    {
        var source = """
            let arr: any = [1, 2, 3];
            Object.defineProperty(arr, "customProp", { value: "hello", writable: true, enumerable: true, configurable: true });
            console.log(arr.customProp);
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n1\n", output);
    }

    // Object.getOwnPropertyDescriptor tests
    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_BasicProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, "x");
            console.log(desc.value);
            console.log(desc.writable);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_NonExistentProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 42 };
            let desc = Object.getOwnPropertyDescriptor(obj, "y");
            console.log(desc === undefined || desc === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_DefinedProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "x", { value: 42, writable: false, enumerable: false, configurable: false });
            let desc = Object.getOwnPropertyDescriptor(obj, "x");
            console.log(desc.value);
            console.log(desc.writable);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_ArrayLength(ExecutionMode mode)
    {
        var source = """
            let arr: any = [1, 2, 3];
            let desc = Object.getOwnPropertyDescriptor(arr, "length");
            console.log(desc.value);
            console.log(desc.writable);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\ntrue\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_ArrayElement(ExecutionMode mode)
    {
        var source = """
            let arr: any = [10, 20, 30];
            let desc = Object.getOwnPropertyDescriptor(arr, "1");
            console.log(desc.value);
            console.log(desc.writable);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_ClassInstance(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                constructor(x: number) {
                    this.x = x;
                }
            }
            let p = new Point(42);
            let desc = Object.getOwnPropertyDescriptor(p, "x");
            console.log(desc.value);
            console.log(desc.writable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_ThenGetDescriptor_Roundtrip(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "name", {
                value: "Alice",
                writable: true,
                enumerable: false,
                configurable: true
            });
            let desc = Object.getOwnPropertyDescriptor(obj, "name");
            console.log(desc.value);
            console.log(desc.writable);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\ntrue\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_WithGetter(ExecutionMode mode)
    {
        var source = """
            let obj: any = { _value: 100 };
            Object.defineProperty(obj, "computed", {
                get: function() { return this._value * 2; },
                enumerable: true,
                configurable: true
            });
            console.log(obj.computed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_WithGetterAndSetter(ExecutionMode mode)
    {
        var source = """
            let obj: any = { _value: 10 };
            Object.defineProperty(obj, "value", {
                get: function() { return this._value; },
                set: function(v: number) { this._value = v; },
                enumerable: true,
                configurable: true
            });
            console.log(obj.value);
            obj.value = 50;
            console.log(obj.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n50\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptor_AccessorProperty(ExecutionMode mode)
    {
        var source = """
            let obj: any = { _x: 5 };
            Object.defineProperty(obj, "x", {
                get: function() { return this._x; },
                enumerable: true,
                configurable: true
            });
            let desc = Object.getOwnPropertyDescriptor(obj, "x");
            console.log(typeof desc.get);
            console.log(desc.enumerable);
            console.log(desc.configurable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_FrozenObjectFails(ExecutionMode mode)
    {
        // When object is frozen, defineProperty should throw
        var source = """
            let obj: any = { x: 1 };
            Object.freeze(obj);
            let threw = false;
            try {
                Object.defineProperty(obj, "y", { value: 2 });
            } catch (e) {
                threw = true;
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_SealedObjectNewPropertyFails(ExecutionMode mode)
    {
        // When object is sealed, adding new property should throw
        var source = """
            let obj: any = { x: 1 };
            Object.seal(obj);
            let threw = false;
            try {
                Object.defineProperty(obj, "y", { value: 2 });
            } catch (e) {
                threw = true;
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Object.getOwnPropertyNames tests
    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_BasicObject(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(names.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(names.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_Array(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let names: string[] = Object.getOwnPropertyNames(arr);
            // Should include "0", "1", "2", "length"
            console.log(names.includes("0"));
            console.log(names.includes("1"));
            console.log(names.includes("2"));
            console.log(names.includes("length"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_ClassInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let names: string[] = Object.getOwnPropertyNames(p);
            console.log(names.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_ReturnsStrings(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number } = { x: 42 };
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(typeof names[0]);
            console.log(names[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\nx\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_WithMixedTypes(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string, age: number, active: boolean } = { name: "Alice", age: 30, active: true };
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(names.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_IncludesNonEnumerableProperties(ExecutionMode mode)
    {
        // Unlike Object.keys(), getOwnPropertyNames should include non-enumerable properties
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "hidden", { value: 42, enumerable: false });
            Object.defineProperty(obj, "visible", { value: 100, enumerable: true });
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(names.includes("hidden"));
            console.log(names.includes("visible"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_IncludesAccessorsAndArrayExpandos(
        ExecutionMode mode)
    {
        var source = """
            const objectValue: any = {};
            Object.defineProperty(objectValue, "accessor", { get() { return 1; } });
            const arrayValue: any[] = [1, 2];
            arrayValue.data = 3;
            Object.defineProperty(arrayValue, "accessor", { get() { return 4; } });
            console.log(Object.getOwnPropertyNames(objectValue).includes("accessor"));
            console.log(Object.getOwnPropertyNames(arrayValue).includes("data"));
            console.log(Object.getOwnPropertyNames(arrayValue).includes("accessor"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_DoesNotIncludeMethods(ExecutionMode mode)
    {
        // Methods defined on the class should NOT appear in getOwnPropertyNames
        // Note: This test uses interpreter only since compiled class instances
        // use different property storage mechanisms
        var source = """
            class Person {
                name: string;
                constructor(n: string) {
                    this.name = n;
                }
                greet(): string {
                    return "Hello";
                }
            }
            let p = new Person("Alice");
            let names: string[] = Object.getOwnPropertyNames(p);
            console.log(names.includes("name"));
            console.log(names.includes("greet"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyNames_IncludesDefinedProperties(ExecutionMode mode)
    {
        // Both getOwnPropertyNames and keys include properties added via defineProperty
        // Note: In this codebase, Object.keys() does not filter non-enumerable properties
        var source = """
            let obj: any = { visible: 1 };
            Object.defineProperty(obj, "hidden", { value: 2, enumerable: false });
            let names: string[] = Object.getOwnPropertyNames(obj);
            console.log(names.length);
            console.log(names.includes("visible"));
            console.log(names.includes("hidden"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_WithBoundGetterSetter(ExecutionMode mode)
    {
        // Regression: accessors stored as bound/wrapper callables must be invocable
        var source = """
            let backing = 0;
            function myGetter(): number { return backing; }
            function mySetter(v: number): void { backing = v; }
            let obj: any = {};
            Object.defineProperty(obj, "val", {
                get: myGetter,
                set: mySetter,
                enumerable: true,
                configurable: true
            });
            obj.val = 42;
            console.log(obj.val);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_WithArrowGetterSetter(ExecutionMode mode)
    {
        // Regression: arrow function accessors via defineProperty
        var source = """
            let storage: any = { _count: 0 };
            let obj: any = {};
            Object.defineProperty(obj, "count", {
                get: () => storage._count,
                set: (v: number) => { storage._count = v; },
                enumerable: true,
                configurable: true
            });
            obj.count = 10;
            console.log(obj.count);
            console.log(storage._count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n10\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_ReadsInheritedValueViaSetterOnlyAccessor(ExecutionMode mode)
    {
        // #801: ECMA-262 §6.2.5.5 ToPropertyDescriptor reads `value` via HasProperty/Get,
        // walking the prototype chain. A descriptor whose `value` is an INHERITED
        // setter-only accessor IS a data descriptor with value === undefined, so the
        // target property must be overwritten to undefined (not preserved).
        var source = """
            const proto: any = {};
            Object.defineProperty(proto, "value", { set() {} });
            const Ctor: any = function () {};
            Ctor.prototype = proto;
            const child: any = new Ctor();
            const o: any = { property: 120 };
            Object.defineProperty(o, "property", child);
            console.log(typeof o.property);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperty_AttributeOnlyRedefinePreservesValue(ExecutionMode mode)
    {
        // #801: an attribute-only descriptor (no `value` key) must preserve the existing
        // value rather than wiping it to the undefined sentinel.
        var source = """
            const o: any = { a: 42 };
            Object.defineProperty(o, "a", { writable: false });
            console.log(o.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Object_DefineProperty_ReadsInheritedEnumerableFromRegExpPrototype(ExecutionMode mode)
    {
        // #801: a descriptor's INHERITED `enumerable` (here via a user-set
        // RegExp.prototype property) must be read through the prototype chain. RegExp
        // instances expose user-set prototype properties (built-in prototype mutability).
        // Interpreter-only: compiled $RegExp prototype mutability is tracked separately.
        var source = """
            (RegExp.prototype as any).enumerable = true;
            const regObj: any = new RegExp();
            const obj: any = {};
            Object.defineProperty(obj, "property", regObj);
            let seen = false;
            for (const p in obj) if (p === "property") seen = true;
            console.log(seen);
            console.log((regObj as any).enumerable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Object_RegExp_InheritsUserSetPrototypeProperty(ExecutionMode mode)
    {
        // #801/#474-adjacent: a plain user property set on RegExp.prototype is visible
        // on RegExp instances via the prototype chain.
        var source = """
            (RegExp.prototype as any).foo = 1;
            console.log((new RegExp() as any).foo);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    // ========================
    // Object.defineProperties
    // ========================

    [Theory, ModeData]
    public void Object_DefineProperties_BasicDataProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperties(obj, {
                name: { value: "Alice", writable: true, enumerable: true, configurable: true },
                age: { value: 30, writable: true, enumerable: true, configurable: true }
            });
            console.log(obj.name);
            console.log(obj.age);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperties_WithAccessors(ExecutionMode mode)
    {
        var source = """
            let obj: any = { _value: 0 };
            Object.defineProperties(obj, {
                value: {
                    get: function() { return obj._value; },
                    set: function(v: number) { obj._value = v * 2; },
                    enumerable: true,
                    configurable: true
                }
            });
            obj.value = 5;
            console.log(obj.value);
            console.log(obj._value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n10\n", output);
    }

    [Theory, ModeData]
    public void Object_DefineProperties_ReturnsTarget(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            let result = Object.defineProperties(obj, {
                x: { value: 42, writable: true, enumerable: true, configurable: true }
            });
            console.log(result === obj);
            console.log(result.x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n42\n", output);
    }

    // ================================
    // Object.getOwnPropertyDescriptors
    // ================================

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptors_BasicObject(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 1, y: 2 };
            let descs: any = Object.getOwnPropertyDescriptors(obj);
            console.log(descs.x.value);
            console.log(descs.y.value);
            console.log(descs.x.writable);
            console.log(descs.x.enumerable);
            console.log(descs.x.configurable);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptors_WithDefinedProperties(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            Object.defineProperty(obj, "name", { value: "test", writable: false, enumerable: true, configurable: false });
            let descs: any = Object.getOwnPropertyDescriptors(obj);
            console.log(descs.name.value);
            console.log(descs.name.writable);
            console.log(descs.name.configurable);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptors_RoundTrip(ExecutionMode mode)
    {
        var source = """
            let original: any = { a: 1, b: "hello" };
            let descs = Object.getOwnPropertyDescriptors(original);
            let copy: any = Object.defineProperties({}, descs);
            console.log(copy.a);
            console.log(copy.b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\n", output);
    }

    [Theory, ModeData]
    public void Object_GetOwnPropertyDescriptors_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: any = {};
            let descs: any = Object.getOwnPropertyDescriptors(obj);
            console.log(Object.keys(descs).length);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }
}
