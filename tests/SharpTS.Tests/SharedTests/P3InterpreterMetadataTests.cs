using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class P3InterpreterMetadataTests
{
    [Theory, ModeData]
    public void BoundFunctionDeletedMetadataAgreesWithOwnDescriptors(ExecutionMode mode)
    {
        AssertOutput("""
            function f() {}
            const bound: any = f.bind(null);
            for (const key of ["length", "name"]) {
                console.log(Object.hasOwn(bound, key));
                console.log(delete bound[key], Object.hasOwn(bound, key),
                    Object.prototype.hasOwnProperty.call(bound, key),
                    Object.getOwnPropertyDescriptor(bound, key) === undefined);
                Object.defineProperty(bound, key, { value: 7, configurable: true });
                console.log(Object.hasOwn(bound, key), bound[key]);
            }
            """, "true\ntrue false false true\ntrue 7\ntrue\ntrue false false true\ntrue 7\n", mode);
    }

    [Theory, ModeData]
    public void ObjectConstructorMetadataCanBeDeletedAndRestored(ExecutionMode mode)
    {
        AssertOutput("""
            const O: any = Object;
            console.log(delete O.name, Object.hasOwn(O, "name"), JSON.stringify(O.name),
                Object.getOwnPropertyDescriptor(O, "name") === undefined);
            console.log(delete O.length, Object.hasOwn(O, "length"), O.length,
                Object.getOwnPropertyDescriptor(O, "length") === undefined);
            Object.defineProperty(O, "name", { value: "Restored", configurable: true });
            Object.defineProperty(O, "length", { value: 3, configurable: true });
            console.log(O.name, O.length, Object.hasOwn(O, "name"), Object.hasOwn(O, "length"));
            """, "true false \"\" true\ntrue false 0 true\nRestored 3 true true\n", mode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, "AbortController")]
    [InlineData(ExecutionMode.Compiled, "AbortController")]
    [InlineData(ExecutionMode.Interpreted, "Map")]
    [InlineData(ExecutionMode.Compiled, "Map")]
    public void BuiltInConstructorNamesAreOwnConfigurableMetadata(ExecutionMode mode, string constructorName)
    {
        AssertOutput("""
                const value: any = CONSTRUCTOR;
                const descriptor: any = Object.getOwnPropertyDescriptor(value, "name");
                console.log(value.name, Reflect.get(value, "name"), Object.hasOwn(value, "name"),
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log(delete value.name, Object.hasOwn(value, "name"), JSON.stringify(value.name),
                    JSON.stringify(Reflect.get(value, "name")),
                    Object.getOwnPropertyDescriptor(value, "name") === undefined);
                Object.defineProperty(value, "name", { value: "replacement", configurable: true });
                console.log(value.name, Reflect.get(value, "name"), Object.hasOwn(value, "name"));
            """.Replace("CONSTRUCTOR", constructorName),
            $"{constructorName} {constructorName} true false false true\ntrue false \"\" \"\" true\nreplacement replacement true\n", mode);
    }

    [Theory, ModeData]
    public void GeneratorIntrinsicConstructorsOwnTheirPrototype(ExecutionMode mode)
    {
        AssertOutput("""
            function* sync() {}
            async function* asynchronous() {}
            for (const fn of [sync, asynchronous]) {
                const ctor: any = Object.getPrototypeOf(fn).constructor;
                const descriptor: any = Object.getOwnPropertyDescriptor(ctor, "prototype");
                console.log(ctor.name, descriptor !== undefined, descriptor.writable,
                    descriptor.enumerable, descriptor.configurable);
                console.log(Object.hasOwn(ctor, "prototype"),
                    Object.prototype.hasOwnProperty.call(ctor, "prototype"),
                    Reflect.deleteProperty(ctor, "prototype"));
                console.log(Object.hasOwn(ctor, "prototype"));
            }
            """, "GeneratorFunction true false false false\ntrue true false\ntrue\nAsyncGeneratorFunction true false false false\ntrue true false\ntrue\n", mode);
    }

    [Theory, ModeData]
    public void GeneratorIntrinsicsBrandInvalidReceiversAsTypeError(ExecutionMode mode)
    {
        AssertOutput("""
            function* values() {}
            async function* asyncValues() {}
            const sync: any = values, asynchronous: any = asyncValues;
            const syncIntrinsic: any = Object.getPrototypeOf(sync.prototype);
            for (const name of ["next", "return", "throw"]) {
                try { syncIntrinsic[name].call({}); console.log("accepted"); }
                catch (error: any) { console.log(name, error.name, error instanceof TypeError); }
            }
            async function run() {
                const intrinsic: any = Object.getPrototypeOf(asynchronous.prototype);
                for (const name of ["next", "return", "throw"]) {
                    try {
                        const result = intrinsic[name].call({});
                        await result;
                        console.log("accepted");
                    }
                    catch (error: any) {
                        console.log(name, error.name, error instanceof TypeError);
                    }
                }
            }
            run();
            """, "next TypeError true\nreturn TypeError true\nthrow TypeError true\nnext TypeError true\nreturn TypeError true\nthrow TypeError true\n", mode);
    }

    // Compiled async generator return/throw reject an invalid receiver synchronously;
    // that independent timing defect is tracked separately from the error brand.
    [Theory, InterpretedOnlyData]
    public void AsyncGeneratorInvalidReceiversReturnRejectedPromises(ExecutionMode mode)
    {
        AssertOutput("""
            async function* values() {}
            async function run() {
                const fn: any = values;
                const intrinsic: any = Object.getPrototypeOf(fn.prototype);
                for (const name of ["next", "return", "throw"]) {
                    let returned = false;
                    try {
                        const result = intrinsic[name].call({});
                        returned = true;
                        await result;
                        console.log("accepted");
                    }
                    catch (error: any) { console.log(name, error.name, returned); }
                }
            }
            run();
            """, "next TypeError true\nreturn TypeError true\nthrow TypeError true\n", mode);
    }

    [Theory, ModeData]
    public void NativeErrorSubclassesInheritTheirNativePrototypeName(ExecutionMode mode)
    {
        AssertOutput("""
            class E extends Error {}
            class T extends TypeError {}
            class Derived extends T {}
            const error: any = new E("error"), subtype: any = new T("typed");
            const derived: any = new Derived("derived");
            console.log(error.message, error.name, error instanceof Error, Object.hasOwn(error, "name"));
            console.log(subtype.message, subtype.name, subtype instanceof TypeError, subtype instanceof Error);
            console.log(derived.message, derived.name, derived instanceof TypeError, Object.hasOwn(derived, "name"));
            derived.name = "custom";
            console.log(derived.name, subtype.name, Object.hasOwn(derived, "name"));
            """, "error Error true false\ntyped TypeError true true\nderived TypeError true false\ncustom TypeError true\n", mode);
    }

    [Theory, ModeData]
    public void NativePromiseSubclassesRejectNonCallableExecutorsWithTypeError(ExecutionMode mode)
    {
        AssertOutput("""
            class C extends Promise<any> {
                constructor(executor: any) { super(executor); }
            }
            for (const executor of [7, null, undefined, {}]) {
                try { new C(executor); console.log("accepted"); }
                catch (error: any) { console.log(error instanceof TypeError, error.name); }
            }
            const valid: any = new C((resolve: any) => resolve(42));
            valid.then((value: any) => console.log(value));
            """, "true TypeError\ntrue TypeError\ntrue TypeError\ntrue TypeError\n42\n", mode);
    }

    // Compiled dynamic native Promise heritage has an independent construction gap.
    [Theory, InterpretedOnlyData]
    public void AliasedNativePromiseSubclassRejectsInvalidExecutor(ExecutionMode mode)
    {
        AssertOutput("""
            const NativePromise: any = Promise;
            class C extends NativePromise {
                constructor(executor: any) { super(executor); }
            }
            try { new C(7); }
            catch (error: any) { console.log(error instanceof TypeError, error.name); }
            """, "true TypeError\n", mode);
    }

    [Theory, ModeData]
    public void MapConstructorNameRetainsOwnDescriptorAndInheritedFallback(ExecutionMode mode)
    {
        AssertOutput("""
            const ctor: any = Map;
            const prototype: any = Function.prototype;
            Object.defineProperty(prototype, "name", { value: "inherited", configurable: true });
            console.log(ctor.name, Reflect.get(ctor, "name"),
                Object.getOwnPropertyDescriptor(ctor, "name").value, prototype.name);
            console.log(delete ctor.name, ctor.name, Reflect.get(ctor, "name"), Object.hasOwn(ctor, "name"));
            Object.defineProperty(ctor, "name", { value: "renamed", configurable: true });
            console.log(ctor.name, Reflect.get(ctor, "name"), Object.hasOwn(ctor, "name"), prototype.name);
            """, "Map Map Map inherited\ntrue inherited inherited false\nrenamed renamed true inherited\n", mode);
    }

    private static void AssertOutput(string source, string expected, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
