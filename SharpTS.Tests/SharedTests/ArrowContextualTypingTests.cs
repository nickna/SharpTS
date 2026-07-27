using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for contextual typing of arrow-function parameters: when an arrow
/// is passed where a function type is expected (e.g. Array.sort comparator),
/// its untyped parameters are inferred from the expected signature. Without
/// this, `(a, b) => keys[a] &lt; keys[b]` sees `a`/`b` as Any and the IL
/// emitter's string-comparison fast path misses.
/// Runs in both interpreter and compiled modes.
/// </summary>
public class ArrowContextualTypingTests
{
    [Theory, ModeData]
    public void SortComparator_StringAtNumericIndex(ExecutionMode mode)
    {
        var source = """
            const keys: string[] = ["banana", "apple", "cherry"];
            const indices: number[] = [0, 1, 2];
            indices.sort((a, b) => {
                if (keys[a] < keys[b]) return -1;
                if (keys[a] > keys[b]) return 1;
                return 0;
            });
            console.log(indices.join(","));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,0,2\n", output);
    }

    [Theory, ModeData]
    public void SortComparator_ThisCaptureInClass(ExecutionMode mode)
    {
        // Exercises the URLSearchParams.sort pattern directly.
        var source = """
            class Bag {
                _keys: string[] = ["c", "a", "b"];
                _values: string[] = ["3", "1", "2"];
                sort(): void {
                    const n = this._keys.length;
                    const idx: number[] = [];
                    for (let i = 0; i < n; i++) idx.push(i);
                    idx.sort((a, b) => {
                        if (this._keys[a] < this._keys[b]) return -1;
                        if (this._keys[a] > this._keys[b]) return 1;
                        return 0;
                    });
                    const nk: string[] = [];
                    const nv: string[] = [];
                    for (let i = 0; i < n; i++) {
                        nk.push(this._keys[idx[i]]);
                        nv.push(this._values[idx[i]]);
                    }
                    this._keys = nk;
                    this._values = nv;
                }
            }
            const b = new Bag();
            b.sort();
            console.log(b._keys.join(","));
            console.log(b._values.join(","));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a,b,c\n1,2,3\n", output);
    }

    [Theory, ModeData]
    public void MapCallback_InfersElementType(ExecutionMode mode)
    {
        var source = """
            const names = ["alice", "bob"];
            const upper = names.map(n => n.toUpperCase());
            console.log(upper.join(","));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ALICE,BOB\n", output);
    }

    [Theory, ModeData]
    public void FilterCallback_InfersElementType(ExecutionMode mode)
    {
        var source = """
            const words = ["apple", "banana", "cherry"];
            const long = words.filter(w => w.length > 5);
            console.log(long.join(","));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("banana,cherry\n", output);
    }
}
