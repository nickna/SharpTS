import { bench } from "./lib/bench.ts";

type Point = { x: number; y: number };

// This function is intentionally unused. It verifies that dynamic mutation in
// unrelated code does not globally disable compact records of every shape.
function deleteUnknownProperty(value: any): void {
    delete value.x;
}

function destructureWithUnrelatedDynamicMutation(n: number): number {
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
    bench(
        "object-destructure-unrelated-dynamic-mutation",
        n,
        () => destructureWithUnrelatedDynamicMutation(n));
}
