import { bench } from "./lib/bench.ts";

function spreadOneSource(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const oneSource = { a: i, b: i + 1, c: i + 2 };
        const oneResult = { ...oneSource, d: i + 3 };
        total = total + oneResult.a + oneResult.d;
    }
    return total;
}

function spreadMultipleOverwrite(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const overwriteFirst = { a: i, b: i + 1, c: i + 2 };
        const overwriteSecond = { b: i + 3, c: i + 4, d: i + 5 };
        const overwriteResult = { ...overwriteFirst, b: i + 6, ...overwriteSecond, c: i + 7 };
        total = total + overwriteResult.a + overwriteResult.b + overwriteResult.c + overwriteResult.d;
    }
    return total;
}

function consumeSpreadResult(value: any): number {
    value.d = value.d + 1;
    return value.a + value.d;
}

function spreadMutationEscape(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const escapeSource = { a: i, b: i + 1, c: i + 2 };
        const escapeResult = { ...escapeSource, d: i + 3 };
        escapeResult.b = escapeResult.b + 1;
        total = total + consumeSpreadResult(escapeResult);
    }
    return total;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-spread-one", n, () => spreadOneSource(n));
    bench("object-spread-overwrite", n, () => spreadMultipleOverwrite(n));
    bench("object-spread-escape", n, () => spreadMutationEscape(n));
}
