import { fibonacci } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [10, 20, 30, 35];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("fibonacci", n, () => fibonacci(n));
}
