import { jsonRoundTrip } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [100, 1000, 10000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("json", n, () => jsonRoundTrip(n));
}
