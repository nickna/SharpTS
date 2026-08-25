import { bench } from "./lib/bench.ts";

// Focused probes for scalar language constructs that can disappear into native
// machine operations when their bindings and value types are stable. Keep each
// optimized form beside an equivalent control so future regressions can be
// attributed to lowering rather than to the surrounding loop.

function numericCompound(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum += i;
    }
    return sum;
}

function numericAssignmentControl(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + i;
    }
    return sum;
}

function add4(...values: number[]): number {
    return values[0] + values[1] + values[2] + values[3];
}

function stableNumericRest(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4(i, 1, 2, 3);
    }
    return sum;
}

function flattenedRestControl(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + i + 1 + 2 + 3;
    }
    return sum;
}

// Permanently retain the next three widened-corpus leads. These are not part
// of the current optimization, but keeping assignment-form controls here stops
// numeric compound assignment from obscuring their residual costs.
function* numericRange(n: number): Generator<number> {
    for (let i: number = 0; i < n; i++) {
        yield i;
    }
}

function generatorRange(n: number): number {
    let sum: number = 0;
    for (const value of numericRange(n)) {
        sum = sum + value;
    }
    return sum;
}

function parseIntegers(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + parseInt(i.toString(), 10);
    }
    return sum;
}

function formatFixed(n: number): number {
    let totalLength: number = 0;
    for (let i: number = 0; i < n; i++) {
        totalLength = totalLength + (i * 0.125).toFixed(2).length;
    }
    return totalLength;
}

const params: number[] = [1000, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("numeric-compound", n, () => numericCompound(n));
    bench("numeric-assignment-control", n, () => numericAssignmentControl(n));
    bench("stable-numeric-rest", n, () => stableNumericRest(n));
    bench("flattened-rest-control", n, () => flattenedRestControl(n));
    bench("generator-range", n, () => generatorRange(n));
    bench("parse-integers", n, () => parseIntegers(n));
    bench("format-fixed", n, () => formatFixed(n));
}
