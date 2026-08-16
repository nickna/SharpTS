import { objectWork } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [1000, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("objects", n, () => objectWork(n));
}
