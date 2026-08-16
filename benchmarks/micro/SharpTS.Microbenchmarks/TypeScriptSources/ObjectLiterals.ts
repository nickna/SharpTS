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
