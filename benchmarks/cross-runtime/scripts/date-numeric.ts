import { bench } from "./lib/bench.ts";

// Stable Date numeric getter/setter workload (#1487). The Date instance remains
// local and no intrinsic method binding is exposed or mutated, so compiled
// SharpTS can keep both helper results as native doubles.
function dateNumericLoop(n: number): number {
    const date = new Date(0);
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        date.setTime(i);
        sum = sum + date.getTime();
    }
    return sum;
}

const params: number[] = [100, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("date-numeric", n, () => dateNumericLoop(n));
}
