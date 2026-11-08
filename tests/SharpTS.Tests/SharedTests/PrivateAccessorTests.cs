using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateAccessorTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void PrivateAccessorsUseSeparateReadAndWriteFacets(ExecutionMode mode)
    {
        const string source = """
            class Counter {
                #stored = 2;
                get #value() { return this.#stored; }
                set #value(value: number | string) { this.#stored = Number(value); }
                run() {
                    this.#value = "3";
                    const old: number = this.#value++;
                    const next: number = ++this.#value;
                    this.#value *= 2;
                    const skipped = this.#value ||= 99;
                    this.#value &&= 12;
                    console.log(old, next, skipped, this.#value);
                }
            }
            new Counter().run();
            """;
        Assert.Equal("3 5 10 12\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void PrivateAccessorBrandsFollowTheEvaluatedClass(ExecutionMode mode)
    {
        const string source = """
            function make(initial) {
                return class Box {
                    before = #value in this;
                    #stored = initial;
                    get #value() { return this.#stored; }
                    static #staticStored = initial;
                    static get #staticValue() { return this.#staticStored; }
                    static set #write(v) { this.#staticStored = v; }
                    static probe(other) { return [#staticValue in other, #write in other]; }
                    static read(other) { return other.#staticValue; }
                    static write(other, v) { return other.#write = v; }
                    read(other) { return other.#value; }
                    has(other) { return #value in other; }
                };
            }
            const A = make(2), B = make(7), a = new A(), b = new B();
            console.log(a.before, a.has(a), a.has(b), a.has(new Proxy(a, {})));
            console.log(a.read(a), b.read(b), Object.getOwnPropertyNames(a).join(","));
            console.log(A.probe(A).join(","), A.probe(B).join(","));
            console.log(A.read(A), A.write(A, 5), A.read(A), B.read(B));
            try { a.read(b); } catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("true true false false\n2 7 before\ntrue,true false,false\n2 5 5 7\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void DerivedInstancesShareTheInstanceBrandButConstructorsHaveTheirOwn(ExecutionMode mode)
    {
        const string source = """
            class Base {
                get #value() { return 7; }
                read() { return this.#value; }
                static get #valueOnClass() { return 8; }
                static has(other: any) { return #valueOnClass in other; }
                static read(other: any) { return other.#valueOnClass; }
            }
            class Derived extends Base {}
            console.log(new Derived().read(), Base.has(Base), Base.has(Derived), Base.read(Base));
            try { Base.read(Derived); } catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("7 true false 8\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void MissingAccessorFacetsThrowGuestTypeErrors(ExecutionMode mode)
    {
        const string source = """
            let events = "";
            class Box {
                get #read() { events += "g"; return 3; }
                set #write(value) { events += "s" + value; }
                check() {
                    const assigned = this.#write = 5;
                    try { this.#read += (events += "r", 1); }
                    catch (e) { console.log(e instanceof TypeError, events); }
                    try { console.log(this.#write); }
                    catch (e) { console.log(e instanceof TypeError, events, assigned); }
                }
            }
            new Box().check();
            """;
        Assert.Equal("true s5gr\ntrue s5gr 5\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void NestedVarDeclarationsBelongToTheAccessor(ExecutionMode mode)
    {
        const string source = """
            let value = 0;
            class C {
                get #x() {
                    if (true) { var value = 7; }
                    return value;
                }
                read() { return this.#x; }
            }
            console.log(new C().read(), value);
            """;
        Assert.Equal("7 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void AccessorCallsAndSuspensionRetainTheOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            class Box {
                value = 3;
                get #value() { return this.value; }
                set #value(v) { this.value = v; }
                get #callable() { return function (increment) { return this.value + increment; }; }
                async run(other) {
                    const result = this.#value += await new Promise(resolve => setTimeout(() => resolve(2), 1));
                    const assigned = (await Promise.resolve(other)).#value = await Promise.resolve(9);
                    console.log(result, assigned, this.#callable(4), other.value);
                }
            }
            new Box().run(new Box());
            """;
        Assert.Equal("5 9 9 9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData("class C { get #x() {} get #x() {} }")]
    [InlineData("const C = class { set #x(v) {} set #x(v) {} };")]
    [InlineData("class C { #x; get #x() {} }")]
    [InlineData("const C = class { set #x(v) {} #x() {} };")]
    [InlineData("class C { static get #x() {} set #x(v) {} }")]
    [InlineData("class C { get #constructor() {} }")]
    [InlineData("class C { set #x(...v) {} }")]
    [InlineData("class C { set #x(v?: number) {} }")]
    [InlineData("class C { get #x(v) {} }")]
    [InlineData("class C { get #x(this) {} }")]
    [InlineData("class C { public get #x() {} }")]
    public void InvalidPrivateAccessorDeclarationsAreRejected(string source)
        => Assert.False(new Parser(new Lexer(source).ScanTokens()).Parse().IsSuccess);

    [Theory]
    [InlineData("get #x() { return 1; }", "this.#x = 1;", "TS2540")]
    [InlineData("set #x(value: number) {}", "this.#x;", "TS2806")]
    [InlineData("set #x(value: number) {}", "this.#x = 'bad';", "TS2322")]
    [InlineData("get #x() { return 1; } set #x(value) {}", "this.#x = 'bad';", "TS2322")]
    [InlineData("get #x() { return 1; }", "({}).#x;", "TS2339")]
    public void CheckerValidatesAccessorFacetAndReceiver(string accessor, string operation, string code)
    {
        string source = $"class C {{ {accessor} test() {{ {operation} }} }}";
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C { value = this.#x; get #x() { return 1; } }", "new C().value")]
    [InlineData("const C = class { #stored = 1; value = this.#x; get #x() { return this.#stored; } };", "new C().value")]
    [InlineData("class C { value = this.#x; #stored = 1; get #x() { return this.#stored; } }", "new C().value")]
    [InlineData("class C { static value = C.#x; static get #x() { return 1; } }", "C.value")]
    public void FieldTypesUseInferredPrivateGetterReturns(string declaration, string access)
    {
        new TypeChecker().Check(TestHarness.ParseOrThrow($"{declaration} const right: number = {access};"));
        var statements = TestHarness.ParseOrThrow($"{declaration} const wrong: string = {access};");
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C { get #first() { return this.#second; } get #second() { return 1; } read() { return this.#first; } }", "new C().read()")]
    [InlineData("const C = class { get #first() { return this.#second; } get #second() { return 1; } read() { return this.#first; } };", "new C().read()")]
    [InlineData("class C { static get #first() { return C.#second; } static get #second() { return 1; } static read() { return C.#first; } }", "C.read()")]
    public void PrivateGetterReturnsInferFromLaterPrivateGetters(string declaration, string access)
    {
        new TypeChecker().Check(TestHarness.ParseOrThrow($"{declaration} const right: number = {access};"));
        var statements = TestHarness.ParseOrThrow($"{declaration} const wrong: string = {access};");
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class Inner { copied = this.value; later = 1; get value() { return this.later; } }")]
    [InlineData("const Inner = class { copied = this.value; later = 1; get value() { return this.later; } };")]
    public void PendingFieldsInUnrelatedNestedClassesDoNotChangePrivateGetterReturns(string nestedClass)
    {
        string declaration = $$"""
            class C {
                get #first() { return this.#second; }
                get #second() {
                    {{nestedClass}}
                    return 7;
                }
                read() { return this.#first; }
            }
            """;
        new TypeChecker().Check(TestHarness.ParseOrThrow($"{declaration} const right: number = new C().read();"));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow($"{declaration} const wrong: string = new C().read();")));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C { set #x(v: number) { if (v) return 123; } }")]
    [InlineData("const C = class { static set #x(v: number) { return undefined; } };")]
    public void CheckerRejectsValueReturnsFromPrivateSetters(string source)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2408", error.Diagnostic.TsCode);
    }

    [Fact]
    public void CheckerRejectsPrivateSetterParameterInitializers()
    {
        const string source = "class C { set #x(value: number = 1) {} }";
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS1052", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void UncheckedJavaScriptPrivateSettersAllowDefaultsAndIgnoreReturnedValues(ExecutionMode mode)
    {
        const string source = """
            class C {
                set #x(value = 1) { console.log(value); return 5; }
                run() { console.log(this.#x = undefined); }
            }
            new C().run();
            """;
        Assert.Equal("1\nundefined\n", TestHarness.RunUnchecked(source, mode));
    }

    [Fact]
    public void AmbientPrivateAccessorFacetsKeepClassBrandsDistinct()
    {
        const string source = """
            declare class A { get #x(): number; }
            declare class B { get #x(): number; }
            declare const b: B;
            const a: A = b;
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
