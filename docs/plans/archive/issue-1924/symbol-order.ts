let first = Symbol("first");
let second = Symbol("second");
let obj: any = {};
obj[first] = 1;
obj[second] = 2;
Object.defineProperty(obj, first, { get: () => 3 });
let objectKeys = Object.getOwnPropertySymbols(obj);
console.log(objectKeys[0] === first, objectKeys[1] === second);

delete obj[first];
obj[first] = 4;
objectKeys = Object.getOwnPropertySymbols(obj);
console.log(objectKeys[0] === second, objectKeys[1] === first);

let array: any = [];
array[first] = 1;
array[second] = 2;
Object.defineProperty(array, first, { writable: false });
let arrayKeys = Object.getOwnPropertySymbols(array);
console.log(arrayKeys[0] === first, arrayKeys[1] === second);
console.log(Object.getOwnPropertyDescriptor(array, first)!.writable);