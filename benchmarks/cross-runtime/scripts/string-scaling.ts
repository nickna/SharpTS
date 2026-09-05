import { stringWork } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

// Separate from the small-input suite so scaling is visible without changing
// the established cases. Uses the exact shared build-and-scan workload.
const params: number[] = [10000, 100000, 1000000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("string-scaling", n, () => stringWork(n), 195 * n);
}
