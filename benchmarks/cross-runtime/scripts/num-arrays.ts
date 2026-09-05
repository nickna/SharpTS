import { bench } from "./lib/bench.ts";

// Plain `number[]` (NOT a typed array — see int-arrays.ts / typed-arrays.ts for
// those). Each array ESCAPES (it is passed to a helper / is module-level), so it
// is the numeric-$Array "PACKED_DOUBLE" elements-kind, not a non-escaping
// List<double> local. This is the param/field/module-level write pattern that
// boxed a double per element write before the unboxed elements-kind — measured at
// ~73x Node, the gap this targets. A non-escaping local would instead promote to
// List<double> and is covered implicitly elsewhere.

// Growth-write: build by index through a helper (so the array escapes), then a
// read pass for a checksum. This preserves the historical `num-write` case ID.
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

function checksum(a: number[], n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

// Fixed-capacity control: setup happens before bench(), so the timed region is
// overwrite + checksum and does not include allocation or geometric growth.
function numOverwrite(a: number[], n: number): number {
    fillIndex(a, n);
    return checksum(a, n);
}

// Read-only control over an already populated array. This separates element
// access and checksum cost from every write/allocation path.
function numRead(a: number[], n: number): number {
    return checksum(a, n);
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
    const expected: number = 1.5 * params[p] * (params[p] - 1);
    bench("num-write", params[p], () => numWrite(params[p]), expected);
}
for (let p: number = 0; p < params.length; p++) {
    const expected: number = 1.5 * params[p] * (params[p] - 1);
    const overwrite: number[] = [];
    const read: number[] = [];
    fillIndex(overwrite, params[p]);
    fillIndex(read, params[p]);
    bench("num-overwrite", params[p], () =>
        numOverwrite(overwrite, params[p]), expected);
    bench("num-read", params[p], () => numRead(read, params[p]), expected);
}
for (let p: number = 0; p < params.length; p++) {
    const expected: number = 1.5 * params[p] * (params[p] - 1);
    bench("num-push", params[p], () => numPush(params[p]), expected);
}
