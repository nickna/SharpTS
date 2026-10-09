import { twice } from "./helper";

function exercise(__this: number): number {
    let total = 0; // @break:function
    {
        total = total + 1; // @step:brace-body
    }
    for (let index = 0; index < __this; index++) { // @step:loop-header
        total = total + index; // @step:loop-body
    }
    if (total > 0) {
        total = total + twice(total);
    }
    try {
        throw "expected";
    } catch (error) {
        total = total + 2; // @break:catch
    }
    return total;
}

class Counter {
    calculate(value: number): number {
        const adjusted = value + 3; // @break:class
        return adjusted;
    }
}

async function asynchronous(value: number): Promise<number> {
    await new Promise(resolve => setTimeout(() => resolve(value), 10));
    const resumed = value + 1; // @break:async
    console.log("async", resumed);
    return resumed;
}

function* sequence(value: number) {
    yield value;
    const resumed = value + 1; // @break:generator
    yield resumed;
}

const limit = 2; // @break:top-level
const computed: number = Reflect.apply(exercise, null, [limit]);
const counter = new Counter();
const calculated = counter.calculate(computed);
const iterator = sequence(calculated);
console.log("first", iterator.next().value);
console.log("second", iterator.next().value);
asynchronous(calculated).then(value => console.log("completed", value));
console.log("done", calculated);
