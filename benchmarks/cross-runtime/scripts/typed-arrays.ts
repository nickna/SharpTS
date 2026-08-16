import { typedArrayKernel } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [1000, 100000, 1000000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("typed-arrays", n, () => typedArrayKernel(n));
}
