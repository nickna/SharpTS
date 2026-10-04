function twice(x: number) { return x * 2; }
const bound: any = twice.bind(null);
console.log(Array.from([1, 2], twice).join(","));
console.log(Array.from([1, 2], bound).join(","));
console.log(Array.from(["1", "2"], Number).join(","));
