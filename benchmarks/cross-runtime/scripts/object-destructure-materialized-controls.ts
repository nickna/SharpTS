import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

function direct(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) sum = sum + point.x + point.y;
    return sum;
}

function hoisted(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    const { x, y } = point;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) sum = sum + x + y;
    return sum;
}

function fractional(n: number): number {
    const point: Point = { x: 1.25, y: 2.5 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = point;
        sum = sum + x + y;
    }
    return sum;
}

function varying(n: number): number {
    const first: Point = { x: 1, y: 2 };
    const second: Point = { x: 3, y: 4 };
    const dynamicFirst: any = first;
    const dynamicSecond: any = second;
    dynamicFirst.extra = true;
    dynamicSecond.extra = true;
    const points: Point[] = [first, second];
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const { x, y } = points[i % 2];
        sum = sum + x + y;
    }
    return sum;
}

function mutated(n: number): number {
    const point: Point = { x: 1, y: 2 };
    const dynamicPoint: any = point;
    dynamicPoint.extra = true;
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        dynamicPoint.x = i;
        const { x, y } = point;
        sum = sum + x + y;
    }
    return sum;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-materialized-direct", n, () => direct(n), n * 3);
    bench("object-materialized-hoisted", n, () => hoisted(n), n * 3);
    bench("object-materialized-fractional", n, () => fractional(n), n * 3.75);
    bench("object-materialized-varying", n, () => varying(n), n * 5);
    bench("object-materialized-mutated", n, () => mutated(n), n * (n - 1) / 2 + n * 2);
}
