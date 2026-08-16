import { bench } from "./lib/bench.ts";

// Plain `number[]` (NOT a typed array — see int-arrays.ts / typed-arrays.ts for
// those). Each array ESCAPES (it is passed to a helper / is module-level), so it
// is the numeric-$Array "PACKED_DOUBLE" elements-kind, not a non-escaping
// List<double> local. This is the param/field/module-level write pattern that
// boxed a double per element write before the unboxed elements-kind — measured at
// ~73x Node, the gap this targets. A non-escaping local would instead promote to
// List<double> and is covered implicitly elsewhere.

// Index-write: build by index through a helper (so the array escapes), then a
// read pass for a checksum. The dominant cost is the per-element write.
function fillIndex(a: number[], n: number): void {
    for (let i: number = 0; i < n; i++) {
        a[i] = i * 3;
    }
}
function numWrite(n: number): number {
    const a: number[] = [];
    fillIndex(a, n);
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

// Push-built: append n elements through a helper, then a read pass. Measures the
// growth + append path rather than fixed-index stores.
function fillPush(a: number[], n: number): void {
    for (let i: number = 0; i < n; i++) {
        a.push(i * 3);
    }
}
function numPush(n: number): number {
    const a: number[] = [];
    fillPush(a, n);
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

const params: number[] = [1000, 100000, 1000000];
for (let p: number = 0; p < params.length; p++) {
    bench("num-write", params[p], () => numWrite(params[p]));
}
for (let p: number = 0; p < params.length; p++) {
    bench("num-push", params[p], () => numPush(params[p]));
}
