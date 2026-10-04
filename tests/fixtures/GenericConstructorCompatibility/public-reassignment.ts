// New unasserted variant of the retained ModuleGenericClassExpressionTests case.
let Box = class<T> {
    value: T;
    constructor(value: T) { this.value = value; }
    read(): string { return "first:" + this.value; }
};
const First = Box;
Box = class<U> {
    value: U;
    constructor(value: U) { this.value = value; }
    read(): string { return "second:" + this.value; }
};
console.log(new First(1).read());
console.log(new Box(2).read());
