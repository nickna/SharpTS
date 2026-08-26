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

function errorConstruction(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            const error = new Error("sparse");
            total = total + error.message.length;
        }
    }
    return total;
}

function errorFirstStackRead(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            total = total + new Error("sparse").stack!.length;
        }
    }
    return total;
}

function errorRepeatedStackRead(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            const error = new Error("sparse");
            const first = error.stack!;
            const second = error.stack!;
            total = total + (first === second ? second.length : -1);
        }
    }
    return total;
}

const n: number = 100000;
bench("throw-branch-control", n, () => branchControl(n));
bench("throw-try-catch-no-throw", n, () => tryCatchNoThrow(n));
bench("throw-primitive", n, () => primitiveThrow(n));
bench("throw-error", n, () => errorThrow(n));
bench("error-construct-no-stack", n, () => errorConstruction(n));
bench("error-first-stack-read", n, () => errorFirstStackRead(n));
bench("error-repeated-stack-read", n, () => errorRepeatedStackRead(n));
