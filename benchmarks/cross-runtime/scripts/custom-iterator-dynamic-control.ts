import { bench } from "./lib/bench.ts";

function mutatedCustomIterator(n: number): number {
    let current: number = 0;
    const iterable: any = {
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

    // Observable alias + protocol write: this must retain the generic path.
    const alias: any = iterable;
    alias.next = alias.next;

    let checksum: number = 0;
    for (const value of iterable) checksum = checksum + value;
    return checksum;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("custom-iterator-dynamic-control", n, () => mutatedCustomIterator(n), n * (n - 1) / 2);
}
