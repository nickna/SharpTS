function create(n: number): any {
    let current: number = 0;
    const object: any = { next() { return current++ + n; } };
    current = 3;
    return object;
}
const object: any = create(10);
console.log(object.next(), object.next());
console.log(Number.isNaN(create(NaN).next()), create(Infinity).next());