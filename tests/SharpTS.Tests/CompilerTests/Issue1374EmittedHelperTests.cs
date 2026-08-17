using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class Issue1374EmittedHelperTests
{
    [Fact]
    public void Binary_data_helpers_coerce_values_return_undefined_and_preserve_endianness()
    {
        const string source = """
            const buffer: any = new ArrayBuffer(8);
            const view = new DataView(buffer);
            console.log(view.setFloat64(0, undefined) === undefined);
            console.log(Number.isNaN(view.getFloat64(0)));
            view.setUint32(0, 0x01020304, true);
            console.log(view.getUint8(0), view.getUint8(1), view.getUint8(2), view.getUint8(3));
            try {
              view.setBigInt64(0, Symbol("1"));
              console.log(false);
            } catch (error) {
              console.log(error instanceof TypeError);
            }
            console.log(buffer.slice(1, 4).byteLength);
            """;

        Assert.Equal("true\ntrue\n4 3 2 1\ntrue\n3\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Collection_helpers_keep_map_iterators_live_and_return_js_values()
    {
        const string source = """
            const map: any = new Map([[1, "a"], [2, "b"], [3, "c"]]);
            const keys: any = map.keys();
            console.log(keys.next().value);
            map.delete(2);
            console.log(keys.next().value);
            console.log(keys.next().done);

            const clearKeys: any = map.keys();
            console.log(clearKeys.next().value);
            console.log(map.clear() === undefined);
            console.log(clearKeys.next().done);

            const setMethod: any = map.set;
            console.log(setMethod.call(map, 4, "d") === map);
            const set: any = new Set([1]);
            console.log(set.clear() === undefined);
            """;

        Assert.Equal("1\n3\ntrue\n1\ntrue\ntrue\ntrue\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void BigInt_helpers_cover_dynamic_arithmetic_updates_radices_and_width_coercion()
    {
        const string source = """
            let letters = "";
            for (let radix: any = 11; radix <= 36; radix++) {
              for (let value: any = 10n; value < radix; value++) {
                letters += value.toString(radix);
                Number(value + 87n);
              }
            }
            console.log(letters.length, letters[0], letters[letters.length - 1]);
            console.log(BigInt.asIntN("8" as any, 255n));
            console.log(BigInt.asUintN(8, -1n));
            """;

        Assert.Equal("351 a z\n-1n\n255n\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Symbol_prototype_helpers_use_symbol_receivers_and_metadata()
    {
        const string source = """
            const symbol: any = Symbol("desc");
            const proto: any = Symbol.prototype;
            console.log(proto.valueOf.call(symbol) === symbol);
            console.log(proto.toString.call(symbol));
            console.log(symbol.description);
            console.log(Symbol.prototype.constructor === Symbol);
            """;

        Assert.Equal("true\nSymbol(desc)\ndesc\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Weak_collection_and_reference_helpers_have_js_constructor_shapes()
    {
        const string source = """
            const key: any = {};
            const weakMap: any = new WeakMap();
            weakMap.set(key, 42);
            console.log(weakMap.get(key), weakMap.has(key));
            const weakSet: any = new WeakSet();
            weakSet.add(key);
            console.log(weakSet.has(key));
            console.log(typeof WeakRef, typeof FinalizationRegistry);
            console.log(Object.getPrototypeOf(WeakRef) === Function.prototype);
            console.log(Object.getPrototypeOf(FinalizationRegistry) === Function.prototype);
            """;

        Assert.Equal("42 true\ntrue\nfunction function\ntrue\ntrue\n", TestHarness.RunCompiled(source));
    }
}
