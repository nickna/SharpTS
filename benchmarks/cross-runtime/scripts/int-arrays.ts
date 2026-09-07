import { bench } from "./lib/bench.ts";
import { int32Kernel } from "./lib/algorithms.ts";

// Companion to typed-arrays.ts, targeting the paths typed-arrays.ts does NOT
// exercise: a NON-Float64 typed array (Int32Array) and in-place compound
// assignment. Before the unboxed fast path was extended past Float64, both
// boxed a double per element — int32 reads/writes through GetIndex/SetIndex, and
// `a[i] += …` through GetIndex/Add/SetIndex (2-3 boxes per element).

// In-place accumulation on a Float64Array via compound assignment `a[i] += …`.
// Four accumulation passes dominate the single fill, so this measures the
// compound-index-assign path, not allocation.
function accumulate(n: number): number {
    const a = new Float64Array(n);
    for (let i: number = 0; i < n; i++) {
        a[i] = i;
    }
    for (let pass: number = 0; pass < 4; pass++) {
        for (let i: number = 0; i < n; i++) {
            a[i] += i * 0.5;
        }
    }
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

const params: number[] = [1000, 100000, 1000000];
for (let p: number = 0; p < params.length; p++) {
    bench("int32-kernel", params[p], () => int32Kernel(params[p]), 1 - (params[p] - 1) % 7 + (params[p] - 2) % 7);
}
for (let p: number = 0; p < params.length; p++) {
    bench("accumulate", params[p], () => accumulate(params[p]), 3 * params[p] * (params[p] - 1) / 2);
}
