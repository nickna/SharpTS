function* numericRange(n: number): Generator<number> {
    for (let i: number = 0; i < n; i++) {
        yield i;
    }
}

// Stable producer + direct consumer: eligible for the private typed bridge.
export function generatorRangeIteration(n: number): number {
    let sum: number = 0;
    for (const value of numericRange(n)) {
        sum = sum + value;
    }
    return sum;
}

// Public-protocol control: retains IteratorResult materialization and makes it
// possible to distinguish producer state costs from the direct for...of bridge.
export function generatorRangeManualNext(n: number): number {
    let sum: number = 0;
    const iterator = numericRange(n);
    while (true) {
        const result = iterator.next();
        if (result.done) break;
        sum = sum + (result.value as number);
    }
    return sum;
}
