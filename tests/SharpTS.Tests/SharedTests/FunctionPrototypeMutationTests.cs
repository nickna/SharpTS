using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FunctionPrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            function F() {}
            const custom: any = {};
            Object.setPrototypeOf(F, custom);
            console.log(Object.getPrototypeOf(F) === custom);
            Object.setPrototypeOf(F, null);
            console.log(Object.getPrototypeOf(F) === null);
            Object.setPrototypeOf(F, Function.prototype);
            console.log(Object.getPrototypeOf(F) === Function.prototype);
            """, "true\ntrue\ntrue\n"];
        yield return ["ordinary-function-forms", """
            function declaration() { return 1; }
            const expression = function () { return 2; };
            const arrow = () => 3;
            const custom = { marker: 7 };
            for (const fn of [declaration, expression, arrow]) {
                Object.setPrototypeOf(fn, custom);
                console.log((fn as any).marker, Reflect.apply(fn, null, []));
                Object.setPrototypeOf(fn, null);
                console.log((fn as any).marker, (fn as any).bind, Object.getPrototypeOf(fn) === null);
                Object.setPrototypeOf(fn, Function.prototype);
                console.log(typeof fn.bind);
            }
            """, "7 1\nundefined undefined true\nfunction\n7 2\nundefined undefined true\nfunction\n7 3\nundefined undefined true\nfunction\n"];
        yield return ["inherited-data-getter-and-symbol", """
            function F() { return 4; }
            const fn: any = F;
            fn.marker = 11;
            const key = Symbol("inherited");
            const custom: any = { value: 8, get receiver() { return (this as any).marker; } };
            custom[key] = 9;
            Object.setPrototypeOf(fn, custom);
            console.log(fn.value, fn.receiver, fn[key], "value" in fn, key in fn);
            console.log(Reflect.get(fn, "receiver", { marker: 13 }), Reflect.has(fn, "value"), Reflect.get(fn, key));
            console.log(Object.prototype.isPrototypeOf.call(custom, fn), Reflect.apply(fn, null, []));
            Object.setPrototypeOf(fn, null);
            console.log(fn.value, fn.receiver, fn[key], "value" in fn, key in fn, fn.marker);
            """, "8 11 9 true true\n13 true 9\ntrue 4\nundefined undefined undefined false false 11\n"];
        yield return ["inherited-setter", """
            function F() {}
            const fn: any = F;
            const custom: any = {};
            Object.defineProperty(custom, "value", {
                get() { return (this as any).marker; },
                set(value: number) { (this as any).marker = value; }
            });
            Object.setPrototypeOf(fn, custom);
            fn.value = 3;
            console.log(fn.marker, Object.hasOwn(fn, "value"));
            fn["value"] = 4;
            console.log(fn.marker, Object.hasOwn(fn, "value"));
            console.log(Reflect.set(fn, "value", 5), Reflect.get(fn, "value"), Object.hasOwn(fn, "value"));
            """, "3 false\n4 false\ntrue 5 false\n"];
        yield return ["function-cycle", """
            function F() {}
            function G() {}
            Object.setPrototypeOf(F, G);
            console.log(Object.getPrototypeOf(F) === G);
            console.log(Reflect.setPrototypeOf(G, F));
            try { Object.setPrototypeOf(G, F); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.getPrototypeOf(G) === Function.prototype);
            """, "true\nfalse\ntrue\ntrue\n"];
        yield return ["deleted-metadata", """
            function F(value: number) {}
            const fn: any = F;
            delete fn.name;
            delete fn.length;
            const custom = { name: "custom", length: 9 };
            Object.setPrototypeOf(fn, custom);
            console.log(fn.name, fn["length"], Reflect.get(fn, "name"));
            Object.setPrototypeOf(fn, null);
            console.log(fn.name, fn.length, Reflect.get(fn, "length"));
            """, "custom 9 custom\nundefined undefined undefined\n"];
        yield return ["object-function-cycle", """
            function F() {}
            const custom = {};
            Object.setPrototypeOf(custom, F);
            console.log(Reflect.setPrototypeOf(F, custom));
            console.log(Object.getPrototypeOf(F) === Function.prototype, Object.getPrototypeOf(custom) === F);
            """, "false\ntrue true\n"];
        yield return ["nonextensible", """
            function F() {}
            const custom = {};
            Object.setPrototypeOf(F, custom);
            Object.preventExtensions(F);
            console.log(Object.setPrototypeOf(F, custom) === F, Reflect.setPrototypeOf(F, custom));
            console.log(Reflect.setPrototypeOf(F, null));
            try { Object.setPrototypeOf(F, null); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.getPrototypeOf(F) === custom);
            """, "true true\nfalse\ntrue\ntrue\n"];
    }

    public static IEnumerable<object[]> ParityCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void FunctionsHonorExplicitInternalPrototypes(string name, string source, string expected, ExecutionMode mode)
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
