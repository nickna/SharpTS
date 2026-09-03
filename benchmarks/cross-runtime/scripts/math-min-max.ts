import { bench } from "./lib/bench.ts";

function mathMinZero(n: number): number {
    let result: number = 0;
    for (let i: number = 0; i < n; i++) {
        result = Math.min();
    }
    return result;
}

function mathMaxOne(a: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + Math.max(a);
    }
    return total;
}

function mathMinTwo(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        // Both operands vary non-linearly so neither host can hoist or
        // constant-fold the intrinsic out of the loop.
        total = total + Math.min(i % 97, i % 89);
    }
    return total;
}

function mathMaxSeveral(
    a: number, b: number, c: number, d: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        // Make the result depend on the iteration so an optimizing host must
        // execute the four-argument max in the measured loop.
        total = total + Math.max(a + (i % 2), b, c, d);
    }
    return total;
}

function mathMaxSeveralInvariant(
    a: number, b: number, c: number, d: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + Math.max(a, b, c, d);
    }
    return total;
}

function mathMinDynamic(a: any, b: any, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        // Alternate number and string inputs. This keeps ToNumber observable
        // inside the loop instead of letting a speculative JIT hoist a stable
        // numeric coercion out of the workload.
        const candidate: any = (i % 2) === 0 ? a : b;
        total = total + Math.min(candidate, 7);
    }
    return total;
}

function mathMaxSpread(values: number[], n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + Math.max(...values);
    }
    return total;
}

// Make the values unknown at compile time while keeping their TypeScript types
// precise enough for SharpTS's fixed-arity path. The varying benchmark above
// separately prevents loop-invariant-code motion.
const seed: number = Date.now() & 1;
const a: number = 7 + seed;
const b: number = 4 + seed;
const c: number = 2 + seed;
const d: number = 3 + seed;
const dynamicA: any = a;
const dynamicB: any = String(b);
const spreadValues: number[] = [a, b, c, d];
const params: number[] = [100, 10000, 100000];

for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];

    bench("math-min-zero", n, () => mathMinZero(n));
    bench("math-max-one", n, () => mathMaxOne(a, n));
    bench("math-min-two", n, () => mathMinTwo(n));
    bench("math-max-several", n, () => mathMaxSeveral(a, b, c, d, n));
    bench("math-max-several-invariant", n, () => mathMaxSeveralInvariant(a, b, c, d, n));
    bench("math-min-dynamic", n, () => mathMinDynamic(dynamicA, dynamicB, n));
    bench("math-max-spread", n, () => mathMaxSpread(spreadValues, n));
}
