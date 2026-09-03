import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

function destructureInvariantFused(n: number): number {
    const point: Point = { x: 1, y: 2 };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x + y;
    }
    return checksum;
}

function destructureInvariantSplit(n: number): number {
    const point: Point = { x: 1, y: 2 };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x;
        checksum = checksum + y;
    }
    return checksum;
}

function destructureInvariantFractional(n: number): number {
    const point: Point = { x: 1.25, y: 2.5 };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        checksum = checksum + x + y;
    }
    return checksum;
}

function destructureVarying(points: Point[], n: number): number {
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = points[i % 2];
        checksum = checksum + x + y;
    }
    return checksum;
}

function directVarying(points: Point[], n: number): number {
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const point: Point = points[i % 2];
        checksum = checksum + point.x + point.y;
    }
    return checksum;
}

const points: Point[] = [{ x: 1, y: 2 }, { x: 3, y: 4 }];

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-destructure-invariant-fused", n, () => destructureInvariantFused(n));
    bench("object-destructure-invariant-split", n, () => destructureInvariantSplit(n));
    bench("object-destructure-invariant-fractional", n, () => destructureInvariantFractional(n));
    bench("object-destructure-varying", n, () => destructureVarying(points, n));
    bench("object-direct-varying", n, () => directVarying(points, n));
}
