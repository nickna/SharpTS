// #1964 identity control keeps prototype results in locals; identity.ts retains #1966's inline IL probe.
function make() { return class Named {}; }
const First = make(), Second = make();
const first = new First(), second = new Second();
const fp = Object.getPrototypeOf(first), sp = Object.getPrototypeOf(second);
console.log(First === Second);
console.log(fp === sp);
console.log(first.constructor === First, second.constructor === Second);
console.log(first instanceof First, first instanceof Second);
