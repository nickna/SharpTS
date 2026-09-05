import { bench } from "./lib/bench.ts";

// Keep the rest comparison in Number/double representation from its first
// iteration on every runtime. An integer-zero seed lets optimizing JS engines
// begin in tagged-small-integer mode and deopt only when larger n overflows it.
const REST_ACCUMULATOR_SEED: number = 0.5;

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
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4(i, 1, 2, 3);
    }
    return sum;
}

// This is the exact source-level control for stableNumericRest: the parenthesized
// term preserves add4's evaluation tree while removing the call/rest machinery.
function flattenedRestControl(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + (i + 1 + 2 + 3);
    }
    return sum;
}

// Retain the former "flattened-rest-control" body as an explicitly named
// dependency-chain probe. Because + is left-associative, every add below depends
// on the prior iteration's sum; it is not an equivalent rest-call control.
function leftAssociatedAccumulation(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + i + 1 + 2 + 3;
    }
    return sum;
}

// Stable alias and constant-index specialization targets. The parameter-bound
// and varying-index cases below retain coverage for the ordinary rest ABI.
function indirectNumericRest(n: number): number {
    const indirectAdd4: (...values: number[]) => number = add4;
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + indirectAdd4(i, 1, 2, 3);
    }
    return sum;
}

function spreadNumericRest(n: number): number {
    const tail: number[] = [1, 2, 3];
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4(i, ...tail);
    }
    return sum;
}

function add4Dynamic(start: number, ...values: number[]): number {
    return values[start] + values[start + 1] + values[start + 2] + values[start + 3];
}

function dynamicIndexNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4Dynamic(0, i, 1, 2, 3);
    }
    return sum;
}

function unknownTargetNumericRest(n: number, fn: (...values: number[]) => number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) sum = sum + fn(i, 1, 2, 3);
    return sum;
}

function varyingIndexNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4Dynamic(i % 2, i, i, i, i, i);
    }
    return sum;
}

function add4Extra(...values: number[]): number {
    return values[0] + values[1] + values[2] + values[3] + 1;
}

function add4MutatingExtra(...values: number[]): number {
    values[0] = values[0] + 1;
    return values[0] + values[1] + values[2] + values[3];
}

function alternatingNumericRest(n: number, first: (...values: number[]) => number,
    second: (...values: number[]) => number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        const fn = i % 2 === 0 ? first : second;
        sum = sum + fn(i, 1, 2, 3);
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

// Permanent cross-runtime guard for the stable decimal parser. Keep the
// numeric toString producer here so this tracks the real #1480 workload while
// the BenchmarkDotNet companion isolates parser-only attribution.
function parseIntegers(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + parseInt(i.toString(), 10);
    }
    return sum;
}

function formatFixed(n: number): number {
    // Content-sensitive guard: length-only checks cannot distinguish the BCL's
    // ties-to-even "0.12" from JavaScript's exact "0.13" for this midpoint.
    if ((0.125).toFixed(2) !== "0.13") {
        throw new Error("incorrect Number.prototype.toFixed rounding");
    }
    let totalLength: number = 0;
    for (let i: number = 0; i < n; i++) {
        totalLength = totalLength + (i * 0.125).toFixed(2).length;
    }
    return totalLength;
}

const params: number[] = [1000, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    const rangeChecksum: number = n * (n - 1) / 2;
    const restChecksum: number = REST_ACCUMULATOR_SEED + rangeChecksum + 6 * n;
    bench("numeric-compound", n, () => numericCompound(n), rangeChecksum);
    bench("numeric-assignment-control", n, () => numericAssignmentControl(n), rangeChecksum);
    bench("stable-numeric-rest", n, () => stableNumericRest(n), restChecksum);
    bench("flattened-rest-control", n, () => flattenedRestControl(n), restChecksum);
    bench("left-associated-accumulation", n, () => leftAssociatedAccumulation(n), restChecksum);
    bench("indirect-numeric-rest", n, () => indirectNumericRest(n), restChecksum);
    bench("spread-numeric-rest", n, () => spreadNumericRest(n), restChecksum);
    bench("dynamic-index-numeric-rest", n, () => dynamicIndexNumericRest(n), restChecksum);
    bench("unknown-target-numeric-rest", n, () => unknownTargetNumericRest(n, add4), restChecksum);
    bench("varying-index-numeric-rest", n, () => varyingIndexNumericRest(n), REST_ACCUMULATOR_SEED + 4 * rangeChecksum);
    bench("alternating-target-numeric-rest", n, () => alternatingNumericRest(n, add4, add4Extra), restChecksum + n / 2);
    bench("mixed-target-numeric-rest", n, () => alternatingNumericRest(n, add4, add4MutatingExtra), restChecksum + n / 2);
    bench("generator-range", n, () => generatorRange(n), rangeChecksum);
    bench("parse-integers", n, () => parseIntegers(n), rangeChecksum);
    bench("format-fixed", n, () => formatFixed(n));
}
