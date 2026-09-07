import { factorial } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [10, 20, 100];
const expected: number[] = [3628800, 2432902008176640000, 9.33262154439441e157];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("factorial", n, () => factorial(n), expected[p]);
}
