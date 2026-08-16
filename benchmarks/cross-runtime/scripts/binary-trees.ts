import { binaryTrees } from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

// Param is tree depth: node count is 2^(depth+1) - 1, so cost grows fast.
const params: number[] = [8, 12, 16];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("binary-trees", n, () => binaryTrees(n));
}
