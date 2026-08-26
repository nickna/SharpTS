import { bench } from "./lib/bench.ts";

function branchControl(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            sum = sum + i;
        } else {
            sum = sum + 1;
        }
    }
    return sum;
}

function tryCatchNoThrow(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                sum = sum + i;
            } else {
                sum = sum + 1;
            }
        } catch (error) {
            sum = sum - 1;
        }
    }
    return sum;
}

function primitiveThrow(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                throw i;
            }
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function errorThrow(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                throw new Error("sparse");
            }
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error instanceof Error ? i : -1);
        }
    }
    return sum;
}

const n: number = 100000;
bench("throw-branch-control", n, () => branchControl(n));
bench("throw-try-catch-no-throw", n, () => tryCatchNoThrow(n));
bench("throw-primitive", n, () => primitiveThrow(n));
bench("throw-error", n, () => errorThrow(n));
