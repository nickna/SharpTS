function restAdd4(...values: number[]): number {
    return values[0] + values[1] + values[2] + values[3];
}

function fixedAdd4(a: number, b: number, c: number, d: number): number {
    return a + b + c + d;
}

function restPick(start: number, ...values: number[]): number {
    return values[start] + values[start + 1] + values[start + 2] + values[start + 3];
}

// Mutation keeps this on the ordinary rest ABI, even as specialization expands.
function packedLength(...values: number[]): number {
    values[0] = 0;
    return values.length;
}

function escapeRest(...values: number[]): number[] { return values; }

function restFixedParameters(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + fixedAdd4(i, 1, 2, 3);
    return sum;
}

function restDirect(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + restAdd4(i, 1, 2, 3);
    return sum;
}

function restAlias(n: number): number {
    const alias: (...values: number[]) => number = restAdd4;
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + alias(i, 1, 2, 3);
    return sum;
}

function restConstantIndex(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + restPick(0, i, 1, 2, 3);
    return sum;
}

function restVaryingIndex(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + restPick(i % 2, i, i, i, i, i);
    return sum;
}

function restPacking(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + packedLength(i, 1, 2, 3);
    return sum;
}

function restEscaping(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + escapeRest(i, 1, 2, 3).length;
    return sum;
}

function restSpread(n: number): number {
    const tail: number[] = [1, 2, 3];
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + restAdd4(i, ...tail);
    return sum;
}

// Select externally through a parameter so the compiler cannot resolve the target.
function restUnknownTarget(n: number, fn: (...values: number[]) => number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + fn(i, 1, 2, 3);
    return sum;
}

function restDynamicDispatch(n: number): number { return restUnknownTarget(n, restAdd4); }

function restSpreadLength(n: number): number {
    const tail: number[] = [1, 2, 3];
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + escapeRest(i, ...tail).length;
    return sum;
}

function restUnknownLength(n: number, fn: (...values: number[]) => number[]): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + fn(i, 1, 2, 3).length;
    return sum;
}

function restDynamicLength(n: number): number { return restUnknownLength(n, escapeRest); }

function restAddExtra(...values: number[]): number {
    return values[0] + values[1] + values[2] + values[3] + 1;
}

function restMutatingExtra(...values: number[]): number {
    values[0] = values[0] + 1;
    return values[0] + values[1] + values[2] + values[3];
}

function restAlternatingInner(n: number, first: (...values: number[]) => number,
    second: (...values: number[]) => number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) {
        const fn = i % 2 === 0 ? first : second;
        sum = sum + fn(i, 1, 2, 3);
    }
    return sum;
}

function restAlternating(n: number): number { return restAlternatingInner(n, restAdd4, restAddExtra); }
function restMixedTargets(n: number): number { return restAlternatingInner(n, restAdd4, restMutatingExtra); }
function restReadTarget(): (...values: number[]) => number { return restAdd4; }
function restLengthTarget(): (...values: number[]) => number[] { return escapeRest; }

function restAdd4Alternative(...values: number[]): number {
    return values[3] + values[2] + values[1] + values[0];
}

function restSelectedTarget(n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) {
        const operation = i % 2 === 0 ? restAdd4 : restAdd4Alternative;
        sum = sum + operation(i, 1, 2, 3);
    }
    return sum;
}
