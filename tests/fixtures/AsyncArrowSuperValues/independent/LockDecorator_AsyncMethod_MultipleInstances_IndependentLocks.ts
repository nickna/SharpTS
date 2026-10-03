async function asyncValue(): Promise<number> {
    return 1;
}

class Counter {
    name: string;
    value: number = 0;

    constructor(name: string) {
        this.name = name;
    }

    @lock
    async increment(): Promise<void> {
        let v = await asyncValue();
        this.value = this.value + v;
    }
}

async function main(): Promise<void> {
    let a: Counter = new Counter("A");
    let b: Counter = new Counter("B");

    await a.increment();
    await a.increment();
    await b.increment();

    console.log(a.value);
    console.log(b.value);
}

main();