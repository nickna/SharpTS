using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeGuestOwnSourceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["presence-during-initializer", """
            const trace: any[] = [];
            class Probe {
                first: number = (() => { trace.push([Object.hasOwn(this, 'later'), this.later === undefined, Object.keys(this)]); return 1; })();
                later: number = 2;
            }
            new Probe(); console.log(JSON.stringify(trace));
            """, "[[false,true,[]]]\n"];
        yield return ["abrupt-escaped-receiver", """
            let escaped: any;
            function fail(receiver: any): number { escaped = receiver; throw new Error('stop'); }
            class Broken { first: number = fail(this); later: number = 2; }
            try { new Broken(); } catch (error) {}
            console.log(Object.hasOwn(escaped, 'first'), Object.hasOwn(escaped, 'later'), JSON.stringify(Object.keys(escaped)));
            """, "false false []\n"];
        yield return ["direct-initializer-reads-inherited-value-before-definition", """
            let reads = 0;
            class Value { first: any = this.later; second: any = this['later']; later: number = 2; }
            Object.defineProperty(Value.prototype, 'later', { get() { reads++; return 7; }, configurable: true });
            const value: any = new Value();
            console.log(value.first, value.second, value.later, reads, Object.keys(value).join(','));
            """, "7 7 2 2 first,second,later\n"];
        yield return ["initializer-expando-chronology", """
            class Ordered {
                first: number = (() => { (this as any).extra = 1; return 2; })();
                later: number = 3;
            }
            const value: any = new Ordered();
            console.log(Object.keys(value).join(','));
            console.log(Object.getOwnPropertyNames(value).join(','));
            console.log(Object.values(value).join(','));
            """, "extra,first,later\nextra,first,later\n1,2,3\n"];
        yield return ["early-assignment-keeps-definition-position", """
            class Ordered {
                first: number = (() => { this.later = 40; (this as any).extra = 1; return 2; })();
                later: number = 3;
            }
            const value: any = new Ordered();
            console.log(Object.keys(value).join(','), value.later);
            console.log(Object.getOwnPropertyDescriptor(value, 'later')!.writable, Object.getOwnPropertyDescriptor(value, 'later')!.enumerable);
            """, "later,extra,first 3\ntrue true\n"];
        yield return ["generic-owner-and-raw-values", """
            class Holder<T> { value: T; constructor(initial: T) { this.value = initial; } }
            const number: any = new Holder<number>(4); const text: any = new Holder<string>('word');
            console.log(number.value, text.value, Object.getOwnPropertyDescriptor(number, 'value')!.value);
            number.value = { tag: 7 }; text.value = undefined;
            console.log(number.value.tag, text.value === undefined, Object.keys(text).join(','));
            class Omitted { number: number; flag: boolean; }
            const omitted: any = new Omitted();
            console.log(omitted.number === undefined, omitted.flag === undefined, Object.keys(omitted).join(','));
            """, "4 word 4\n7 true value\ntrue true number,flag\n"];
        yield return ["generic-initial-prototype-keeps-canonical-identity", """
            class Generic<T> { seen: any = Object.getPrototypeOf(this); value: T; constructor(value: T) { this.value = value; } }
            const number: any = new Generic<number>(1); const text: any = new Generic<string>('word');
            console.log(number.seen === Generic.prototype, text.seen === Generic.prototype);
            console.log(Object.getPrototypeOf(number) === Generic.prototype, Object.getPrototypeOf(text) === Generic.prototype);
            console.log(Object.getPrototypeOf(number) === Object.getPrototypeOf(text), number.value, text.value);
            """, "true true\ntrue true\ntrue 1 word\n"];
        yield return ["delete-recreate-and-accessor-metadata", """
            class Values { a: number = 1; b: number = 2; }
            const value: any = new Values(); let reads = 0;
            delete value.a; value.c = 3; value.a = 4;
            Object.defineProperty(value, 'b', { get() { reads++; return 20; }, enumerable: false, configurable: true });
            const descriptor: any = Object.getOwnPropertyDescriptor(value, 'b');
            console.log(Object.keys(value).join(','), Object.getOwnPropertyNames(value).join(','), typeof descriptor.get, reads);
            console.log(Object.values(value).join(','), Object.entries(value).map((item: any) => item.join(':')).join(','), reads);
            console.log(value.b, reads);
            Object.defineProperty(value, 'b', { value: 9, writable: true, enumerable: true, configurable: true });
            console.log(Object.keys(value).join(','), value.b);
            """, "c,a b,c,a function 0\n3,4 c:3,a:4 0\n20 1\nb,c,a 9\n"];
        yield return ["failed-mutations-keep-ledger", """
            class Value { a: number = 1; }
            const value: any = new Value(); Object.preventExtensions(value);
            value.a = 2; value.missing = 3;
            console.log(value.a, Object.hasOwn(value, 'missing'), Object.keys(value).join(','));
            Object.freeze(value); value.a = 4;
            console.log(Reflect.deleteProperty(value, 'a'), value.a, Object.getOwnPropertyDescriptor(value, 'a')!.writable);
            try { Object.defineProperty(value, 'a', { value: 8 }); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.keys(value).join(','), value.a);
            """, "2 false a\nfalse 2 false\ntrue\na 2\n"];
        yield return ["initializer-integrity-change-commits-only-successful-definitions", """
            let escaped: any; let mode = 0; let calls = 0;
            function prepare(receiver: any): number {
                calls++; escaped = receiver; receiver.first = 40;
                if (mode === 0) Object.preventExtensions(receiver); else Object.freeze(receiver);
                return 2;
            }
            class Value { first: number = prepare(this); later: number = 3; }
            for (mode = 0; mode < 2; mode++) {
                try { new Value(); } catch (error) { console.log(error instanceof TypeError); }
                console.log(escaped.first, Object.hasOwn(escaped, 'later'), Object.keys(escaped).join(','), calls);
            }
            """, "true\n2 false first 1\ntrue\n40 false first 2\n"];
        yield return ["symbol-and-index-order", """
            class Value { first: number = 1; }
            const value: any = new Value(); const one = Symbol('key'); const two = Symbol('key');
            value[one] = 3; value['10'] = 10; value['2'] = 2;
            Object.defineProperty(value, two, { value: 4, enumerable: true, configurable: true });
            delete value[one]; value[one] = 5;
            console.log(Reflect.ownKeys(value).map((key: any) => typeof key === 'symbol' ? (key === one ? 'one' : 'two') : key).join(','));
            console.log(Object.getOwnPropertySymbols(value)[0] === two, value[one], Object.getOwnPropertyDescriptor(value, one)!.value);
            """, "2,10,first,two,one\ntrue 5 5\n"];
        yield return ["prototype-before-initializer-and-explicit-null", """
            const chosen: any = { inherited: 7 }; let reads = 0; const trace: any[] = [];
            function Other() {}
            const alternate: any = Other.bind(null);
            Object.defineProperty(alternate, 'prototype', { get() { reads++; return chosen; }, configurable: true });
            class Value { first: number = (() => { trace.push(Object.getPrototypeOf(this) === chosen, (this as any).inherited); return 1; })(); }
            const value: any = Reflect.construct(Value, [], alternate);
            console.log(JSON.stringify(trace), reads, Object.getPrototypeOf(value) === chosen);
            Object.setPrototypeOf(value, null); console.log(Object.getPrototypeOf(value) === null, Object.keys(value).join(','));
            """, "[true,7] 1 true\ntrue first\n"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeSourceOwnOperationsUseCompletedDefinitions(string name, string source, string expected)
    {
        _ = name;
        var assembly = Compile(source, true);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void ExactPredicateRejectsUnsupportedDerivedOwnersAndPerEvaluationTemplates()
    {
        const string source = """
            class Base { value: number = 1; }
            class Derived extends Base { later: number = 2; }
            class Generic<T> { value: T; }
            class GenericDerived<T> extends Generic<T> { later: number = 2; }
            class Collision { x = 1; X = 2; }
            const Expression = class extends Base { later = 2; };
            function make() { class Local { value = 1; } return Local; }
            const First: any = make(); const Second: any = make();
            console.log(First === Second, Object.getPrototypeOf(new First()) === Object.getPrototypeOf(new Second()));
            console.log(Object.keys(new Derived()).join(','), Object.keys(new Expression()).join(','));
            """;
        var assembly = Compile(source, true); var contract = assembly.GetType("$IGuestOwnProperties")!;
        var predicate = contract.GetMethod("IsOwnPropertyReceiver")!;
        Assert.True((bool)predicate.Invoke(Activator.CreateInstance(NativeSourceTypeLookup.Find(assembly, "Base")!), null)!);
        var derivedInstance = Activator.CreateInstance(NativeSourceTypeLookup.Find(assembly, "Derived")!)!;
        Assert.False((bool)predicate.Invoke(derivedInstance, null)!);
        assembly.GetType("$Runtime")!.GetMethod("ObjectDefineProperty")!.Invoke(null,
            [derivedInstance, "extra", new Dictionary<string, object> { ["value"] = 9d, ["enumerable"] = true, ["configurable"] = true, ["writable"] = true }]);
        var ownState = assembly.GetType("$GuestOwnPropertyState")!;
        var hasOwn = ownState.GetMethod("HasOwn", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)hasOwn.Invoke(null, [derivedInstance, "value"])!);
        Assert.False((bool)hasOwn.Invoke(null, [derivedInstance, "extra"])!);
        var generic = assembly.GetTypes().Single(type => type.Name == "Generic" && type.IsGenericTypeDefinition).MakeGenericType(typeof(double));
        var derived = assembly.GetTypes().Single(type => type.Name == "GenericDerived" && type.IsGenericTypeDefinition).MakeGenericType(typeof(double));
        Assert.True((bool)predicate.Invoke(Activator.CreateInstance(generic), null)!);
        Assert.False((bool)predicate.Invoke(Activator.CreateInstance(derived), null)!);
        Assert.False(contract.IsAssignableFrom(NativeSourceTypeLookup.Find(assembly, "Collision")));
        var localTemplates = assembly.GetTypes().Where(type => type.Name.Contains("Local", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(localTemplates);
        Assert.All(localTemplates, type => Assert.False(contract.IsAssignableFrom(type)));
        var evaluatedContract = assembly.GetType("$IClassDefinitionInstance")!;
        var evaluatedTemplates = assembly.GetTypes().Where(type => type.IsClass && evaluatedContract.IsAssignableFrom(type)).ToArray();
        Assert.NotEmpty(evaluatedTemplates);
        Assert.All(evaluatedTemplates, type => Assert.Null(type.GetMethod("$ReadOwnDescriptor",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)));
        Assert.Equal("false false\nvalue,later value,later\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void GenericFallbackCaptureUsesTheSemanticPrototypeButActualClosedFieldStorage()
    {
        // No prototype operation occurs in the source, so constructors have no
        // selected-newTarget sidecar and must use the lookup-only capture path.
        var assembly = Compile("""
            class Generic<T> { count: number = 9; value: T; constructor(value: T) { this.value = value; } }
            """, true);
        // Execute class evaluation, which registers its semantic prototype;
        // creating a CLR instance before guest evaluation is outside this slice.
        Assert.Equal("", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
        var definition = assembly.GetTypes().Single(type => type.Name == "Generic" && type.IsGenericTypeDefinition);
        var runtime = assembly.GetType("$Runtime")!;
        var read = runtime.GetMethod("GetProperty")!; var prototypeOf = runtime.GetMethod("ObjectGetPrototypeOf")!;
        foreach (var (argument, value) in new (Type, object)[] { (typeof(double), 4d), (typeof(string), "word") })
        {
            var closed = definition.MakeGenericType(argument); var instance = Activator.CreateInstance(closed, [value])!;
            Assert.Equal(value, read.Invoke(null, [instance, "value"]));
            Assert.Equal(9d, closed.GetProperty("Count")!.GetValue(instance));
            Assert.Same(read.Invoke(null, [definition, "prototype"]), prototypeOf.Invoke(null, [instance]));
        }
        var uninitialized = Compile("""
            function fail(): any { throw new Error('lookup must not initialize a user class'); }
            class Uninitialized { static value: any = fail(); }
            """, true);
        var lookup = uninitialized.GetType("$Runtime")!.GetMethod("LookupRegisteredClassPrototype", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(lookup.Invoke(null, [NativeSourceTypeLookup.Find(uninitialized, "Uninitialized")]));
    }

    [Fact]
    public void ClassFreeNativeOutputOmitsTheEntireOwnPropertyFamily()
    {
        var assembly = Compile("console.log(1);", true);
        foreach (string name in new[] { "$IGuestOwnProperties", "$GuestOwnState", "$GuestOwnPropertyState" })
            Assert.Null(assembly.GetType(name));
        Assert.Null(assembly.GetType("$Runtime")!.GetMethod("LookupRegisteredClassPrototype", BindingFlags.Static | BindingFlags.NonPublic));
        Assert.NotNull(assembly.GetType("$INativeCallable"));
        Assert.Equal("1\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void NormalCompiledClassesKeepTheirExistingOutputWithoutTheOptionalContract()
    {
        var assembly = Compile("class Value { value = 1; } console.log(new Value().value);", false);
        Assert.Null(assembly.GetType("$IGuestOwnProperties"));
        Assert.DoesNotContain(assembly.GetTypes().SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)),
            field => field.Name == "$InitialOwnPrototypeValue");
        Assert.Equal("1\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    private static Assembly Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"native_own_{Guid.NewGuid():N}", false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types); byte[] bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
