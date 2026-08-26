// Object-literal allocation benchmarks. Measures cost of `{ ... }` in
// hot loops — common in real code (return values, options bags, AST-like
// tree builders). Each iteration constructs a fresh Dictionary<string,
// object> and stores boxed values.

function smallLiteralLoop(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const o = { x: i, y: i + 1 };
        total = total + (o.x as number);
    }
    return total;
}

function mediumLiteralLoop(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const o = { a: i, b: i + 1, c: i + 2, d: i + 3, e: i + 4 };
        total = total + (o.a as number);
    }
    return total;
}

function nestedLiteralLoop(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const o = {
            point: { x: i, y: i + 1 },
            label: "pt"
        };
        const inner = o.point as any;
        total = total + (inner.x as number);
    }
    return total;
}

function spreadOneSourceLoop(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const oneSource = { a: i, b: i + 1, c: i + 2 };
        const oneResult = { ...oneSource, d: i + 3 };
        total = total + oneResult.a + oneResult.d;
    }
    return total;
}

function spreadMultipleOverwriteLoop(n: number): number {
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

function spreadMutationEscapeLoop(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const escapeSource = { a: i, b: i + 1, c: i + 2 };
        const escapeResult = { ...escapeSource, d: i + 3 };
        escapeResult.b = escapeResult.b + 1;
        total = total + consumeSpreadResult(escapeResult);
    }
    return total;
}

function objectKeysExactLoop(n: number): number {
    const bdnExactKeysRecord = { alpha: 1, beta: 2, gamma: 3, delta: 4 };
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const bdnExactKeys: string[] = Object.keys(bdnExactKeysRecord);
        total = total + bdnExactKeys.length;
    }
    return total;
}

function objectKeysMutationLoop(n: number): number {
    const bdnMutatedKeysRecord: any = { alpha: 1, beta: 2, gamma: 3 };
    bdnMutatedKeysRecord.delta = 4;
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const bdnMutatedKeys: string[] = Object.keys(bdnMutatedKeysRecord);
        total = total + bdnMutatedKeys.length;
    }
    return total;
}
