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

    console.log(counter.total, counter.step, sum);
}

main();
