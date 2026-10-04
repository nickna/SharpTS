// New #1906 control isolates identity from the inline prototype-call IL failure.
function make() { return class {}; }
const First = make();
const Second = make();
console.log(First === Second);
