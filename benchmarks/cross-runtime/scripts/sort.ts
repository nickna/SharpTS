import { bench } from "./lib/bench.ts";

// Array.prototype.sort with a comparator: exercises comparator-call overhead +
// the engine's sort (V8/JSC TimSort vs .NET introsort). Data is generated with
// a seeded Park-Miller LCG (products stay < 2^53, exact in IEEE doubles) so
// SharpTS, Node, and Bun all sort byte-identical inputs.

function makeNumbers(n: number): number[] {
    const out: number[] = [];
    let state: number = 123456789;
    for (let i: number = 0; i < n; i++) {
        state = (state * 48271) % 2147483647;
        out.push(state);
    }
    return out;
}

function makeRecords(n: number): { key: number; tag: string }[] {
    const out: { key: number; tag: string }[] = [];
    let state: number = 987654321;
    for (let i: number = 0; i < n; i++) {
        state = (state * 48271) % 2147483647;
        out.push({ key: state, tag: "t" + (state % 1000) });
    }
    return out;
}

// Copy a FRESH (unsorted) array each call — sorting an already-sorted array
// collapses to O(n) and would misrepresent the cost. The slice() is the one
// intentional per-call allocation.
function sortNumbers(src: number[]): number {
    const c: number[] = src.slice();
    c.sort((a: number, b: number): number => a - b);
    return c[0] + c[c.length - 1];
}

function sortRecords(src: { key: number; tag: string }[]): number {
    const c = src.slice();
    c.sort((a: { key: number; tag: string }, b: { key: number; tag: string }): number => a.key - b.key);
    return c[0].key;
}

const params: number[] = [100, 1000, 10000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    const nums: number[] = makeNumbers(n);
    const recs = makeRecords(n);
    bench("sort", n, () => sortNumbers(nums) + sortRecords(recs));
}
