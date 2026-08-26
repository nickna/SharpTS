import { bench } from "./lib/bench.ts";

function destructureArray(n: number): number {
    const pair: number[] = [1, 2];
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const [left, right] = pair;
        checksum = checksum + left + right;
    }
    return checksum;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("array-destructure", n, () => destructureArray(n));
}
