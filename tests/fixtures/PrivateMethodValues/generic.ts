class Box<T> {
    value: T;
    constructor(value: T) { this.value = value; }
    #read(): T { return this.value; }
    extract() { return this.#read; }
}
const first = new Box<number>(3);
const second = new Box<string>("seven");
const read = first.extract();
console.log(read === first.extract(), (read as any) === second.extract());
console.log(read.call(second));
console.log(read.call({ value: 11 }));
