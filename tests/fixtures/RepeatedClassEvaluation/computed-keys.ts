// New #1906 investigation control: one generic syntax node, two evaluations.
let count = 0;
function key() { count++; return "key" + count; }
function make() { return class<T> { [key()] = 5; }; }
const First = make();
const early: any = new First<number>();
const Second = make();
const late: any = new First<string>();
const second: any = new Second<number>();
console.log(First === Second, count);
console.log(early.key1, late.key1, late.key2, second.key1, second.key2);
