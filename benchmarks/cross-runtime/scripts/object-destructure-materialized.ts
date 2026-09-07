import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

function destructureMaterialized(n: number): number {
    // The dynamic alias makes this literal dictionary-backed from construction.
    // object-destructure-carrier-materialized covers an actual carrier transition.
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
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
    bench("object-destructure-materialized", n, () => destructureMaterialized(n), n * 3);
}
