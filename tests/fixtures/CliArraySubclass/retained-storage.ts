function numbers(): number[] { return [1, 2, 3]; }
const packed = numbers();
packed[1] = 4;
packed.push(5);
console.log(packed.join(','));
const dynamic: any = packed;
dynamic[1] = 'boxed';
delete dynamic[2];
dynamic[100000] = 9;
console.log(dynamic.length, 2 in dynamic, dynamic[1], dynamic[100000]);
dynamic.length = 2;
console.log(dynamic.join(','), 100000 in dynamic);
class Values extends Array<number> {}
const derived = new Values(3);
derived[1] = 7;
console.log(derived.length, 0 in derived, derived[1]);
function collect(...values: number[]): number[] { return values; }
console.log(collect(...numbers(), 6).join(','));
Object.freeze(packed);
console.log(Object.isFrozen(packed), packed[0]);
