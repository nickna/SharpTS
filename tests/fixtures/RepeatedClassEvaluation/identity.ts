// New #1906 investigation control, not a recovered historical source.
function make() { return class {}; }
const First = make();
const Second = make();
console.log(First === Second);
console.log(Object.getPrototypeOf(new First()) === Object.getPrototypeOf(new Second()));
