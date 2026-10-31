function* values() { yield 1; yield 2; yield 3; }
let sum = 0;
for (const value of values()) sum += value;
console.log(sum);
