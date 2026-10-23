using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassPrototypeOwnKeyReflectionTests
{
    public const string CreationOrderIssueSource = """
        class C { first() {} get middle() { return 1; } last() {} }
        console.log(Object.getOwnPropertyNames(C.prototype).join(","));
        console.log(Reflect.ownKeys(C.prototype).join(","));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["creation-order-first-middle-last-issue", CreationOrderIssueSource,
            "constructor,first,middle,last\nconstructor,first,middle,last\n"];
        yield return ["issue", """
            class C { a() { return 1; } }
            console.log(typeof C.prototype);
            console.log(Object.keys(C.prototype).join(","));
            console.log(Object.getOwnPropertyNames(C.prototype).join(","));
            """, "object\n\nconstructor,a\n"];
        yield return ["declaration-expression-generic-own-only", """
            const inherited = Symbol("inherited"), own = Symbol("own");
            class Parent<T> { inherited() {} get ancestor() { return 1; } [inherited]() {} }
            class Declaration extends Parent<number> {
                field = 1; #hidden = 2;
                static staticValue = 3; static staticMethod() {}
                own() {} get pair() { return 4; } set pair(value: number) {} [own]() {}
            }
            const Expression = class extends Parent<string> {
                field = 1; #hidden = 2;
                static staticValue = 3; static staticMethod() {}
                own() {} get pair() { return 4; } set pair(value: number) {} [own]() {}
            };
            class Generic<T> extends Parent<T> {
                field = 1; #hidden = 2;
                static staticValue = 3; static staticMethod() {}
                own() {} get pair() { return 4; } set pair(value: number) {} [own]() {}
            }
            for (const owner of [Declaration, Expression, Generic]) {
                const prototype: any = owner.prototype;
                const names = Object.getOwnPropertyNames(prototype), symbols = Object.getOwnPropertySymbols(prototype);
                console.log(names.join(","), symbols.length, symbols[0] === own,
                    !Reflect.ownKeys(prototype).includes(inherited));
                console.log(Object.keys(prototype).length, Object.values(prototype).length, Object.entries(prototype).length,
                    names.includes("field"), names.includes("hidden"), names.includes("staticValue"),
                    names.includes("staticMethod"), names.includes("inherited"), names.includes("ancestor"));
            }
            """, "constructor,own,pair 1 true true\n0 0 0 false false false false false false\nconstructor,own,pair 1 true true\n0 0 0 false false false false false false\nconstructor,own,pair 1 true true\n0 0 0 false false false false false false\n"];
        yield return ["numeric-string-symbol-order-and-pairing", """
            const first = Symbol("same"), second = Symbol("same");
            class C {
                ["10"]() {} alpha() {}
                get pair() { return 1; }
                ["2"]() {} [first]() {} ["01"]() {}
                set pair(value: number) {}
                get [second]() { return 2; }
                omega() {}
                set [second](value: number) {}
            }
            const prototype: any = C.prototype;
            prototype.beta = 3; prototype["1"] = 1;
            function keys(value: any): string {
                return Reflect.ownKeys(value).map(key => key === first ? "first" : key === second ? "second" : String(key)).join(",");
            }
            console.log(Object.getOwnPropertyNames(prototype).join(","));
            console.log(keys(prototype), Object.keys(prototype).join(","));
            const symbols = Object.getOwnPropertySymbols(prototype);
            const pair = Object.getOwnPropertyDescriptor(prototype, "pair")!;
            const symbolPair = Object.getOwnPropertyDescriptor(prototype, second)!;
            console.log(symbols.length, symbols[0] === first, symbols[1] === second,
                typeof pair.get, typeof pair.set, typeof symbolPair.get, typeof symbolPair.set,
                pair.enumerable, symbolPair.enumerable);
            """, "1,2,10,constructor,alpha,pair,01,omega,beta\n1,2,10,constructor,alpha,pair,01,omega,beta,first,second 1,beta\n2 true true function function function function false false\n"];
        yield return ["creation-order-in-place-update-does-not-invoke-getters", """
            const first = Symbol('same'), second = Symbol('same');
            let reads = 0;
            class Owner {
                first() {}
                get middle() { reads++; return 1; }
                [first]() {}
                last() {}
                set pair(value: number) {}
                [second]() {}
                get pair() { reads++; return 2; }
            }
            const prototype: any = Owner.prototype;
            function keys(value: any): string {
                return Reflect.ownKeys(value).map(key => key === first ? 'first-symbol'
                    : key === second ? 'second-symbol' : String(key)).join(',');
            }
            console.log(Object.getOwnPropertyNames(prototype).join(','), keys(prototype), reads);
            Object.defineProperty(prototype, 'middle', { enumerable: true });
            Object.defineProperty(prototype, 'first', { writable: false });
            console.log(Object.getOwnPropertyNames(prototype).join(','), keys(prototype), reads);
            const pair = Object.getOwnPropertyDescriptor(prototype, 'pair')!;
            console.log(Object.keys(prototype).join(','), typeof pair.get, typeof pair.set, reads);
            """, "constructor,first,middle,last,pair constructor,first,middle,last,pair,first-symbol,second-symbol 0\nconstructor,first,middle,last,pair constructor,first,middle,last,pair,first-symbol,second-symbol 0\nmiddle function function 0\n"];
        yield return ["creation-order-expression-and-generic-computed-accessors", """
            const symbol = Symbol('member'), instanceField = 'instanceField', events: string[] = [];
            let reads = 0;
            function key(label: string): any { events.push(label); return label; }
            const Expression = class {
                first() {}
                get [key('middle')]() { reads++; return 1; }
                static observed = Object.getOwnPropertyNames(this.prototype).join(',');
                [symbol]() {}
                last() {}
                set [key('middle')](value: number) {}
            };
            class Generic<T> {
                first() {}
                get [key('middle')]() { reads++; return 2; }
                static observed = Object.getOwnPropertyNames(this.prototype).join(',');
                ["instanceField"] = 1;
                [symbol]() {}
                last() {}
                set [key('middle')](value: number) {}
            }
            const first = new Generic<number>(), second = new Generic<string>();
            for (const owner of [Expression, Generic]) {
                const prototype: any = owner.prototype;
                console.log(Object.getOwnPropertyNames(prototype).join(','),
                    Reflect.ownKeys(prototype).map(key => key === symbol ? 'symbol' : String(key)).join(','),
                    owner.observed === Object.getOwnPropertyNames(prototype).join(','));
                const descriptor = Object.getOwnPropertyDescriptor(prototype, 'middle')!;
                const symbols = Object.getOwnPropertySymbols(prototype);
                console.log(typeof descriptor.get, typeof descriptor.set, symbols.length,
                    symbols[0] === symbol, Reflect.ownKeys(prototype).includes(instanceField), reads);
            }
            console.log(Object.getPrototypeOf(first) === Generic.prototype,
                Object.getPrototypeOf(second) === Generic.prototype, events.join(','), reads);
            """, "constructor,first,middle,last constructor,first,middle,last,symbol true\nfunction function 1 true false 0\nconstructor,first,middle,last constructor,first,middle,last,symbol true\nfunction function 1 true false 0\ntrue true middle,middle,middle,middle 0\n"];
        yield return ["creation-order-deferred-registration-preserves-forward-class-body", """
            const key = 'field';
            class Owner { [key] = 1; make() { return new Later(); } }
            class Later { value = 7; }
            console.log(new Owner().make().value);
            """, "7\n"];
        yield return ["enumerable-apis-avoid-getters-and-keep-receiver", """
            const symbol = Symbol("access");
            let gets = 0, wrongReceiver = 0;
            class C {
                method() {}
                get declared() { gets += 1000; return 99; }
            }
            const prototype: any = C.prototype;
            prototype.value = 2;
            Object.defineProperty(prototype, "access", {
                get() { gets++; if (this !== prototype) wrongReceiver++; return 3; },
                enumerable: true, configurable: true
            });
            Object.defineProperty(prototype, "hidden", {
                get() { gets += 100; return 4; }, enumerable: false, configurable: true
            });
            Object.defineProperty(prototype, "setterOnly", { set(value: any) {}, enumerable: true, configurable: true });
            Object.defineProperty(prototype, symbol, {
                get() { gets += 10; if (this !== prototype) wrongReceiver++; return 5; },
                enumerable: true, configurable: true
            });
            console.log(Object.keys(prototype).join(","), Object.getOwnPropertyNames(prototype).includes("hidden"),
                Object.getOwnPropertySymbols(prototype)[0] === symbol, Reflect.ownKeys(prototype).includes(symbol), gets);
            console.log(Object.prototype.propertyIsEnumerable.call(prototype, "value"),
                Object.prototype.propertyIsEnumerable.call(prototype, symbol),
                Object.prototype.propertyIsEnumerable.call(prototype, "method"));
            const descriptors: any = Object.getOwnPropertyDescriptors(prototype);
            console.log(typeof descriptors.access.get, typeof descriptors[symbol].get,
                descriptors.setterOnly.get === undefined, descriptors.hidden.enumerable, gets);
            console.log(Object.values(prototype).join(","), gets, wrongReceiver);
            console.log(Object.entries(prototype).map(entry => entry[0] + ":" + entry[1]).join("|"), gets, wrongReceiver);
            console.log(prototype[symbol], gets, wrongReceiver);
            const copy: any = Object.assign({}, prototype);
            console.log(copy.value, copy.access, copy[symbol], Object.hasOwn(copy, "setterOnly"),
                copy.setterOnly === undefined, gets, wrongReceiver);
            class Target {}
            const target: any = Target.prototype;
            Object.defineProperties(target, {
                alpha: { value: 4, writable: true, enumerable: true, configurable: true }
            });
            Object.defineProperty(target, symbol, { value: 8, enumerable: true, configurable: true });
            console.log(target.alpha, target[symbol], Object.keys(target).join(","),
                Object.getOwnPropertySymbols(target)[0] === symbol);
            class DescriptorMap { ignored() {} }
            const map: any = DescriptorMap.prototype, events: string[] = [];
            Object.defineProperty(map, "first", {
                get() {
                    events.push("first");
                    return { get value() { events.push("convert"); return 7; }, enumerable: true, configurable: true };
                },
                enumerable: true, configurable: true
            });
            Object.defineProperty(map, symbol, {
                get() {
                    events.push("symbol:" + (Object.hasOwn(target, "first") ? "applied" : "pending"));
                    return { value: 9, enumerable: true, configurable: true };
                },
                enumerable: true, configurable: true
            });
            console.log(Object.keys(map).join(","), events.length);
            Object.defineProperties(target, map);
            console.log(events.join(","), target.first, target[symbol], Object.keys(target).join(","));
            class UpdateMap {}
            const update: any = UpdateMap.prototype;
            update.alpha = { value: 6 };
            Object.defineProperties(target, update);
            const alpha = Object.getOwnPropertyDescriptor(target, "alpha")!;
            console.log(alpha.value, alpha.writable, alpha.enumerable, alpha.configurable);
            """, "value,access,setterOnly true true true 0\ntrue true false\nfunction function true false 0\n2,3, 1 0\nvalue:2|access:3|setterOnly:undefined 2 0\n5 12 0\n2 3 5 true true 23 0\n4 8 alpha true\nfirst 0\nfirst,convert,symbol:pending 7 9 alpha,first\n6 true true true\n"];
        yield return ["object-reflect-descriptor-parity", """
            const symbol = Symbol("method");
            class C {
                method() { return 1; }
                get access() { return 2; }
                set access(value: number) {}
                [symbol]() { return 3; }
            }
            const prototype: any = C.prototype;
            Object.defineProperty(prototype, "extra", { value: 4, writable: false, enumerable: true, configurable: false });
            const keys: any[] = ["constructor", "method", "access", "extra", symbol];
            for (const key of keys) {
                const ordinary: any = Object.getOwnPropertyDescriptor(prototype, key);
                const reflected: any = Reflect.getOwnPropertyDescriptor(prototype, key);
                console.log(typeof ordinary, ordinary.value === reflected.value, ordinary.get === reflected.get,
                    ordinary.set === reflected.set, ordinary.writable === reflected.writable,
                    ordinary.enumerable === reflected.enumerable, ordinary.configurable === reflected.configurable);
            }
            const constructor = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
            const method = Object.getOwnPropertyDescriptor(prototype, "method")!;
            console.log(constructor.value === C, constructor.writable, constructor.enumerable, constructor.configurable,
                method.writable, method.enumerable, method.configurable);
            console.log(Object.getOwnPropertyDescriptor(prototype, "missing") === undefined,
                Reflect.getOwnPropertyDescriptor(prototype, "missing") === undefined);
            """, "object true true true true true true\nobject true true true true true true\nobject true true true true true true\nobject true true true true true true\nobject true true true true true true\ntrue true false true true false true\ntrue true\n"];
        yield return ["transparent-proxy-reflection", """
            const symbol = Symbol("value");
            let receiver: any;
            class C { method() {} }
            const prototype: any = C.prototype;
            prototype.extra = 4;
            prototype[symbol] = 7;
            Object.defineProperty(prototype, "access", {
                get() { receiver = this; return 5; }, enumerable: true, configurable: true
            });
            const proxy: any = new Proxy(prototype, {});
            console.log(Object.getOwnPropertyNames(proxy).join(",") === Object.getOwnPropertyNames(prototype).join(","),
                Object.getOwnPropertySymbols(proxy)[0] === symbol,
                Reflect.ownKeys(proxy).length === Reflect.ownKeys(prototype).length, receiver === undefined);
            const descriptors: any = Object.getOwnPropertyDescriptors(proxy);
            console.log(descriptors.extra.value, descriptors[symbol].value, typeof descriptors.access.get,
                descriptors.method.enumerable, receiver === undefined);
            console.log(Object.keys(proxy).join(","), Object.values(proxy).join(","), receiver === proxy);
            console.log(Object.entries(proxy).map(entry => entry[0] + ":" + entry[1]).join("|"), receiver === proxy);
            """, "true true true true\n4 7 function false true\nextra,access 4,5 true\nextra:4|access:5 true\n"];
        yield return ["auto-accessor-own-inventory", """
            class C { accessor label: string = "first"; method() {} }
            const prototype: any = C.prototype, descriptor: any = Object.getOwnPropertyDescriptor(prototype, "label");
            const reflected: any = Reflect.getOwnPropertyDescriptor(prototype, "label");
            console.log(Object.keys(prototype).length, Object.getOwnPropertyNames(prototype).join(","),
                Object.getOwnPropertySymbols(prototype).length);
            console.log(typeof descriptor.get, typeof descriptor.set, descriptor.enumerable, descriptor.configurable,
                descriptor.get === reflected.get, descriptor.set === reflected.set);
            const instance: any = new C();
            descriptor.set.call(instance, "second");
            console.log(descriptor.get.call(instance), instance.label, Object.hasOwn(instance, "label"));
            """, "0 constructor,label,method 0\nfunction function false true true true\nsecond second false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Where(mode => mode == ExecutionMode.Interpreted || (string)row[0] switch
            {
                // #2570: compiled prototype auto-accessor descriptors are absent.
                "auto-accessor-own-inventory" => false,
                _ => true
            })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> PrototypeCreationOrderCases() => Cases().Where(row =>
        (string)row[0] is "creation-order-first-middle-last-issue"
            or "numeric-string-symbol-order-and-pairing"
            or "creation-order-in-place-update-does-not-invoke-getters"
            or "creation-order-expression-and-generic-computed-accessors"
            or "creation-order-deferred-registration-preserves-forward-class-body");

    public const string RecreationOrderSource = """
        const first = Symbol('same'), second = Symbol('same');
        let reads = 0;
        class Owner {
            first() {}
            get middle() { reads++; return 1; }
            [first]() {}
            last() {}
            set pair(value: number) {}
            [second]() {}
            get pair() { reads++; return 2; }
        }
        const prototype: any = Owner.prototype;
        function keys(value: any): string {
            return Reflect.ownKeys(value).map(key => key === first ? 'first-symbol'
                : key === second ? 'second-symbol' : String(key)).join(',');
        }
        console.log(Object.getOwnPropertyNames(prototype).join(','), keys(prototype), reads);
        delete prototype.middle;
        Object.defineProperty(prototype, 'middle', {
            get() { reads++; return 3; }, enumerable: true, configurable: true
        });
        delete prototype.first;
        Object.defineProperty(prototype, 'first', {
            value: () => 4, writable: true, enumerable: true, configurable: true
        });
        console.log(Object.getOwnPropertyNames(prototype).join(','), keys(prototype), reads);
        const pair = Object.getOwnPropertyDescriptor(prototype, 'pair')!;
        console.log(Object.keys(prototype).join(','), typeof pair.get, typeof pair.set, reads);
        """;

    public const string RecreationOrderExpected = "constructor,first,middle,last,pair constructor,first,middle,last,pair,first-symbol,second-symbol 0\nconstructor,last,pair,middle,first constructor,last,pair,middle,first,first-symbol,second-symbol 0\nmiddle,first function function 0\n";

    [Fact]
    public void DeletionAndRecreationAppendClassPrototypeKeysWithoutInvokingGetters()
    {
        const string source = RecreationOrderSource;
        const string expected = RecreationOrderExpected;
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void CompiledDeletionAndRecreationAppendClassPrototypeKeysWithoutInvokingGetters()
    {
        var (errors, output) = TestHarness.CompileVerifyAndRun(RecreationOrderSource);
        Assert.Empty(errors);
        Assert.Equal(RecreationOrderExpected, output);
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ClassPrototypeReflectionUsesOnlyItsOwnPropertyInventory(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
