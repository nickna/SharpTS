// New #1905 boundary control: different type-parameter names, public members.
class Box<T> {
    value: T;
    constructor(value: T) { this.value = value; }
    read(): T { return this.value; }
}
class Other<U> {
    value: U;
    constructor(value: U) { this.value = value; }
    read(): U { return this.value; }
}
const Alias: typeof Box = Other;
console.log(new Alias<number>(7).read());
