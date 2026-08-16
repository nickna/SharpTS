using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Typed arrays and Buffers are iterable in JS (<c>%TypedArray%.prototype[@@iterator]</c>;
/// Buffer is a Uint8Array subclass), so every iteration position must accept them
/// identically in both execution modes.
/// </summary>
/// <remarks>
/// <para>
/// The interpreter carried three near-copies of one "which values are iterable" switch —
/// <c>ExecuteForOf</c>, <c>GetIterableElements</c> (spread / <c>yield*</c>), and the
/// for-await-of one in <c>Interpreter.Async.cs</c> — and they had drifted: Buffer appeared
/// in one, typed arrays in none. So <c>[...u8]</c> and <c>for (const b of u8)</c> disagreed
/// with each other and with the compiled path, which expands both. (#1282)
/// </para>
/// <para>
/// Expectations were checked against Node v25.5.0.
/// </para>
/// </remarks>
public class TypedArrayIterationParityTests
{
    private static void Expect(string source, string expected, ExecutionMode mode)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal(Normalize(expected), Normalize(output));
    }

    // Both sides need normalizing, not just the output: these source files are checked out
    // with CRLF on Windows, so the expected raw-string literals carry \r\n of their own.
    private static string Normalize(string s) =>
        string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

    [Theory, ModeData]
    public void ForOf_OverTypedArrayAndBuffer(ExecutionMode mode)
    {
        // Previously (interpreted): "for...of requires an iterable (array, Map, Set, or iterator)."
        Expect("""
            const u8 = new Uint8Array([1, 2, 3]);
            const i16 = new Int16Array([-1, 300]);
            const buf = Buffer.from([4, 5]);

            const a: number[] = [];
            for (const x of u8) { a.push(x as number); }
            console.log('u8  ' + JSON.stringify(a));

            const b: number[] = [];
            for (const x of i16) { b.push(x as number); }
            console.log('i16 ' + JSON.stringify(b));

            const c: number[] = [];
            for (const x of buf) { c.push(x as number); }
            console.log('buf ' + JSON.stringify(c));
            """,
            """
            u8  [1,2,3]
            i16 [-1,300]
            buf [4,5]
            """, mode);
    }

    [Theory, ModeData]
    public void ArrayLiteralSpread_OfTypedArrayAndBuffer(ExecutionMode mode)
    {
        Expect("""
            const u8 = new Uint8Array([1, 2, 3]);
            const buf = Buffer.from([4, 5]);
            console.log('u8   ' + JSON.stringify([...u8]));
            console.log('buf  ' + JSON.stringify([...buf]));
            console.log('both ' + JSON.stringify([0, ...u8, ...buf]));
            """,
            """
            u8   [1,2,3]
            buf  [4,5]
            both [0,1,2,3,4,5]
            """, mode);
    }

    [Theory, ModeData]
    public void ForAwaitOf_OverTypedArrayAndBuffer(ExecutionMode mode)
    {
        Expect("""
            async function main() {
                const values: number[] = [];
                for await (const x of new Uint8Array([1, 2])) values.push(x);
                for await (const x of new Int16Array([-1, 300])) values.push(x);
                for await (const x of Buffer.from([4, 5])) values.push(x);
                console.log(JSON.stringify(values));
            }
            main();
            """, "[1,2,-1,300,4,5]", mode);
    }

    [Theory, ModeData]
    public void YieldStar_OverTypedArrayAndBuffer(ExecutionMode mode)
    {
        Expect("""
            const u8 = new Uint8Array([1, 2]);
            const i16 = new Int16Array([-1, 300]);
            const buf = Buffer.from([4, 5]);

            function* g() {
                yield 0;
                yield* u8;
                yield* i16;
                yield* buf;
            }

            const values: number[] = [...g()];
            console.log(JSON.stringify(values));
            """,
            "[0,1,2,-1,300,4,5]", mode);
    }

    [Theory, ModeData]
    public void AsyncGeneratorYieldStar_OverTypedArrayAndBuffer(ExecutionMode mode)
    {
        Expect("""
            const u8 = new Uint8Array([1, 2]);
            const buf = Buffer.from([3, 4]);

            async function* g(): AsyncGenerator<number> {
                yield 0;
                yield* u8;
                yield* buf;
            }

            async function main() {
                const values: number[] = [];
                for await (const value of g()) values.push(value);
                console.log(JSON.stringify(values));
            }
            main();
            """,
            "[0,1,2,3,4]", mode);
    }

    /// <summary>
    /// <c>Array.from</c> picks the iterator protocol over the array-like (length + indices)
    /// path via <c>IsIterableSource</c>, which is documented to agree with
    /// <c>GetIterableElements</c>. It had fallen behind: typed arrays and Buffers took the
    /// array-like path, whose property reader does not understand them, so <c>length</c> came
    /// back undefined and <c>ToLength(undefined)</c> made the result empty — a silent
    /// <c>[]</c> rather than an error.
    /// </summary>
    [Theory, ModeData]
    public void ArrayFrom_OverTypedArrayAndBuffer(ExecutionMode mode)
    {
        Expect("""
            console.log('u8   ' + JSON.stringify(Array.from(new Uint8Array([1, 2, 3]))));
            console.log('buf  ' + JSON.stringify(Array.from(Buffer.from([4, 5]))));
            console.log('map  ' + JSON.stringify(Array.from(new Uint8Array([1, 2]), (x: any) => (x as number) * 10)));
            """,
            """
            u8   [1,2,3]
            buf  [4,5]
            map  [10,20]
            """, mode);
    }

    /// <summary>
    /// A zero-length typed array contributes nothing rather than throwing.
    /// </summary>
    [Theory, ModeData]
    public void EmptyTypedArray_IteratesToNothing(ExecutionMode mode)
    {
        Expect("""
            const empty = new Uint8Array(0);
            const acc: number[] = [];
            for (const x of empty) { acc.push(x as number); }
            console.log(JSON.stringify(acc) + ' ' + JSON.stringify([...empty]));
            """, "[] []", mode);
    }
}
