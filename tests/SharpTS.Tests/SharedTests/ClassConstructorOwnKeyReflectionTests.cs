using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorOwnKeyReflectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["declaration-expression-reflection", """
            const key = Symbol("method");
            function declared(): any {
                class Owner { static value = 1; static method() {} static [key]() {} }
                return Owner;
            }
            function expression(): any {
                return class Owner { static value = 1; static method() {} static [key]() {} };
            }
            for (const factory of [declared, expression]) {
                const owner: any = factory(), names = Object.getOwnPropertyNames(owner);
                const symbols = Object.getOwnPropertySymbols(owner), all = Reflect.ownKeys(owner);
                console.log(Object.getOwnPropertyDescriptor(owner, "method") !== undefined,
                    Object.getOwnPropertyDescriptor(owner, key) !== undefined);
                console.log(names.includes("length"), names.includes("name"), names.includes("prototype"),
                    names.includes("value"), names.includes("method"));
                console.log(symbols.includes(key), all.includes("method"), all.includes(key),
                    Object.keys(owner).includes("value"), !Object.keys(owner).includes("method"));
                const descriptors: any = Object.getOwnPropertyDescriptors(owner);
                const value: any = descriptors.value, method: any = descriptors.method, symbol: any = descriptors[key];
                console.log(value.value, value.writable, value.enumerable, value.configurable,
                    method.value === owner.method, method.enumerable, symbol.value === owner[key],
                    symbol.enumerable, Object.getOwnPropertySymbols(descriptors)[0] === key);
            }
            """, "true true\ntrue true true true true\ntrue true true true true\n1 true true true true false true false true\ntrue true\ntrue true true true true\ntrue true true true true\n1 true true true true false true false true\n"];
        yield return ["definition-order-and-static-initialization", """
            const first = Symbol("same"), second = Symbol("same"), trace: string[] = [];
            function key(label: string, value: string): any { trace.push(label); return value; }
            function keys(owner: any): string {
                return Reflect.ownKeys(owner).map(k => k === first ? "first-symbol" : k === second ? "second-symbol" : String(k)).join(",");
            }
            class Owner {
                static [key("field-key", "first")] = (trace.push("field-value"), 1);
                static [key("method-key", "later")]() {}
                static [first]() {}
                static ["2"] = 2;
                static get middle() { return 3; }
                static { console.log("inside", keys(this)); }
                static ["01"] = 1;
                static [second] = 5;
                static after() {}
                static ["1"] = 1;
            }
            console.log(trace.join(","));
            console.log("final", keys(Owner));
            console.log(Object.getOwnPropertyNames(Owner).join(","), Object.keys(Owner).join(","));
            const symbols = Object.getOwnPropertySymbols(Owner);
            console.log(symbols.length, symbols[0] === first, symbols[1] === second);
            """, "inside 2,length,name,prototype,later,middle,after,first,first-symbol\nfield-key,method-key,field-value\nfinal 1,2,length,name,prototype,later,middle,after,first,01,first-symbol,second-symbol\n1,2,length,name,prototype,later,middle,after,first,01 1,2,first,01\n2 true true\n"];
        yield return ["enumerable-apis-getters-and-receiver", """
            const key = Symbol("value");
            let gets = 0, wrongReceiver = 0;
            class Owner { static value = 2; static method() {} }
            const owner: any = Owner;
            Object.defineProperty(owner, "access", { get() { gets++; if (this !== owner) wrongReceiver++; return 3; }, enumerable: true, configurable: true });
            Object.defineProperty(owner, "hidden", { get() { gets += 100; return 9; }, enumerable: false, configurable: true });
            Object.defineProperty(owner, key, { get() { gets++; if (this !== owner) wrongReceiver++; return 5; }, enumerable: true, configurable: true });
            Object.defineProperty(owner, "setterOnly", { set(value: any) {}, enumerable: true, configurable: true });
            console.log(Object.keys(owner).join(","), Object.getOwnPropertyNames(owner).includes("hidden"),
                Object.getOwnPropertySymbols(owner)[0] === key, Reflect.ownKeys(owner).includes("setterOnly"), gets);
            const descriptors: any = Object.getOwnPropertyDescriptors(owner);
            console.log(Object.hasOwn(descriptors, "setterOnly"), descriptors.setterOnly.get === undefined,
                typeof descriptors.access.get, typeof descriptors[key].get, descriptors.hidden.enumerable, gets);
            console.log(Object.values(owner).join(","), gets, wrongReceiver);
            console.log(Object.entries(owner).map(entry => entry[0] + ":" + entry[1]).join("|"), gets, wrongReceiver);
            const copy: any = Object.assign({}, owner);
            console.log(copy.value, copy.access, Object.hasOwn(copy, "setterOnly"), copy.setterOnly === undefined,
                copy[key], gets, wrongReceiver);
            """, "value,access,setterOnly true true true 0\ntrue true function function false 0\n2,3, 1 0\nvalue:2|access:3|setterOnly:undefined 2 0\n2 3 true true 5 4 0\n"];
        yield return ["generic-child-own-only-reflection", """
            const inherited = Symbol("parent"), own = Symbol("child");
            class Parent<T> { static base = 1; static parentMethod() {} static [inherited] = 2; }
            class Child<T> extends Parent<T> { static local = 3; static childMethod() {} static [own] = 4; }
            const child: any = Child, names = Object.getOwnPropertyNames(child);
            const symbols = Object.getOwnPropertySymbols(child), all = Reflect.ownKeys(child);
            console.log(child.base, child[inherited], typeof child.parentMethod, child.local, child[own]);
            console.log(names.includes("childMethod"), names.includes("local"), !names.includes("parentMethod"), !names.includes("base"),
                symbols.length, symbols[0] === own, !all.includes(inherited));
            console.log(Object.keys(child).join(","), Object.values(child).join(","),
                Object.getOwnPropertyDescriptor(child, "base") === undefined,
                Object.getOwnPropertyDescriptor(child, inherited) === undefined);
            const descriptors: any = Object.getOwnPropertyDescriptors(child);
            console.log(Object.hasOwn(descriptors, "length"), Object.hasOwn(descriptors, "name"), Object.hasOwn(descriptors, "prototype"),
                Object.hasOwn(descriptors, "childMethod"), !Object.hasOwn(descriptors, "parentMethod"),
                descriptors[own].value, !Object.hasOwn(descriptors, inherited));
            """, "1 2 function 3 4\ntrue true true true 1 true true\nlocal 3 true true\ntrue true true true true 4 true\n"];
        yield return ["expando-deletion-recreation-order", """
            const first = Symbol("same"), second = Symbol("same");
            class Owner { static original = 1; static method() {} }
            const owner: any = Owner;
            function keys(): string {
                return Reflect.ownKeys(owner).map(k => k === first ? "first-symbol" : k === second ? "second-symbol" : String(k)).join(",");
            }
            owner.left = 2;
            Object.defineProperty(owner, "right", { value: 3, writable: true, enumerable: false, configurable: true });
            owner[first] = 4; owner[second] = 5; owner["10"] = 10; owner["2"] = 2;
            console.log("before", keys());
            console.log(delete owner.left, delete owner[first], !Object.hasOwn(owner, "left"), !Object.hasOwn(owner, first));
            owner.left = 6; owner[first] = 7;
            Object.defineProperty(owner, "original", { value: 9, writable: true, enumerable: true, configurable: true });
            console.log("after", keys());
            console.log(Object.keys(owner).join(","), Object.getOwnPropertyNames(owner).includes("right"));
            const symbols = Object.getOwnPropertySymbols(owner), descriptors: any = Object.getOwnPropertyDescriptors(owner);
            console.log(symbols[0] === second, symbols[1] === first, descriptors.left.value, descriptors[first].value,
                descriptors.original.value, descriptors.right.enumerable);
            """, "before 2,10,length,name,prototype,method,original,left,right,first-symbol,second-symbol\ntrue true true true\nafter 2,10,length,name,prototype,method,original,right,left,second-symbol,first-symbol\n2,10,original,left true\ntrue true 6 7 9 false\n"];
        yield return ["snapshots-and-live-enumerable-descriptors", """
            function create(): any { class Owner { static first = 1; static removed = 2; static later = 3; } return Owner; }
            for (const kind of ['values', 'entries', 'assign']) {
                const owner: any = create(), first = Symbol('first'), late = Symbol('late'), state = { gets: 0 };
                Object.defineProperty(owner, 'first', {
                    get() {
                        state.gets++; owner.extra = 9; owner[late] = 10; delete owner.removed;
                        Object.defineProperty(owner, 'later', { enumerable: false }); return 7;
                    }, enumerable: true, configurable: true
                });
                owner[first] = 4;
                if (kind === 'values') console.log(kind, Object.values(owner).join(','), state.gets);
                else if (kind === 'entries') console.log(kind, Object.entries(owner).map(entry => entry[0] + ':' + entry[1]).join(','), state.gets);
                else {
                    const copy: any = Object.assign({}, owner);
                    console.log(kind, Object.keys(copy).join(','), copy.first, copy[first], !Object.hasOwn(copy, 'removed'),
                        !Object.hasOwn(copy, 'later'), !Object.hasOwn(copy, 'extra'), !Object.hasOwn(copy, late), state.gets);
                }
            }
            """, "values 7 1\nentries first:7 1\nassign first 7 4 true true true true 1\n"];
        yield return ["generic-transparent-proxy-reflection", """
            const key = Symbol("value");
            let receiver: any;
            class Generic<T> { static value = 4; static method() { return this; } }
            const owner: any = Generic;
            owner[key] = 7;
            Object.defineProperty(owner, "access", { get() { receiver = this; return this.value + 1; }, enumerable: true, configurable: true });
            const proxy: any = new Proxy(owner, {});
            function keys(value: any): string { return Reflect.ownKeys(value).map(k => k === key ? "symbol" : String(k)).join(","); }
            console.log(Object.getOwnPropertyNames(proxy).join(",") === Object.getOwnPropertyNames(owner).join(","),
                Object.getOwnPropertySymbols(proxy)[0] === key, keys(proxy) === keys(owner));
            const descriptors: any = Object.getOwnPropertyDescriptors(proxy);
            console.log(descriptors.value.value, descriptors.method.value === owner.method, descriptors[key].value,
                typeof descriptors.access.get, receiver === undefined);
            console.log(Object.keys(proxy).join(","), Object.values(proxy).join(","), receiver === proxy);
            const copy: any = Object.assign({}, proxy);
            console.log(copy.value, copy.access, copy[key], receiver === proxy, proxy.method() === proxy);
            const omitted = new Proxy(owner, { ownKeys(target) { return Reflect.ownKeys(target).filter(k => k !== "prototype"); } });
            try { Reflect.ownKeys(omitted); } catch (error) { console.log(error instanceof TypeError); }
            """, "true true true\n4 true 7 function true\nvalue,access 4,5 true\n4 5 7 true true\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Where(mode => mode == ExecutionMode.Interpreted || (string)row[0] switch
            {
                // #2635 owns declared and recreated constructor key ordering.
                "definition-order-and-static-initialization" or "expando-deletion-recreation-order" => false,
                // #2637: compiled enumeration reads later keys after their descriptors change.
                "snapshots-and-live-enumerable-descriptors" => false,
                _ => true
            })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ClassReflectionUsesOwnPropertyKeys(string name, string source, string expected, ExecutionMode mode)
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
