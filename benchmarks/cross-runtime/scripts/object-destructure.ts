import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

function destructureObject(n: number): number {
    const point: Point = { x: 1, y: 2 };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x + y;
    }
    return checksum;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-destructure", n, () => destructureObject(n));
}
