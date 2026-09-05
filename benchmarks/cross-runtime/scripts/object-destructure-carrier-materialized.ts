import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

function destructureCarrierMaterialized(n: number): number {
    // Discarded push retains compact allocation despite the subsequent dynamic write.
    const points: Point[] = [];
    points.push({ x: 1, y: 2 });
    const point: Point = points[0];
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
    bench("object-destructure-carrier-materialized", n,
        () => destructureCarrierMaterialized(n), n * 3);
}
