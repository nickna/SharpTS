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

// Same consumer without spread: separates spread/source allocation from call and mutation costs.
function directMutationEscape(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const directResult = { a: i, b: i + 1, c: i + 2, d: i + 3 };
        directResult.b = directResult.b + 1;
        total = total + consumeSpreadResult(directResult);
    }
    return total;
}

// Same mutations with no function boundary: local mutation can retain shape promotion.
function spreadInlineMutation(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const inlineSource = { a: i, b: i + 1, c: i + 2 };
        const inlineResult = { ...inlineSource, d: i + 3 };
        inlineResult.b = inlineResult.b + 1;
        inlineResult.d = inlineResult.d + 1;
        total = total + (inlineResult.a + inlineResult.d);
    }
    return total;
}

// Retain all results before consuming them. This includes collection allocation/traversal.
function spreadRetainedResults(n: number): number {
    const retained: any[] = [];
    for (let i: number = 0; i < n; i++) {
        const retainedSource = { a: i, b: i + 1, c: i + 2 };
        const retainedResult = { ...retainedSource, d: i + 3 };
        retainedResult.b = retainedResult.b + 1;
        retained.push(retainedResult);
    }
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + consumeSpreadResult(retained[i]);
    }
    return total;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-spread-one", n, () => spreadOneSource(n), n * (n + 2));
    bench("object-spread-overwrite", n, () => spreadMultipleOverwrite(n), 2 * n * n + 13 * n);
    bench("object-spread-escape", n, () => spreadMutationEscape(n), n * (n + 3));
    bench("object-direct-escape", n, () => directMutationEscape(n), n * (n + 3));
    bench("object-spread-inline", n, () => spreadInlineMutation(n), n * (n + 3));
    bench("object-spread-retained", n, () => spreadRetainedResults(n), n * (n + 3));
}
