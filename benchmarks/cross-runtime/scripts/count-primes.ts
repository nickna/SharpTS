import { countPrimes } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [1000, 10000, 100000];
const expected: number[] = [168, 1229, 9592];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("count-primes", n, () => countPrimes(n), expected[p]);
}
