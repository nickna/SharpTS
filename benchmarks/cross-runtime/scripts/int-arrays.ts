import { bench } from "./lib/bench.ts";

// Companion to typed-arrays.ts, targeting the paths typed-arrays.ts does NOT
// exercise: a NON-Float64 typed array (Int32Array) and in-place compound
// assignment. Before the unboxed fast path was extended past Float64, both
// boxed a double per element — int32 reads/writes through GetIndex/SetIndex, and
// `a[i] += …` through GetIndex/Add/SetIndex (2-3 boxes per element).

// Int32Array numeric kernel: fill + 3-point stencil sweep. Mirrors
// typedArrayKernel but on a 4-byte signed integer element type, so it measures
// the unboxed Get/Set fast path for a non-Float64 typed array.
function int32Kernel(n: number): number {
    const a: Int32Array = new Int32Array(n);
    for (let i: number = 0; i < n; i++) {
        a[i] = i * 3 - (i % 7);
    }
    let sum: number = 0;
    for (let i: number = 1; i < n - 1; i++) {
        sum = sum + (a[i - 1] - 2 * a[i] + a[i + 1]);
    }
    return sum;
}

// In-place accumulation on a Float64Array via compound assignment `a[i] += …`.
// Four accumulation passes dominate the single fill, so this measures the
// compound-index-assign path, not allocation.
function accumulate(n: number): number {
    const a: Float64Array = new Float64Array(n);
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
    bench("int32-kernel", params[p], () => int32Kernel(params[p]));
}
for (let p: number = 0; p < params.length; p++) {
    bench("accumulate", params[p], () => accumulate(params[p]));
}
