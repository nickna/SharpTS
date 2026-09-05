// Keep the rest comparison in Number/double representation from its first
// iteration on every runtime. An integer-zero seed lets optimizing JS engines
// begin in tagged-small-integer mode and deopt only when larger n overflows it.
const REST_ACCUMULATOR_SEED: number = 0.5;

// Focused probes for scalar language constructs that can disappear into native
// machine operations when their bindings and value types are stable. Keep each
// optimized form beside an equivalent control so future regressions can be
// attributed to lowering rather than to the surrounding loop.

function numericCompound(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum += i;
    }
    return sum;
}

function numericAssignmentControl(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + i;
    }
    return sum;
}

function add4(...values: number[]): number {
    return values[0] + values[1] + values[2] + values[3];
}

function stableNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4(i, 1, 2, 3);
    }
    return sum;
}

// This is the exact source-level control for stableNumericRest: the parenthesized
// term preserves add4's evaluation tree while removing the call/rest machinery.
function flattenedRestControl(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + (i + 1 + 2 + 3);
    }
    return sum;
}

// Retain the former "flattened-rest-control" body as an explicitly named
// dependency-chain probe. Because + is left-associative, every add below depends
// on the prior iteration's sum; it is not an equivalent rest-call control.
function leftAssociatedAccumulation(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + i + 1 + 2 + 3;
    }
    return sum;
}

// Stable alias and constant-index opportunities, beside genuine fallback probes.
function indirectNumericRest(n: number): number {
    const indirectAdd4: (...values: number[]) => number = add4;
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + indirectAdd4(i, 1, 2, 3);
    }
    return sum;
}

function spreadNumericRest(n: number): number {
    const tail: number[] = [1, 2, 3];
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4(i, ...tail);
    }
    return sum;
}

function add4Dynamic(start: number, ...values: number[]): number {
    return values[start] + values[start + 1] + values[start + 2] + values[start + 3];
}

function dynamicIndexNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4Dynamic(0, i, 1, 2, 3);
    }
    return sum;
}

function add4Alternative(...values: number[]): number {
    return values[3] + values[2] + values[1] + values[0];
}

function selectedNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        const operation = i % 2 === 0 ? add4 : add4Alternative;
        sum = sum + operation(i, 1, 2, 3);
    }
    return sum;
}

function varyingIndexNumericRest(n: number): number {
    let sum: number = REST_ACCUMULATOR_SEED;
    for (let i: number = 0; i < n; i++) {
        sum = sum + add4Dynamic(i % 2, i, i, 1, 2, 3);
    }
    return sum;
}
