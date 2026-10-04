class Box {
    value: number;
    constructor(value: number) { this.value = value; }
    #read(): number { return this.value; }
    extract() { return this.#read; }
}
const first = new Box(3);
const second = new Box(7);
const read = first.extract();
console.log(read === first.extract(), read === second.extract());
console.log(read.call(second));
console.log(read.call({ value: 11 }));
