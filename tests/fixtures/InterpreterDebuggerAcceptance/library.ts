export class Counter {
    constructor(private value: number) {}

    increment(): number {
        this.value++;
        return this.value;
    }
}

export function makeClosure(seed: number): (delta: number) => number {
    const captured = seed;
    return (delta: number) => captured + delta;
}

export async function afterAwait(): Promise<number> {
    const value = await Promise.resolve(40);
    return value + 2;
}

export function* values(): Generator<number> {
    yield 1;
    const resumed = 2;
    yield resumed;
}
