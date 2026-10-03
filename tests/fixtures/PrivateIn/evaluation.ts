// New investigation fixture for #1903, not a recovered historical source.
class Box {
    #field = 1;
    static has(value: any) { return #field in value; }
}
let count = 0;
function next() { count++; return new Box(); }
console.log(Box.has(next()), count);
let errors = 0;
for (const value of [null, undefined, 0, false, "", 1n, Symbol("x")]) {
    try { Box.has(value); }
    catch (error) { if (error instanceof TypeError) errors++; }
}
console.log(errors);
