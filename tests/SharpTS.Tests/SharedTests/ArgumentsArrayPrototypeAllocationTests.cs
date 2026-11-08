using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArgumentsArrayPrototypeAllocationTests
{
    [Fact]
    public void CompiledFilterReadsIndicesWithoutAllocatingForTheSourceLength()
    {
        const string source = """
            function run(value) {
                const args = arguments;
                const trace = [];
                Object.defineProperty(args, "length", {
                    get() { trace.push("length"); return 4294967296; }
                });
                Object.defineProperty(args, "0", {
                    get() { trace.push("index"); throw "stopped"; }
                });
                try { Array.prototype.filter.call(args, () => true); }
                catch (error) { trace.push(error); }
                console.log(trace.join(","));

                trace.length = 0;
                const filter = Array.prototype.filter;
                try { filter.apply(args, [() => true]); }
                catch (error) { trace.push(error); }
                console.log(trace.join(","));
            }
            run(1);
            """;
        Assert.Equal("length,index,stopped\nlength,index,stopped\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void CompiledFlatCoercesDepthBeforeReadingArgumentIndices()
    {
        const string source = """
            function run(value) {
                const args = arguments;
                const trace = [];
                Object.defineProperty(args, "length", {
                    get() { trace.push("length"); return 4294967296; }
                });
                Object.defineProperty(args, "0", {
                    get() { trace.push("index"); throw "stopped"; }
                });
                const depth = { valueOf() { trace.push("depth"); return 1; } };
                try { Array.prototype.flat.call(args, (trace.push("argument"), depth)); }
                catch (error) { trace.push(error); }
                console.log(trace.join(","));
            }
            run(1);

            function flatten(first, second, third) {
                delete arguments[1];
                console.log(JSON.stringify(Array.prototype.flat.call(arguments, 1)));
                const flat = Array.prototype.flat;
                console.log(JSON.stringify(flat.apply(arguments, [])),
                    JSON.stringify(flat.apply(arguments, [null])));
            }
            flatten([1, [2]], 9, [3]);
            """;
        Assert.Equal("argument,length,depth,index,stopped\n[1,[2],3]\n[1,[2],3] [[1,[2]],[3]]\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
