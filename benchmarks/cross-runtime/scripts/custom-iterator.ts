import { bench } from "./lib/bench.ts";

function stableCustomIterator(n: number): number {
    let current: number = 0;
    const iterable = {
        [Symbol.iterator]() { return this; },
        next() {
            if (current < n) {
                const value: number = current;
                current = current + 1;
                return { value, done: false };
            }
            return { value: 0, done: true };
        }
    };

    let checksum: number = 0;
    for (const value of iterable) checksum = checksum + value;
    return checksum;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("custom-iterator", n, () => stableCustomIterator(n), n * (n - 1) / 2);
}
