import { bench } from "./lib/bench.ts";

function branchControl(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1);
    }
    return sum;
}

function tryCatchNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1);
        } catch (error) {
            sum = sum - 1;
        }
    }
    return sum;
}

function primitiveThrowLocal(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throw i;
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function returnPrimitiveFromCallee(value: number): number {
    return value;
}

function throwPrimitiveFromCallee(value: number): void {
    throw value;
}

function calleeNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            sum = sum + ((i & (throwEvery - 1)) === 0 ? returnPrimitiveFromCallee(i) : 1);
        } catch (error) {
            sum = sum - 1;
        }
    }
    return sum;
}

function primitiveThrowCallee(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throwPrimitiveFromCallee(i);
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function finallyNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            try {
                sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1);
            } finally {
                sum = sum + 0;
            }
        } catch (error) {
            sum = sum - 1;
        }
    }
    return sum;
}

function primitiveThrowThroughFinally(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            try {
                if ((i & (throwEvery - 1)) === 0) throw i;
                sum = sum + 1;
            } finally {
                sum = sum + 0;
            }
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function errorThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throw new Error("sparse");
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error instanceof Error ? i : -1);
        }
    }
    return sum;
}

function errorConstruction(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) {
            const error = new Error("sparse");
            total = total + error.message.length;
        }
    }
    return total;
}

function errorFirstStackRead(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) total = total + new Error("sparse").stack!.length;
    }
    return total;
}

function errorRepeatedStackRead(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) {
            const error = new Error("sparse");
            const first = error.stack!;
            const second = error.stack!;
            total = total + (first === second ? second.length : -1);
        }
    }
    return total;
}

function expectedControlResult(n: number, throwEvery: number): number {
    const throwCount: number = Math.floor((n - 1) / throwEvery) + 1;
    const thrownSum: number = throwEvery * (throwCount - 1) * throwCount / 2;
    return thrownSum + n - throwCount;
}

function runPrimitiveCases(n: number, throwEvery: number): void {
    const suffix: string = "-every-" + throwEvery;
    const expected: number = expectedControlResult(n, throwEvery);
    bench("throw-branch-control" + suffix, n, () => branchControl(n, throwEvery), expected);
    bench("throw-try-catch-no-throw" + suffix, n, () => tryCatchNoThrow(n, throwEvery), expected);
    bench("throw-primitive-local" + suffix, n, () => primitiveThrowLocal(n, throwEvery), expected);
    bench("throw-callee-no-throw" + suffix, n, () => calleeNoThrow(n, throwEvery), expected);
    bench("throw-primitive-callee" + suffix, n, () => primitiveThrowCallee(n, throwEvery), expected);
    bench("throw-finally-no-throw" + suffix, n, () => finallyNoThrow(n, throwEvery), expected);
    bench("throw-primitive-through-finally" + suffix, n, () => primitiveThrowThroughFinally(n, throwEvery), expected);
}

const n: number = 100000;
runPrimitiveCases(n, 16);
runPrimitiveCases(n, 1024);
const expected1024: number = expectedControlResult(n, 1024);
bench("throw-error-every-1024", n, () => errorThrow(n, 1024), expected1024);
bench("error-construct-no-stack-every-1024", n, () => errorConstruction(n, 1024), 588);
bench("error-first-stack-read-every-1024", n, () => errorFirstStackRead(n, 1024));
bench("error-repeated-stack-read-every-1024", n, () => errorRepeatedStackRead(n, 1024));
