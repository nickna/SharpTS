// Importing this module makes the native compiler serialize the DNS late-binding
// call targets. They need not be invoked; the compile itself exercises the
// metadata lookup seam.
import * as dns from "dns";

class Counter {
    accessor total: number = 0;
    #step: number;

    constructor(step: number = 2) {
        this.#step = step;
    }

    get step(): number {
        return this.#step;
    }

    bump(): void {
        this.total += this.#step;
    }
}

let prototypeConstructorCalls = 0;
class PrototypeProbe {
    constructor() {
        prototypeConstructorCalls++;
    }

    method(): string {
        return "prototype";
    }

    async asyncMethod(): Promise<string> {
        return "async";
    }

    *generatorMethod() {
        yield "generator";
    }
}

function* sequence(count: number) {
    for (let i = 0; i < count; i++) {
        yield i;
    }
}

async function* asyncSequence(count: number) {
    for (let i = 0; i < count; i++) {
        yield i * 10;
    }
}

// A runtime open generic closed over a user class (Task<Counter> from
// Promise<Counter>) is the exact shape whose MakeGenericType throws
// PlatformNotSupportedException under Native AOT — this function forces the
// EmitGenerics TypeBuilderInstantiation fallback on every CI run. The async
// machinery above only forces the MethodBuilderInstantiation fallback.
async function make(step: number): Promise<Counter> {
    return new Counter(step);
}

async function main() {
    const counter = new Counter();
    counter.bump();

    let sum = 0;
    for (const value of sequence(3)) {
        sum += value;
    }
    for await (const value of asyncSequence(3)) {
        sum += value;
    }

    const made = await make(7);

    // A typed user-class array (List<Counter> from Counter[]) closes the other
    // runtime open generic over a TypeBuilder argument.
    const items: Counter[] = [new Counter(5), new Counter(6)];
    for (const c of items) {
        c.bump();
    }
    let totals = 0;
    for (const c of items) {
        totals += c.total;
    }

    // The native compiler must emit the compiler-only class prototype
    // constructor without falling back to reflection or running guest code.
    const prototype: any = PrototypeProbe.prototype;
    const prototypeAsync = await prototype.asyncMethod();
    const prototypeGenerator = prototype.generatorMethod().next().value;

    console.log(
        counter.total,
        counter.step,
        sum,
        made.step,
        totals,
        prototypeConstructorCalls,
        prototype.method(),
        prototypeAsync,
        prototypeGenerator);
}

main();
