import { bench } from "./lib/bench.ts";

function shiftDrain(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) {
        values.push(i);
    }

    let checksum: number = 0;
    while (values.length > 0) {
        checksum = checksum + values.shift();
    }
    return checksum;
}

function unshiftBuild(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) {
        values.unshift(i);
    }
    return values.length + values[0] + values[n - 1];
}

const sizes: number[] = [1000, 10000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("array-shift-drain", n, () => shiftDrain(n));
    bench("array-unshift-build", n, () => unshiftBuild(n));
}
