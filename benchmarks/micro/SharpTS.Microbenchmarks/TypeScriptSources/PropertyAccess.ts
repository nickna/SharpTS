// Property-access benchmarks. Measures cost of `obj.foo` lookup in tight
// loops — pervasive in real code (AST traversal, options reads, accessor
// chains). Each iteration accesses the same property on a fresh or
// reused object.

// Single property read on a plain object literal — the basic GetProperty
// dispatch path.
function singlePropLoop(n: number): number {
    const obj = { x: 42, y: 17 };
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + (obj.x as number);
    }
    return total;
}

// Property chain `obj.a.b.c` — repeated GetProperty per dot.
function chainPropLoop(n: number): number {
    const obj = { a: { b: { c: 42 } } };
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const inner = obj.a as any;
        const inner2 = inner.b as any;
        total = total + (inner2.c as number);
    }
    return total;
}

// Method call on object literal (`obj.method(args)`) — combines
// GetProperty + invoke dispatch. Avoid `(obj.compute as any)(...)` —
// that wraps the callee in Expr.TypeAssertion and routes through the
// generic call path instead of EmitMethodCall.
function methodCallLoop(n: number): number {
    const obj = {
        compute: (a, b) => a + b,
        base: 7
    };
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + (obj.compute(i, obj.base) as number);
    }
    return total;
}

// Class instance property read — different code path from object literals.
class Point {
    x: number;
    y: number;
    constructor(x: number, y: number) {
        this.x = x;
        this.y = y;
    }
}

function classPropLoop(n: number): number {
    const p = new Point(42, 17);
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + p.x;
    }
    return total;
}

// Property write — `obj.x = v`. Tight loop assigning to the same property
// each iteration on a single object. Measures dispatch cost of SetProperty.
// Explicit annotation keeps `x` widened to number rather than literal 0,
// otherwise the type checker rejects the iteration assignment.
function singlePropWriteLoop(n: number): number {
    const obj: { x: number, y: number } = { x: 0, y: 0 };
    for (let i: number = 0; i < n; i++) {
        obj.x = i;
    }
    return obj.x as number;
}
